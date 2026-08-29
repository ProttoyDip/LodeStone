using Lodestone.Application.DTOs.Risk;

namespace Lodestone.Application.Interfaces;

public enum RiskQueueResolutionOutcome
{
    Resolved = 0,
    NotFound = 1,
    AlreadyResolved = 2,
    ConcurrencyConflict = 3
}

public interface ICounselorQueueRepository
{
    Task<IReadOnlyList<RiskQueueItemDto>> GetOpenAsync(CancellationToken cancellationToken = default);
    Task<RiskQueueResolutionOutcome> ResolveAsync(
        int queueEntryId,
        string resolvedByUserId,
        string? rowVersionToken,
        CancellationToken cancellationToken = default);
}
