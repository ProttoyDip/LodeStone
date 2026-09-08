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

    /// <summary>
    /// The open entry, its scored snapshot, and a comparison population of the most recent snapshot
    /// per consenting student in the same schema. Null when the entry is missing, resolved, or the
    /// student no longer consents to monitoring.
    /// </summary>
    Task<RiskExplanationContext?> GetExplanationContextAsync(
        int queueEntryId,
        DateTime asOfUtc,
        int maximumPopulationAgeDays,
        CancellationToken cancellationToken = default);

    Task<RiskQueueResolutionOutcome> ResolveAsync(
        int queueEntryId,
        string resolvedByUserId,
        string? rowVersionToken,
        CancellationToken cancellationToken = default);
}
