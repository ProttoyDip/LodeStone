using System.Data;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lodestone.Infrastructure.Repositories;

/// <summary>Atomic, idempotent persistence for model scores and counselor cases.</summary>
public sealed class RiskScoreRepository : IRiskScoringRepository
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RiskScoreRepository(ApplicationDbContext context, TimeProvider timeProvider)
        => (_context, _timeProvider) = (context, timeProvider);

    public async Task<RiskScoringRun> StartRunAsync(
        RiskModelDescriptor descriptor,
        int candidateCount,
        string? actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (candidateCount < 0) throw new ArgumentOutOfRangeException(nameof(candidateCount));

        var nowUtc = UtcNow;
        var run = new RiskScoringRun
        {
            RunKey = Guid.NewGuid(),
            ModelVersion = Required(descriptor.ModelVersion, nameof(descriptor.ModelVersion), 128),
            FeatureSchemaVersion = Required(descriptor.FeatureSchemaVersion, nameof(descriptor.FeatureSchemaVersion), 64),
            StartedAtUtc = nowUtc,
            Status = RiskScoringRunStatus.Running,
            CandidateCount = candidateCount,
            CreatedAtUtc = nowUtc,
            CreatedBy = Normalize(actorUserId, 450)
        };
        _context.RiskScoringRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task CompleteRunAsync(
        RiskScoringRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Id <= 0) throw new ArgumentException("A persisted scoring run is required.", nameof(run));
        if (run.CompletedAtUtc is null || run.Status == RiskScoringRunStatus.Running)
            throw new InvalidOperationException("A scoring run must have a terminal status and completion time.");
        if (new[]
            {
                run.CandidateCount,
                run.ScoredCount,
                run.SkippedCount,
                run.FailedCount,
                run.QueueCreatedCount,
                run.QueueEscalatedCount
            }.Any(count => count < 0))
            throw new InvalidOperationException("Scoring-run counts cannot be negative.");

        run.FailureSummary = Normalize(run.FailureSummary, 2_000);
        run.ModifiedAtUtc = UtcNow;
        if (_context.Entry(run).State == EntityState.Detached)
            _context.RiskScoringRuns.Update(run);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<RiskScoringRun?> GetLatestRunAsync(CancellationToken cancellationToken = default)
        => _context.RiskScoringRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<RiskScorePersistenceResult> PersistAsync(
        RiskFeatureSnapshot snapshot,
        RiskModelDescriptor descriptor,
        double probability,
        RiskLevel level,
        DateTime scoredAtUtc,
        int? scoringRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (snapshot.Id <= 0) throw new ArgumentException("A persisted feature snapshot is required.", nameof(snapshot));
        if (!double.IsFinite(probability) || probability is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(probability));
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));

        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var modelVersion = Required(descriptor.ModelVersion, nameof(descriptor.ModelVersion), 128);
            var currentSnapshot = await _context.RiskFeatureSnapshots
                .Include(item => item.StudentProfile)
                    .ThenInclude(profile => profile!.User)
                .Include(item => item.StudentProfile)
                    .ThenInclude(profile => profile!.RiskMonitoringConsent)
                .SingleOrDefaultAsync(item => item.Id == snapshot.Id, cancellationToken);

            if (currentSnapshot?.StudentProfile?.User?.IsActive != true ||
                currentSnapshot.StudentProfile.RiskMonitoringConsent?.IsConsented != true ||
                currentSnapshot.FeatureSchemaVersion != descriptor.FeatureSchemaVersion ||
                currentSnapshot.ObservedDays != descriptor.ObservedDays)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new RiskScorePersistenceResult(
                    RiskScorePersistenceOutcome.NotEligible,
                    null,
                    false,
                    false);
            }

            var existing = await _context.RiskScores
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    score => score.RiskFeatureSnapshotId == currentSnapshot.Id &&
                             score.ModelVersion == modelVersion,
                    cancellationToken);
            if (existing is not null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new RiskScorePersistenceResult(
                    RiskScorePersistenceOutcome.AlreadyExists,
                    existing,
                    false,
                    false);
            }

            var scoredUtc = EnsureUtc(scoredAtUtc, nameof(scoredAtUtc));
            var score = new RiskScore
            {
                StudentProfileId = currentSnapshot.StudentProfileId,
                RiskFeatureSnapshotId = currentSnapshot.Id,
                RiskScoringRunId = scoringRunId,
                CourseKey = currentSnapshot.CourseKey,
                WindowEndUtc = currentSnapshot.WindowEndUtc,
                FeatureSchemaVersion = currentSnapshot.FeatureSchemaVersion,
                Probability = probability,
                Level = level,
                ScoredAtUtc = scoredUtc,
                ModelVersion = modelVersion,
                CreatedAtUtc = scoredUtc
            };
            _context.RiskScores.Add(score);

            var queueCreated = false;
            var queueEscalated = false;
            if (probability >= descriptor.QueueThreshold)
            {
                var queueEntry = await _context.RiskQueueEntries
                    .SingleOrDefaultAsync(
                        entry => entry.StudentProfileId == currentSnapshot.StudentProfileId && !entry.IsResolved,
                        cancellationToken);
                if (queueEntry is null)
                {
                    queueEntry = new RiskQueueEntry
                    {
                        StudentProfileId = currentSnapshot.StudentProfileId,
                        RiskScore = score,
                        TriggerRiskScore = score,
                        Level = level,
                        LastSignaledAtUtc = scoredUtc,
                        IsResolved = false,
                        CreatedAtUtc = scoredUtc
                    };
                    _context.RiskQueueEntries.Add(queueEntry);
                    queueCreated = true;
                    _context.AuditLogs.Add(QueueAudit(
                        "RiskQueue.Created",
                        currentSnapshot.StudentProfileId,
                        scoredUtc));
                }
                else
                {
                    queueEntry.RiskScore = score;
                    queueEntry.LastSignaledAtUtc = scoredUtc;
                    queueEntry.ModifiedAtUtc = scoredUtc;
                    if (level > queueEntry.Level)
                    {
                        queueEntry.Level = level;
                        queueEscalated = true;
                        _context.AuditLogs.Add(QueueAudit(
                            "RiskQueue.Escalated",
                            currentSnapshot.StudentProfileId,
                            scoredUtc));
                    }
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new RiskScorePersistenceResult(
                RiskScorePersistenceOutcome.Created,
                score,
                queueCreated,
                queueEscalated);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            ResetFailedRiskMutations();
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private static AuditLog QueueAudit(string action, int studentProfileId, DateTime timestampUtc)
        => new()
        {
            Action = action,
            EntityName = nameof(RiskQueueEntry),
            EntityId = studentProfileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Details = "A consent-gated behavioral risk case changed.",
            TimestampUtc = timestampUtc
        };

    private void ResetFailedRiskMutations()
    {
        var entries = _context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is RiskScore or RiskQueueEntry or AuditLog &&
                            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        // Dependents are detached before their newly-created scores so navigation fix-up
        // cannot keep a rolled-back graph alive in this scoped context.
        foreach (var entry in entries.OrderBy(entry => entry.Entity is RiskScore ? 1 : 0))
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;
        }
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default) throw new ArgumentException("A timestamp is required.", parameterName);
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string Required(string? value, string parameterName, int maximumLength)
        => Normalize(value, maximumLength)
           ?? throw new ArgumentException("A non-empty value is required.", parameterName);

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
