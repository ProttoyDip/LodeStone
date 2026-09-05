using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.DTOs.Counselor;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Interfaces;

/// <summary>Booking-specific queries. Implemented in Infrastructure.</summary>
public interface IBookingRepository
{
    Task<IReadOnlyList<CounselorBooking>> GetByStudentIdAsync(int studentProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorBooking>> GetByCounselorIdAsync(int counselorProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorBooking>> GetCounselorWorkspaceAsync(int counselorProfileId, DateTime recentFromUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorAvailabilitySlot>> GetAvailableSlotsAsync(int? counselorProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorProfile>> GetAllCounselorsAsync(CancellationToken cancellationToken = default);
    Task<CounselorBooking?> TryCreateConfirmedAsync(int studentProfileId, int slotId, string? notes, CancellationToken cancellationToken = default);
    Task<BookingCancellationResult> CancelOwnedAsync(int studentProfileId, int bookingId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Confirmed bookings starting inside the window that have not had a reminder sent, with the
    /// student and counselor loaded so a reminder can be addressed and described.
    /// </summary>
    Task<IReadOnlyList<CounselorBooking>> GetBookingsDueForReminderAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps the reminder as sent. Returns false when another worker stamped it first, so a
    /// concurrent sweep cannot send a second email for the same session.
    /// </summary>
    Task<bool> TryMarkReminderSentAsync(
        int bookingId,
        DateTime sentAtUtc,
        CancellationToken cancellationToken = default);

    Task<CounselorProfile?> GetCounselorByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorAvailabilitySlot>> GetCounselorSlotsAsync(int counselorProfileId, CancellationToken cancellationToken = default);
    Task<bool> HasOverlappingSlotAsync(int counselorProfileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
    Task AddSlotAsync(CounselorAvailabilitySlot slot, CancellationToken cancellationToken = default);
    Task<AvailabilityRemovalResult> RemoveOwnedSlotAsync(int counselorProfileId, int slotId, CancellationToken cancellationToken = default);
    Task<CounselorBookingUpdateResult> RecordCounselorOutcomeAsync(
        int counselorProfileId,
        string counselorUserId,
        int bookingId,
        BookingStatus outcome,
        string? sessionNotes,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
