using System.Globalization;

namespace Lodestone.Application.Interfaces;

/// <summary>Records when signed-in accounts were last active so administrators can see who is online.</summary>
public interface IUserPresenceService
{
    /// <summary>An account counts as online when it was seen within this window.</summary>
    static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

    /// <summary>The earliest last-seen time that still counts as online at <paramref name="nowUtc"/>.</summary>
    static DateTime OnlineCutoff(DateTime nowUtc) => nowUtc - OnlineWindow;

    /// <summary>"Online", or "Offline" with how long ago the account was last active.</summary>
    static string Describe(DateTime? lastSeenUtc, DateTime nowUtc)
    {
        if (lastSeenUtc is not { } seen) return "Offline · never seen";
        if (seen >= OnlineCutoff(nowUtc)) return "Online";

        var age = nowUtc - seen;
        if (age.TotalMinutes < 60) return $"Offline · {Math.Max(1, (int)age.TotalMinutes)} min ago";
        if (age.TotalHours < 24) return $"Offline · {(int)age.TotalHours} h ago";
        return "Offline · " + seen.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Records activity for the account. Cheap to call on every request; writes are throttled.</summary>
    Task TouchAsync(string userId, CancellationToken cancellationToken = default);
}
