namespace Lodestone.Application.DTOs.Risk;

/// <summary>
/// Application-owned, immutable feature contract. ML.NET implementation types must map to this
/// registry; the application never accepts an unregistered schema or feature order.
/// </summary>
public sealed class RiskFeatureSchemaDefinition
{
    private readonly IReadOnlyDictionary<string, int> _indexes;

    public RiskFeatureSchemaDefinition(string version, int observedDays, IReadOnlyList<string> featureNames)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("A feature schema version is required.", nameof(version));
        if (observedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(observedDays));
        ArgumentNullException.ThrowIfNull(featureNames);
        if (featureNames.Count == 0 || featureNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Feature schemas require non-empty feature names.", nameof(featureNames));

        var names = featureNames.Select(name => name.Trim()).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new ArgumentException("Feature schema names must be unique and ordered.", nameof(featureNames));

        Version = version.Trim();
        ObservedDays = observedDays;
        FeatureNames = Array.AsReadOnly(names);
        _indexes = names
            .Select((name, index) => new { name, index })
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
    }

    public string Version { get; }
    public int ObservedDays { get; }
    public IReadOnlyList<string> FeatureNames { get; }

    public int IndexOf(string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName) || !_indexes.TryGetValue(featureName, out var index))
            throw new ArgumentException($"'{featureName}' is not a feature in schema '{Version}'.", nameof(featureName));
        return index;
    }
}

/// <summary>Canonical schemas supported by the application boundary and runtime loader.</summary>
public static class RiskFeatureSchemas
{
    public static readonly RiskFeatureSchemaDefinition Withdrawal28DayV1 = new(
        RiskFeatureSchema.Withdrawal28DayV1,
        RiskFeatureSchema.Withdrawal28DayObservedDays,
        [
            "ActiveDayRate",
            "ActivitySpanDays",
            "DaysSinceLastAccess",
            "ForumInteractionCount",
            "CourseInteractionCount",
            "LateOrMissingAssignmentCount"
        ]);

    public static readonly RiskFeatureSchemaDefinition Withdrawal28DayV2 = new(
        RiskFeatureSchema.Withdrawal28DayV2,
        RiskFeatureSchema.Withdrawal28DayObservedDays,
        [
            "RecentActiveDayRate",
            "PriorActiveDayRate",
            "ActiveDayRateTrend",
            "RecentCourseClickRate",
            "PriorCourseClickRate",
            "CourseClickRateTrend",
            "InactivityStreakDays",
            "AssessmentDueRate",
            "AssessmentOnTimeRate",
            "AssessmentLateOrMissingRate",
            "CourseProgressRatio",
            "CohortActivityPercentile"
        ]);

    private static readonly IReadOnlyDictionary<string, RiskFeatureSchemaDefinition> ByVersion =
        new[] { Withdrawal28DayV1, Withdrawal28DayV2 }
            .ToDictionary(schema => schema.Version, StringComparer.Ordinal);

    public static bool TryGet(string? version, out RiskFeatureSchemaDefinition schema)
    {
        if (!string.IsNullOrWhiteSpace(version) && ByVersion.TryGetValue(version.Trim(), out var found))
        {
            schema = found;
            return true;
        }

        schema = null!;
        return false;
    }

    public static RiskFeatureSchemaDefinition GetRequired(string? version)
        => TryGet(version, out var schema)
            ? schema
            : throw new ArgumentException($"Unsupported risk feature schema '{version}'.", nameof(version));
}
