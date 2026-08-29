using System.Data;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lodestone.Infrastructure.Repositories;

/// <summary>Persistence for consent-gated, first-party student activity events.</summary>
public sealed class ActivityLogRepository : IActivityLogRepository
{
    private readonly ApplicationDbContext _context;

    public ActivityLogRepository(ApplicationDbContext context) => _context = context;

    public async Task<bool> RecordLoginIfConsentedAsync(
        string userId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var timestamp = occurredAtUtc.Kind switch
        {
            DateTimeKind.Utc => occurredAtUtc,
            DateTimeKind.Local => occurredAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc)
        };

        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var profileId = await _context.StudentProfiles
                .Where(profile => profile.UserId == userId &&
                                  profile.RiskMonitoringConsent != null &&
                                  profile.RiskMonitoringConsent.IsConsented)
                .Select(profile => (int?)profile.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (profileId is null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return false;
            }

            _context.ActivityLogs.Add(new ActivityLog
            {
                StudentProfileId = profileId.Value,
                OccurredAtUtc = timestamp,
                LoginCount = 1
            });
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return true;
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
