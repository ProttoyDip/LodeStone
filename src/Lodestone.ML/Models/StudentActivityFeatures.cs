using Lodestone.Application.DTOs.Risk;

namespace Lodestone.ML.Models;

/// <summary>
/// Versioned behavioral feature vector consumed by the risk model. Keep this type free of labels
/// and identifiers so the saved model has the same input contract during training and inference.
/// The active contract is selected only through <c>RiskFeatureSchemas</c>; v1 fields are retained
/// unchanged for backward-compatible artifact loading.
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

    // withdrawal-28d-v2: all values use only the 28-day observation window ending at the anchor.
    public float RecentActiveDayRate { get; set; }
    public float PriorActiveDayRate { get; set; }
    public float ActiveDayRateTrend { get; set; }
    public float RecentCourseClickRate { get; set; }
    public float PriorCourseClickRate { get; set; }
    public float CourseClickRateTrend { get; set; }
    public float InactivityStreakDays { get; set; }
    public float AssessmentDueRate { get; set; }
    public float AssessmentOnTimeRate { get; set; }
    public float AssessmentLateOrMissingRate { get; set; }
    public float CourseProgressRatio { get; set; }
    public float CohortActivityPercentile { get; set; }

    // withdrawal-28d-v3: additive on top of v2, still window-bounded and leakage-safe.
    /// <summary>Second difference of active-day rate across three equal window periods.</summary>
    public float ActivityTrendAcceleration { get; set; }
    /// <summary>Population standard deviation of total daily clicks across the window.</summary>
    public float ClickVolatility { get; set; }
    /// <summary>Share of window clicks that were forum activity rather than course material.</summary>
    public float ForumEngagementShare { get; set; }
    /// <summary>Fraction of the window's four 7-day weeks with zero active days.</summary>
    public float InactiveWeekRate { get; set; }
    /// <summary>Count of the most recent consecutive due assessments that were late or missing.</summary>
    public float AssessmentMissStreak { get; set; }

    // withdrawal-28d-v4-experiment: course-to-anchor assessment history. Scores are normalized
    // to [0, 1] and only assessments due by the observation anchor are eligible.
    public float PriorAssessmentsDueCount { get; set; }
    public float PriorAssessmentCompletionRate { get; set; }
    public float PriorAssessmentLateRate { get; set; }
    public float PriorAssessmentMeanScore { get; set; }
    public float PriorAssessmentFailRate { get; set; }
    public float LastAssessmentScore { get; set; }
}
