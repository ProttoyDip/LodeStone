using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Models;
using Microsoft.ML;

namespace Lodestone.ML.Training;

public enum TrainingWeightStrategy
{
    Balanced,
    SquareRootBalanced,
    Unweighted
}

/// <summary>
/// Deterministic, student-grouped cross-validation used only inside the training partition.
/// It never receives validation or locked-test rows.
/// </summary>
public sealed class GroupedCrossValidator
{
    private const int FoldCount = 3;

    private readonly MLContext _mlContext;
    private readonly FeatureEngineering _features;
    private readonly ModelTrainer _trainer;
    private readonly ModelEvaluator _evaluator;

    public GroupedCrossValidator(
        MLContext mlContext,
        FeatureEngineering features,
        ModelTrainer trainer,
        ModelEvaluator evaluator)
        => (_mlContext, _features, _trainer, _evaluator) = (mlContext, features, trainer, evaluator);

    public IReadOnlyList<CrossValidationCandidateResult> Evaluate(
        IReadOnlyList<StudentActivityObservation> trainingRows,
        RiskFeatureSchemaDefinition schema,
        IReadOnlyList<ModelTrainingCandidate> candidates,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(trainingRows);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) throw new ArgumentException("At least one candidate is required.", nameof(candidates));

        var foldByStudent = AssignFolds(trainingRows, seed);
        var results = new List<CrossValidationCandidateResult>(candidates.Count);
        foreach (var candidate in candidates)
        {
            try
            {
                var folds = new List<ModelMetrics>(FoldCount);
                for (var fold = 0; fold < FoldCount; fold++)
                {
                    var fit = trainingRows
                        .Where(row => foldByStudent[row.StudentGroupKey] != fold)
                        .Select(Clone)
                        .ToArray();
                    var heldOut = trainingRows
                        .Where(row => foldByStudent[row.StudentGroupKey] == fold)
                        .Select(Clone)
                        .ToArray();
                    ApplyClassWeights(fit);
                    if (UsesCohortCalibration(schema))
                    {
                        var calibrator = CohortFeatureCalibrator.Fit(fit);
                        calibrator.Apply(fit);
                        calibrator.Apply(heldOut);
                    }

                    var fittedData = _mlContext.Data.LoadFromEnumerable(fit);
                    var heldOutData = _mlContext.Data.LoadFromEnumerable(heldOut);
                    var model = _trainer.Train(fittedData, _features.BuildPipeline(schema.FeatureNames), candidate);
                    folds.Add(_evaluator.Evaluate(model, heldOutData, .5f));
                }

                results.Add(new CrossValidationCandidateResult
                {
                    CandidateId = candidate.Id,
                    Algorithm = candidate.Algorithm.ToString(),
                    Hyperparameters = new Dictionary<string, string>(candidate.ToReportValues(), StringComparer.Ordinal),
                    MeanAreaUnderRocCurve = folds.Average(metric => metric.AreaUnderRocCurve),
                    MeanAreaUnderPrecisionRecallCurve = folds.Average(metric => metric.AreaUnderPrecisionRecallCurve),
                    MeanRecall = folds.Average(metric => metric.Recall),
                    MeanPrecision = folds.Average(metric => metric.Precision),
                    MeanF1Score = folds.Average(metric => metric.F1Score),
                    FoldCount = FoldCount,
                    IsUsable = true
                });
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                results.Add(new CrossValidationCandidateResult
                {
                    CandidateId = candidate.Id,
                    Algorithm = candidate.Algorithm.ToString(),
                    Hyperparameters = new Dictionary<string, string>(candidate.ToReportValues(), StringComparer.Ordinal),
                    FoldCount = FoldCount,
                    IsUsable = false,
                    FailureReason = exception.GetType().Name
                });
            }
        }

        return results;
    }

    private static IReadOnlyDictionary<string, int> AssignFolds(
        IReadOnlyList<StudentActivityObservation> rows,
        int seed)
    {
        var strata = rows
            .GroupBy(row => row.StudentGroupKey, StringComparer.Ordinal)
            .Select(group => new StudentClass(group.Key, group.Any(row => row.IsAtRisk)))
            .GroupBy(value => value.IsPositive)
            .ToDictionary(group => group.Key, group => group.Select(value => value.StudentKey).ToArray());
        if ((strata.GetValueOrDefault(true)?.Length ?? 0) < FoldCount ||
            (strata.GetValueOrDefault(false)?.Length ?? 0) < FoldCount)
        {
            throw new InvalidDataException("Grouped cross-validation requires at least three positive and three negative students in training.");
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        Assign(strata[true], seed, result);
        Assign(strata[false], unchecked(seed * 397) ^ 0x41C64E6D, result);
        return result;
    }

    private static void Assign(IEnumerable<string> source, int seed, IDictionary<string, int> result)
    {
        var students = source.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var random = new Random(seed);
        for (var index = students.Length - 1; index > 0; index--)
        {
            var target = random.Next(index + 1);
            (students[index], students[target]) = (students[target], students[index]);
        }
        for (var index = 0; index < students.Length; index++) result.Add(students[index], index % FoldCount);
    }

    internal static bool UsesCohortCalibration(RiskFeatureSchemaDefinition schema)
        => string.Equals(schema.Version, RiskFeatureSchema.Withdrawal28DayV2, StringComparison.Ordinal)
           || string.Equals(schema.Version, RiskFeatureSchema.Withdrawal28DayV3, StringComparison.Ordinal)
           || string.Equals(schema.Version, RiskFeatureSchema.Withdrawal28DayV4Experiment, StringComparison.Ordinal);

    internal static void ApplyClassWeights(
        IReadOnlyList<StudentActivityObservation> rows,
        TrainingWeightStrategy strategy = TrainingWeightStrategy.Balanced)
    {
        var positives = rows.Count(row => row.IsAtRisk);
        var negatives = rows.Count - positives;
        if (positives == 0 || negatives == 0)
            throw new InvalidDataException("A cross-validation fitting fold must contain both classes.");

        var positiveWeight = 1f;
        var negativeWeight = 1f;
        if (strategy is not TrainingWeightStrategy.Unweighted)
        {
            var targetRatio = strategy == TrainingWeightStrategy.Balanced
                ? negatives / (double)positives
                : Math.Sqrt(negatives / (double)positives);
            var scale = rows.Count / (positives * targetRatio + negatives);
            positiveWeight = (float)(targetRatio * scale);
            negativeWeight = (float)scale;
        }

        foreach (var row in rows) row.ExampleWeight = row.IsAtRisk ? positiveWeight : negativeWeight;
    }

    private static StudentActivityObservation Clone(StudentActivityObservation source) => new()
    {
        ActiveDayRate = source.ActiveDayRate,
        ActivitySpanDays = source.ActivitySpanDays,
        DaysSinceLastAccess = source.DaysSinceLastAccess,
        ForumInteractionCount = source.ForumInteractionCount,
        CourseInteractionCount = source.CourseInteractionCount,
        LateOrMissingAssignmentCount = source.LateOrMissingAssignmentCount,
        RecentActiveDayRate = source.RecentActiveDayRate,
        PriorActiveDayRate = source.PriorActiveDayRate,
        ActiveDayRateTrend = source.ActiveDayRateTrend,
        RecentCourseClickRate = source.RecentCourseClickRate,
        PriorCourseClickRate = source.PriorCourseClickRate,
        CourseClickRateTrend = source.CourseClickRateTrend,
        InactivityStreakDays = source.InactivityStreakDays,
        AssessmentDueRate = source.AssessmentDueRate,
        AssessmentOnTimeRate = source.AssessmentOnTimeRate,
        AssessmentLateOrMissingRate = source.AssessmentLateOrMissingRate,
        CourseProgressRatio = source.CourseProgressRatio,
        CohortActivityPercentile = source.CohortActivityPercentile,
        ActivityTrendAcceleration = source.ActivityTrendAcceleration,
        ClickVolatility = source.ClickVolatility,
        ForumEngagementShare = source.ForumEngagementShare,
        InactiveWeekRate = source.InactiveWeekRate,
        AssessmentMissStreak = source.AssessmentMissStreak,
        PriorAssessmentsDueCount = source.PriorAssessmentsDueCount,
        PriorAssessmentCompletionRate = source.PriorAssessmentCompletionRate,
        PriorAssessmentLateRate = source.PriorAssessmentLateRate,
        PriorAssessmentMeanScore = source.PriorAssessmentMeanScore,
        PriorAssessmentFailRate = source.PriorAssessmentFailRate,
        LastAssessmentScore = source.LastAssessmentScore,
        IsAtRisk = source.IsAtRisk,
        StudentGroupKey = source.StudentGroupKey,
        EnrollmentKey = source.EnrollmentKey,
        ObservationDay = source.ObservationDay,
        CoursePresentationKey = source.CoursePresentationKey,
        WithdrawalDay = source.WithdrawalDay
    };

    private sealed record StudentClass(string StudentKey, bool IsPositive);
}
