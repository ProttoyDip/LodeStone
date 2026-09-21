using System.Collections.Concurrent;
using Lodestone.Application.Common;
using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Services;

public sealed class UserPresenceService : IUserPresenceService
{
    /// <summary>Minimum gap between database writes for one account.</summary>
    private static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(30);

    private static readonly ConcurrentDictionary<string, DateTime> LastWrites = new();
    private static readonly ConcurrentDictionary<string, string> LastZones = new();

    private readonly ApplicationDbContext _context;

    public UserPresenceService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task TouchAsync(
        string userId,
        string? timeZoneId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;

        // Only zones this server can resolve are stored; the value comes from a cookie, so it is untrusted.
        var zone = UserTime.IsValid(timeZoneId) ? timeZoneId!.Trim() : null;
        var zoneChanged = zone is not null &&
                          (!LastZones.TryGetValue(userId, out var known) || !string.Equals(known, zone, StringComparison.Ordinal));

        var now = DateTime.UtcNow;
        var throttled = LastWrites.TryGetValue(userId, out var last) && now - last < WriteInterval;
        if (throttled && !zoneChanged) return;
        LastWrites[userId] = now;

        var users = _context.Users.Where(user => user.Id == userId);
        if (zoneChanged)
        {
            await users.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.LastSeenUtc, now)
                    .SetProperty(user => user.TimeZoneId, zone),
                cancellationToken);
            LastZones[userId] = zone!;
        }
        else
        {
            await users.ExecuteUpdateAsync(
                setters => setters.SetProperty(user => user.LastSeenUtc, now),
                cancellationToken);
        }
    }
}
