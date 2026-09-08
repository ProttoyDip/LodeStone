namespace Lodestone.Application.DTOs.Risk;

/// <summary>
/// Counselor-readable phrasing for each model feature.
/// </summary>
/// <remarks>
/// <para>
/// Every phrase here describes exactly what the feature measures and nothing more. That constraint
/// is the point of this type. The model sees clickstream activity and assessment timing pulled
/// from the VLE; it does not see class attendance, quiz marks, or any record of a conversation.
/// Labelling <c>RecentActiveDayRate</c> as "attendance" would put a word in front of a counselor
/// that means a person missing from a room, and they would act on it.
/// </para>
/// <para>
/// An explanation that overstates what was measured is worse than no explanation, because it
/// borrows the authority of a number to make a claim the number cannot support. If a future schema
/// adds a feature, add its phrase here rather than deriving one at the view layer, so that every
/// surface says the same true thing.
/// </para>
/// </remarks>
public static class RiskFeatureVocabulary
{
    private static readonly IReadOnlyDictionary<string, string> Phrases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // withdrawal-28d-v1
            ["ActiveDayRate"] = "share of days with any course platform activity",
            ["ActivitySpanDays"] = "days between first and last platform activity",
            ["DaysSinceLastAccess"] = "days since the platform was last opened",
            ["ForumInteractionCount"] = "forum page views",
            ["CourseInteractionCount"] = "course material page views",
            ["LateOrMissingAssignmentCount"] = "assessments submitted late or not at all",

            // withdrawal-28d-v2
            ["RecentActiveDayRate"] = "share of days active on the platform, last 28 days",
            ["PriorActiveDayRate"] = "share of days active on the platform, the 28 days before that",
            ["ActiveDayRateTrend"] = "change in days-active between those two periods",
            ["RecentCourseClickRate"] = "course material views per day, last 28 days",
            ["PriorCourseClickRate"] = "course material views per day, the 28 days before that",
            ["CourseClickRateTrend"] = "change in course material views between those two periods",
            ["InactivityStreakDays"] = "longest run of consecutive days with no platform activity",
            ["AssessmentDueRate"] = "share of the course's assessments due so far",
            ["AssessmentOnTimeRate"] = "share of due assessments submitted on time",
            ["AssessmentLateOrMissingRate"] = "share of due assessments late or not submitted",
            ["CourseProgressRatio"] = "how far through the course presentation this point falls",
            ["CohortActivityPercentile"] = "platform activity compared with others on the same course",

            // withdrawal-28d-v3
            ["ActivityTrendAcceleration"] = "whether the activity trend is steepening or levelling off",
            ["ClickVolatility"] = "how erratic day-to-day platform activity has been",
            ["ForumEngagementShare"] = "share of platform activity spent in the forum",
            ["InactiveWeekRate"] = "share of weeks with no platform activity at all",
            ["AssessmentMissStreak"] = "consecutive assessments missed"
        };

    /// <summary>
    /// The counselor-readable phrase for a feature. Falls back to the raw feature name, which is
    /// unlovely but honest: an unknown feature must never be given an invented meaning.
    /// </summary>
    public static string Describe(string featureName)
        => string.IsNullOrWhiteSpace(featureName)
            ? string.Empty
            : Phrases.TryGetValue(featureName, out var phrase) ? phrase : featureName;

    /// <summary>True when a feature has an agreed phrase rather than falling back to its raw name.</summary>
    public static bool IsDescribed(string featureName)
        => !string.IsNullOrWhiteSpace(featureName) && Phrases.ContainsKey(featureName);

    /// <summary>Every feature name with an agreed phrase.</summary>
    public static IReadOnlyCollection<string> DescribedFeatures => (IReadOnlyCollection<string>)Phrases.Keys;
}
