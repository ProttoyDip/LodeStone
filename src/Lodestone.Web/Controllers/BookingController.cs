using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.Exceptions;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Enums;
using Lodestone.Web.ViewModels.Booking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Roles = RoleConstants.Student)]
public class BookingController : Controller
{
    private readonly IBookingService _bookings;
    private readonly ICurrentUserService _currentUser;
    private readonly IStudentProfileRepository _students;

    public BookingController(IBookingService bookings, ICurrentUserService currentUser, IStudentProfileRepository students)
        => (_bookings, _currentUser, _students) = (bookings, currentUser, students);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var studentId = await GetStudentIdAsync(cancellationToken);
        if (!studentId.HasValue) return Forbid();
        var all = await _bookings.GetStudentBookingsAsync(studentId.Value, cancellationToken);
        var nowUtc = DateTime.UtcNow;
        return View(new BookingIndexViewModel
        {
            Upcoming = all.Where(item => item.Status == BookingStatus.Confirmed && item.StartUtc > nowUtc).OrderBy(item => item.StartUtc).ToList(),
            History = all.Where(item => item.Status != BookingStatus.Confirmed || item.StartUtc <= nowUtc).OrderByDescending(item => item.StartUtc).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? counselorId, CancellationToken cancellationToken)
        => View(await BuildCreateViewModelAsync(counselorId, null, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateBookingDto model, int? counselorId, CancellationToken cancellationToken)
    {
        var studentId = await GetStudentIdAsync(cancellationToken);
        if (!studentId.HasValue) return Forbid();
        if (model.AvailabilitySlotId <= 0) ModelState.AddModelError(nameof(model.AvailabilitySlotId), "Select an available time.");
        if (model.Notes?.Length > 1000) ModelState.AddModelError(nameof(model.Notes), "Notes cannot exceed 1,000 characters.");
        if (!ModelState.IsValid) return View(await BuildCreateViewModelAsync(counselorId, model, cancellationToken));

        try
        {
            await _bookings.CreateBookingAsync(studentId.Value, model, cancellationToken);
            TempData["Success"] = "Your counselor appointment is confirmed.";
            return RedirectToAction(nameof(Index));
        }
        catch (BookingSlotUnavailableException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildCreateViewModelAsync(counselorId, model, cancellationToken));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        var studentId = await GetStudentIdAsync(cancellationToken);
        if (!studentId.HasValue) return Forbid();
        var result = await _bookings.CancelAsync(studentId.Value, id, cancellationToken);
        if (result == BookingCancellationResult.NotFound) return NotFound();
        TempData[result == BookingCancellationResult.Cancelled ? "Success" : "Error"] = result == BookingCancellationResult.Cancelled
            ? "Your appointment was cancelled and the time is available again."
            : "This appointment can no longer be cancelled.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<BookingCreateViewModel> BuildCreateViewModelAsync(int? counselorId, CreateBookingDto? model, CancellationToken cancellationToken)
    {
        var counselors = await _bookings.GetCounselorsAsync(cancellationToken);
        var selected = counselorId ?? counselors.FirstOrDefault()?.Id;
        return new BookingCreateViewModel
        {
            Counselors = counselors,
            SelectedCounselorId = selected,
            Slots = selected.HasValue ? await _bookings.GetAvailableSlotsAsync(selected, cancellationToken) : Array.Empty<BookingSlotDto>(),
            NewBooking = model ?? new CreateBookingDto(0, null)
        };
    }

    private async Task<int?> GetStudentIdAsync(CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(_currentUser.UserId) ? null : await _students.GetIdByUserIdAsync(_currentUser.UserId, cancellationToken);
}
