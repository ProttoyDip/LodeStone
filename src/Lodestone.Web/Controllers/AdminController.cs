using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Policy = PolicyConstants.CanAccessAdmin)]
public class AdminController : Controller
{
    private readonly IAdminDashboardService _adminDashboardService;
    private readonly IForumService _forumService;

    public AdminController(
        IAdminDashboardService adminDashboardService,
        IForumService forumService)
    {
        _adminDashboardService = adminDashboardService;
        _forumService = forumService;
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
}
