using System.Data;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
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
        var rows = await _context.RiskQueueEntries
            .AsNoTracking()
            .Where(entry => !entry.IsResolved)
            .OrderByDescending(entry => entry.Level)
            .ThenBy(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new
            {
                entry.Id,
                entry.StudentProfileId,
                StudentName = entry.StudentProfile != null && entry.StudentProfile.User != null
                    ? entry.StudentProfile.User.FullName
                    : null,
                StudentNumber = entry.StudentProfile != null ? entry.StudentProfile.StudentNumber : null,
                entry.Level,
                entry.IsResolved,
                entry.CreatedAtUtc,
                CourseKey = entry.RiskScore != null ? entry.RiskScore.CourseKey : string.Empty,
                Probability = entry.RiskScore != null ? entry.RiskScore.Probability : 0,
                ScoredAtUtc = entry.RiskScore != null ? entry.RiskScore.ScoredAtUtc : default,
                RowVersion = entry.RowVersion,
                ActiveDayRate = entry.RiskScore != null && entry.RiskScore.RiskFeatureSnapshot != null
                    ? entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV2
                        ? entry.RiskScore.RiskFeatureSnapshot.RecentActiveDayRate ?? 0
                        : entry.RiskScore.RiskFeatureSnapshot.ActiveDayRate
                    : 0,
                DaysSinceLastAccess = entry.RiskScore != null && entry.RiskScore.RiskFeatureSnapshot != null
                    ? entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV2
                        ? entry.RiskScore.RiskFeatureSnapshot.InactivityStreakDays ?? 0
                        : entry.RiskScore.RiskFeatureSnapshot.DaysSinceLastAccess
                    : 0,
                LateOrMissingAssignmentCount = entry.RiskScore != null && entry.RiskScore.RiskFeatureSnapshot != null
                    ? entry.RiskScore.RiskFeatureSnapshot.FeatureSchemaVersion == RiskFeatureSchema.Withdrawal28DayV2
                        ? entry.RiskScore.RiskFeatureSnapshot.AssessmentLateOrMissingRate ?? 0
                        : entry.RiskScore.RiskFeatureSnapshot.LateOrMissingAssignmentCount
                    : 0
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new RiskQueueItemDto(
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
                row.LateOrMissingAssignmentCount))
            .ToArray();
    }

    public async Task<RiskQueueResolutionOutcome> ResolveAsync(
        int queueEntryId,
        string resolvedByUserId,
        string? rowVersionToken,
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
            entry.ModifiedAtUtc = nowUtc;
            entry.ModifiedBy = actor;
            await _context.SaveChangesAsync(cancellationToken);

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = actor,
                Action = "RiskQueue.Resolved",
                EntityName = nameof(RiskQueueEntry),
                EntityId = entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Details = "A counselor resolved a behavioral risk case.",
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
