using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Roles = RoleConstants.Admin, Policy = PolicyConstants.CanManageVolunteers)]
[Route("Admin/Volunteers")]
public sealed class AdminVolunteerController : Controller
{
    private readonly IVolunteerSupportService _volunteerSupportService;
    private readonly IAdminDashboardService _adminDashboardService;
    private readonly ILogger<AdminVolunteerController> _logger;

    public AdminVolunteerController(
        IVolunteerSupportService volunteerSupportService,
        IAdminDashboardService adminDashboardService,
        ILogger<AdminVolunteerController> logger)
    {
        _volunteerSupportService = volunteerSupportService;
        _adminDashboardService = adminDashboardService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        await SetAdminShellAsync("Volunteers", cancellationToken);
        var overview = await _volunteerSupportService.GetAdminOverviewAsync(q, cancellationToken);
        return View("~/Views/Admin/Volunteers.cshtml", new AdminVolunteerIndexViewModel
        {
            Overview = overview,
            Query = q?.Trim()
        });
    }

    [HttpGet("{volunteerProfileId:int}/assign")]
    public async Task<IActionResult> Assign(int volunteerProfileId, CancellationToken cancellationToken)
    {
        if (volunteerProfileId <= 0) return NotFound();
        var options = await _volunteerSupportService.GetAssignmentOptionsAsync(volunteerProfileId, cancellationToken);
        if (options is null) return NotFound();

        await SetAdminShellAsync("Assign volunteer", cancellationToken);
        return View("~/Views/Admin/AssignVolunteer.cshtml", new AdminVolunteerAssignmentViewModel
        {
            Options = options,
            Input = new VolunteerAssignmentInputModel
            {
                VolunteerProfileId = volunteerProfileId,
                Target = VolunteerAssignmentTarget.Student,
                Role = "Peer Mentor"
            }
        });
    }

    [HttpPost("{volunteerProfileId:int}/assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        int volunteerProfileId,
        [Bind(Prefix = nameof(AdminVolunteerAssignmentViewModel.Input))] VolunteerAssignmentInputModel input,
        CancellationToken cancellationToken)
    {
        input.VolunteerProfileId = volunteerProfileId;
        if (!ModelState.IsValid)
            return await RenderAssignmentAsync(input, cancellationToken);

        try
        {
            var result = await _volunteerSupportService.AssignVolunteerAsync(
                new CreateVolunteerAssignmentDto(
                    volunteerProfileId,
                    input.Target!.Value,
                    input.StudentProfileId,
                    input.Program,
                    input.EnrollmentYear,
                    input.Role,
                    input.Notes),
                cancellationToken);

            TempData["AdminSuccess"] = result.TargetedStudents == 1
                ? "Volunteer assignment saved."
                : $"Volunteer assignment saved for {result.TargetedStudents} students.";
            return RedirectToAction(nameof(Assign), new { volunteerProfileId });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RenderAssignmentAsync(input, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RenderAssignmentAsync(input, cancellationToken);
        }
    }

    [HttpPost("{volunteerId:int}/approve")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(int volunteerId, string? q, CancellationToken cancellationToken)
        => RunVolunteerMutationAsync(
            volunteerId,
            q,
            "Volunteer approved.",
            (id, token) => _volunteerSupportService.ApproveVolunteerAsync(id, token),
            cancellationToken);

    [HttpPost("{volunteerId:int}/reject")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(int volunteerId, string? q, CancellationToken cancellationToken)
        => RunVolunteerMutationAsync(
            volunteerId,
            q,
            "Volunteer application rejected.",
            (id, token) => _volunteerSupportService.RejectVolunteerAsync(id, token),
            cancellationToken);

    [HttpPost("{volunteerId:int}/activate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Activate(int volunteerId, string? q, CancellationToken cancellationToken)
        => RunVolunteerMutationAsync(
            volunteerId,
            q,
            "Volunteer support access activated.",
            (id, token) => _volunteerSupportService.SetVolunteerActiveAsync(id, true, token),
            cancellationToken);

    [HttpPost("{volunteerId:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(int volunteerId, string? q, CancellationToken cancellationToken)
        => RunVolunteerMutationAsync(
            volunteerId,
            q,
            "Volunteer support access deactivated.",
            (id, token) => _volunteerSupportService.SetVolunteerActiveAsync(id, false, token),
            cancellationToken);

    [HttpPost("{volunteerProfileId:int}/assignments/{assignmentId:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateAssignment(
        int volunteerProfileId,
        int assignmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _volunteerSupportService.DeactivateAssignmentAsync(
                volunteerProfileId,
                assignmentId,
                cancellationToken);
            TempData[updated ? "AdminSuccess" : "AdminError"] = updated
                ? "Assignment deactivated."
                : "That assignment was not found.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not deactivate volunteer assignment {AssignmentId}.", assignmentId);
            TempData["AdminError"] = "The assignment could not be changed.";
        }

        return RedirectToAction(nameof(Assign), new { volunteerProfileId });
    }

    private async Task<IActionResult> RenderAssignmentAsync(
        VolunteerAssignmentInputModel input,
        CancellationToken cancellationToken)
    {
        var options = await _volunteerSupportService.GetAssignmentOptionsAsync(
            input.VolunteerProfileId,
            cancellationToken);
        if (options is null) return NotFound();

        await SetAdminShellAsync("Assign volunteer", cancellationToken);
        return View("~/Views/Admin/AssignVolunteer.cshtml", new AdminVolunteerAssignmentViewModel
        {
            Options = options,
            Input = input
        });
    }

    private async Task<IActionResult> RunVolunteerMutationAsync(
        int volunteerId,
        string? query,
        string successMessage,
        Func<int, CancellationToken, Task<bool>> mutation,
        CancellationToken cancellationToken)
    {
        if (volunteerId <= 0) return NotFound();

        try
        {
            var updated = await mutation(volunteerId, cancellationToken);
            TempData[updated ? "AdminSuccess" : "AdminError"] = updated
                ? successMessage
                : "That volunteer was not found.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["AdminError"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update volunteer {VolunteerId}.", volunteerId);
            TempData["AdminError"] = "The volunteer could not be updated.";
        }

        return RedirectToAction(nameof(Index), new { q = query });
    }

    private async Task SetAdminShellAsync(string title, CancellationToken cancellationToken)
    {
        ViewData["AdminShell"] = await _adminDashboardService.GetShellAsync(cancellationToken);
        ViewData["AdminActiveSection"] = AdminSectionType.Volunteers.ToString();
        ViewData["Title"] = title;
    }
}
