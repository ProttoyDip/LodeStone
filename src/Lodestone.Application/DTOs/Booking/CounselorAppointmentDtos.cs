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
    bool CanRecordOutcome)
{
    /// <summary>
    /// A template opening for the session note, built by <c>SessionReportDrafter</c> from the
    /// booking record alone. Offered into an empty notes box; never saved unless the counselor
    /// submits it.
    /// </summary>
    public string? SuggestedSessionNotes { get; init; }
}

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
