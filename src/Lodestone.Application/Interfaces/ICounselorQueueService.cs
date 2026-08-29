using Lodestone.Application.DTOs.Risk;

namespace Lodestone.Application.Interfaces;

public interface ICounselorQueueService
{
    Task<IReadOnlyList<RiskQueueItemDto>> GetQueueAsync(CancellationToken cancellationToken = default);
    Task<RiskQueueResolutionOutcome> TryResolveAsync(
        int queueEntryId,
        string resolvedByUserId,
        string? rowVersionToken,
        CancellationToken cancellationToken = default);
}
