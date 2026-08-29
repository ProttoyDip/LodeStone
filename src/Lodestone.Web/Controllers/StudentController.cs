using Lodestone.Application.DTOs.Student;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Web.ViewModels.Student;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Roles = RoleConstants.Student)]
public class StudentController : Controller
{
    private readonly IStudentDashboardService _dashboardService;
    private readonly IRiskMonitoringConsentService _consentService;
    private readonly IStudentNumberVerificationService _studentNumberVerificationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<StudentController> _logger;

    public StudentController(
        IStudentDashboardService dashboardService,
        IRiskMonitoringConsentService consentService,
        IStudentNumberVerificationService studentNumberVerificationService,
        ICurrentUserService currentUserService,
        ILogger<StudentController> logger)
        => (_dashboardService, _consentService, _studentNumberVerificationService, _currentUserService, _logger)
            = (dashboardService, consentService, studentNumberVerificationService, currentUserService, logger);

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        var dashboard = await _dashboardService.GetAsync(_currentUserService.UserId, cancellationToken);
        if (dashboard is null) return Forbid();

        var consent = await _consentService.GetAsync(_currentUserService.UserId, cancellationToken);
        var verification = await _studentNumberVerificationService.GetCurrentAsync(
            _currentUserService.UserId,
            cancellationToken);
        return View(new StudentHomeViewModel(dashboard, consent, verification));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitStudentNumber(
        StudentNumberClaimViewModel model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();

        if (!ModelState.IsValid)
        {
            TempData["StudentIdentityError"] = ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                ?? "Enter a valid student number.";
            return RedirectToPrivacy();
        }

        try
        {
            var result = await _studentNumberVerificationService.SubmitAsync(
                _currentUserService.UserId,
                model.StudentNumber,
                cancellationToken);

            switch (result.Outcome)
            {
                case StudentNumberClaimOutcome.Submitted:
                    TempData["StudentIdentitySuccess"] =
                        "Student number submitted for Admin verification. Learning-activity imports will wait until it is approved.";
                    break;
                case StudentNumberClaimOutcome.PendingClaimExists:
                    TempData["StudentIdentityError"] =
                        "A student number is already awaiting Admin verification.";
                    break;
                case StudentNumberClaimOutcome.AlreadyVerified:
                    TempData["StudentIdentityError"] =
                        "Your student number is already verified and cannot be changed here.";
                    break;
                case StudentNumberClaimOutcome.InvalidStudentNumber:
                    TempData["StudentIdentityError"] =
                        "Enter 1-64 letters, numbers, periods, underscores, slashes, or hyphens.";
                    break;
                default:
                    TempData["StudentIdentityError"] =
                        "The student number could not be submitted. Nothing changed; please try again.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not submit a student-number claim for user {UserId}.", _currentUserService.UserId);
            TempData["StudentIdentityError"] =
                "The student number could not be submitted. Nothing changed; please try again.";
        }

        return RedirectToPrivacy();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRiskMonitoring(bool enabled, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();

        try
        {
            await _consentService.SetAsync(_currentUserService.UserId, enabled, cancellationToken);
            if (enabled)
            {
                var verification = await _studentNumberVerificationService.GetCurrentAsync(
                    _currentUserService.UserId,
                    cancellationToken);
                TempData["StudentPrivacySuccess"] = verification?.IsVerified == true
                    ? "Weekly support monitoring is now on. New 28-day activity snapshots may be scored for counselor follow-up."
                    : "Weekly support monitoring is now on. Imports will begin only after an Admin verifies your student number.";
            }
            else
            {
                TempData["StudentPrivacySuccess"] =
                    "Weekly support monitoring is now off. Learning-activity logs, imported snapshots, model scores, and support cases were removed.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update risk-monitoring consent for user {UserId}.", _currentUserService.UserId);
            TempData["StudentPrivacyError"] = "Your monitoring choice could not be saved. Nothing changed; please try again.";
        }

        return RedirectToPrivacy();
    }

    private RedirectResult RedirectToPrivacy()
        => Redirect($"{Url.Action(nameof(Index))}#privacy");
}
