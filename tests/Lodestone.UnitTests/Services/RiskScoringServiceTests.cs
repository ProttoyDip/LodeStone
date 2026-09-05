using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class RiskScoringServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 9, 30, 0, TimeSpan.Zero);
    private static readonly RiskModelDescriptor ValidDescriptor = new(
        "model-v1",
        RiskFeatureSchema.Withdrawal28DayV1,
        RiskFeatureSchema.Withdrawal28DayObservedDays,
        0.62);

    [Theory]
    [InlineData("", RiskFeatureSchema.Withdrawal28DayV1, 28, 0.5)]
    [InlineData("model-v1", "unsupported-schema", 28, 0.5)]
    [InlineData("model-v1", RiskFeatureSchema.Withdrawal28DayV1, 14, 0.5)]
    [InlineData("model-v1", RiskFeatureSchema.Withdrawal28DayV1, 28, -0.1)]
    [InlineData("model-v1", RiskFeatureSchema.Withdrawal28DayV1, 28, 1.1)]
    public async Task ScoreSnapshotAsync_RejectsInvalidModelDescriptor(
        string modelVersion,
        string schema,
        int observedDays,
        double queueThreshold)
    {
        var fixture = new Fixture(new RiskModelDescriptor(
            modelVersion,
            schema,
            observedDays,
            queueThreshold));

        var action = () => fixture.Service.ScoreSnapshotAsync(1);

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Snapshots.Verify(value => value.GetByIdForScoringAsync(
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0.249, RiskLevel.Low)]
    [InlineData(0.25, RiskLevel.Moderate)]
    [InlineData(0.50, RiskLevel.High)]
    [InlineData(0.75, RiskLevel.Critical)]
    [InlineData(1.0, RiskLevel.Critical)]
    public async Task ScoreSnapshotAsync_UsesFixedDisplayBandsAndForwardsModelQueueThreshold(
        double probability,
        RiskLevel expectedLevel)
    {
        var fixture = new Fixture(ValidDescriptor);
        var snapshot = ValidSnapshot();
        fixture.Snapshots.Setup(value => value.GetByIdForScoringAsync(
                snapshot.Id,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        fixture.Predictor.Setup(value => value.Predict(It.IsAny<RiskModelInput>()))
            .Returns(new RiskModelPrediction(probability));
        RiskLevel? persistedLevel = null;
        RiskModelDescriptor? persistedDescriptor = null;
        fixture.Scoring.Setup(value => value.PersistAsync(
                snapshot,
                It.IsAny<RiskModelDescriptor>(),
                probability,
                It.IsAny<RiskLevel>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback<RiskFeatureSnapshot, RiskModelDescriptor, double, RiskLevel, DateTime, int?, CancellationToken>(
                (_, descriptor, _, level, _, _, _) =>
                {
                    persistedLevel = level;
                    persistedDescriptor = descriptor;
                })
            .ReturnsAsync(new RiskScorePersistenceResult(
                RiskScorePersistenceOutcome.Created,
                null,
                false,
                false));

        var result = await fixture.Service.ScoreSnapshotAsync(snapshot.Id);

        result.Scored.Should().BeTrue();
        persistedLevel.Should().Be(expectedLevel);
        persistedDescriptor!.QueueThreshold.Should().Be(0.62);
    }

    [Fact]
    public async Task ScoreSnapshotAsync_MapsTheExplicitV2FeatureContractWithoutUsingV1Fields()
    {
        var descriptor = new RiskModelDescriptor(
            "model-v2",
            RiskFeatureSchema.Withdrawal28DayV2,
            RiskFeatureSchema.Withdrawal28DayObservedDays,
            .50)
        {
            FeatureNames = RiskFeatureSchemas.Withdrawal28DayV2.FeatureNames
        };
        var fixture = new Fixture(descriptor);
        var snapshot = ValidV2Snapshot();
        fixture.Snapshots.Setup(value => value.GetByIdForScoringAsync(
                snapshot.Id,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        RiskModelInput? observedInput = null;
        fixture.Predictor.Setup(value => value.Predict(It.IsAny<RiskModelInput>()))
            .Callback<RiskModelInput>(input => observedInput = input)
            .Returns(new RiskModelPrediction(.8));
        fixture.Scoring.Setup(value => value.PersistAsync(
                snapshot,
                descriptor,
                .8,
                RiskLevel.Critical,
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskScorePersistenceResult(
                RiskScorePersistenceOutcome.Created,
                null,
                true,
                false));

        var result = await fixture.Service.ScoreSnapshotAsync(snapshot.Id);

        result.Scored.Should().BeTrue();
        observedInput.Should().NotBeNull();
        observedInput!.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV2);
        observedInput.FeatureValues.Should().Equal(
            .1f, .2f, -.1f, 1f, 2f, -1f, 10f, .05f, .5f, .5f, .4f, .25f);
        fixture.Notifier.Verify(value => value.NotifyChangedAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("active-rate")]
    [InlineData("activity-span")]
    [InlineData("recency")]
    [InlineData("negative-count")]
    public async Task ScoreSnapshotAsync_RejectsInvalidFeaturesBeforeInference(string invalidFeature)
    {
        var fixture = new Fixture(ValidDescriptor);
        var snapshot = ValidSnapshot();
        switch (invalidFeature)
        {
            case "active-rate": snapshot.ActiveDayRate = 1.01f; break;
            case "activity-span": snapshot.ActivitySpanDays = 29; break;
            case "recency": snapshot.DaysSinceLastAccess = float.NaN; break;
            case "negative-count": snapshot.CourseInteractionCount = -1; break;
        }
        fixture.Snapshots.Setup(value => value.GetByIdForScoringAsync(
                snapshot.Id,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var action = () => fixture.Service.ScoreSnapshotAsync(snapshot.Id);

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Predictor.Verify(value => value.Predict(It.IsAny<RiskModelInput>()), Times.Never);
        fixture.Scoring.Verify(value => value.PersistAsync(
            It.IsAny<RiskFeatureSnapshot>(),
            It.IsAny<RiskModelDescriptor>(),
            It.IsAny<double>(),
            It.IsAny<RiskLevel>(),
            It.IsAny<DateTime>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task ScoreSnapshotAsync_RejectsInvalidPredictionProbability(double probability)
    {
        var fixture = new Fixture(ValidDescriptor);
        var snapshot = ValidSnapshot();
        fixture.Snapshots.Setup(value => value.GetByIdForScoringAsync(
                snapshot.Id,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        fixture.Predictor.Setup(value => value.Predict(It.IsAny<RiskModelInput>()))
            .Returns(new RiskModelPrediction(probability));

        var action = () => fixture.Service.ScoreSnapshotAsync(snapshot.Id);

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Scoring.Verify(value => value.PersistAsync(
            It.IsAny<RiskFeatureSnapshot>(),
            It.IsAny<RiskModelDescriptor>(),
            It.IsAny<double>(),
            It.IsAny<RiskLevel>(),
            It.IsAny<DateTime>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunPendingSnapshotsAsync_IsolatesSnapshotFailureAndCompletesPartialRun()
    {
        var fixture = new Fixture(ValidDescriptor);
        var run = RunningRun(candidateCount: 2);
        fixture.Snapshots.Setup(value => value.GetPendingIdsAsync(
                ValidDescriptor,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 10, 20 });
        fixture.Scoring.Setup(value => value.StartRunAsync(
                ValidDescriptor,
                2,
                "admin-user",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        fixture.Snapshots.Setup(value => value.GetByIdForScoringAsync(
                10,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidSnapshot(10));
        fixture.Snapshots.Setup(value => value.GetByIdForScoringAsync(
                20,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidSnapshot(20));
        fixture.Predictor.SetupSequence(value => value.Predict(It.IsAny<RiskModelInput>()))
            .Throws(new InvalidOperationException("bad row"))
            .Returns(new RiskModelPrediction(0.8));
        fixture.Scoring.Setup(value => value.PersistAsync(
                It.Is<RiskFeatureSnapshot>(snapshot => snapshot.Id == 20),
                ValidDescriptor,
                0.8,
                RiskLevel.Critical,
                It.IsAny<DateTime>(),
                run.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskScorePersistenceResult(
                RiskScorePersistenceOutcome.Created,
                null,
                true,
                false));
        fixture.Scoring.Setup(value => value.CompleteRunAsync(
                run,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await fixture.Service.RunPendingSnapshotsAsync(" admin-user ");

        result.Status.Should().Be(RiskScoringRunStatus.PartiallyCompleted);
        result.ScoredCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.QueueCreatedCount.Should().Be(1);
        result.FailureSummary.Should().Contain("Snapshot 10: InvalidOperationException");
        fixture.Notifier.Verify(value => value.NotifyChangedAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunPendingSnapshotsAsync_TerminalizesRunWhenCancellationArrivesBetweenRows()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(ValidDescriptor);
        var run = RunningRun(candidateCount: 2);
        fixture.Snapshots.Setup(value => value.GetPendingIdsAsync(
                ValidDescriptor,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                null,
                cancellation.Token))
            .ReturnsAsync(new[] { 10, 20 });
        fixture.Scoring.Setup(value => value.StartRunAsync(
                ValidDescriptor,
                2,
                null,
                cancellation.Token))
            .ReturnsAsync(run);
        fixture.Snapshots.Setup(value => value.GetByIdForScoringAsync(
                10,
                It.IsAny<DateTime>(),
                RiskScoringPolicy.MaximumSnapshotAgeDays,
                cancellation.Token))
            .ReturnsAsync(ValidSnapshot(10));
        fixture.Predictor.Setup(value => value.Predict(It.IsAny<RiskModelInput>()))
            .Returns(new RiskModelPrediction(0.4));
        fixture.Scoring.Setup(value => value.PersistAsync(
                It.IsAny<RiskFeatureSnapshot>(),
                ValidDescriptor,
                0.4,
                RiskLevel.Moderate,
                It.IsAny<DateTime>(),
                run.Id,
                cancellation.Token))
            .Callback(() => cancellation.Cancel())
            .ReturnsAsync(new RiskScorePersistenceResult(
                RiskScorePersistenceOutcome.Created,
                null,
                false,
                false));
        fixture.Scoring.Setup(value => value.CompleteRunAsync(run, CancellationToken.None))
            .Returns(Task.CompletedTask);

        var action = () => fixture.Service.RunPendingSnapshotsAsync(null, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        run.Status.Should().Be(RiskScoringRunStatus.Cancelled);
        run.CompletedAtUtc.Should().Be(Now.UtcDateTime);
        run.ScoredCount.Should().Be(1);
        run.FailureSummary.Should().Be("The scoring run was cancelled.");
        fixture.Scoring.Verify(value => value.CompleteRunAsync(run, CancellationToken.None), Times.Once);
        fixture.Snapshots.Verify(value => value.GetByIdForScoringAsync(
            20,
            It.IsAny<DateTime>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static RiskFeatureSnapshot ValidSnapshot(int id = 1)
        => new()
        {
            Id = id,
            StudentProfileId = 100,
            CourseKey = "COURSE-A",
            WindowEndUtc = Now.UtcDateTime.AddDays(-1),
            ObservedDays = RiskFeatureSchema.Withdrawal28DayObservedDays,
            FeatureSchemaVersion = RiskFeatureSchema.Withdrawal28DayV1,
            ActiveDayRate = 0.5f,
            ActivitySpanDays = 20,
            DaysSinceLastAccess = 2,
            ForumInteractionCount = 3,
            CourseInteractionCount = 40,
            LateOrMissingAssignmentCount = 1
        };

    private static RiskFeatureSnapshot ValidV2Snapshot(int id = 1)
        => new()
        {
            Id = id,
            StudentProfileId = 100,
            CourseKey = "COURSE-A",
            WindowEndUtc = Now.UtcDateTime.AddDays(-1),
            ObservedDays = RiskFeatureSchema.Withdrawal28DayObservedDays,
            FeatureSchemaVersion = RiskFeatureSchema.Withdrawal28DayV2,
            RecentActiveDayRate = .1f,
            PriorActiveDayRate = .2f,
            ActiveDayRateTrend = -.1f,
            RecentCourseClickRate = 1,
            PriorCourseClickRate = 2,
            CourseClickRateTrend = -1,
            InactivityStreakDays = 10,
            AssessmentDueRate = .05f,
            AssessmentOnTimeRate = .5f,
            AssessmentLateOrMissingRate = .5f,
            CourseProgressRatio = .4f,
            CohortActivityPercentile = .25f
        };

    private static RiskScoringRun RunningRun(int candidateCount)
        => new()
        {
            Id = 9,
            RunKey = Guid.NewGuid(),
            ModelVersion = ValidDescriptor.ModelVersion,
            FeatureSchemaVersion = ValidDescriptor.FeatureSchemaVersion,
            StartedAtUtc = Now.UtcDateTime,
            Status = RiskScoringRunStatus.Running,
            CandidateCount = candidateCount
        };

    private sealed class Fixture
    {
        public Fixture(RiskModelDescriptor descriptor)
        {
            Predictor.SetupGet(value => value.Descriptor).Returns(descriptor);
            Service = new RiskScoringService(
                Predictor.Object,
                Snapshots.Object,
                Scoring.Object,
                Queue.Object,
                Notifier.Object,
                new FixedTimeProvider(Now));
        }

        public Mock<IRiskModelPredictor> Predictor { get; } = new();
        public Mock<IRiskFeatureSnapshotRepository> Snapshots { get; } = new();
        public Mock<IRiskScoringRepository> Scoring { get; } = new();
        public Mock<ICounselorQueueRepository> Queue { get; } = new();
        public Mock<IRiskQueueNotifier> Notifier { get; } = new();
        public RiskScoringService Service { get; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
