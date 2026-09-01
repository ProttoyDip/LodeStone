using Lodestone.Application.Interfaces;
using Lodestone.Application.DTOs.Nudges;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Enums;
using Lodestone.ML.Models;
using Lodestone.Web.ViewModels.Counselor;
using Lodestone.Web.ViewModels.Risk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Policy = PolicyConstants.CanViewRiskQueue)]
public class CounselorController : Controller
{
    private readonly ICounselorQueueService _queueService;
    private readonly IBookingService _bookingService;
    private readonly INudgeService _nudgeService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IRiskSnapshotAdministrationService _riskSnapshotAdministrationService;
    private readonly IRiskModelStatusProvider _riskModelStatusProvider;
    private readonly ILogger<CounselorController> _logger;

    public CounselorController(
        ICounselorQueueService queueService,
        IBookingService bookingService,
        INudgeService nudgeService,
        ICurrentUserService currentUserService,
        IRiskSnapshotAdministrationService riskSnapshotAdministrationService,
        IRiskModelStatusProvider riskModelStatusProvider,
        ILogger<CounselorController> logger)
        => (_queueService, _bookingService, _nudgeService, _currentUserService, _riskSnapshotAdministrationService,
                _riskModelStatusProvider, _logger) =
            (queueService, bookingService, nudgeService, currentUserService, riskSnapshotAdministrationService,
                riskModelStatusProvider, logger);

    [HttpGet]
    public async Task<IActionResult> Queue(CancellationToken cancellationToken)
    {
        try
        {
            var items = await _queueService.GetQueueAsync(cancellationToken);
            var riskRuntime = await GetRiskRuntimeStatusAsync(cancellationToken);
            return View(new CounselorQueueViewModel
            {
                Items = items,
                RefreshedAtUtc = DateTime.UtcNow,
                RiskRuntime = riskRuntime
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the counselor support queue.");
            return View(new CounselorQueueViewModel
            {
                LoadFailed = true,
                ErrorMessage = "The support queue could not be loaded. No cases were changed."
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(
        int queueEntryId,
        string? rowVersionToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();

        try
        {
            var outcome = await _queueService.TryResolveAsync(
                queueEntryId,
                _currentUserService.UserId,
                rowVersionToken,
                cancellationToken);

            switch (outcome)
            {
                case RiskQueueResolutionOutcome.Resolved:
                    TempData["QueueSuccess"] = "The support case was marked as resolved.";
                    break;
                case RiskQueueResolutionOutcome.NotFound:
                case RiskQueueResolutionOutcome.AlreadyResolved:
                    TempData["QueueConflict"] = "That case is no longer open. The queue has been refreshed.";
                    break;
                case RiskQueueResolutionOutcome.ConcurrencyConflict:
                    TempData["QueueConflict"] = "Another counselor changed that case. Review the refreshed queue before trying again.";
                    break;
                default:
                    TempData["QueueError"] = "The case could not be resolved. Please try again.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not resolve queue entry {QueueEntryId}.", queueEntryId);
            TempData["QueueError"] = "The case could not be resolved. Please try again.";
        }

        return RedirectToAction(nameof(Queue));
    }

    private async Task<RiskRuntimeStatusViewModel> GetRiskRuntimeStatusAsync(
        CancellationToken cancellationToken)
    {
        var modelStatus = _riskModelStatusProvider.Status;
        try
        {
            var snapshotStatus = await _riskSnapshotAdministrationService.GetStatusAsync(cancellationToken);
            return new RiskRuntimeStatusViewModel
            {
                ModelStatus = modelStatus,
                SnapshotStatus = snapshotStatus
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load risk runtime status for the counselor queue.");
            return new RiskRuntimeStatusViewModel
            {
                ModelStatus = modelStatus,
                StatusError = "Model availability is shown, but the latest scoring status could not be loaded."
            };
        }
    }

    [HttpGet]
    public async Task<IActionResult> Reviews(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();

        try
        {
            var page = await _bookingService.GetCounselorAppointmentsAsync(
                _currentUserService.UserId,
                cancellationToken);
            return page is null
                ? Forbid()
                : View(new CounselorAppointmentsViewModel
                {
                    Page = page,
                    RefreshedAtUtc = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the counselor appointment workspace.");
            return View(new CounselorAppointmentsViewModel
            {
                LoadFailed = true,
                ErrorMessage = "Appointments could not be loaded. No records were changed."
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordAppointmentOutcome(
        int bookingId,
        BookingStatus outcome,
        string? sessionNotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();

        try
        {
            var result = await _bookingService.RecordCounselorOutcomeAsync(
                _currentUserService.UserId,
                bookingId,
                outcome,
                sessionNotes,
                cancellationToken);

            switch (result)
            {
                case Lodestone.Application.DTOs.Booking.CounselorBookingUpdateResult.Updated:
                    TempData["AppointmentSuccess"] = outcome == BookingStatus.Completed
                        ? "The appointment was marked as completed."
                        : "The appointment was marked as a no-show.";
                    break;
                case Lodestone.Application.DTOs.Booking.CounselorBookingUpdateResult.NotFound:
                    TempData["AppointmentConflict"] = "That appointment is not available in your workspace.";
                    break;
                case Lodestone.Application.DTOs.Booking.CounselorBookingUpdateResult.NotEligible:
                    TempData["AppointmentConflict"] = "That appointment is not ready for this update. The list has been refreshed.";
                    break;
                default:
                    TempData["AppointmentError"] = "Choose a valid outcome and keep session notes within 2,000 characters.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record an outcome for booking {BookingId}.", bookingId);
            TempData["AppointmentError"] = "The appointment could not be updated. Please try again.";
        }

        return RedirectToAction(nameof(Reviews));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManualNudge(
        int bookingId,
        ManualNudgeTemplate template,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        if (!ModelState.IsValid)
        {
            TempData["ManualNudgeError"] =
                "Choose one of the approved neutral prompt templates and try again.";
            return RedirectToAction(nameof(Reviews));
        }

        try
        {
            var result = await _nudgeService.CreateManualForBookingAsync(
                _currentUserService.UserId,
                bookingId,
                template,
                cancellationToken);

            switch (result)
            {
                case NudgeMutationResult.Updated:
                    TempData["ManualNudgeSuccess"] =
                        "A neutral optional in-app prompt was sent. It is independent of risk monitoring and requires no further action from the student.";
                    break;
                case NudgeMutationResult.PreferenceDisabled:
                    TempData["ManualNudgeConflict"] =
                        "This student has chosen not to receive optional in-app prompts. No prompt was sent.";
                    break;
                case NudgeMutationResult.CooldownActive:
                    TempData["ManualNudgeConflict"] =
                        "A manual prompt was already sent to this student within the last seven days. No additional prompt was sent.";
                    break;
                case NudgeMutationResult.NotFound:
                case NudgeMutationResult.NotEligible:
                    TempData["ManualNudgeConflict"] =
                        "That appointment is not eligible for a prompt in your workspace. The list has been refreshed.";
                    break;
                default:
                    TempData["ManualNudgeError"] =
                        "Choose one of the approved neutral prompt templates and try again.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not create a manual in-app support prompt for booking {BookingId}.", bookingId);
            TempData["ManualNudgeError"] =
                "The optional support prompt could not be sent. Nothing changed; please try again.";
        }

        return RedirectToAction(nameof(Reviews));
    }
}
