using System.Text;
using System.Text.Encodings.Web;
using System.Globalization;
using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.DTOs.Student;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Infrastructure.Email;
using Lodestone.ML.Models;
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
    private readonly IRiskSnapshotAdministrationService _riskSnapshotAdministrationService;
    private readonly IRiskModelStatusProvider _riskModelStatusProvider;
    private readonly IStudentNumberVerificationService _studentNumberVerificationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IAdminDashboardService adminDashboardService,
        IForumService forumService,
        ICounselorProvisioningService counselorProvisioningService,
        IEmailService emailService,
        IRiskSnapshotAdministrationService riskSnapshotAdministrationService,
        IRiskModelStatusProvider riskModelStatusProvider,
        IStudentNumberVerificationService studentNumberVerificationService,
        ICurrentUserService currentUserService,
        ILogger<AdminController> logger)
    {
        _adminDashboardService = adminDashboardService;
        _forumService = forumService;
        _counselorProvisioningService = counselorProvisioningService;
        _emailService = emailService;
        _riskSnapshotAdministrationService = riskSnapshotAdministrationService;
        _riskModelStatusProvider = riskModelStatusProvider;
        _studentNumberVerificationService = studentNumberVerificationService;
        _currentUserService = currentUserService;
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
    public async Task<IActionResult> RiskMonitoring(CancellationToken cancellationToken)
        => await RenderRiskOperationsAsync(null, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveStudentNumberClaim(
        int claimId,
        string rowVersionToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        if (claimId <= 0 || string.IsNullOrWhiteSpace(rowVersionToken))
        {
            TempData["RiskOperationsError"] = "The student-number review request was incomplete. Refresh and try again.";
            return RedirectToRiskVerification();
        }

        try
        {
            var result = await _studentNumberVerificationService.ApproveAsync(
                claimId,
                _currentUserService.UserId,
                rowVersionToken,
                cancellationToken);
            SetClaimReviewMessage(result, approved: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not approve student-number claim {ClaimId}.", claimId);
            TempData["RiskOperationsError"] = "The student number could not be approved. Nothing changed; refresh and try again.";
        }

        return RedirectToRiskVerification();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectStudentNumberClaim(
        int claimId,
        string rowVersionToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        if (claimId <= 0 || string.IsNullOrWhiteSpace(rowVersionToken))
        {
            TempData["RiskOperationsError"] = "The student-number review request was incomplete. Refresh and try again.";
            return RedirectToRiskVerification();
        }

        try
        {
            var result = await _studentNumberVerificationService.RejectAsync(
                claimId,
                _currentUserService.UserId,
                rowVersionToken,
                cancellationToken);
            SetClaimReviewMessage(result, approved: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not reject student-number claim {ClaimId}.", claimId);
            TempData["RiskOperationsError"] = "The student number could not be rejected. Nothing changed; refresh and try again.";
        }

        return RedirectToRiskVerification();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetVerifiedStudentNumber(
        int studentProfileId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        if (studentProfileId <= 0)
        {
            TempData["RiskOperationsError"] = "The verified mapping reset request was incomplete. Refresh and try again.";
            return RedirectToRiskVerification();
        }

        try
        {
            var result = await _studentNumberVerificationService.ResetAsync(
                studentProfileId,
                _currentUserService.UserId,
                cancellationToken);

            if (result.Outcome == StudentNumberClaimOutcome.Reset)
            {
                TempData["RiskOperationsSuccess"] =
                    "The verified mapping was reset. Monitoring was disabled and its activity logs, snapshots, scores, and support cases were deleted.";
            }
            else if (result.Outcome == StudentNumberClaimOutcome.NotFound)
            {
                TempData["RiskOperationsWarning"] = "That verified mapping no longer exists. The page has been refreshed.";
            }
            else
            {
                TempData["RiskOperationsError"] = "The verified mapping could not be reset. Nothing changed; refresh and try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not reset the verified student-number mapping for profile {StudentProfileId}.", studentProfileId);
            TempData["RiskOperationsError"] = "The verified mapping could not be reset. Nothing changed; refresh and try again.";
        }

        return RedirectToRiskVerification();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<IActionResult> ImportRiskSnapshots(
        IFormFile? snapshotCsv,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();

        if (snapshotCsv is null || snapshotCsv.Length == 0)
        {
            ModelState.AddModelError(nameof(snapshotCsv), "Choose a non-empty CSV file to import.");
            return await RenderRiskOperationsAsync(null, cancellationToken);
        }

        if (snapshotCsv.Length > 25 * 1024 * 1024)
        {
            ModelState.AddModelError(nameof(snapshotCsv), "Snapshot CSV files are limited to 25 MB.");
            return await RenderRiskOperationsAsync(null, cancellationToken);
        }

        try
        {
            await using var stream = snapshotCsv.OpenReadStream();
            var result = await _riskSnapshotAdministrationService.ImportCsvAsync(
                stream,
                snapshotCsv.FileName,
                _currentUserService.UserId,
                cancellationToken);
            return await RenderRiskOperationsAsync(result, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Admin snapshot import rejected for file {FileName}.", snapshotCsv.FileName);
            ModelState.AddModelError(nameof(snapshotCsv), ex.Message);
            return await RenderRiskOperationsAsync(null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin snapshot import failed for file {FileName}.", snapshotCsv.FileName);
            ModelState.AddModelError(
                nameof(snapshotCsv),
                "The snapshot file could not be imported. No partial import was kept; review the file and try again.");
            return await RenderRiskOperationsAsync(null, cancellationToken);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunRiskScoring(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        if (!_riskModelStatusProvider.Status.IsAvailable)
        {
            TempData["RiskOperationsError"] =
                "Scoring was not started because the risk model is not available.";
            return RedirectToAction(nameof(RiskMonitoring));
        }

        try
        {
            var run = await _riskSnapshotAdministrationService.RunNowAsync(
                _currentUserService.UserId,
                cancellationToken);
            var summary = $"Run {run.RunKey:N} processed {run.CandidateCount:N0} candidates: " +
                          $"{run.ScoredCount:N0} scored, {run.SkippedCount:N0} skipped, and {run.FailedCount:N0} failed.";

            if (run.Status == Lodestone.Domain.Enums.RiskScoringRunStatus.Completed)
                TempData["RiskOperationsSuccess"] = summary;
            else if (run.Status == Lodestone.Domain.Enums.RiskScoringRunStatus.PartiallyCompleted)
                TempData["RiskOperationsWarning"] = summary;
            else
                TempData["RiskOperationsError"] = summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An administrator-triggered risk scoring run could not start or complete.");
            TempData["RiskOperationsError"] =
                "The scoring run could not be completed. No heuristic or fallback scores were created.";
        }

        return RedirectToAction(nameof(RiskMonitoring));
    }

    [HttpGet]
    public IActionResult DownloadRiskSnapshotTemplate()
    {
        const string header =
            "StudentNumber,CourseKey,WindowEndUtc,ObservedDays,FeatureSchemaVersion," +
            "ActiveDayRate,ActivitySpanDays,DaysSinceLastAccess,ForumInteractionCount," +
            "CourseInteractionCount,LateOrMissingAssignmentCount\r\n";
        var currentWindowEnd = DateTime.UtcNow.Date.ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture);
        var example =
            $"STU-0001,COURSE-01,{currentWindowEnd},28,withdrawal-28d-v1," +
            "0.5,26,2,8,120,1\r\n";
        return File(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(header + example),
            "text/csv; charset=utf-8",
            "risk-snapshot-template.csv");
    }

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

    private async Task<IActionResult> RenderRiskOperationsAsync(
        Lodestone.Application.DTOs.Risk.RiskSnapshotImportResultDto? importResult,
        CancellationToken cancellationToken)
    {
        ViewData["AdminShell"] = await _adminDashboardService.GetShellAsync(cancellationToken);
        ViewData["AdminActiveSection"] = AdminSectionType.RiskMonitoring.ToString();
        ViewData["Title"] = "Risk model operations";

        var modelStatus = _riskModelStatusProvider.Status;
        Lodestone.Application.DTOs.Risk.RiskSnapshotStatusDto? snapshotStatus = null;
        string? statusError = null;
        IReadOnlyList<StudentNumberClaimDto> pendingClaims = [];
        IReadOnlyList<VerifiedStudentNumberDto> verifiedStudentNumbers = [];
        string? verificationError = null;

        try
        {
            snapshotStatus = await _riskSnapshotAdministrationService.GetStatusAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load risk snapshot or scoring-run status.");
            statusError = "Snapshot and scoring-run status could not be loaded. Import and scoring controls are restricted until status is available.";
        }

        try
        {
            pendingClaims = await _studentNumberVerificationService.GetPendingAsync(cancellationToken);
            verifiedStudentNumbers = await _studentNumberVerificationService.GetVerifiedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load student-number verification status.");
            verificationError = "Student-number claims could not be loaded. Review and reset controls are unavailable until this status can be verified.";
        }

        return View("RiskMonitoring", new RiskOperationsViewModel
        {
            ModelStatus = modelStatus,
            SnapshotStatus = snapshotStatus,
            ImportResult = importResult,
            StatusError = statusError,
            PendingStudentNumberClaims = pendingClaims,
            VerifiedStudentNumbers = verifiedStudentNumbers,
            VerificationError = verificationError
        });
    }

    private void SetClaimReviewMessage(StudentNumberClaimResultDto result, bool approved)
    {
        switch (result.Outcome)
        {
            case StudentNumberClaimOutcome.Approved when approved:
                TempData["RiskOperationsSuccess"] =
                    $"Student number {result.Claim?.ClaimedStudentNumber ?? string.Empty} was verified. Consented imports can now match this account.";
                break;
            case StudentNumberClaimOutcome.Rejected when !approved:
                TempData["RiskOperationsSuccess"] =
                    "The student-number claim was rejected. The student can correct and resubmit it.";
                break;
            case StudentNumberClaimOutcome.DuplicateStudentNumber:
                TempData["RiskOperationsError"] =
                    "This number is already verified for another account. The claim was not approved.";
                break;
            case StudentNumberClaimOutcome.ConcurrencyConflict:
                TempData["RiskOperationsWarning"] =
                    "That claim changed after this page loaded. Nothing changed; review the refreshed claim before acting.";
                break;
            case StudentNumberClaimOutcome.AlreadyReviewed:
                TempData["RiskOperationsWarning"] =
                    "That claim has already been reviewed. The page has been refreshed.";
                break;
            case StudentNumberClaimOutcome.NotFound:
                TempData["RiskOperationsWarning"] =
                    "That claim no longer exists. The page has been refreshed.";
                break;
            default:
                TempData["RiskOperationsError"] =
                    "The student-number review could not be completed. Nothing changed; refresh and try again.";
                break;
        }
    }

    private RedirectResult RedirectToRiskVerification()
        => Redirect($"{Url.Action(nameof(RiskMonitoring))}#student-number-verification-title");

    private static string GetTitle(AdminSectionType section)
        => section switch
        {
            AdminSectionType.RiskMonitoring => "Risk model operations",
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
