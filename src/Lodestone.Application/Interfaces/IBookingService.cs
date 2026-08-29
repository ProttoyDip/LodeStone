using Lodestone.Application.DTOs.Booking;

namespace Lodestone.Application.Interfaces;

public interface IBookingService
{
    Task<BookingDto> CreateBookingAsync(int studentProfileId, CreateBookingDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingDto>> GetStudentBookingsAsync(int studentProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingDto>> GetUpcomingAsync(int counselorProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorSummaryDto>> GetCounselorsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingSlotDto>> GetAvailableSlotsAsync(int? counselorProfileId = null, CancellationToken cancellationToken = default);
    Task<BookingCancellationResult> CancelAsync(int studentProfileId, int bookingId, CancellationToken cancellationToken = default);
}
