using System.Text;
using System.Text.Encodings.Web;
using Lodestone.Application.DTOs.Student;
using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Email;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Web.ViewModels.Auth;
using Lodestone.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Lodestone.Web.Controllers;

/// <summary>
/// Handles the full self-service account lifecycle: sign in / out, student
/// self-registration, and the forgot / reset password flow. Backed by ASP.NET
/// Core Identity (<see cref="SignInManager{T}"/> / <see cref="UserManager{T}"/>).
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IPublicAccountLinkBuilder _publicAccountLinkBuilder;
    private readonly IActivityLogService _activityLogService;
    private readonly IRiskMonitoringConsentService _riskMonitoringConsentService;
    private readonly IStudentNumberVerificationService _studentNumberVerificationService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IPublicAccountLinkBuilder publicAccountLinkBuilder,
        IActivityLogService activityLogService,
        IRiskMonitoringConsentService riskMonitoringConsentService,
        IStudentNumberVerificationService studentNumberVerificationService,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _emailService = emailService;
        _publicAccountLinkBuilder = publicAccountLinkBuilder;
        _activityLogService = activityLogService;
        _riskMonitoringConsentService = riskMonitoringConsentService;
        _studentNumberVerificationService = studentNumberVerificationService;
        _logger = logger;
    }

    // ---- Login -----------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // Already signed in — send them where they belong.
        if (_signInManager.IsSignedIn(User))
            return await RedirectAfterSignInAsync(await _userManager.GetUserAsync(User), returnUrl);

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is not null && !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated. Please contact support.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            if (user is not null)
            {
                user.LastLoginUtc = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
            }
            _logger.LogInformation("A user signed in.");
            await RecordStudentLoginAsync(user);
            return await RedirectAfterSignInAsync(user, returnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is temporarily locked after too many attempts. Try again later.");
            return View(model);
        }

        // Generic message — never reveal whether the email exists.
        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return View(model);
    }

    // ---- Register (student self-service) ---------------------------------

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        if (_signInManager.IsSignedIn(User))
            return RedirectToAction("Index", "Student");

        ViewData["ReturnUrl"] = returnUrl;
        return View(new RegisterViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
            return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            // Student numbers are never self-verified. Registration creates the
            // profile first, then submits the supplied number for Admin review.
            StudentProfile = new StudentProfile()
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, RoleConstants.Student);
        _logger.LogInformation("A new student account was created.");

        try
        {
            var claim = await _studentNumberVerificationService.SubmitAsync(
                user.Id,
                model.StudentNumber,
                HttpContext.RequestAborted);

            if (claim.Outcome == StudentNumberClaimOutcome.Submitted)
            {
                TempData["StudentIdentitySuccess"] =
                    "Your student number was submitted for Admin verification. Learning-activity imports will wait until it is approved.";
            }
            else
            {
                TempData["StudentIdentityError"] = RegistrationClaimFailureMessage(claim.Outcome);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not submit the initial student-number claim.");
            TempData["StudentIdentityError"] =
                "Your account was created, but the student number could not be submitted. Submit it again from Privacy.";
        }

        try
        {
            await _riskMonitoringConsentService.SetAsync(user.Id, model.EnableRiskMonitoring);
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not save the initial risk-monitoring choice.");
            if (model.EnableRiskMonitoring)
            {
                TempData["StudentPrivacyError"] =
                    "Your account was created, but weekly monitoring could not be enabled. It remains off until you enable it from Privacy.";
            }
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        await RecordStudentLoginAsync(user);
        // New students always land on the student dashboard.
        return await RedirectAfterSignInAsync(user, returnUrl);
    }

    // ---- Logout ----------------------------------------------------------

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    // ---- Forgot password -------------------------------------------------

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        SetRecoveryResponseHeaders();
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        SetRecoveryResponseHeaders();
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        // Only send a link if the account genuinely exists, but always show the
        // same confirmation so we don't leak which emails are registered.
        if (user is not null && user.IsActive)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var resetUrl = _publicAccountLinkBuilder.BuildPasswordResetUrl(
                user.Email ?? model.Email,
                encodedToken);

            await TrySendResetEmailAsync(user.Email ?? model.Email, resetUrl);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation()
    {
        SetRecoveryResponseHeaders();
        return View();
    }

    // ---- Reset password --------------------------------------------------

    [HttpGet]
    public IActionResult ResetPassword(string? email = null, string? token = null)
    {
        SetRecoveryResponseHeaders();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            return BadRequest("A valid password reset link is required.");

        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        SetRecoveryResponseHeaders();
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        // Same generic redirect whether or not the account exists.
        if (user is null)
            return RedirectToAction(nameof(ResetPasswordConfirmation));

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token));
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "This password reset link is invalid or has expired.");
            return View(model);
        }

        var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.Password);
        if (result.Succeeded)
            return RedirectToAction(nameof(ResetPasswordConfirmation));

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return View(model);
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation()
    {
        SetRecoveryResponseHeaders();
        return View();
    }

    // ---- Access denied ---------------------------------------------------

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ---- Helpers ---------------------------------------------------------

    private async Task TrySendResetEmailAsync(string email, string resetUrl)
    {
        var safeUrl = HtmlEncoder.Default.Encode(resetUrl);
        var inner =
            EmailTemplate.Heading("Reset your password") +
            EmailTemplate.Para("We received a request to reset your Lodestone password. Click the button below to choose a new one.") +
            EmailTemplate.Button(safeUrl, "Reset Password") +
            EmailTemplate.SmallMuted("If you didn't request this, you can safely ignore this email — your password won't change. This link expires in 24 hours.");

        var body = EmailTemplate.Wrap(inner, "Reset your Lodestone password");
        try
        {
            await _emailService.SendAsync(email, "Reset your Lodestone password", body);
        }
        catch (Exception)
        {
            // Do not pass the exception to logging: SMTP exceptions can include recipients,
            // message bodies, or reset URLs from a downstream transport.
            _logger.LogWarning("Failed to send a password reset email.");
        }
    }

    private async Task<IActionResult> RedirectAfterSignInAsync(ApplicationUser? user, string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        // Resolve roles from the store — the current-request principal isn't
        // refreshed with the new identity until the next request.
        var roles = user is null
            ? Array.Empty<string>()
            : (await _userManager.GetRolesAsync(user)).ToArray();

        if (roles.Contains(RoleConstants.Admin))
            return RedirectToAction("Index", "Admin");
        if (roles.Contains(RoleConstants.Counselor))
            return RedirectToAction("Queue", "Counselor");
        if (roles.Contains(RoleConstants.Volunteer))
            return RedirectToAction("Dashboard", "Volunteer");
        if (roles.Contains(RoleConstants.Student))
            return RedirectToAction("Index", "Student");

        return RedirectToAction("Index", "Student");
    }

    private async Task RecordStudentLoginAsync(ApplicationUser? user)
    {
        if (user is null || !await _userManager.IsInRoleAsync(user, RoleConstants.Student)) return;
        try
        {
            await _activityLogService.RecordLoginAsync(user.Id);
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not record student sign-in activity.");
        }
    }

    private static string RegistrationClaimFailureMessage(StudentNumberClaimOutcome outcome)
        => outcome switch
        {
            StudentNumberClaimOutcome.InvalidStudentNumber =>
                "Your account was created, but the student number was not valid. Submit it again from Privacy.",
            StudentNumberClaimOutcome.PendingClaimExists =>
                "Your account was created and already has a student number awaiting Admin verification.",
            StudentNumberClaimOutcome.AlreadyVerified =>
                "Your account was created and its student number is already verified.",
            _ => "Your account was created, but the student number could not be submitted. Submit it again from Privacy."
        };

    private void SetRecoveryResponseHeaders()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
