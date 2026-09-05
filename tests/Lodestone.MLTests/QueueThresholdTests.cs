using FluentAssertions;
using Lodestone.ML.Prediction;
using Xunit;

namespace Lodestone.MLTests;

/// <summary>
/// The queue threshold decides who a counselor sees. It is an operational capacity choice, not the
/// artifact's publication threshold, which is measured at the quality gate and would surface
/// roughly a third of all student-weeks.
/// </summary>
public sealed class QueueThresholdTests
{
    private const double ArtifactThreshold = 0.5283;

    [Fact]
    public void A_configured_threshold_replaces_the_artifact_threshold()
        => LoadedRiskModelPredictor.ResolveQueueThreshold(0.83, ArtifactThreshold)
            .Should().Be(0.83);

    [Fact]
    public void No_configuration_keeps_the_artifact_threshold()
        => LoadedRiskModelPredictor.ResolveQueueThreshold(null, ArtifactThreshold)
            .Should().Be(ArtifactThreshold);

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_unusable_configured_value_falls_back_rather_than_emptying_or_flooding_the_queue(double configured)
        => LoadedRiskModelPredictor.ResolveQueueThreshold(configured, ArtifactThreshold)
            .Should().Be(ArtifactThreshold);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void The_boundary_values_are_accepted(double configured)
        => LoadedRiskModelPredictor.ResolveQueueThreshold(configured, ArtifactThreshold)
            .Should().Be(configured);
}
