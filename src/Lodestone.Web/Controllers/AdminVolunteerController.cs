using System.Text;
using System.Text.Encodings.Web;
using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Infrastructure.Email;
using Lodestone.Web.Services;
using Lodestone.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Lodestone.Web.Controllers;

[Authorize(Roles = RoleConstants.Admin, Policy = PolicyConstants.CanManageVolunteers)]
[Route("Admin/Volunteers")]
public sealed class AdminVolunteerController : Controller
{
    private readonly IVolunteerSupportService _volunteerSupportService;
    private readonly IVolunteerProvisioningService _volunteerProvisioningService;
    private readonly IAdminDashboardService _adminDashboardService;
    private readonly IEmailService _emailService;
    private readonly IPublicAccountLinkBuilder _publicAccountLinkBuilder;
    private readonly ILogger<AdminVolunteerController> _logger;

    public AdminVolunteerController(
        IVolunteerSupportService volunteerSupportService,
        IVolunteerProvisioningService volunteerProvisioningService,
        IAdminDashboardService adminDashboardService,
        IEmailService emailService,
        IPublicAccountLinkBuilder publicAccountLinkBuilder,
        ILogger<AdminVolunteerController> logger)
    {
        _volunteerSupportService = volunteerSupportService;
        _volunteerProvisioningService = volunteerProvisioningService;
        _adminDashboardService = adminDashboardService;
        _emailService = emailService;
        _publicAccountLinkBuilder = publicAccountLinkBuilder;
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

    [HttpGet("invite")]
    public async Task<IActionResult> Invite(CancellationToken cancellationToken)
    {
        await SetAdminShellAsync("Invite volunteer", cancellationToken);
        return View("~/Views/Admin/InviteVolunteer.cshtml", new InviteVolunteerViewModel());
    }

    [HttpPost("invite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(InviteVolunteerViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await SetAdminShellAsync("Invite volunteer", cancellationToken);
            return View("~/Views/Admin/InviteVolunteer.cshtml", model);
        }

        var result = await _volunteerProvisioningService.InviteAsync(
            new InviteVolunteerDto(model.Email),
            cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            await SetAdminShellAsync("Invite volunteer", cancellationToken);
            return View("~/Views/Admin/InviteVolunteer.cshtml", model);
        }

        var sent = await SendVolunteerSetupEmailAsync(result, cancellationToken);
        TempData[sent ? "AdminSuccess" : "AdminError"] = sent
            ? "Invitation sent. The volunteer will appear here once they set a password and complete their profile."
            : "The account was created, but the invitation email could not be sent. Use Resend invitation.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("resend-setup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendSetup(string email, CancellationToken cancellationToken)
    {
        var result = await _volunteerProvisioningService.CreateSetupTokenAsync(
            email ?? string.Empty,
            cancellationToken);
        if (!result.Succeeded)
        {
            TempData["AdminError"] = result.Errors.FirstOrDefault() ?? "The setup link could not be generated.";
            return RedirectToAction(nameof(Index));
        }

        var sent = await SendVolunteerSetupEmailAsync(result, cancellationToken);
        TempData[sent ? "AdminSuccess" : "AdminError"] = sent
            ? "A new invitation was sent."
            : "The invitation email could not be sent. Try again later.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{volunteerProfileId:int}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(
        int volunteerProfileId,
        int? replacementVolunteerProfileId,
        string? q,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _volunteerProvisioningService.RemoveAsync(
                volunteerProfileId,
                replacementVolunteerProfileId,
                cancellationToken);

            if (result.RequiresReplacement)
            {
                TempData["AdminError"] =
                    $"This volunteer is handling {result.TransferredItems} support " +
                    $"{(result.TransferredItems == 1 ? "request" : "requests")}. " +
                    "Choose another volunteer to take them over before removing the account.";
            }
            else if (!result.Succeeded)
            {
                TempData["AdminError"] = result.Errors.FirstOrDefault() ?? "The volunteer could not be removed.";
            }
            else
            {
                TempData["AdminSuccess"] = result.TransferredItems == 0
                    ? "Volunteer removed."
                    : $"Volunteer removed and {result.TransferredItems} support " +
                      $"{(result.TransferredItems == 1 ? "request was" : "requests were")} moved to the chosen volunteer.";
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not remove volunteer {VolunteerProfileId}.", volunteerProfileId);
            TempData["AdminError"] = "The volunteer could not be removed.";
        }

        return RedirectToAction(nameof(Index), new { q });
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

    private async Task<bool> SendVolunteerSetupEmailAsync(
        VolunteerProvisioningResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.Email) || string.IsNullOrWhiteSpace(result.PasswordSetupToken))
            return false;

        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(result.PasswordSetupToken));
        var resetUrl = _publicAccountLinkBuilder.BuildPasswordResetUrl(result.Email, token);
        var safeUrl = HtmlEncoder.Default.Encode(resetUrl);
        var body = EmailTemplate.Wrap(
            EmailTemplate.Heading("You have been invited to volunteer")
            + EmailTemplate.Para("An administrator invited you to provide peer support on Lodestone. Choose a password, then tell us a little about yourself so your profile can be approved.")
            + EmailTemplate.Button(safeUrl, "Set password")
            + EmailTemplate.SmallMuted("If you were not expecting this invitation, contact your Lodestone administrator."),
            "You have been invited to volunteer on Lodestone");

        try
        {
            await _emailService.SendAsync(
                result.Email,
                "You have been invited to volunteer on Lodestone",
                body,
                cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            // The account exists and is usable; only the invitation failed, and it can be resent.
            _logger.LogWarning(exception, "Failed to send a volunteer account setup email.");
            return false;
        }
    }

    private async Task SetAdminShellAsync(string title, CancellationToken cancellationToken)
    {
        ViewData["AdminShell"] = await _adminDashboardService.GetShellAsync(cancellationToken);
        ViewData["AdminActiveSection"] = AdminSectionType.Volunteers.ToString();
        ViewData["Title"] = title;
    }
}
