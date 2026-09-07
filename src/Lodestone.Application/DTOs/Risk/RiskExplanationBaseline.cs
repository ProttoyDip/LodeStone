namespace Lodestone.Application.DTOs.Risk;

/// <summary>
/// The reference student an explanation is measured against.
/// </summary>
/// <remarks>
/// <para>
/// A contribution only means something relative to some comparison. "This student's inactivity
/// raised their score" is incomplete; raised it compared with what? This type carries that answer
/// explicitly, and <see cref="Description"/> carries it all the way to the counselor, so nobody
/// reads a contribution without knowing what it was measured against.
/// </para>
/// <para>
/// Baselines are computed from feature values the caller already holds. No new data is collected
/// to build one.
/// </para>
/// </remarks>
public sealed class RiskExplanationBaseline
{
    private readonly IReadOnlyDictionary<string, float> _values;

    private RiskExplanationBaseline(
        string featureSchemaVersion,
        IReadOnlyDictionary<string, float> values,
        string description,
        int sampleSize)
    {
        FeatureSchemaVersion = featureSchemaVersion;
        _values = values;
        Description = description;
        SampleSize = sampleSize;
    }

    public string FeatureSchemaVersion { get; }

    /// <summary>Plain-language statement of what the comparison group is.</summary>
    public string Description { get; }

    /// <summary>How many students the baseline was computed from.</summary>
    public int SampleSize { get; }

    public bool TryGetValue(string featureName, out float value) => _values.TryGetValue(featureName, out value);

    /// <summary>
    /// Builds a baseline from the median of each feature across the supplied students.
    /// </summary>
    /// <remarks>
    /// The median rather than the mean, because these features are skewed: a handful of students
    /// with enormous click counts would drag a mean somewhere no real student sits, and every
    /// contribution would then be measured against a fiction.
    /// </remarks>
    public static RiskExplanationBaseline FromPopulation(
        string featureSchemaVersion,
        IReadOnlyList<RiskModelInput> population,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureSchemaVersion);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var schema = RiskFeatureSchemas.GetRequired(featureSchemaVersion);
        var matching = population
            .Where(input => string.Equals(input.FeatureSchemaVersion, featureSchemaVersion, StringComparison.Ordinal))
            .ToArray();
        if (matching.Length == 0)
        {
            throw new ArgumentException(
                $"No student in the population uses schema '{featureSchemaVersion}'.",
                nameof(population));
        }

        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        for (var index = 0; index < schema.FeatureNames.Count; index++)
        {
            var column = matching
                .Select(input => input.FeatureValues[index])
                .OrderBy(value => value)
                .ToArray();
            values[schema.FeatureNames[index]] = Median(column);
        }

        return new RiskExplanationBaseline(featureSchemaVersion, values, description, matching.Length);
    }

    private static float Median(IReadOnlyList<float> sorted)
    {
        if (sorted.Count == 0) return 0f;
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2f;
    }
}
