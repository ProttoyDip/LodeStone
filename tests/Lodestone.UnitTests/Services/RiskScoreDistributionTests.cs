using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class RiskScoreDistributionTests
{
    [Fact]
    public void From_BinsIntoTenFixedBucketsThatSumToOne()
    {
        var distribution = RiskScoreDistribution.From(new[] { 0.0, 0.05, 0.15, 0.55, 0.95, 1.0 });

        distribution.RowCount.Should().Be(6);
        distribution.BinFractions.Should().HaveCount(RiskScoreDistribution.BinCount);
        distribution.BinFractions.Sum().Should().BeApproximately(1d, 1e-9);
        distribution.BinFractions[0].Should().BeApproximately(2 / 6d, 1e-9, "0.0 and 0.05 both fall in the first bin");
        distribution.BinFractions[9].Should().BeApproximately(2 / 6d, 1e-9, "1.0 is clamped into the last bin, not an eleventh");
        distribution.Mean.Should().BeApproximately(0.45, 1e-9);
    }

    [Fact]
    public void Psi_IsZeroForIdenticalDistributionsAndGrowsWithShift()
    {
        var baseline = RiskScoreDistribution.From(Enumerable.Range(0, 1000).Select(i => i / 1000d).ToArray());
        var same = RiskScoreDistribution.From(Enumerable.Range(0, 1000).Select(i => i / 1000d).ToArray());
        var shifted = RiskScoreDistribution.From(Enumerable.Range(0, 1000).Select(i => 0.6 + 0.4 * i / 1000d).ToArray());

        baseline.PopulationStabilityIndexTo(same).Should().BeApproximately(0d, 1e-9);
        var psi = baseline.PopulationStabilityIndexTo(shifted);
        psi.Should().BeGreaterThan(0.25);
        RiskScoreDriftDto.Classify(psi).Should().Be(RiskDriftSeverity.Significant);
    }

    [Theory]
    [InlineData(0.05, RiskDriftSeverity.Stable)]
    [InlineData(0.10, RiskDriftSeverity.Moderate)]
    [InlineData(0.24, RiskDriftSeverity.Moderate)]
    [InlineData(0.25, RiskDriftSeverity.Significant)]
    public void Classify_UsesConventionalPsiCutoffs(double psi, RiskDriftSeverity expected)
        => RiskScoreDriftDto.Classify(psi).Should().Be(expected);

    [Fact]
    public void From_EmptyInputProducesAllZeroHistogram()
    {
        var distribution = RiskScoreDistribution.From(Array.Empty<double>());
        distribution.RowCount.Should().Be(0);
        distribution.BinFractions.Should().OnlyContain(fraction => fraction == 0d);
    }
}
