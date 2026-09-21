using System.Collections.Concurrent;
using System.Globalization;

namespace Lodestone.Application.Common;

/// <summary>
/// Converts the UTC timestamps the platform stores into a user's own time zone. The zone is the
/// IANA id the user's browser reported (for example "Asia/Dhaka"); anything missing or unknown
/// falls back to UTC, so a caller never has to handle a bad value.
/// </summary>
public static class UserTime
{
    public const int MaxTimeZoneIdLength = 64;

    private static readonly ConcurrentDictionary<string, TimeZoneInfo?> Cache = new(StringComparer.Ordinal);

    /// <summary>True when the id names a time zone this server can resolve.</summary>
    public static bool IsValid(string? timeZoneId) => TryFind(timeZoneId) is not null;

    public static TimeZoneInfo Resolve(string? timeZoneId) => TryFind(timeZoneId) ?? TimeZoneInfo.Utc;

    public static DateTime ToLocal(DateTime utc, string? timeZoneId) => ToLocal(utc, Resolve(timeZoneId));

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo? zone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone ?? TimeZoneInfo.Utc);

    /// <summary>Short zone label for a moment in time: "UTC", "GMT+6" or "GMT-4" (daylight saving aware).</summary>
    public static string Label(DateTime utc, string? timeZoneId) => Label(utc, Resolve(timeZoneId));

    public static string Label(DateTime utc, TimeZoneInfo? zone)
    {
        var offset = (zone ?? TimeZoneInfo.Utc).GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        if (offset == TimeSpan.Zero) return "UTC";

        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        var minutes = absolute.Minutes == 0 ? string.Empty : ":" + absolute.Minutes.ToString("00", CultureInfo.InvariantCulture);
        return $"GMT{sign}{absolute.Hours}{minutes}";
    }

    /// <summary>Formats a UTC moment in the user's zone, followed by the zone label.</summary>
    public static string Format(DateTime utc, string? timeZoneId, string format)
        => ToLocal(utc, timeZoneId).ToString(format, CultureInfo.InvariantCulture) + " " + Label(utc, timeZoneId);

    private static TimeZoneInfo? TryFind(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > MaxTimeZoneIdLength) return null;

        return Cache.GetOrAdd(timeZoneId, id =>
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return null;
            }
        });
    }
}
