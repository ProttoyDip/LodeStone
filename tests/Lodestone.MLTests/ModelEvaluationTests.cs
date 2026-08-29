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

    private sealed record PrescoredRow(
        [property: ColumnName("Label")] bool Label,
        float Probability,
        float Score,
        [property: ColumnName("PredictedLabel")] bool PredictedLabel);
}
