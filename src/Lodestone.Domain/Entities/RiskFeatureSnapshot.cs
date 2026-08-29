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

    public ICollection<RiskScore> RiskScores { get; set; } = new List<RiskScore>();
}
