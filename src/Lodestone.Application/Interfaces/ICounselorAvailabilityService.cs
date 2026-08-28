using Lodestone.Application.DTOs.Counselor;

namespace Lodestone.Application.Interfaces;

public interface ICounselorAvailabilityService
{
    Task<CounselorAvailabilityPageDto?> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task PublishAsync(string userId, PublishAvailabilitySlotDto dto, CancellationToken cancellationToken = default);
    Task<AvailabilityRemovalResult> RemoveAsync(string userId, int slotId, CancellationToken cancellationToken = default);
}
