using Lodestone.Application.DTOs.Booking;

namespace Lodestone.Web.ViewModels.Booking;

public sealed class BookingIndexViewModel
{
    public IReadOnlyList<BookingDto> Upcoming { get; init; } = Array.Empty<BookingDto>();
    public IReadOnlyList<BookingDto> History { get; init; } = Array.Empty<BookingDto>();
}

public sealed class BookingCreateViewModel
{
    public IReadOnlyList<CounselorSummaryDto> Counselors { get; init; } = Array.Empty<CounselorSummaryDto>();
    public IReadOnlyList<BookingSlotDto> Slots { get; init; } = Array.Empty<BookingSlotDto>();
    public int? SelectedCounselorId { get; init; }
    public CreateBookingDto NewBooking { get; init; } = new(0, null);
}
