using System.Collections.Concurrent;
using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Services;

public sealed class UserPresenceService : IUserPresenceService
{
    /// <summary>Minimum gap between database writes for one account.</summary>
    private static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(30);

    private static readonly ConcurrentDictionary<string, DateTime> LastWrites = new();

    private readonly ApplicationDbContext _context;

    public UserPresenceService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task TouchAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;

        var now = DateTime.UtcNow;
        if (LastWrites.TryGetValue(userId, out var last) && now - last < WriteInterval) return;
        LastWrites[userId] = now;

        await _context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastSeenUtc, now), cancellationToken);
    }
}
