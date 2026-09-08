using System.Data;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lodestone.Infrastructure.Repositories;

public sealed class CounselorQueueRepository : ICounselorQueueRepository
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public CounselorQueueRepository(ApplicationDbContext context, TimeProvider timeProvider)
        => (_context, _timeProvider) = (context, timeProvider);

    public async Task<IReadOnlyList<RiskQueueItemDto>> GetOpenAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await ProjectItems(_context.RiskQueueEntries
                .AsNoTracking()
                .Where(entry => !entry.IsResolved)
                .OrderByDescending(entry => entry.Level)
                .ThenBy(entry => entry.CreatedAtUtc)
                .ThenBy(entry => entry.Id))
            .ToListAsync(cancellationToken);

        return rows.Select(ToItem).ToArray();
    }

    public async Task<RiskExplanationContext?> GetExplanationContextAsync(
        int queueEntryId,
        DateTime asOfUtc,
        int maximumPopulationAgeDays,
        CancellationToken cancellationToken = default)
    {
        var row = await ProjectItems(_context.RiskQueueEntries
                .AsNoTracking()
                .Where(entry => entry.Id == queueEntryId
                                && !entry.IsResolved
                                && entry.StudentProfile != null
                                && entry.StudentProfile.RiskMonitoringConsent != null
                                && entry.StudentProfile.RiskMonitoringConsent.IsConsented))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var snapshot = await _context.RiskQueueEntries
            .AsNoTracking()
            .Where(entry => entry.Id == queueEntryId)
            .Select(entry => entry.RiskScore!.RiskFeatureSnapshot)
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null) return null;

        // One snapshot per student -- the latest -- so a heavily-monitored student does not weigh
        // more in the median than anyone else. Consent is checked here too, not only at scoring.
        var cutoffUtc = asOfUtc.AddDays(-maximumPopulationAgeDays);
        var population = await _context.RiskFeatureSnapshots
            .AsNoTracking()
            .Where(candidate => candidate.FeatureSchemaVersion == snapshot.FeatureSchemaVersion
                                && candidate.ObservedDays == snapshot.ObservedDays
                                && candidate.WindowEndUtc >= cutoffUtc
                                && candidate.WindowEndUtc <= asOfUtc
                                && candidate.StudentProfile != null
                                && candidate.StudentProfile.RiskMonitoringConsent != null
                                && candidate.StudentProfile.RiskMonitoringConsent.IsConsented)
            .GroupBy(candidate => candidate.StudentProfileId)
            .Select(group => group.OrderByDescending(candidate => candidate.WindowEndUtc).ThenByDescending(candidate => candidate.Id).First())
            .ToListAsync(cancellationToken);

        return new RiskExplanationContext(ToItem(row), snapshot, population);
    }

    private static IQueryable<QueueRow> ProjectItems(IQueryable<RiskQueueEntry> entries)
        => entries.Select(entry => new QueueRow(
            entry.Id,
            entry.StudentProfileId,
            entry.StudentProfile != null && entry.StudentProfile.User != null
                ? entry.StudentProfile.User.FullName
                : null,
            entry.StudentProfile != null ? entry.StudentProfile.StudentNumber : null,
            entry.Level,
            entry.IsResolved,
            entry.CreatedAtUtc,
            entry.RiskScore != null ? entry.RiskScore.CourseKey : string.Empty,
            entry.RiskScore != null ? entry.RiskScore.Probability : 0,
            entry.RiskScore != null ? entry.RiskScore.ScoredAtUtc : default,
            entry.RowVersion,
            entry.RiskScore != null && entry.RiskScore.RiskFeatureSnapshot != null
                ? (entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV2
                        || entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV3)
                    ? entry.RiskScore.RiskFeatureSnapshot.RecentActiveDayRate ?? 0
                    : entry.RiskScore.RiskFeatureSnapshot.ActiveDayRate
                : 0,
            entry.RiskScore != null && entry.RiskScore.RiskFeatureSnapshot != null
                ? (entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV2
                        || entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV3)
                    ? entry.RiskScore.RiskFeatureSnapshot.InactivityStreakDays ?? 0
                    : entry.RiskScore.RiskFeatureSnapshot.DaysSinceLastAccess
                : 0,
            entry.RiskScore != null && entry.RiskScore.RiskFeatureSnapshot != null
                ? (entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV2
                        || entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV3)
                    ? entry.RiskScore.RiskFeatureSnapshot.AssessmentLateOrMissingRate ?? 0
                    : entry.RiskScore.RiskFeatureSnapshot.LateOrMissingAssignmentCount
                : 0));

    private static RiskQueueItemDto ToItem(QueueRow row)
        => new(
            row.Id,
            row.StudentProfileId,
            string.IsNullOrWhiteSpace(row.StudentName) ? "Student" : row.StudentName,
            row.Level,
            row.IsResolved,
            row.CreatedAtUtc,
            row.CourseKey,
            row.Probability,
            row.ScoredAtUtc,
            Convert.ToBase64String(row.RowVersion ?? Array.Empty<byte>()),
            row.StudentNumber,
            row.ActiveDayRate,
            row.DaysSinceLastAccess,
            row.LateOrMissingAssignmentCount);

    private sealed record QueueRow(
        int Id,
        int StudentProfileId,
        string? StudentName,
        string? StudentNumber,
        Domain.Enums.RiskLevel Level,
        bool IsResolved,
        DateTime CreatedAtUtc,
        string CourseKey,
        double Probability,
        DateTime ScoredAtUtc,
        byte[]? RowVersion,
        float ActiveDayRate,
        float DaysSinceLastAccess,
        float LateOrMissingAssignmentCount);

    public async Task<RiskQueueResolutionOutcome> ResolveAsync(
        int queueEntryId,
        string resolvedByUserId,
        string? rowVersionToken,
        RiskCaseResolution resolution,
        string? resolutionNote,
        CancellationToken cancellationToken = default)
    {
        if (queueEntryId <= 0) throw new ArgumentOutOfRangeException(nameof(queueEntryId));
        if (string.IsNullOrWhiteSpace(resolvedByUserId))
            throw new ArgumentException("A resolving user is required.", nameof(resolvedByUserId));

        if (string.IsNullOrWhiteSpace(rowVersionToken))
            return RiskQueueResolutionOutcome.ConcurrencyConflict;

        byte[] expectedRowVersion;
        try
        {
            expectedRowVersion = Convert.FromBase64String(rowVersionToken);
        }
        catch (FormatException)
        {
            return RiskQueueResolutionOutcome.ConcurrencyConflict;
        }

        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        RiskQueueEntry? entry = null;
        try
        {
            entry = await _context.RiskQueueEntries
                .SingleOrDefaultAsync(item => item.Id == queueEntryId, cancellationToken);
            if (entry is null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return RiskQueueResolutionOutcome.NotFound;
            }
            if (entry.IsResolved)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return RiskQueueResolutionOutcome.AlreadyResolved;
            }

            _context.Entry(entry).Property(item => item.RowVersion).OriginalValue = expectedRowVersion;

            var actor = resolvedByUserId.Trim();
            if (actor.Length > 450) actor = actor[..450];
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            entry.IsResolved = true;
            entry.ResolvedByUserId = actor;
            entry.ResolvedAtUtc = nowUtc;
            entry.Resolution = resolution;
            entry.ResolutionNote = resolutionNote;
            entry.ModifiedAtUtc = nowUtc;
            entry.ModifiedBy = actor;
            await _context.SaveChangesAsync(cancellationToken);

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = actor,
                Action = "RiskQueue.Resolved",
                EntityName = nameof(RiskQueueEntry),
                EntityId = entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Details = $"A counselor resolved a behavioral risk case as {resolution}.",
                TimestampUtc = nowUtc
            });
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return RiskQueueResolutionOutcome.Resolved;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            if (entry is not null) _context.Entry(entry).State = EntityState.Detached;
            return RiskQueueResolutionOutcome.ConcurrencyConflict;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }
}
