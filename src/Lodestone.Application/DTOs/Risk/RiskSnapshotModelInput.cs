using Lodestone.Domain.Entities;

namespace Lodestone.Application.DTOs.Risk;

/// <summary>
/// Turns a stored feature snapshot into the ordered input a model expects. One place, so scoring
/// and explanation cannot disagree about which column is which.
/// </summary>
public static class RiskSnapshotModelInput
{
    public static RiskModelInput From(RiskFeatureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new RiskModelInput(snapshot.FeatureSchemaVersion, FeatureValues(snapshot));
    }

    private static IReadOnlyList<float> FeatureValues(RiskFeatureSnapshot snapshot)
        => snapshot.FeatureSchemaVersion switch
        {
            RiskFeatureSchema.Withdrawal28DayV1 =>
            [
                snapshot.ActiveDayRate,
                snapshot.ActivitySpanDays,
                snapshot.DaysSinceLastAccess,
                snapshot.ForumInteractionCount,
                snapshot.CourseInteractionCount,
                snapshot.LateOrMissingAssignmentCount
            ],
            RiskFeatureSchema.Withdrawal28DayV2 => V2Values(snapshot),
            RiskFeatureSchema.Withdrawal28DayV3 =>
            [
                .. V2Values(snapshot),
                Required(snapshot.ActivityTrendAcceleration, nameof(snapshot.ActivityTrendAcceleration)),
                Required(snapshot.ClickVolatility, nameof(snapshot.ClickVolatility)),
                Required(snapshot.ForumEngagementShare, nameof(snapshot.ForumEngagementShare)),
                Required(snapshot.InactiveWeekRate, nameof(snapshot.InactiveWeekRate)),
                Required(snapshot.AssessmentMissStreak, nameof(snapshot.AssessmentMissStreak))
            ],
            _ => throw new InvalidOperationException("The snapshot has an unsupported feature schema.")
        };

    private static float[] V2Values(RiskFeatureSnapshot snapshot) =>
    [
        Required(snapshot.RecentActiveDayRate, nameof(snapshot.RecentActiveDayRate)),
        Required(snapshot.PriorActiveDayRate, nameof(snapshot.PriorActiveDayRate)),
        Required(snapshot.ActiveDayRateTrend, nameof(snapshot.ActiveDayRateTrend)),
        Required(snapshot.RecentCourseClickRate, nameof(snapshot.RecentCourseClickRate)),
        Required(snapshot.PriorCourseClickRate, nameof(snapshot.PriorCourseClickRate)),
        Required(snapshot.CourseClickRateTrend, nameof(snapshot.CourseClickRateTrend)),
        Required(snapshot.InactivityStreakDays, nameof(snapshot.InactivityStreakDays)),
        Required(snapshot.AssessmentDueRate, nameof(snapshot.AssessmentDueRate)),
        Required(snapshot.AssessmentOnTimeRate, nameof(snapshot.AssessmentOnTimeRate)),
        Required(snapshot.AssessmentLateOrMissingRate, nameof(snapshot.AssessmentLateOrMissingRate)),
        Required(snapshot.CourseProgressRatio, nameof(snapshot.CourseProgressRatio)),
        Required(snapshot.CohortActivityPercentile, nameof(snapshot.CohortActivityPercentile))
    ];

    private static float Required(float? value, string name)
        => value ?? throw new InvalidOperationException($"The snapshot is missing required feature '{name}'.");
}
