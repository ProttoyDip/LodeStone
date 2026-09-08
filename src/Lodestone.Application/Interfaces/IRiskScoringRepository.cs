using Lodestone.Application.DTOs.Risk;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Interfaces;

public enum RiskScorePersistenceOutcome
{
    Created = 0,
    AlreadyExists = 1,
    NotEligible = 2
}

public sealed record RiskScorePersistenceResult(
    RiskScorePersistenceOutcome Outcome,
    RiskScore? RiskScore,
    bool QueueCreated,
    bool QueueEscalated);

public interface IRiskScoringRepository
{
    Task<RiskScoringRun> StartRunAsync(
        RiskModelDescriptor descriptor,
        int candidateCount,
        string? actorUserId,
        CancellationToken cancellationToken = default);
    Task CompleteRunAsync(RiskScoringRun run, CancellationToken cancellationToken = default);
    Task<RiskScoringRun?> GetLatestRunAsync(CancellationToken cancellationToken = default);
    Task<RiskScoringRun?> GetRunByKeyAsync(Guid runKey, CancellationToken cancellationToken = default);

    /// <summary>Every score a run produced, with whether it opened or escalated a counselor case. Audited per export.</summary>
    Task<IReadOnlyList<RiskScoringRunRowDto>> GetRunRowsAsync(int runId, string actorUserId, CancellationToken cancellationToken = default);
    Task<RiskScorePersistenceResult> PersistAsync(
        RiskFeatureSnapshot snapshot,
        RiskModelDescriptor descriptor,
        double probability,
        RiskLevel level,
        DateTime scoredAtUtc,
        int? scoringRunId,
        CancellationToken cancellationToken = default);
}
