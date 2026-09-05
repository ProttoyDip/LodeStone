using Lodestone.Application.DTOs.Student;
using Lodestone.Application.DTOs.Nudges;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Enums;
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
    private readonly INudgeService _nudgeService;
    private readonly ILogger<StudentController> _logger;

    public StudentController(
        IStudentDashboardService dashboardService,
        IRiskMonitoringConsentService consentService,
        IStudentNumberVerificationService studentNumberVerificationService,
        ICurrentUserService currentUserService,
        INudgeService nudgeService,
        ILogger<StudentController> logger)
        => (_dashboardService, _consentService, _studentNumberVerificationService, _currentUserService, _nudgeService, _logger)
            = (dashboardService, consentService, studentNumberVerificationService, currentUserService, nudgeService, logger);

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        var dashboard = await _dashboardService.GetAsync(_currentUserService.UserId, cancellationToken);
        if (dashboard is null) return Forbid();

        var consent = await _consentService.GetAsync(_currentUserService.UserId, cancellationToken);
        var verification = await _studentNumberVerificationService.GetCurrentAsync(
            _currentUserService.UserId,
            cancellationToken);

        StudentNudgeStateDto? nudges = null;
        string? nudgeLoadError = null;
        try
        {
            nudges = await _nudgeService.GetForStudentAsync(_currentUserService.UserId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load optional in-app support prompts.");
            nudgeLoadError = "Optional support prompts could not be loaded. Your prompt preference was not changed.";
        }

        return View(new StudentHomeViewModel(dashboard, consent, verification)
        {
            NudgeState = nudges,
            NudgeLoadError = nudgeLoadError
        });
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInAppNudgePreference(bool enabled, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        if (!ModelState.IsValid)
        {
            TempData["StudentNudgeError"] =
                "Your optional support-prompt choice could not be saved. Nothing changed; please try again.";
            return RedirectToNudgePreferences();
        }

        try
        {
            var result = await _nudgeService.SetInAppPreferenceAsync(
                _currentUserService.UserId,
                enabled,
                cancellationToken);

            if (result == NudgeMutationResult.Updated)
            {
                TempData["StudentNudgeSuccess"] = enabled
                    ? "Optional in-app support prompts are on. You can change this choice at any time."
                    : "Optional in-app support prompts are off. Existing prompts are hidden and no new prompt will be delivered.";
            }
            else
            {
                TempData["StudentNudgeError"] =
                    "Your optional support-prompt choice could not be saved. Nothing changed; please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update the optional in-app support-prompt preference.");
            TempData["StudentNudgeError"] =
                "Your optional support-prompt choice could not be saved. Nothing changed; please try again.";
        }

        return RedirectToNudgePreferences();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondToNudge(
        int nudgeId,
        NudgeResponseAction action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        if (!ModelState.IsValid)
        {
            TempData["StudentNudgeError"] =
                "That support-prompt action was not recognised. Nothing changed; please try again.";
            return RedirectToNudgePreferences();
        }

        try
        {
            var result = await _nudgeService.RespondAsync(
                _currentUserService.UserId,
                nudgeId,
                action,
                cancellationToken);

            switch (result)
            {
                case NudgeMutationResult.Updated:
                    TempData["StudentNudgeSuccess"] = action switch
                    {
                        NudgeResponseAction.Acknowledge => "The support prompt was acknowledged.",
                        NudgeResponseAction.Snooze => "The support prompt was snoozed for seven days.",
                        NudgeResponseAction.Dismiss => "The support prompt was dismissed.",
                        _ => "The support prompt was updated."
                    };
                    break;
                case NudgeMutationResult.NotActionable:
                case NudgeMutationResult.NotFound:
                    TempData["StudentNudgeError"] =
                        "That support prompt is no longer available. Your list has been refreshed.";
                    break;
                default:
                    TempData["StudentNudgeError"] =
                        "The support prompt could not be updated. Nothing changed; please try again.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update an optional in-app support prompt.");
            TempData["StudentNudgeError"] =
                "The support prompt could not be updated. Nothing changed; please try again.";
        }

        return RedirectToNudgePreferences();
    }

    private RedirectResult RedirectToPrivacy()
        => Redirect($"{Url.Action(nameof(Index))}#privacy");

    private RedirectToActionResult RedirectToNudgePreferences()
        => RedirectToAction(nameof(Index), null, null, "support-prompts");
}
