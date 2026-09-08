using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Enums;
using Lodestone.Web.ViewModels.Student;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Roles = RoleConstants.Student, Policy = PolicyConstants.CanRequestPeerSupport)]
[Route("Student")]
public sealed class StudentSupportController : Controller
{
    private readonly IVolunteerSupportService _supportService;
    private readonly IPeerChatService _chat;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<StudentSupportController> _logger;

    public StudentSupportController(
        IVolunteerSupportService supportService,
        IPeerChatService chat,
        ICurrentUserService currentUser,
        ILogger<StudentSupportController> logger)
    {
        _supportService = supportService;
        _chat = chat;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("RequestSupport")]
    public IActionResult RequestSupport()
    {
        ViewData["Title"] = "Request peer support";
        return View("~/Views/Student/RequestSupport.cshtml", new RequestSupportViewModel());
    }

    [HttpPost("RequestSupport")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestSupport(
        RequestSupportViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Request peer support";
            return View("~/Views/Student/RequestSupport.cshtml", model);
        }

        try
        {
            await _supportService.CreateSupportRequestAsync(
                new CreateSupportRequestDto(model.Category!.Value, model.Message, model.Availability),
                cancellationToken);
            TempData["SupportSuccess"] = "Your support request is pending. An assigned volunteer can now review it.";
            return RedirectToAction(nameof(MyRequests));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not create a peer-support request.");
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewData["Title"] = "Request peer support";
            return View("~/Views/Student/RequestSupport.cshtml", model);
        }
    }

    [HttpGet("MyRequests")]
    public async Task<IActionResult> MyRequests(CancellationToken cancellationToken)
    {
        var requests = await _supportService.GetRequestsForStudentAsync(cancellationToken);
        var volunteers = await _supportService.GetAssignedVolunteersForStudentAsync(cancellationToken);
        ViewData["Title"] = "My support requests";
        return View("~/Views/Student/MyRequests.cshtml", new StudentSupportRequestsViewModel
        {
            AssignedVolunteers = volunteers,
            Pending = requests.Where(request => request.Status == SupportRequestStatus.Pending).ToList().AsReadOnly(),
            Active = requests.Where(request => request.Status == SupportRequestStatus.Accepted).ToList().AsReadOnly(),
            History = requests
                .Where(request => request.Status is SupportRequestStatus.Completed or SupportRequestStatus.Escalated)
                .ToList()
                .AsReadOnly()
        });
    }

    [HttpPost("StartConversation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartConversation(int volunteerProfileId, CancellationToken cancellationToken)
    {
        if (volunteerProfileId <= 0) return NotFound();

        try
        {
            var requestId = await _supportService.StartConversationWithVolunteerAsync(volunteerProfileId, cancellationToken);
            return RedirectToAction(nameof(ViewRequest), new { requestId });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not start a direct peer-support conversation.");
            TempData["SupportError"] = ex.Message;
            return RedirectToAction(nameof(MyRequests));
        }
    }

    [HttpGet("ViewRequest/{requestId:int}")]
    public async Task<IActionResult> ViewRequest(int requestId, CancellationToken cancellationToken)
    {
        if (requestId <= 0) return NotFound();
        var request = await _supportService.GetRequestForStudentAsync(requestId, cancellationToken);
        if (request is null) return NotFound();

        ViewData["Title"] = request.Title;
        return View("~/Views/Student/ViewRequest.cshtml", request);
    }

    /// <summary>No-script fallback for the live conversation; the hub does the same work.</summary>
    [HttpPost("ViewRequest/{requestId:int}/reply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int requestId, string? message, CancellationToken cancellationToken)
    {
        if (requestId <= 0) return NotFound();
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();

        try
        {
            await _chat.PostMessageAsync(userId, requestId, message ?? string.Empty, cancellationToken);
            TempData["SupportSuccess"] = "Your message was sent.";
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["SupportError"] = ex.Message;
        }

        return RedirectToAction(nameof(ViewRequest), new { requestId });
    }
}
