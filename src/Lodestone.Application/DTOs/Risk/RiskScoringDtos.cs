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

/// <summary>One scored snapshot inside a run, for audit export. Student identified by verified number only.</summary>
public sealed record RiskScoringRunRowDto(
    int RiskScoreId,
    string StudentReference,
    string CourseKey,
    DateTime WindowEndUtc,
    double Probability,
    RiskLevel Level,
    DateTime ScoredAtUtc,
    bool QueuedOrEscalated);

public sealed record RiskScoringRunExportDto(
    RiskScoringRunDto Run,
    IReadOnlyList<RiskScoringRunRowDto> Rows);
