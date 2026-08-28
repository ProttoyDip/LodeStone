using Lodestone.Application.DTOs.Counselor;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Roles = RoleConstants.Counselor)]
[Route("Counselor/Availability")]
public sealed class CounselorAvailabilityController : Controller
{
    private readonly ICounselorAvailabilityService _availability;
    private readonly ICurrentUserService _currentUser;

    public CounselorAvailabilityController(ICounselorAvailabilityService availability, ICurrentUserService currentUser)
        => (_availability, _currentUser) = (availability, currentUser);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Challenge();
        var page = await _availability.GetAsync(_currentUser.UserId, cancellationToken);
        return page is null ? Forbid() : View(page);
    }

    [HttpPost("Publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(PublishAvailabilitySlotDto model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Challenge();
        try
        {
            await _availability.PublishAsync(_currentUser.UserId, model, cancellationToken);
            TempData["Success"] = "Availability published.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Challenge();
        var result = await _availability.RemoveAsync(_currentUser.UserId, id, cancellationToken);
        TempData[result == AvailabilityRemovalResult.Removed ? "Success" : "Error"] = result switch
        {
            AvailabilityRemovalResult.Removed => "Availability removed.",
            AvailabilityRemovalResult.Booked => "A booked appointment cannot be removed.",
            _ => "That availability was not found."
        };
        return RedirectToAction(nameof(Index));
    }
}
