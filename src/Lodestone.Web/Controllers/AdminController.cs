using System.Text;
using System.Text.Encodings.Web;
using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Infrastructure.Email;
using Lodestone.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Lodestone.Web.Controllers;

[Authorize(Policy = PolicyConstants.CanAccessAdmin)]
public class AdminController : Controller
{
    private readonly IAdminDashboardService _adminDashboardService;
    private readonly IForumService _forumService;
    private readonly ICounselorProvisioningService _counselorProvisioningService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IAdminDashboardService adminDashboardService,
        IForumService forumService,
        ICounselorProvisioningService counselorProvisioningService,
        IEmailService emailService,
        ILogger<AdminController> logger)
    {
        _adminDashboardService = adminDashboardService;
        _forumService = forumService;
        _counselorProvisioningService = counselorProvisioningService;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dashboard = await _adminDashboardService.GetDashboardAsync(cancellationToken);
        ViewData["AdminShell"] = dashboard.Shell;
        ViewData["AdminActiveSection"] = AdminSectionType.Dashboard.ToString();
        ViewData["Title"] = "Support operations";

        return View(new AdminDashboardViewModel(dashboard));
    }

    [HttpGet]
    public Task<IActionResult> RiskMonitoring(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.RiskMonitoring, q, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> CounselorBookings(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.CounselorBookings, q, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> ForumModeration(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.ForumModeration, q, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Students(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.Students, q, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Counselors(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.Counselors, q, page, cancellationToken);

    [HttpGet]
    public async Task<IActionResult> CreateCounselor(CancellationToken cancellationToken)
    {
        ViewData["AdminShell"] = await _adminDashboardService.GetShellAsync(cancellationToken);
        ViewData["AdminActiveSection"] = AdminSectionType.Counselors.ToString();
        ViewData["Title"] = "Create counselor";
        return View(new CreateCounselorViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCounselor(CreateCounselorViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["AdminShell"] = await _adminDashboardService.GetShellAsync(cancellationToken);
            ViewData["AdminActiveSection"] = AdminSectionType.Counselors.ToString();
            return View(model);
        }

        var result = await _counselorProvisioningService.CreateAsync(
            new CreateCounselorDto(model.FullName, model.Email, model.Specialization), cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            ViewData["AdminShell"] = await _adminDashboardService.GetShellAsync(cancellationToken);
            ViewData["AdminActiveSection"] = AdminSectionType.Counselors.ToString();
            return View(model);
        }

        var sent = await SendCounselorSetupEmailAsync(result, cancellationToken);
        TempData[sent ? "AdminSuccess" : "AdminError"] = sent
            ? "Counselor account created and the setup link was sent."
            : "Counselor account created, but the setup email could not be sent. Use Resend setup link.";
        return RedirectToAction(nameof(Counselors));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendCounselorSetup(string email, CancellationToken cancellationToken)
    {
        var result = await _counselorProvisioningService.CreateSetupTokenAsync(email ?? string.Empty, cancellationToken);
        if (!result.Succeeded)
        {
            TempData["AdminError"] = result.Errors.FirstOrDefault() ?? "The setup link could not be generated.";
            return RedirectToAction(nameof(CreateCounselor));
        }

        var sent = await SendCounselorSetupEmailAsync(result, cancellationToken);
        TempData[sent ? "AdminSuccess" : "AdminError"] = sent
            ? "A new counselor setup link was sent."
            : "The setup email could not be sent. Try again later.";
        return RedirectToAction(nameof(CreateCounselor));
    }

    [HttpGet]
    public Task<IActionResult> Volunteers(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.Volunteers, q, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Users(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.Users, q, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Notifications(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.Notifications, q, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> AuditLogs(string? q, int page, CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.AuditLogs, q, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Profile(CancellationToken cancellationToken)
        => RenderSectionAsync(AdminSectionType.Profile, null, 1, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewForumPost(
        int postId,
        bool publish,
        string? q,
        CancellationToken cancellationToken)
    {
        var reviewed = await _forumService.ReviewPostAsync(postId, publish, cancellationToken);
        TempData[reviewed ? "AdminSuccess" : "AdminError"] = reviewed
            ? publish
                ? "The discussion has been restored to the community."
                : "The discussion has been removed from the community."
            : "That discussion was not found. It may have already been reviewed.";

        return RedirectToAction(nameof(ForumModeration), new { q });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationRead(int id, string? q, CancellationToken cancellationToken)
    {
        var updated = await _adminDashboardService.MarkNotificationReadAsync(id, cancellationToken);
        TempData[updated ? "AdminSuccess" : "AdminError"] = updated
            ? "Notification marked as read."
            : "That notification was not found.";
        return RedirectToAction(nameof(Notifications), new { q });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllNotificationsRead(string? q, CancellationToken cancellationToken)
    {
        var updated = await _adminDashboardService.MarkAllNotificationsReadAsync(cancellationToken);
        TempData["AdminSuccess"] = updated == 0
            ? "There were no unread notifications."
            : $"Marked {updated} {(updated == 1 ? "notification" : "notifications")} as read.";
        return RedirectToAction(nameof(Notifications), new { q });
    }

    private async Task<IActionResult> RenderSectionAsync(
        AdminSectionType section,
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        ViewData["AdminShell"] = await _adminDashboardService.GetShellAsync(cancellationToken);
        ViewData["AdminActiveSection"] = section.ToString();
        ViewData["Title"] = GetTitle(section);
        ViewData["AdminQuery"] = query?.Trim();

        var sectionPage = await _adminDashboardService.GetSectionAsync(section, query, page, cancellationToken);
        return View("Section", new AdminSectionViewModel(sectionPage));
    }

    private static string GetTitle(AdminSectionType section)
        => section switch
        {
            AdminSectionType.RiskMonitoring => "Support queue",
            AdminSectionType.CounselorBookings => "Bookings",
            AdminSectionType.ForumModeration => "Forum moderation",
            AdminSectionType.Students => "Students",
            AdminSectionType.Counselors => "Counselors",
            AdminSectionType.Volunteers => "Volunteers",
            AdminSectionType.Users => "Users",
            AdminSectionType.Notifications => "Notifications",
            AdminSectionType.AuditLogs => "Audit logs",
            AdminSectionType.Profile => "Profile",
            _ => "Support operations"
        };

    private async Task<bool> SendCounselorSetupEmailAsync(CounselorProvisioningResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.Email) || string.IsNullOrWhiteSpace(result.PasswordSetupToken)) return false;
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(result.PasswordSetupToken));
        var resetUrl = Url.Action("ResetPassword", "Account", new { email = result.Email, token }, Request.Scheme)!;
        var safeUrl = HtmlEncoder.Default.Encode(resetUrl);
        var body = EmailTemplate.Wrap(
            EmailTemplate.Heading("Set up your counselor account")
            + EmailTemplate.Para("An administrator created a Lodestone counselor account for you. Choose a password to finish setup.")
            + EmailTemplate.Button(safeUrl, "Set password")
            + EmailTemplate.SmallMuted("If you were not expecting this invitation, contact your Lodestone administrator."),
            "Set up your Lodestone counselor account");
        try
        {
            await _emailService.SendAsync(result.Email, "Set up your Lodestone counselor account", body, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send counselor setup email to {Email}.", result.Email);
            return false;
        }
    }
}
