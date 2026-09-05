using Lodestone.ML.Models;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace Lodestone.ML.Training;

/// <summary>Scores held-out rows, chooses a constrained validation threshold and reports test metrics.</summary>
public sealed class ModelEvaluator
{
    private readonly MLContext _mlContext;

    public ModelEvaluator(MLContext mlContext) => _mlContext = mlContext;

    public float SelectThreshold(
        ITransformer model,
        IDataView validationData,
        double minimumRecall = ModelQualityGates.MinimumRecall,
        double minimumPrecision = ModelQualityGates.MinimumPrecision)
    {
        var rows = Score(model, validationData);
        ValidateBothClasses(rows, "validation");

        var candidates = rows.Select(row => row.Probability)
            .Append(0f)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        ThresholdMetrics? best = null;
        foreach (var threshold in candidates)
        {
            var metrics = Calculate(rows, threshold);
            if (metrics.Recall + 1e-12 < minimumRecall || metrics.Precision + 1e-12 < minimumPrecision)
                continue;

            if (best is null
                || metrics.F1 > best.F1 + 1e-12
                || (NearlyEqual(metrics.F1, best.F1) && metrics.Recall > best.Recall + 1e-12)
                || (NearlyEqual(metrics.F1, best.F1) && NearlyEqual(metrics.Recall, best.Recall)
                    && metrics.Precision > best.Precision + 1e-12)
                || (NearlyEqual(metrics.F1, best.F1) && NearlyEqual(metrics.Recall, best.Recall)
                    && NearlyEqual(metrics.Precision, best.Precision) && threshold > best.Threshold))
            {
                best = metrics;
            }
        }

        if (best is null)
        {
            throw new ModelQualityGateException(
                $"No validation threshold satisfies recall >= {minimumRecall:F2} and precision >= {minimumPrecision:F2}.");
        }

        return best.Threshold;
    }

    public ModelMetrics Evaluate(ITransformer model, IDataView testData)
        => Evaluate(model, testData, 0.5f);

    public ModelMetrics Evaluate(ITransformer model, IDataView data, float threshold, string? modelVersion = null)
    {
        var scoredData = model.Transform(data);
        var rows = _mlContext.Data.CreateEnumerable<ScoredObservation>(scoredData, reuseRowObject: false).ToArray();
        ValidateBothClasses(rows, "evaluation");
        var thresholdMetrics = Calculate(rows, threshold);
        var builtIn = _mlContext.BinaryClassification.Evaluate(
            scoredData,
            labelColumnName: "Label",
            scoreColumnName: nameof(RiskPrediction.Score),
            probabilityColumnName: nameof(RiskPrediction.Probability));

        return new ModelMetrics
        {
            Accuracy = thresholdMetrics.Accuracy,
            AreaUnderRocCurve = builtIn.AreaUnderRocCurve,
            AreaUnderPrecisionRecallCurve = builtIn.AreaUnderPrecisionRecallCurve,
            F1Score = thresholdMetrics.F1,
            Precision = thresholdMetrics.Precision,
            Recall = thresholdMetrics.Recall,
            BrierScore = rows.Average(row =>
            {
                var label = row.Label ? 1d : 0d;
                return Math.Pow(row.Probability - label, 2);
            }),
            FalseAlertsPer100StudentWeeks = rows.Length == 0
                ? 0d
                : thresholdMetrics.FalsePositive * 100d / rows.Length,
            DecisionThreshold = threshold,
            RowCount = rows.Length,
            PositiveCount = rows.Count(row => row.Label),
            NegativeCount = rows.Count(row => !row.Label),
            TruePositive = thresholdMetrics.TruePositive,
            FalsePositive = thresholdMetrics.FalsePositive,
            TrueNegative = thresholdMetrics.TrueNegative,
            FalseNegative = thresholdMetrics.FalseNegative,
            ModelVersion = modelVersion
        };
    }

    public IReadOnlyList<ScoredObservation> Score(ITransformer model, IDataView data)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(data);
        return _mlContext.Data.CreateEnumerable<ScoredObservation>(model.Transform(data), reuseRowObject: false).ToArray();
    }

    public IReadOnlyList<ThresholdCurvePoint> BuildThresholdCurve(
        ITransformer model,
        IDataView data,
        int maximumPoints = 101)
    {
        if (maximumPoints < 2) throw new ArgumentOutOfRangeException(nameof(maximumPoints));
        var rows = Score(model, data);
        ValidateBothClasses(rows, "threshold curve");
        var thresholds = rows.Select(row => row.Probability)
            .Append(0f)
            .Append(1f)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (thresholds.Length > maximumPoints)
        {
            thresholds = Enumerable.Range(0, maximumPoints)
                .Select(index => thresholds[(int)Math.Round(index * (thresholds.Length - 1d) / (maximumPoints - 1d))])
                .Distinct()
                .ToArray();
        }

        return thresholds.Select(threshold =>
        {
            var values = Calculate(rows, threshold);
            return new ThresholdCurvePoint
            {
                Threshold = threshold,
                Precision = values.Precision,
                Recall = values.Recall,
                F1Score = values.F1,
                TruePositive = values.TruePositive,
                FalsePositive = values.FalsePositive,
                TrueNegative = values.TrueNegative,
                FalseNegative = values.FalseNegative
            };
        }).ToArray();
    }

    /// <summary>
    /// Finds the exact highest-precision operating point for every requested recall floor. Unlike
    /// the display curve, this sweeps every distinct model score in O(n log n) time and therefore
    /// cannot skip a narrow gate-satisfying threshold during downsampling.
    /// </summary>
    public IReadOnlyList<ThresholdCurvePoint?> FindBestPrecisionAtOrAboveRecall(
        ITransformer model,
        IDataView data,
        IReadOnlyList<double> recallFloors)
    {
        ArgumentNullException.ThrowIfNull(recallFloors);
        if (recallFloors.Count == 0)
            return [];
        if (recallFloors.Any(floor => !double.IsFinite(floor) || floor is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(recallFloors),
                "Recall floors must be between zero and one.");
        }

        var rows = Score(model, data);
        ValidateBothClasses(rows, "threshold frontier");
        var sorted = rows
            .OrderByDescending(row => row.Probability)
            .ToArray();
        var totalPositive = sorted.Count(row => row.Label);
        var totalNegative = sorted.Length - totalPositive;
        var best = new ThresholdCurvePoint?[recallFloors.Count];
        var truePositive = 0;
        var falsePositive = 0;

        var index = 0;
        while (index < sorted.Length)
        {
            var threshold = sorted[index].Probability;
            do
            {
                if (sorted[index].Label) truePositive++;
                else falsePositive++;
                index++;
            } while (index < sorted.Length && sorted[index].Probability.Equals(threshold));

            var falseNegative = totalPositive - truePositive;
            var trueNegative = totalNegative - falsePositive;
            var precision = truePositive / (double)(truePositive + falsePositive);
            var recall = truePositive / (double)totalPositive;
            var point = new ThresholdCurvePoint
            {
                Threshold = threshold,
                Precision = precision,
                Recall = recall,
                F1Score = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall),
                TruePositive = truePositive,
                FalsePositive = falsePositive,
                TrueNegative = trueNegative,
                FalseNegative = falseNegative
            };

            for (var floorIndex = 0; floorIndex < recallFloors.Count; floorIndex++)
            {
                if (recall + 1e-12 < recallFloors[floorIndex])
                    continue;
                if (best[floorIndex] is null || IsBetterPrecisionPoint(point, best[floorIndex]!))
                    best[floorIndex] = point;
            }
        }

        return best;
    }

    private static ThresholdMetrics Calculate(IReadOnlyList<ScoredObservation> rows, float threshold)
    {
        var tp = 0;
        var fp = 0;
        var tn = 0;
        var fn = 0;
        foreach (var row in rows)
        {
            var predicted = row.Probability >= threshold;
            if (predicted && row.Label) tp++;
            else if (predicted) fp++;
            else if (row.Label) fn++;
            else tn++;
        }

        var precision = tp + fp == 0 ? 0d : tp / (double)(tp + fp);
        var recall = tp + fn == 0 ? 0d : tp / (double)(tp + fn);
        var f1 = precision + recall == 0 ? 0d : 2 * precision * recall / (precision + recall);
        var accuracy = rows.Count == 0 ? 0d : (tp + tn) / (double)rows.Count;
        return new ThresholdMetrics(threshold, accuracy, precision, recall, f1, tp, fp, tn, fn);
    }

    private static void ValidateBothClasses(IReadOnlyList<ScoredObservation> rows, string partition)
    {
        if (rows.Count == 0)
            throw new InvalidDataException($"The {partition} partition contains no observations.");
        if (!rows.Any(row => row.Label) || !rows.Any(row => !row.Label))
            throw new InvalidDataException($"The {partition} partition must contain both withdrawal and non-withdrawal observations.");
        if (rows.Any(row => !float.IsFinite(row.Probability) || row.Probability is < 0 or > 1))
            throw new InvalidDataException($"The model produced an invalid probability in the {partition} partition.");
    }

    private static bool IsBetterPrecisionPoint(ThresholdCurvePoint candidate, ThresholdCurvePoint current)
        => candidate.Precision > current.Precision + 1e-12
           || (NearlyEqual(candidate.Precision, current.Precision)
               && candidate.Recall > current.Recall + 1e-12)
           || (NearlyEqual(candidate.Precision, current.Precision)
               && NearlyEqual(candidate.Recall, current.Recall)
               && candidate.Threshold > current.Threshold);

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 1e-12;

    private sealed record ThresholdMetrics(
        float Threshold,
        double Accuracy,
        double Precision,
        double Recall,
        double F1,
        int TruePositive,
        int FalsePositive,
        int TrueNegative,
        int FalseNegative);
}

public sealed class ScoredObservation
{
    [ColumnName("Label")]
    public bool Label { get; set; }

    public float Probability { get; set; }
    public float Score { get; set; }
}
