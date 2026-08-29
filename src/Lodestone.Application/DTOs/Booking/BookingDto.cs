using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Booking;

public record BookingDto(
    int Id,
    int CounselorProfileId,
    string CounselorName,
    string? Specialization,
    DateTime StartUtc,
    DateTime EndUtc,
    BookingStatus Status,
    bool CanCancel);

public record CreateBookingDto(int AvailabilitySlotId, string? Notes);

public record BookingSlotDto(
    int Id,
    int CounselorProfileId,
    string CounselorName,
    string? Specialization,
    DateTime StartUtc,
    DateTime EndUtc);

public enum BookingCancellationResult
{
    Cancelled,
    NotFound,
    NotCancellable
}
