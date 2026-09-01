using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Booking;

public sealed record CounselorAppointmentDto(
    int BookingId,
    string StudentName,
    string? StudentNumber,
    DateTime StartUtc,
    DateTime EndUtc,
    BookingStatus Status,
    string? RequestNotes,
    string? SessionNotes,
    bool CanRecordOutcome);

public sealed record CounselorAppointmentsPageDto(
    string CounselorName,
    IReadOnlyList<CounselorAppointmentDto> AwaitingOutcome,
    IReadOnlyList<CounselorAppointmentDto> Upcoming,
    IReadOnlyList<CounselorAppointmentDto> Recent);

public enum CounselorBookingUpdateResult
{
    Updated,
    NotFound,
    NotEligible,
    InvalidRequest
}
