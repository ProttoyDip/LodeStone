using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

/// <summary>
/// A privacy-scoped, course-level feature window ready for risk-model inference.
/// It contains behavioral counts only and never journal or message content.
/// </summary>
public class RiskFeatureSnapshot : AuditableEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public string CourseKey { get; set; } = string.Empty;
    public DateTime WindowEndUtc { get; set; }
    public int ObservedDays { get; set; }
    public string FeatureSchemaVersion { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceFileSha256 { get; set; } = string.Empty;

    public float ActiveDayRate { get; set; }
    public float ActivitySpanDays { get; set; }
    public float DaysSinceLastAccess { get; set; }
    public float ForumInteractionCount { get; set; }
    public float CourseInteractionCount { get; set; }
    public float LateOrMissingAssignmentCount { get; set; }

    // withdrawal-28d-v2 is deliberately stored as separately named, nullable columns. This
    // prevents a v2 import from silently reinterpreting the six-field v1 contract.
    public float? RecentActiveDayRate { get; set; }
    public float? PriorActiveDayRate { get; set; }
    public float? ActiveDayRateTrend { get; set; }
    public float? RecentCourseClickRate { get; set; }
    public float? PriorCourseClickRate { get; set; }
    public float? CourseClickRateTrend { get; set; }
    public float? InactivityStreakDays { get; set; }
    public float? AssessmentDueRate { get; set; }
    public float? AssessmentOnTimeRate { get; set; }
    public float? AssessmentLateOrMissingRate { get; set; }
    public float? CourseProgressRatio { get; set; }
    public float? CohortActivityPercentile { get; set; }

    public ICollection<RiskScore> RiskScores { get; set; } = new List<RiskScore>();
}
