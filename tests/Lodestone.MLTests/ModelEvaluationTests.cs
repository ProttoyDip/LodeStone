using FluentAssertions;
using Lodestone.ML.Training;
using Microsoft.ML;
using Microsoft.ML.Data;
using Xunit;

namespace Lodestone.MLTests;

public sealed class ModelEvaluationTests
{
    [Fact]
    public void SelectThreshold_maximizes_f1_subject_to_recall_and_precision_constraints()
    {
        var ml = new MLContext(seed: 42);
        var data = ml.Data.LoadFromEnumerable(new[]
        {
            new PrescoredRow(true, .90f, .90f, true),
            new PrescoredRow(true, .80f, .80f, true),
            new PrescoredRow(true, .40f, .40f, false),
            new PrescoredRow(false, .70f, .70f, true),
            new PrescoredRow(false, .30f, .30f, false)
        });
        var identity = ml.Transforms.CopyColumns("Probe", "Score").Fit(data);
        var evaluator = new ModelEvaluator(ml);

        var threshold = evaluator.SelectThreshold(identity, data, minimumRecall: .66, minimumPrecision: .60);

        threshold.Should().BeApproximately(.40f, .000001f);
        var metrics = evaluator.Evaluate(identity, data, threshold);
        metrics.Recall.Should().Be(1);
        metrics.Precision.Should().Be(.75);
        metrics.F1Score.Should().BeApproximately(6d / 7d, 0.000001);
    }

    [Fact]
    public void SelectThreshold_rejects_validation_data_when_no_candidate_meets_both_constraints()
    {
        var ml = new MLContext(seed: 42);
        var data = ml.Data.LoadFromEnumerable(new[]
        {
            new PrescoredRow(true, .40f, .40f, false),
            new PrescoredRow(true, .30f, .30f, false),
            new PrescoredRow(false, .90f, .90f, true),
            new PrescoredRow(false, .80f, .80f, true)
        });
        var identity = ml.Transforms.CopyColumns("Probe", "Score").Fit(data);
        var evaluator = new ModelEvaluator(ml);

        var act = () => evaluator.SelectThreshold(identity, data, minimumRecall: 1, minimumPrecision: .75);

        act.Should().Throw<ModelQualityGateException>()
            .WithMessage("*No validation threshold*");
    }

    [Fact]
    public void Exact_recall_frontier_evaluates_every_distinct_score_and_keeps_ties_atomic()
    {
        var ml = new MLContext(seed: 42);
        var rows = Enumerable.Range(0, 250)
            .Select(index => new PrescoredRow(
                Label: index is 123 or 124,
                Probability: 1f - index / 300f,
                Score: 1f - index / 300f,
                PredictedLabel: false))
            .Append(new PrescoredRow(true, .50f, .50f, false))
            .Concat(Enumerable.Range(0, 200)
                .Select(_ => new PrescoredRow(false, .50f, .50f, false)))
            .ToArray();
        var data = ml.Data.LoadFromEnumerable(rows);
        var identity = ml.Transforms.CopyColumns("Probe", "Score").Fit(data);
        var evaluator = new ModelEvaluator(ml);

        var best = evaluator.FindBestPrecisionAtOrAboveRecall(identity, data, [.66]);

        best.Should().ContainSingle();
        best[0].Should().NotBeNull();
        best[0]!.Recall.Should().BeApproximately(2d / 3d, .000001);
        best[0]!.Threshold.Should().BeApproximately(1f - 124f / 300f, .000001f);
        best[0]!.TruePositive.Should().Be(2);
        best[0]!.FalsePositive.Should().Be(123);
    }

    private sealed record PrescoredRow(
        [property: ColumnName("Label")] bool Label,
        float Probability,
        float Score,
        [property: ColumnName("PredictedLabel")] bool PredictedLabel);
}
