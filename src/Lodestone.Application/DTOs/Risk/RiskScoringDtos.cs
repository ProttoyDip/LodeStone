using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Risk;

public sealed record RiskScoringResultDto(
    int SnapshotId,
    int? RiskScoreId,
    bool Scored,
    bool QueueCreated,
    bool QueueEscalated,
    string? SkipReason);

public sealed record RiskScoringRunDto(
    int Id,
    Guid RunKey,
    string ModelVersion,
    string FeatureSchemaVersion,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    RiskScoringRunStatus Status,
    int CandidateCount,
    int ScoredCount,
    int SkippedCount,
    int FailedCount,
    int QueueCreatedCount,
    int QueueEscalatedCount,
    string? FailureSummary);
