using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

/// <summary>Operational record for one idempotent batch-scoring attempt.</summary>
public class RiskScoringRun : AuditableEntity
{
    public Guid RunKey { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string FeatureSchemaVersion { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public RiskScoringRunStatus Status { get; set; } = RiskScoringRunStatus.Running;
    public int CandidateCount { get; set; }
    public int ScoredCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int QueueCreatedCount { get; set; }
    public int QueueEscalatedCount { get; set; }
    public string? FailureSummary { get; set; }

    public ICollection<RiskScore> RiskScores { get; set; } = new List<RiskScore>();
}
