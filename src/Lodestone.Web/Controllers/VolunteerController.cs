using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Web.ViewModels.Volunteer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Roles = RoleConstants.Volunteer, Policy = PolicyConstants.CanProvidePeerSupport)]
[Route("Volunteer")]
public sealed class VolunteerController : Controller
{
    private readonly IVolunteerSupportService _supportService;
    private readonly ILogger<VolunteerController> _logger;

    public VolunteerController(
        IVolunteerSupportService supportService,
        ILogger<VolunteerController> logger)
    {
        _supportService = supportService;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));

    [HttpGet("Dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        try
        {
            var dashboard = await _supportService.GetVolunteerDashboardAsync(cancellationToken);
            ViewData["Title"] = "Volunteer dashboard";
            return View("~/Views/Volunteer/Dashboard.cshtml", new VolunteerDashboardViewModel
            {
                Dashboard = dashboard
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("Requests/{requestId:int}")]
    public async Task<IActionResult> ViewRequest(int requestId, CancellationToken cancellationToken)
    {
        if (requestId <= 0) return NotFound();
        var request = await _supportService.GetRequestForVolunteerAsync(requestId, cancellationToken);
        if (request is null) return NotFound();

        ViewData["Title"] = request.Title;
        return View("~/Views/Volunteer/ViewRequest.cshtml", BuildRequestViewModel(request));
    }

    [HttpPost("Requests/{requestId:int}/accept")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> AcceptRequest(int requestId, CancellationToken cancellationToken)
        => RunRequestMutationAsync(
            requestId,
            "Request accepted. It is now in Active support.",
            (id, token) => _supportService.AcceptRequestAsync(id, token),
            redirectToDetail: true,
            cancellationToken);

    [HttpPost("Requests/{requestId:int}/reject")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RejectRequest(int requestId, CancellationToken cancellationToken)
        => RunRequestMutationAsync(
            requestId,
            "Request removed from your pending list. It remains pending for another assigned volunteer.",
            (id, token) => _supportService.RejectRequestAsync(id, token),
            redirectToDetail: false,
            cancellationToken);

    [HttpPost("Requests/{requestId:int}/complete")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CompleteRequest(int requestId, CancellationToken cancellationToken)
        => RunRequestMutationAsync(
            requestId,
            "Support request marked complete.",
            (id, token) => _supportService.CompleteRequestAsync(id, token),
            redirectToDetail: false,
            cancellationToken);

    [HttpPost("Requests/{requestId:int}/interactions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddInteraction(
        int requestId,
        [Bind(Prefix = nameof(VolunteerViewRequestViewModel.Interaction))] VolunteerInteractionInputModel input,
        CancellationToken cancellationToken)
    {
        input.RequestId = requestId;
        if (!ModelState.IsValid)
        {
            TempData["SupportError"] = FirstModelError("Enter a valid guidance message.");
            return RedirectToAction(nameof(ViewRequest), new { requestId });
        }

        try
        {
            var interaction = await _supportService.AddInteractionAsync(
                requestId,
                input.Message,
                cancellationToken);
            TempData[interaction is null ? "SupportError" : "SupportSuccess"] = interaction is null
                ? "This request is no longer available for updates."
                : "Guidance message added.";
            return RedirectToAction(nameof(ViewRequest), new { requestId });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not add a peer-support interaction to request {RequestId}.", requestId);
            TempData["SupportError"] = "The guidance message could not be saved.";
            return RedirectToAction(nameof(ViewRequest), new { requestId });
        }
    }

    [HttpPost("Requests/{requestId:int}/escalate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EscalateRequest(
        int requestId,
        [Bind(Prefix = nameof(VolunteerViewRequestViewModel.Escalation))] VolunteerEscalationInputModel input,
        CancellationToken cancellationToken)
    {
        input.RequestId = requestId;
        if (!ModelState.IsValid)
        {
            TempData["SupportError"] = FirstModelError("Enter a valid escalation note.");
            return RedirectToAction(nameof(ViewRequest), new { requestId });
        }

        return await RunRequestMutationAsync(
            requestId,
            "Request escalated for counselor follow-up.",
            (id, token) => _supportService.EscalateRequestAsync(id, input.Message, token),
            redirectToDetail: false,
            cancellationToken);
    }

    private async Task<IActionResult> RunRequestMutationAsync(
        int requestId,
        string successMessage,
        Func<int, CancellationToken, Task<bool>> mutation,
        bool redirectToDetail,
        CancellationToken cancellationToken)
    {
        if (requestId <= 0) return NotFound();

        try
        {
            var updated = await mutation(requestId, cancellationToken);
            TempData[updated ? "SupportSuccess" : "SupportError"] = updated
                ? successMessage
                : "This request is no longer available for that action.";
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update peer-support request {RequestId}.", requestId);
            TempData["SupportError"] = "The support request could not be updated. Refresh and try again.";
        }

        return redirectToDetail
            ? RedirectToAction(nameof(ViewRequest), new { requestId })
            : RedirectToAction(nameof(Dashboard));
    }

    private string FirstModelError(string fallback)
        => ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
            ?? fallback;

    private static VolunteerViewRequestViewModel BuildRequestViewModel(
        Lodestone.Application.DTOs.Volunteer.SupportRequestDto request)
        => new()
        {
            Request = request,
            Interaction = new VolunteerInteractionInputModel { RequestId = request.Id },
            Escalation = new VolunteerEscalationInputModel { RequestId = request.Id }
        };
}
