using FluentAssertions;
using Lodestone.Application.Common;
using Lodestone.Application.Services;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class UserTimeTests
{
    private static readonly DateTime Moment = new(2026, 9, 21, 2, 5, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Asia/Dhaka", "21 Sep 2026, 08:05 GMT+6")]
    [InlineData("America/New_York", "20 Sep 2026, 22:05 GMT-4")]   // daylight saving: EDT, and the date changes
    [InlineData("Asia/Kolkata", "21 Sep 2026, 07:35 GMT+5:30")]     // half-hour offset
    [InlineData("UTC", "21 Sep 2026, 02:05 UTC")]
    public void Format_ShowsTheMomentInTheUsersZoneWithALabel(string zone, string expected)
        => UserTime.Format(Moment, zone, "dd MMM yyyy, HH:mm").Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    [InlineData("../../etc/passwd")]
    public void UnknownOrMissingZones_FallBackToUtc(string? zone)
    {
        UserTime.IsValid(zone).Should().BeFalse();
        UserTime.Format(Moment, zone, "HH:mm").Should().Be("02:05 UTC");
    }

    [Fact]
    public void OverlongZoneIds_AreRejectedBeforeAnyLookup()
        => UserTime.IsValid(new string('a', UserTime.MaxTimeZoneIdLength + 1)).Should().BeFalse();

    [Fact]
    public void Draft_WritesTheSessionInTheCounselorsOwnZone()
    {
        var dhaka = UserTime.Resolve("Asia/Dhaka");
        var start = new DateTime(2026, 9, 8, 20, 0, 0, DateTimeKind.Utc);   // 02:00 next day in Dhaka

        var draft = SessionReportDrafter.Draft(new SessionReportDrafter.SessionFacts(
            start, start.AddMinutes(50), start.AddDays(-6), StudentLeftBookingNote: false,
            PriorCompletedSessions: 0, LastCompletedSessionUtc: null, TimeZone: dhaka));

        draft.Should().StartWith("Session 9 Sep 2026, 02:00–02:50 GMT+6 (50 min).");
        draft.Should().Contain("Booked 3 Sep 2026.");
    }
}
