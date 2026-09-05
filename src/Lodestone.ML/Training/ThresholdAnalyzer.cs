using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Models;
using Microsoft.ML;

namespace Lodestone.ML.Training;

/// <summary>
/// Diagnostic-only precision/recall exploration. It trains candidates on the training partition
/// and reports their full validation threshold curve so gate values can be chosen from measured
/// attainable operating points instead of guesses. The locked test partition is never loaded,
/// transformed or scored here, and nothing is published.
/// </summary>
public sealed class ThresholdAnalyzer
{
    private static readonly double[] RecallFloors =
        [.02, .05, .10, .15, .20, .30, .40, .50, .65, .70, .80, .90];

    private readonly MLContext _mlContext;
    private readonly OuladDataLoader _loader;
    private readonly FeatureEngineering _features;
    private readonly ModelTrainer _trainer;
    private readonly ModelEvaluator _evaluator;

    public ThresholdAnalyzer(
        MLContext mlContext,
        OuladDataLoader loader,
        FeatureEngineering features,
        ModelTrainer trainer,
        ModelEvaluator evaluator)
    {
        _mlContext = mlContext;
        _loader = loader;
        _features = features;
        _trainer = trainer;
        _evaluator = evaluator;
    }

    public ThresholdAnalysisReport Analyze(
        string dataDirectory,
        string featureSchemaVersion,
        IReadOnlyList<ModelTrainingCandidate> candidates,
        int seed,
        TrainingWeightStrategy weightStrategy = TrainingWeightStrategy.Balanced,
        WithdrawalLabelStrategy labelStrategy = WithdrawalLabelStrategy.Within28Days,
        double trainingFraction = 0.70,
        double validationFraction = 0.15)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            throw new ArgumentException("At least one candidate is required.", nameof(candidates));

        var schema = RiskFeatureSchemas.GetRequired(featureSchemaVersion);
        var observations = _loader.LoadObservations(dataDirectory, schema.Version, labelStrategy);
        var stratificationReference = labelStrategy == WithdrawalLabelStrategy.Within28Days
            ? null
            : _loader.LoadObservations(
                dataDirectory,
                schema.Version,
                WithdrawalLabelStrategy.Within28Days);
        var split = GroupDataSplitter.Split(
            observations,
            seed,
            trainingFraction,
            validationFraction,
            stratificationReference);

        if (GroupedCrossValidator.UsesCohortCalibration(schema))
        {
            var calibrator = CohortFeatureCalibrator.Fit(split.Training);
            calibrator.Apply(split.Training);
            calibrator.Apply(split.Validation);
        }

        GroupedCrossValidator.ApplyClassWeights(split.Training, weightStrategy);
        var trainingData = _mlContext.Data.LoadFromEnumerable(split.Training);
        var validationData = _mlContext.Data.LoadFromEnumerable(split.Validation);

        var results = new List<CandidateThresholdCurve>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var model = _trainer.Train(trainingData, _features.BuildPipeline(schema.FeatureNames), candidate);
            var rankingMetrics = _evaluator.Evaluate(model, validationData, threshold: .5f);
            var curve = _evaluator.BuildThresholdCurve(model, validationData, maximumPoints: 201);
            var bestPoints = _evaluator.FindBestPrecisionAtOrAboveRecall(
                model,
                validationData,
                RecallFloors);
            results.Add(new CandidateThresholdCurve
            {
                CandidateId = candidate.Id,
                Algorithm = candidate.Algorithm.ToString(),
                AreaUnderRocCurve = rankingMetrics.AreaUnderRocCurve,
                AreaUnderPrecisionRecallCurve = rankingMetrics.AreaUnderPrecisionRecallCurve,
                Curve = curve,
                BestPrecisionAtOrAboveRecall = BuildAttainability(bestPoints)
            });
        }

        var positives = split.Validation.Count(row => row.IsAtRisk);
        return new ThresholdAnalysisReport
        {
            FeatureSchemaVersion = schema.Version,
            AnalyzedAtUtc = DateTime.UtcNow,
            Seed = seed,
            TrainingWeightStrategy = weightStrategy.ToString(),
            LabelStrategy = labelStrategy.ToString(),
            SplitLabelStrategy = WithdrawalLabelStrategy.Within28Days.ToString(),
            ValidationRows = split.Validation.Count,
            ValidationPositives = positives,
            ValidationPositiveRate = split.Validation.Count == 0
                ? 0
                : positives / (double)split.Validation.Count,
            Candidates = results
        };
    }

    /// <summary>
    /// For each recall floor, the highest precision any threshold reaches while still meeting it.
    /// This is exactly the question a recall/precision gate pair asks of a model.
    /// </summary>
    private static IReadOnlyList<RecallFloorAttainability> BuildAttainability(
        IReadOnlyList<ThresholdCurvePoint?> bestPoints)
    {
        return RecallFloors.Select((floor, index) =>
        {
            var best = bestPoints[index];
            return new RecallFloorAttainability
            {
                RecallFloor = floor,
                IsAttainable = best is not null,
                BestPrecision = best?.Precision ?? 0,
                RecallAtBestPrecision = best?.Recall ?? 0,
                ThresholdAtBestPrecision = best?.Threshold ?? 0
            };
        }).ToArray();
    }
}

public sealed class ThresholdAnalysisReport
{
    public required string FeatureSchemaVersion { get; init; }
    public required DateTime AnalyzedAtUtc { get; init; }
    public required int Seed { get; init; }
    public string TrainingWeightStrategy { get; init; } = nameof(Training.TrainingWeightStrategy.Balanced);
    public string LabelStrategy { get; init; } = nameof(WithdrawalLabelStrategy.Within28Days);
    public string SplitLabelStrategy { get; init; } = nameof(WithdrawalLabelStrategy.Within28Days);
    public required int ValidationRows { get; init; }
    public required int ValidationPositives { get; init; }
    public required double ValidationPositiveRate { get; init; }
    public required IReadOnlyList<CandidateThresholdCurve> Candidates { get; init; }
}

public sealed class CandidateThresholdCurve
{
    public required string CandidateId { get; init; }
    public required string Algorithm { get; init; }
    public double AreaUnderRocCurve { get; init; }
    public double AreaUnderPrecisionRecallCurve { get; init; }
    public required IReadOnlyList<RecallFloorAttainability> BestPrecisionAtOrAboveRecall { get; init; }
    public required IReadOnlyList<ThresholdCurvePoint> Curve { get; init; }
}

public sealed class RecallFloorAttainability
{
    public required double RecallFloor { get; init; }
    public required bool IsAttainable { get; init; }
    public required double BestPrecision { get; init; }
    public required double RecallAtBestPrecision { get; init; }
    public required double ThresholdAtBestPrecision { get; init; }
}
