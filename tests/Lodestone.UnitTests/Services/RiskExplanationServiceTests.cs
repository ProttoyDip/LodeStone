using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class RiskExplanationServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task ExplainQueueEntryAsync_ReturnsNullWhenTheEntryIsNotOpenOrConsentIsGone()
    {
        var queue = new Mock<ICounselorQueueRepository>();
        queue.Setup(r => r.GetExplanationContextAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RiskExplanationContext?)null);

        var result = await Service(queue, explainer: Available()).ExplainQueueEntryAsync(42);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExplainQueueEntryAsync_ExplainsWithoutCallingTheModelWhenNoneIsLoaded()
    {
        var explainer = new Mock<IRiskModelExplainer>(MockBehavior.Strict);
        explainer.SetupGet(e => e.IsAvailable).Returns(false);

        var result = await Service(Repo(Context(populationSize: 10)), explainer).ExplainQueueEntryAsync(1);

        result!.Explanation.Should().BeNull();
        result.UnavailableReason.Should().Contain("No risk model is loaded");
        explainer.Verify(e => e.Explain(It.IsAny<RiskModelInput>(), It.IsAny<RiskExplanationBaseline>()), Times.Never);
    }

    [Fact]
    public async Task ExplainQueueEntryAsync_RefusesATooSmallComparisonGroup()
    {
        var explainer = Available();

        var result = await Service(Repo(Context(populationSize: RiskExplanationService.MinimumBaselinePopulation - 1)), explainer)
            .ExplainQueueEntryAsync(1);

        result!.Explanation.Should().BeNull();
        result.UnavailableReason.Should().Contain("too few");
        explainer.Verify(e => e.Explain(It.IsAny<RiskModelInput>(), It.IsAny<RiskExplanationBaseline>()), Times.Never);
    }

    [Fact]
    public async Task ExplainQueueEntryAsync_BuildsAMedianBaselineFromThePopulationAndDescribesIt()
    {
        RiskExplanationBaseline? seen = null;
        var explainer = Available();
        explainer.Setup(e => e.Explain(It.IsAny<RiskModelInput>(), It.IsAny<RiskExplanationBaseline>()))
            .Callback<RiskModelInput, RiskExplanationBaseline>((_, baseline) => seen = baseline)
            .Returns(new RiskExplanation(0.9, "m", RiskFeatureSchema.Withdrawal28DayV3, Array.Empty<RiskFactor>(), "d", null));

        var result = await Service(Repo(Context(populationSize: 7)), explainer).ExplainQueueEntryAsync(1);

        result!.Explanation.Should().NotBeNull();
        seen.Should().NotBeNull();
        seen!.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV3);
        seen.SampleSize.Should().Be(7);
        seen.Description.Should().Contain("7 consenting students");
        // Population values run 0..6 for RecentActiveDayRate (see Snapshot), so the median is 3 tenths.
        seen.TryGetValue("RecentActiveDayRate", out var median).Should().BeTrue();
        median.Should().BeApproximately(0.3f, 0.001f);
    }

    [Fact]
    public async Task ExplainQueueEntryAsync_ReportsASchemaMismatchInsteadOfThrowing()
    {
        var explainer = Available();
        explainer.Setup(e => e.Explain(It.IsAny<RiskModelInput>(), It.IsAny<RiskExplanationBaseline>()))
            .Returns((RiskExplanation?)null);

        var result = await Service(Repo(Context(populationSize: 6)), explainer).ExplainQueueEntryAsync(1);

        result!.Explanation.Should().BeNull();
        result.UnavailableReason.Should().Contain("different feature schema");
    }

    [Fact]
    public void RiskSnapshotModelInput_MapsAllSeventeenV3FeaturesInSchemaOrder()
    {
        var snapshot = Snapshot(studentId: 1, recentActiveDayRate: 0.5f);
        var input = RiskSnapshotModelInput.From(snapshot);

        input.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV3);
        input.FeatureValues.Should().HaveCount(RiskFeatureSchemas.GetRequired(RiskFeatureSchema.Withdrawal28DayV3).FeatureNames.Count);
        input.GetFeature("RecentActiveDayRate").Should().Be(0.5f);
        input.GetFeature("AssessmentMissStreak").Should().Be(2f);
    }

    private static Mock<IRiskModelExplainer> Available()
    {
        var explainer = new Mock<IRiskModelExplainer>();
        explainer.SetupGet(e => e.IsAvailable).Returns(true);
        return explainer;
    }

    private static Mock<ICounselorQueueRepository> Repo(RiskExplanationContext context)
    {
        var queue = new Mock<ICounselorQueueRepository>();
        queue.Setup(r => r.GetExplanationContextAsync(1, Now, RiskScoringPolicy.MaximumSnapshotAgeDays, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        return queue;
    }

    private static RiskExplanationContext Context(int populationSize)
        => new(
            new RiskQueueItemDto(1, 1, "Student", RiskLevel.High, false, Now, "AAA", 0.9, Now),
            Snapshot(studentId: 1, recentActiveDayRate: 0.1f),
            Enumerable.Range(0, populationSize).Select(i => Snapshot(studentId: i + 100, recentActiveDayRate: i / 10f)).ToArray());

    private static RiskFeatureSnapshot Snapshot(int studentId, float recentActiveDayRate)
        => new()
        {
            Id = studentId,
            StudentProfileId = studentId,
            FeatureSchemaVersion = RiskFeatureSchema.Withdrawal28DayV3,
            ObservedDays = 28,
            WindowEndUtc = Now.AddDays(-1),
            RecentActiveDayRate = recentActiveDayRate,
            PriorActiveDayRate = 0.4f,
            ActiveDayRateTrend = -0.1f,
            RecentCourseClickRate = 3f,
            PriorCourseClickRate = 4f,
            CourseClickRateTrend = -0.2f,
            InactivityStreakDays = 3f,
            AssessmentDueRate = 0.5f,
            AssessmentOnTimeRate = 0.6f,
            AssessmentLateOrMissingRate = 0.4f,
            CourseProgressRatio = 0.3f,
            CohortActivityPercentile = 0.2f,
            ActivityTrendAcceleration = 0.05f,
            ClickVolatility = 1.5f,
            ForumEngagementShare = 0.1f,
            InactiveWeekRate = 0.25f,
            AssessmentMissStreak = 2f
        };

    private static RiskExplanationService Service(Mock<ICounselorQueueRepository> queue, Mock<IRiskModelExplainer> explainer)
        => new(queue.Object, explainer.Object, new FixedTimeProvider(new DateTimeOffset(Now)));
}
