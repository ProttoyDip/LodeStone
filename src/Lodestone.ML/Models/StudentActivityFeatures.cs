using Lodestone.Application.DTOs.Risk;

namespace Lodestone.ML.Models;

/// <summary>
/// Version-one behavioral feature vector consumed by the risk model. Keep this type free of
/// labels and identifiers so the saved model has the same input contract during training and
/// inference.
/// </summary>
public class StudentActivityFeatures
{
    public const string SchemaVersion = RiskFeatureSchema.Withdrawal28DayV1;

    public static readonly IReadOnlyList<string> FeatureNames =
    [
        nameof(ActiveDayRate),
        nameof(ActivitySpanDays),
        nameof(DaysSinceLastAccess),
        nameof(ForumInteractionCount),
        nameof(CourseInteractionCount),
        nameof(LateOrMissingAssignmentCount)
    ];

    public float ActiveDayRate { get; set; }
    public float ActivitySpanDays { get; set; }
    public float DaysSinceLastAccess { get; set; }
    /// <summary>Clicks on OULAD sites whose activity_type is forumng.</summary>
    public float ForumInteractionCount { get; set; }

    /// <summary>Clicks on all non-forum OULAD VLE sites; forum clicks are intentionally excluded.</summary>
    public float CourseInteractionCount { get; set; }
    public float LateOrMissingAssignmentCount { get; set; }
}
