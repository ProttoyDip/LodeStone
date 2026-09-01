namespace Lodestone.Application.DTOs.Risk;

/// <summary>The canonical feature schema currently accepted by the withdrawal model.</summary>
public static class RiskFeatureSchema
{
    public const string Withdrawal28DayV1 = "withdrawal-28d-v1";
    public const string Withdrawal28DayV2 = "withdrawal-28d-v2";
    public const string Withdrawal28DayV3 = "withdrawal-28d-v3";
    public const int Withdrawal28DayObservedDays = 28;
}

public static class RiskMonitoringPolicy
{
    public const string CurrentVersion = "v1";
}

public static class RiskScoringPolicy
{
    public const int MaximumSnapshotAgeDays = 8;
}

/// <summary>
/// Framework-neutral model input for one student/course/window snapshot.  The six-argument
/// constructor is deliberately retained for the stable v1 application contract.
/// </summary>
public sealed class RiskModelInput
{
    private readonly float[] _featureValues;

    public RiskModelInput(
        float activeDayRate,
        float activitySpanDays,
        float daysSinceLastAccess,
        float forumInteractionCount,
        float courseInteractionCount,
        float lateOrMissingAssignmentCount)
        : this(
            RiskFeatureSchema.Withdrawal28DayV1,
            [
                activeDayRate,
                activitySpanDays,
                daysSinceLastAccess,
                forumInteractionCount,
                courseInteractionCount,
                lateOrMissingAssignmentCount
            ])
    {
    }

    /// <summary>
    /// Creates an input whose values are in the exact order declared by the named, registered
    /// feature schema.  Callers must never infer or reorder a feature vector themselves.
    /// </summary>
    public RiskModelInput(string featureSchemaVersion, IReadOnlyList<float> featureValues)
    {
        var schema = RiskFeatureSchemas.GetRequired(featureSchemaVersion);
        ArgumentNullException.ThrowIfNull(featureValues);
        if (featureValues.Count != schema.FeatureNames.Count)
        {
            throw new ArgumentException(
                $"Feature schema '{schema.Version}' requires {schema.FeatureNames.Count} values, " +
                $"but {featureValues.Count} were supplied.",
                nameof(featureValues));
        }
        if (featureValues.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Risk-model feature values must be finite.", nameof(featureValues));

        FeatureSchemaVersion = schema.Version;
        _featureValues = featureValues.ToArray();
    }

    public string FeatureSchemaVersion { get; }
    public IReadOnlyList<float> FeatureValues => _featureValues;

    public float GetFeature(string featureName)
    {
        var index = RiskFeatureSchemas.GetRequired(FeatureSchemaVersion).IndexOf(featureName);
        return _featureValues[index];
    }

    // Stable v1 convenience accessors. They intentionally throw for v2 rather than silently
    // interpreting a different feature order as the legacy contract.
    public float ActiveDayRate => GetV1Feature(nameof(ActiveDayRate));
    public float ActivitySpanDays => GetV1Feature(nameof(ActivitySpanDays));
    public float DaysSinceLastAccess => GetV1Feature(nameof(DaysSinceLastAccess));
    public float ForumInteractionCount => GetV1Feature(nameof(ForumInteractionCount));
    public float CourseInteractionCount => GetV1Feature(nameof(CourseInteractionCount));
    public float LateOrMissingAssignmentCount => GetV1Feature(nameof(LateOrMissingAssignmentCount));

    public static RiskModelInput CreateWithdrawal28DayV2(
        float recentActiveDayRate,
        float priorActiveDayRate,
        float activeDayRateTrend,
        float recentCourseClickRate,
        float priorCourseClickRate,
        float courseClickRateTrend,
        float inactivityStreakDays,
        float assessmentDueRate,
        float assessmentOnTimeRate,
        float assessmentLateOrMissingRate,
        float courseProgressRatio,
        float cohortActivityPercentile)
        => new(
            RiskFeatureSchema.Withdrawal28DayV2,
            [
                recentActiveDayRate,
                priorActiveDayRate,
                activeDayRateTrend,
                recentCourseClickRate,
                priorCourseClickRate,
                courseClickRateTrend,
                inactivityStreakDays,
                assessmentDueRate,
                assessmentOnTimeRate,
                assessmentLateOrMissingRate,
                courseProgressRatio,
                cohortActivityPercentile
            ]);

    private float GetV1Feature(string featureName)
    {
        if (!string.Equals(FeatureSchemaVersion, RiskFeatureSchema.Withdrawal28DayV1, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Legacy v1 feature '{featureName}' cannot be read from schema '{FeatureSchemaVersion}'.");
        }

        return GetFeature(featureName);
    }
}

/// <summary>Framework-neutral inference result. Probability is validated by Application.</summary>
public sealed record RiskModelPrediction(double Probability);

/// <summary>Metadata that binds a model artifact to its feature schema and queue threshold.</summary>
public sealed record RiskModelDescriptor(
    string ModelVersion,
    string FeatureSchemaVersion,
    int ObservedDays,
    double QueueThreshold)
{
    /// <summary>Exact ordered input names accepted by the loaded artifact, when available.</summary>
    public IReadOnlyList<string> FeatureNames { get; init; } = Array.Empty<string>();

    /// <summary>Immutable publication identifier supplied by the validated artifact manifest.</summary>
    public string? PublicationId { get; init; }
}
