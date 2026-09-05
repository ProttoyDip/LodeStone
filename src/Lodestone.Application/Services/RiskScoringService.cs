using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

/// <summary>Validates feature/model compatibility and orchestrates idempotent scoring.</summary>
public sealed class RiskScoringService : IRiskScoringService
{
    private const int MaximumFailureSummaryLength = 2_000;

    private readonly IRiskModelPredictor _predictor;
    private readonly IRiskFeatureSnapshotRepository _snapshots;
    private readonly IRiskScoringRepository _scoring;
    private readonly ICounselorQueueRepository _queue;
    private readonly IRiskQueueNotifier _queueNotifier;
    private readonly TimeProvider _timeProvider;

    public RiskScoringService(
        IRiskModelPredictor predictor,
        IRiskFeatureSnapshotRepository snapshots,
        IRiskScoringRepository scoring,
        ICounselorQueueRepository queue,
        IRiskQueueNotifier queueNotifier,
        TimeProvider timeProvider)
    {
        _predictor = predictor;
        _snapshots = snapshots;
        _scoring = scoring;
        _queue = queue;
        _queueNotifier = queueNotifier;
        _timeProvider = timeProvider;
    }

    public async Task<RiskScoreDto> ScoreStudentAsync(
        int studentProfileId,
        CancellationToken cancellationToken = default)
    {
        var descriptor = ValidatedDescriptor();
        var asOfUtc = UtcNow;
        var pendingIds = await _snapshots.GetPendingIdsAsync(
            descriptor,
            asOfUtc,
            RiskScoringPolicy.MaximumSnapshotAgeDays,
            studentProfileId,
            cancellationToken);
        if (pendingIds.Count == 0)
            throw new InvalidOperationException("The student has no eligible unscored risk snapshots.");

        var result = await ScoreSnapshotCoreAsync(pendingIds[0], null, descriptor, cancellationToken);
        if (result.Persistence.RiskScore is null)
            throw new InvalidOperationException(result.SkipReason ?? "The snapshot is no longer eligible for scoring.");

        return ToDto(result.Persistence.RiskScore, result.Snapshot);
    }

    public async Task ScoreAllStudentsAsync(CancellationToken cancellationToken = default)
        => await RunPendingSnapshotsAsync(null, cancellationToken);

    public Task<IReadOnlyList<RiskQueueItemDto>> GetOpenQueueAsync(
        CancellationToken cancellationToken = default)
        => _queue.GetOpenAsync(cancellationToken);

    public async Task<RiskScoringResultDto> ScoreSnapshotAsync(
        int snapshotId,
        int? scoringRunId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ScoreSnapshotCoreAsync(
            snapshotId,
            scoringRunId,
            ValidatedDescriptor(),
            cancellationToken);
        return ToResultDto(snapshotId, result);
    }

    public async Task<RiskScoringRunDto> RunPendingSnapshotsAsync(
        string? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = ValidatedDescriptor();
        var asOfUtc = UtcNow;
        var pendingIds = await _snapshots.GetPendingIdsAsync(
            descriptor,
            asOfUtc,
            RiskScoringPolicy.MaximumSnapshotAgeDays,
            null,
            cancellationToken);
        var run = await _scoring.StartRunAsync(
            descriptor,
            pendingIds.Count,
            NormalizeActor(actorUserId),
            cancellationToken);

        var failures = new List<string>();
        try
        {
            foreach (var snapshotId in pendingIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await ScoreSnapshotCoreAsync(snapshotId, run.Id, descriptor, cancellationToken);
                    switch (result.Persistence.Outcome)
                    {
                        case RiskScorePersistenceOutcome.Created:
                            run.ScoredCount++;
                            if (result.Persistence.QueueCreated) run.QueueCreatedCount++;
                            if (result.Persistence.QueueEscalated) run.QueueEscalatedCount++;
                            break;
                        case RiskScorePersistenceOutcome.AlreadyExists:
                        case RiskScorePersistenceOutcome.NotEligible:
                            run.SkippedCount++;
                            break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    run.FailedCount++;
                    if (failures.Count < 10)
                        failures.Add($"Snapshot {snapshotId}: {exception.GetType().Name}");
                }
            }

            run.CompletedAtUtc = UtcNow;
            run.Status = run.FailedCount switch
            {
                0 => RiskScoringRunStatus.Completed,
                _ when run.ScoredCount == 0 && run.SkippedCount == 0 => RiskScoringRunStatus.Failed,
                _ => RiskScoringRunStatus.PartiallyCompleted
            };
            run.FailureSummary = failures.Count == 0
                ? null
                : Truncate(string.Join("; ", failures), MaximumFailureSummaryLength);

            await _scoring.CompleteRunAsync(run, cancellationToken);
            return ToRunDto(run);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.CompletedAtUtc = UtcNow;
            run.Status = RiskScoringRunStatus.Cancelled;
            run.FailureSummary = "The scoring run was cancelled.";
            await _scoring.CompleteRunAsync(run, CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (run.Status == RiskScoringRunStatus.Running)
        {
            run.CompletedAtUtc = UtcNow;
            run.Status = RiskScoringRunStatus.Failed;
            run.FailureSummary = Truncate(
                $"The scoring run failed: {exception.GetType().Name}.",
                MaximumFailureSummaryLength);
            await _scoring.CompleteRunAsync(run, CancellationToken.None);
            throw;
        }
    }

    private async Task<ScoreSnapshotCoreResult> ScoreSnapshotCoreAsync(
        int snapshotId,
        int? scoringRunId,
        RiskModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var snapshot = await _snapshots.GetByIdForScoringAsync(
            snapshotId,
            UtcNow,
            RiskScoringPolicy.MaximumSnapshotAgeDays,
            cancellationToken);
        if (snapshot is null)
        {
            return new ScoreSnapshotCoreResult(
                new RiskScorePersistenceResult(
                    RiskScorePersistenceOutcome.NotEligible,
                    null,
                    false,
                    false),
                null,
                "Snapshot not found, inactive, or monitoring consent is absent.");
        }

        EnsureSnapshotCompatibility(snapshot, descriptor);
        RiskModelInput input;
        try
        {
            input = ToModelInput(snapshot);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException("The stored risk snapshot contains invalid versioned feature values.", exception);
        }
        ValidateInput(input, descriptor.ObservedDays);
        var prediction = _predictor.Predict(input)
            ?? throw new InvalidOperationException("The risk model returned no prediction.");
        if (!double.IsFinite(prediction.Probability) || prediction.Probability is < 0 or > 1)
            throw new InvalidOperationException("The risk model returned an invalid probability.");

        var persistence = await _scoring.PersistAsync(
            snapshot,
            descriptor,
            prediction.Probability,
            ToRiskLevel(prediction.Probability),
            UtcNow,
            scoringRunId,
            cancellationToken);

        if (persistence.QueueCreated || persistence.QueueEscalated)
            await _queueNotifier.NotifyChangedAsync(cancellationToken);

        return new ScoreSnapshotCoreResult(
            persistence,
            snapshot,
            persistence.Outcome == RiskScorePersistenceOutcome.AlreadyExists
                ? "An idempotent score already exists for this snapshot and model."
                : persistence.Outcome == RiskScorePersistenceOutcome.NotEligible
                    ? "Monitoring consent was withdrawn before the score could be persisted."
                    : null);
    }

    private RiskModelDescriptor ValidatedDescriptor()
    {
        var descriptor = _predictor.Descriptor
            ?? throw new InvalidOperationException("The risk model descriptor is unavailable.");
        if (string.IsNullOrWhiteSpace(descriptor.ModelVersion))
            throw new InvalidOperationException("The risk model version is required.");
        if (!RiskFeatureSchemas.TryGet(descriptor.FeatureSchemaVersion, out var schema))
            throw new InvalidOperationException(
                $"The model requires unsupported feature schema '{descriptor.FeatureSchemaVersion}'.");
        if (descriptor.ObservedDays != schema.ObservedDays)
            throw new InvalidOperationException("The model descriptor observation window does not match its feature schema.");
        if (descriptor.FeatureNames.Count > 0 &&
            !descriptor.FeatureNames.SequenceEqual(schema.FeatureNames, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The model descriptor feature names do not match its feature schema.");
        }
        if (!double.IsFinite(descriptor.QueueThreshold) || descriptor.QueueThreshold is < 0 or > 1)
            throw new InvalidOperationException("The model queue threshold must be between zero and one.");
        return descriptor with { ModelVersion = descriptor.ModelVersion.Trim() };
    }

    private static void EnsureSnapshotCompatibility(
        RiskFeatureSnapshot snapshot,
        RiskModelDescriptor descriptor)
    {
        if (!string.Equals(snapshot.FeatureSchemaVersion, descriptor.FeatureSchemaVersion, StringComparison.Ordinal) ||
            snapshot.ObservedDays != descriptor.ObservedDays)
        {
            throw new InvalidOperationException("The snapshot feature schema does not match the loaded model.");
        }
    }

    private static RiskModelInput ToModelInput(RiskFeatureSnapshot snapshot)
        => new(snapshot.FeatureSchemaVersion, SnapshotFeatureValues(snapshot));

    private static void ValidateInput(RiskModelInput input, int observedDays)
    {
        var values = input.FeatureValues;
        if (values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Risk model features must be finite.");

        if (string.Equals(input.FeatureSchemaVersion, RiskFeatureSchema.Withdrawal28DayV2, StringComparison.Ordinal))
        {
            // V2 contains signed trend fields; all other values remain non-negative.
            if (values.Where((_, index) => index is not 2 and not 5).Any(value => value < 0) ||
                values.Where((_, index) => index is 2 or 5).Any(value => value is < -1 or > 1))
                throw new InvalidOperationException("Risk model features contain invalid values.");
        }
        else if (values.Any(value => value < 0))
            throw new InvalidOperationException("Risk model features must be non-negative.");

        if (string.Equals(input.FeatureSchemaVersion, RiskFeatureSchema.Withdrawal28DayV1, StringComparison.Ordinal))
        {
            if (input.ActiveDayRate > 1)
                throw new InvalidOperationException("ActiveDayRate must be between zero and one.");
            if (input.ActivitySpanDays > observedDays || input.DaysSinceLastAccess > observedDays)
                throw new InvalidOperationException("Day-based features cannot exceed the observation window.");
            return;
        }

        if (input.GetFeature("RecentActiveDayRate") > 1 || input.GetFeature("PriorActiveDayRate") > 1 ||
            input.GetFeature("InactivityStreakDays") > observedDays ||
            input.GetFeature("AssessmentOnTimeRate") > 1 ||
            input.GetFeature("AssessmentLateOrMissingRate") > 1 ||
            input.GetFeature("CourseProgressRatio") > 1 ||
            input.GetFeature("CohortActivityPercentile") > 1)
        {
            throw new InvalidOperationException("Versioned risk-model feature values are outside their valid range.");
        }
    }

    private static IReadOnlyList<float> SnapshotFeatureValues(RiskFeatureSnapshot snapshot)
        => snapshot.FeatureSchemaVersion switch
        {
            RiskFeatureSchema.Withdrawal28DayV1 =>
            [
                snapshot.ActiveDayRate,
                snapshot.ActivitySpanDays,
                snapshot.DaysSinceLastAccess,
                snapshot.ForumInteractionCount,
                snapshot.CourseInteractionCount,
                snapshot.LateOrMissingAssignmentCount
            ],
            RiskFeatureSchema.Withdrawal28DayV2 =>
            [
                Required(snapshot.RecentActiveDayRate, nameof(snapshot.RecentActiveDayRate)),
                Required(snapshot.PriorActiveDayRate, nameof(snapshot.PriorActiveDayRate)),
                Required(snapshot.ActiveDayRateTrend, nameof(snapshot.ActiveDayRateTrend)),
                Required(snapshot.RecentCourseClickRate, nameof(snapshot.RecentCourseClickRate)),
                Required(snapshot.PriorCourseClickRate, nameof(snapshot.PriorCourseClickRate)),
                Required(snapshot.CourseClickRateTrend, nameof(snapshot.CourseClickRateTrend)),
                Required(snapshot.InactivityStreakDays, nameof(snapshot.InactivityStreakDays)),
                Required(snapshot.AssessmentDueRate, nameof(snapshot.AssessmentDueRate)),
                Required(snapshot.AssessmentOnTimeRate, nameof(snapshot.AssessmentOnTimeRate)),
                Required(snapshot.AssessmentLateOrMissingRate, nameof(snapshot.AssessmentLateOrMissingRate)),
                Required(snapshot.CourseProgressRatio, nameof(snapshot.CourseProgressRatio)),
                Required(snapshot.CohortActivityPercentile, nameof(snapshot.CohortActivityPercentile))
            ],
            _ => throw new InvalidOperationException("The snapshot has an unsupported feature schema.")
        };

    private static float Required(float? value, string name)
        => value ?? throw new InvalidOperationException($"The snapshot is missing required feature '{name}'.");

    private static RiskLevel ToRiskLevel(double probability)
        => probability switch
        {
            < RiskThresholdConstants.LowUpperBound => RiskLevel.Low,
            < RiskThresholdConstants.ModerateUpperBound => RiskLevel.Moderate,
            < RiskThresholdConstants.HighUpperBound => RiskLevel.High,
            _ => RiskLevel.Critical
        };

    private static RiskScoreDto ToDto(RiskScore score, RiskFeatureSnapshot? snapshot)
        => new(
            score.StudentProfileId,
            snapshot?.StudentProfile?.User?.FullName ?? "Student",
            score.Probability,
            score.Level,
            score.ScoredAtUtc,
            score.Id,
            score.RiskFeatureSnapshotId,
            score.CourseKey,
            score.WindowEndUtc,
            score.FeatureSchemaVersion,
            score.ModelVersion);

    private static RiskScoringResultDto ToResultDto(int snapshotId, ScoreSnapshotCoreResult result)
        => new(
            snapshotId,
            result.Persistence.RiskScore?.Id,
            result.Persistence.Outcome == RiskScorePersistenceOutcome.Created,
            result.Persistence.QueueCreated,
            result.Persistence.QueueEscalated,
            result.SkipReason);

    internal static RiskScoringRunDto ToRunDto(RiskScoringRun run)
        => new(
            run.Id,
            run.RunKey,
            run.ModelVersion,
            run.FeatureSchemaVersion,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.Status,
            run.CandidateCount,
            run.ScoredCount,
            run.SkippedCount,
            run.FailedCount,
            run.QueueCreatedCount,
            run.QueueEscalatedCount,
            run.FailureSummary);

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private static string? NormalizeActor(string? actorUserId)
        => string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();

    private static string Truncate(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record ScoreSnapshotCoreResult(
        RiskScorePersistenceResult Persistence,
        RiskFeatureSnapshot? Snapshot,
        string? SkipReason);
}
