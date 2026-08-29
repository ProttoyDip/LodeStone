using Lodestone.Application.DTOs.Counselor;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;

namespace Lodestone.Application.Services;

public sealed class CounselorAvailabilityService : ICounselorAvailabilityService
{
    private readonly IBookingRepository _bookings;
    private readonly IAuditLogService _audit;
    private readonly IUnitOfWork _unitOfWork;

    public CounselorAvailabilityService(IBookingRepository bookings, IAuditLogService audit, IUnitOfWork unitOfWork)
        => (_bookings, _audit, _unitOfWork) = (bookings, audit, unitOfWork);

    public async Task<CounselorAvailabilityPageDto?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var counselor = await _bookings.GetCounselorByUserIdAsync(userId, cancellationToken);
        if (counselor is null) return null;
        var slots = await _bookings.GetCounselorSlotsAsync(counselor.Id, cancellationToken);
        return new CounselorAvailabilityPageDto(
            string.IsNullOrWhiteSpace(counselor.User?.FullName) ? "Counselor" : counselor.User.FullName,
            slots.Select(s => new AvailabilitySlotDto(s.Id, s.CounselorProfileId, s.StartUtc, s.EndUtc, s.IsBooked)).ToList().AsReadOnly());
    }

    public async Task PublishAsync(string userId, PublishAvailabilitySlotDto dto, CancellationToken cancellationToken = default)
    {
        var counselor = await _bookings.GetCounselorByUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Counselor profile not found.");
        var startUtc = dto.StartUtc.ToUniversalTime();
        var endUtc = dto.EndUtc.ToUniversalTime();
        var duration = endUtc - startUtc;
        if (startUtc <= DateTime.UtcNow || duration < TimeSpan.FromMinutes(30) || duration > TimeSpan.FromMinutes(120))
            throw new ArgumentException("Availability must be in the future and between 30 and 120 minutes.", nameof(dto));
        if (await _bookings.HasOverlappingSlotAsync(counselor.Id, startUtc, endUtc, cancellationToken))
            throw new InvalidOperationException("This availability overlaps an existing slot.");

        await _bookings.AddSlotAsync(new CounselorAvailabilitySlot
        {
            CounselorProfileId = counselor.Id,
            StartUtc = startUtc,
            EndUtc = endUtc,
            CreatedAtUtc = DateTime.UtcNow
        }, cancellationToken);
        _audit.Record("AvailabilityPublished", nameof(CounselorAvailabilitySlot), details: $"CounselorProfileId={counselor.Id};StartUtc={startUtc:O};EndUtc={endUtc:O}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<AvailabilityRemovalResult> RemoveAsync(string userId, int slotId, CancellationToken cancellationToken = default)
    {
        var counselor = await _bookings.GetCounselorByUserIdAsync(userId, cancellationToken);
        if (counselor is null) return AvailabilityRemovalResult.NotFound;
        var result = await _bookings.RemoveOwnedSlotAsync(counselor.Id, slotId, cancellationToken);
        if (result == AvailabilityRemovalResult.Removed)
        {
            _audit.Record("AvailabilityRemoved", nameof(CounselorAvailabilitySlot), slotId.ToString());
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return result;
    }
}
