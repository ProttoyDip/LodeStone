using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.DTOs.Counselor;
using Lodestone.Domain.Entities;

namespace Lodestone.Application.Interfaces;

/// <summary>Booking-specific queries. Implemented in Infrastructure.</summary>
public interface IBookingRepository
{
    Task<IReadOnlyList<CounselorBooking>> GetByStudentIdAsync(int studentProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorBooking>> GetByCounselorIdAsync(int counselorProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorAvailabilitySlot>> GetAvailableSlotsAsync(int? counselorProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorProfile>> GetAllCounselorsAsync(CancellationToken cancellationToken = default);
    Task<CounselorBooking?> TryCreateConfirmedAsync(int studentProfileId, int slotId, string? notes, CancellationToken cancellationToken = default);
    Task<BookingCancellationResult> CancelOwnedAsync(int studentProfileId, int bookingId, CancellationToken cancellationToken = default);
    Task<CounselorProfile?> GetCounselorByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CounselorAvailabilitySlot>> GetCounselorSlotsAsync(int counselorProfileId, CancellationToken cancellationToken = default);
    Task<bool> HasOverlappingSlotAsync(int counselorProfileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
    Task AddSlotAsync(CounselorAvailabilitySlot slot, CancellationToken cancellationToken = default);
    Task<AvailabilityRemovalResult> RemoveOwnedSlotAsync(int counselorProfileId, int slotId, CancellationToken cancellationToken = default);
}
