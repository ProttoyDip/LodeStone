using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

/// <summary>A scored risk result produced by the ML pipeline for a student at a point in time.</summary>
public class RiskScore : AuditableEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public int RiskFeatureSnapshotId { get; set; }
    public RiskFeatureSnapshot? RiskFeatureSnapshot { get; set; }

    public int? RiskScoringRunId { get; set; }
    public RiskScoringRun? RiskScoringRun { get; set; }

    public string CourseKey { get; set; } = string.Empty;
    public DateTime WindowEndUtc { get; set; }
    public string FeatureSchemaVersion { get; set; } = string.Empty;
    public double Probability { get; set; }
    public RiskLevel Level { get; set; }
    public DateTime ScoredAtUtc { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
}
