using Lodestone.Application.DTOs.Risk;
using Lodestone.Domain.Entities;

namespace Lodestone.Application.Interfaces;

/// <summary>What an explanation needs from storage: the scored snapshot and a comparison group.</summary>
/// <param name="Snapshot">The snapshot the queued score was computed from.</param>
/// <param name="Population">
/// Most recent snapshot per consenting student in the same schema. Used only to compute median
/// baseline values; nothing about any individual in it is surfaced.
/// </param>
public sealed record RiskExplanationContext(
    RiskQueueItemDto QueueItem,
    RiskFeatureSnapshot Snapshot,
    IReadOnlyList<RiskFeatureSnapshot> Population);

/// <summary>
/// Explains, on demand, why an open queue entry carries the score it does.
/// </summary>
/// <remarks>
/// Nothing computed here is persisted. The caller must already be authorized to see the queue;
/// the service additionally requires that the student still consents to monitoring, so withdrawing
/// consent also withdraws the ability to explain.
/// </remarks>
public interface IRiskExplanationService
{
    Task<RiskQueueExplanationDto?> ExplainQueueEntryAsync(int queueEntryId, CancellationToken cancellationToken = default);
}

/// <summary>An open queue entry alongside its explanation, or the reason none could be produced.</summary>
public sealed record RiskQueueExplanationDto(
    RiskQueueItemDto QueueItem,
    RiskExplanation? Explanation,
    string? UnavailableReason)
{
    /// <summary>
    /// What the model leans on across everyone, not just this student. Lets a counselor see whether
    /// a factor that moved this score is one the model generally trusts or an outlier. Empty when
    /// neither training metadata nor a large enough population can supply it.
    /// </summary>
    public IReadOnlyList<RiskFeatureImportance> ModelImportance { get; init; } = Array.Empty<RiskFeatureImportance>();

    /// <summary>Where <see cref="ModelImportance"/> came from, in counselor-readable words.</summary>
    public string? ModelImportanceSource { get; init; }
}
