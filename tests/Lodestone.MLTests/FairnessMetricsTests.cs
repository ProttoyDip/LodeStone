using FluentAssertions;
using Lodestone.ML.Evaluation;
using Xunit;

namespace Lodestone.MLTests;

public sealed class FairnessMetricsTests
{
    [Fact]
    public void Compute_MatchesAHandCheckedConfusionMatrix()
    {
        // Four flagged (>= .5): two genuinely at risk, two not. Two below: one at risk, one not.
        var rows = new[]
        {
            Row(true, .90f), Row(true, .60f), Row(false, .70f), Row(false, .50f),
            Row(true, .40f), Row(false, .10f)
        };

        var metrics = FairnessMetrics.Compute("group", rows, threshold: .5);

        metrics.TruePositive.Should().Be(2);
        metrics.FalsePositive.Should().Be(2);
        metrics.FalseNegative.Should().Be(1);
        metrics.TrueNegative.Should().Be(1);
        metrics.Recall.Should().BeApproximately(2d / 3d, 1e-9);
        metrics.Precision.Should().BeApproximately(.5, 1e-9);
        metrics.FalsePositiveRate.Should().BeApproximately(2d / 3d, 1e-9);
        metrics.FalseNegativeRate.Should().BeApproximately(1d / 3d, 1e-9);
        metrics.BaseRate.Should().BeApproximately(.5, 1e-9);
        metrics.SelectionRate.Should().BeApproximately(2d / 3d, 1e-9);
    }

    [Fact]
    public void Compute_FlagsARowSittingExactlyOnTheThreshold()
    {
        // ModelEvaluator treats probability >= threshold as flagged. If the audit used > instead,
        // its numbers would drift from the published metrics for reasons no reader could see.
        var metrics = FairnessMetrics.Compute("group", [Row(true, .5f), Row(false, .4f)], threshold: .5);

        metrics.TruePositive.Should().Be(1);
        metrics.FalseNegative.Should().Be(0);
    }

    [Fact]
    public void AreaUnderRocCurve_IsOneWhenEveryPositiveOutranksEveryNegative()
        => FairnessMetrics.AreaUnderRocCurve([Row(false, .1f), Row(false, .2f), Row(true, .8f), Row(true, .9f)])
            .Should().BeApproximately(1d, 1e-9);

    [Fact]
    public void AreaUnderRocCurve_IsAHalfWhenEveryScoreIsIdentical()
    {
        // A model that separates nothing must not look perfect. This is what the averaged-rank
        // handling of ties buys, and getting it wrong would report 1.0 for a constant predictor.
        var rows = new[] { Row(true, .5f), Row(false, .5f), Row(true, .5f), Row(false, .5f) };

        FairnessMetrics.AreaUnderRocCurve(rows).Should().BeApproximately(.5, 1e-9);
    }

    [Fact]
    public void AreaUnderRocCurve_IsUndefinedForASingleClassGroup()
        => FairnessMetrics.AreaUnderRocCurve([Row(true, .9f), Row(true, .2f)]).Should().BeNull();

    [Fact]
    public void Gap_AndRatio_NeedTwoGroupsToMeanAnything()
    {
        var single = new[] { Metrics(recall: .5, selectionRate: .2) };

        FairnessMetrics.Gap(single, metrics => metrics.Recall).Should().BeNull();
        FairnessMetrics.Ratio(single, metrics => metrics.SelectionRate).Should().BeNull();
    }

    [Fact]
    public void Ratio_ReportsTheLeastSelectedGroupAgainstTheMost()
    {
        var groups = new[]
        {
            Metrics(recall: .8, selectionRate: .40),
            Metrics(recall: .5, selectionRate: .10),
            Metrics(recall: .6, selectionRate: .25)
        };

        FairnessMetrics.Gap(groups, metrics => metrics.Recall).Should().BeApproximately(.3, 1e-9);
        FairnessMetrics.Ratio(groups, metrics => metrics.SelectionRate).Should().BeApproximately(.25, 1e-9);
    }

    [Fact]
    public void Ratio_IsUndefinedWhenNoGroupIsEverSelected()
    {
        // Dividing by a zero selection rate would produce NaN or infinity and be rendered as a
        // fairness figure. Above a threshold no one reaches, there is simply nothing to compare.
        var groups = new[] { Metrics(recall: 0, selectionRate: 0), Metrics(recall: 0, selectionRate: 0) };

        FairnessMetrics.Ratio(groups, metrics => metrics.SelectionRate).Should().BeNull();
    }

    private static FairnessMetrics.Outcome Row(bool label, float probability) => new(label, probability);

    private static FairnessGroupMetrics Metrics(double recall, double selectionRate)
        => new() { Recall = recall, SelectionRate = selectionRate };
}
