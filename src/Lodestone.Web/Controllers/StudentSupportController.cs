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
    private readonly ILogger<StudentSupportController> _logger;

    public StudentSupportController(
        IVolunteerSupportService supportService,
        ILogger<StudentSupportController> logger)
    {
        _supportService = supportService;
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
        ViewData["Title"] = "My support requests";
        return View("~/Views/Student/MyRequests.cshtml", new StudentSupportRequestsViewModel
        {
            Pending = requests.Where(request => request.Status == SupportRequestStatus.Pending).ToList().AsReadOnly(),
            Active = requests.Where(request => request.Status == SupportRequestStatus.Accepted).ToList().AsReadOnly(),
            History = requests
                .Where(request => request.Status is SupportRequestStatus.Completed or SupportRequestStatus.Escalated)
                .ToList()
                .AsReadOnly()
        });
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
}
