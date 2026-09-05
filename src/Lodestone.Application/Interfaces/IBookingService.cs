using Lodestone.Application.DTOs.Booking;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Interfaces;

public interface IBookingService
{
    Task<BookingDto> CreateBookingAsync(int studentProfileId, CreateBookingDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingDto>> GetStudentBookingsAsync(int studentProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingDto>> GetUpcomingAsync(int counselorProfileId, CancellationToken cancellationToken = default);
    Task<CounselorAppointmentsPageDto?> GetCounselorAppointmentsAsync(string counselorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorSummaryDto>> GetCounselorsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingSlotDto>> GetAvailableSlotsAsync(int? counselorProfileId = null, CancellationToken cancellationToken = default);
    Task<BookingCancellationResult> CancelAsync(int studentProfileId, int bookingId, CancellationToken cancellationToken = default);
    Task<CounselorBookingUpdateResult> RecordCounselorOutcomeAsync(
        string counselorUserId,
        int bookingId,
        BookingStatus outcome,
        string? sessionNotes,
        CancellationToken cancellationToken = default);
}
