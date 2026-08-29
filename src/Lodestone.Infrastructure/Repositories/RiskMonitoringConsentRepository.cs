using System.Data;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lodestone.Infrastructure.Repositories;

public sealed class RiskMonitoringConsentRepository : IRiskMonitoringConsentRepository
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RiskMonitoringConsentRepository(ApplicationDbContext context, TimeProvider timeProvider)
        => (_context, _timeProvider) = (context, timeProvider);

    public Task<RiskMonitoringConsentDto?> GetByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => _context.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => profile.RiskMonitoringConsent == null
                ? new RiskMonitoringConsentDto(
                    profile.Id,
                    false,
                    RiskMonitoringPolicy.CurrentVersion,
                    null,
                    null)
                : new RiskMonitoringConsentDto(
                    profile.Id,
                    profile.RiskMonitoringConsent.IsConsented,
                    profile.RiskMonitoringConsent.PolicyVersion,
                    profile.RiskMonitoringConsent.ConsentedAtUtc,
                    profile.RiskMonitoringConsent.WithdrawnAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<RiskMonitoringConsentDto> SetByUserIdAsync(
        string userId,
        bool isConsented,
        string? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("A user identifier is required.", nameof(userId));

        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var normalizedUserId = userId.Trim();
            var profile = await _context.StudentProfiles
                .Include(item => item.RiskMonitoringConsent)
                .SingleOrDefaultAsync(item => item.UserId == normalizedUserId, cancellationToken)
                ?? throw new KeyNotFoundException("A student profile was not found for the current user.");
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var actor = NormalizeActor(actorUserId);
            var consent = profile.RiskMonitoringConsent;
            var stateChanged = consent is null ||
                               consent.IsConsented != isConsented ||
                               consent.PolicyVersion != RiskMonitoringPolicy.CurrentVersion;

            if (consent is null)
            {
                consent = new RiskMonitoringConsent
                {
                    StudentProfileId = profile.Id,
                    IsConsented = isConsented,
                    PolicyVersion = RiskMonitoringPolicy.CurrentVersion,
                    ConsentedAtUtc = isConsented ? nowUtc : null,
                    WithdrawnAtUtc = isConsented ? null : nowUtc,
                    CreatedAtUtc = nowUtc,
                    CreatedBy = actor
                };
                _context.RiskMonitoringConsents.Add(consent);
            }
            else if (stateChanged)
            {
                consent.IsConsented = isConsented;
                consent.PolicyVersion = RiskMonitoringPolicy.CurrentVersion;
                if (isConsented)
                {
                    consent.ConsentedAtUtc = nowUtc;
                    consent.WithdrawnAtUtc = null;
                }
                else
                {
                    consent.WithdrawnAtUtc = nowUtc;
                }
                consent.ModifiedAtUtc = nowUtc;
                consent.ModifiedBy = actor;
            }

            if (stateChanged)
            {
                _context.RiskMonitoringConsentHistory.Add(new RiskMonitoringConsentHistory
                {
                    StudentProfileId = profile.Id,
                    IsConsented = isConsented,
                    PolicyVersion = RiskMonitoringPolicy.CurrentVersion,
                    ChangedAtUtc = nowUtc,
                    ChangedByUserId = actor
                });
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = actor,
                    Action = isConsented ? "RiskMonitoring.Consented" : "RiskMonitoring.Withdrawn",
                    EntityName = nameof(RiskMonitoringConsent),
                    EntityId = profile.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Details = $"Monitoring policy {RiskMonitoringPolicy.CurrentVersion}.",
                    TimestampUtc = nowUtc
                });
            }

            if (!isConsented)
                await RemoveDerivedRiskDataAsync(profile.Id, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return ToDto(consent);
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

    private async Task RemoveDerivedRiskDataAsync(int studentProfileId, CancellationToken cancellationToken)
    {
        var queueEntries = await _context.RiskQueueEntries
            .Where(entry => entry.StudentProfileId == studentProfileId)
            .ToListAsync(cancellationToken);
        var scores = await _context.RiskScores
            .Where(score => score.StudentProfileId == studentProfileId)
            .ToListAsync(cancellationToken);
        var snapshots = await _context.RiskFeatureSnapshots
            .Where(snapshot => snapshot.StudentProfileId == studentProfileId)
            .ToListAsync(cancellationToken);
        var activityLogs = await _context.ActivityLogs
            .Where(activity => activity.StudentProfileId == studentProfileId)
            .ToListAsync(cancellationToken);

        _context.RiskQueueEntries.RemoveRange(queueEntries);
        _context.RiskScores.RemoveRange(scores);
        _context.RiskFeatureSnapshots.RemoveRange(snapshots);
        _context.ActivityLogs.RemoveRange(activityLogs);
    }

    private static RiskMonitoringConsentDto ToDto(RiskMonitoringConsent consent)
        => new(
            consent.StudentProfileId,
            consent.IsConsented,
            consent.PolicyVersion,
            consent.ConsentedAtUtc,
            consent.WithdrawnAtUtc);

    private static string? NormalizeActor(string? actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId)) return null;
        var normalized = actorUserId.Trim();
        return normalized.Length <= 450 ? normalized : normalized[..450];
    }
}
