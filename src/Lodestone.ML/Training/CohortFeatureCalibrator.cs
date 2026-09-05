using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Models;

namespace Lodestone.ML.Training;

/// <summary>
/// Fits the v2 cohort-relative feature from training students only. Validation and locked-test
/// rows are transformed against those frozen training distributions and never contribute to the
/// percentile they receive.
/// </summary>
public sealed class CohortFeatureCalibrator
{
    private readonly IReadOnlyDictionary<CohortAnchorKey, float[]> _byCourseAndAnchor;
    private readonly IReadOnlyDictionary<string, float[]> _byCourse;
    private readonly float[] _global;

    private CohortFeatureCalibrator(
        IReadOnlyDictionary<CohortAnchorKey, float[]> byCourseAndAnchor,
        IReadOnlyDictionary<string, float[]> byCourse,
        float[] global)
    {
        _byCourseAndAnchor = byCourseAndAnchor;
        _byCourse = byCourse;
        _global = global;
    }

    public static CohortFeatureCalibrator Fit(IReadOnlyList<StudentActivityObservation> training)
    {
        ArgumentNullException.ThrowIfNull(training);
        if (training.Count == 0)
            throw new InvalidDataException("Cohort calibration requires non-empty training observations.");
        if (training.Any(row => string.IsNullOrWhiteSpace(row.CoursePresentationKey)))
            throw new InvalidDataException("Cohort calibration requires a course/presentation key for every training observation.");

        static float[] Ordered(IEnumerable<StudentActivityObservation> rows)
            => rows.Select(row => row.RecentActiveDayRate).OrderBy(value => value).ToArray();

        var byCourseAndAnchor = training
            .GroupBy(row => new CohortAnchorKey(row.CoursePresentationKey, row.ObservationDay))
            .ToDictionary(group => group.Key, Ordered);
        var byCourse = training
            .GroupBy(row => row.CoursePresentationKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, Ordered, StringComparer.Ordinal);

        return new CohortFeatureCalibrator(byCourseAndAnchor, byCourse, Ordered(training));
    }

    public void Apply(IReadOnlyList<StudentActivityObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        foreach (var observation in observations)
        {
            if (string.IsNullOrWhiteSpace(observation.CoursePresentationKey))
                throw new InvalidDataException("Cohort calibration requires a course/presentation key for every observation.");
            if (!float.IsFinite(observation.RecentActiveDayRate))
                throw new InvalidDataException("Cohort calibration requires a finite recent activity rate.");

            var key = new CohortAnchorKey(observation.CoursePresentationKey, observation.ObservationDay);
            var reference = _byCourseAndAnchor.GetValueOrDefault(key)
                ?? _byCourse.GetValueOrDefault(observation.CoursePresentationKey)
                ?? _global;
            observation.CohortActivityPercentile = Percentile(reference, observation.RecentActiveDayRate);
        }
    }

    private static float Percentile(IReadOnlyList<float> ordered, float value)
    {
        if (ordered.Count == 0)
            throw new InvalidDataException("Cohort calibration has no reference observations.");

        var lessThan = 0;
        var equal = 0;
        foreach (var reference in ordered)
        {
            if (reference < value) lessThan++;
            else if (reference.Equals(value)) equal++;
        }

        // Mid-rank percentile is stable for ties and remains in [0, 1].
        return (float)((lessThan + (equal / 2d)) / ordered.Count);
    }

    private readonly record struct CohortAnchorKey(string CoursePresentationKey, int ObservationDay);
}
