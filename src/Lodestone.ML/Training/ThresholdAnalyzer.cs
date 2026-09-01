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
        double trainingFraction = 0.70,
        double validationFraction = 0.15)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            throw new ArgumentException("At least one candidate is required.", nameof(candidates));

        var schema = RiskFeatureSchemas.GetRequired(featureSchemaVersion);
        var observations = _loader.LoadObservations(dataDirectory, schema.Version);
        var split = GroupDataSplitter.Split(observations, seed, trainingFraction, validationFraction);

        if (GroupedCrossValidator.UsesCohortCalibration(schema))
        {
            var calibrator = CohortFeatureCalibrator.Fit(split.Training);
            calibrator.Apply(split.Training);
            calibrator.Apply(split.Validation);
        }

        GroupedCrossValidator.ApplyClassWeights(split.Training);
        var trainingData = _mlContext.Data.LoadFromEnumerable(split.Training);
        var validationData = _mlContext.Data.LoadFromEnumerable(split.Validation);

        var results = new List<CandidateThresholdCurve>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var model = _trainer.Train(trainingData, _features.BuildPipeline(schema.FeatureNames), candidate);
            var curve = _evaluator.BuildThresholdCurve(model, validationData, maximumPoints: 201);
            results.Add(new CandidateThresholdCurve
            {
                CandidateId = candidate.Id,
                Algorithm = candidate.Algorithm.ToString(),
                Curve = curve,
                BestPrecisionAtOrAboveRecall = BuildAttainability(curve)
            });
        }

        var positives = split.Validation.Count(row => row.IsAtRisk);
        return new ThresholdAnalysisReport
        {
            FeatureSchemaVersion = schema.Version,
            AnalyzedAtUtc = DateTime.UtcNow,
            Seed = seed,
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
        IReadOnlyList<ThresholdCurvePoint> curve)
    {
        double[] floors = [.50, .55, .60, .65, .70, .75, .80, .85, .90];
        return floors.Select(floor =>
        {
            var feasible = curve.Where(point => point.Recall + 1e-12 >= floor).ToArray();
            var best = feasible.Length == 0
                ? null
                : feasible.OrderByDescending(point => point.Precision).First();
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
    public required int ValidationRows { get; init; }
    public required int ValidationPositives { get; init; }
    public required double ValidationPositiveRate { get; init; }
    public required IReadOnlyList<CandidateThresholdCurve> Candidates { get; init; }
}

public sealed class CandidateThresholdCurve
{
    public required string CandidateId { get; init; }
    public required string Algorithm { get; init; }
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
