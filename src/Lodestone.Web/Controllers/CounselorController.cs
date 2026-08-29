using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Web.ViewModels.Counselor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Policy = PolicyConstants.CanViewRiskQueue)]
public class CounselorController : Controller
{
    private readonly ICounselorQueueService _queueService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CounselorController> _logger;

    public CounselorController(
        ICounselorQueueService queueService,
        ICurrentUserService currentUserService,
        ILogger<CounselorController> logger)
        => (_queueService, _currentUserService, _logger) = (queueService, currentUserService, logger);

    [HttpGet]
    public async Task<IActionResult> Queue(CancellationToken cancellationToken)
    {
        try
        {
            var items = await _queueService.GetQueueAsync(cancellationToken);
            return View(new CounselorQueueViewModel
            {
                Items = items,
                RefreshedAtUtc = DateTime.UtcNow
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

    public IActionResult Reviews() => View();
}
