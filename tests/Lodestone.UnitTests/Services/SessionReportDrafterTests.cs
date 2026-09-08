using FluentAssertions;
using Lodestone.Application.Services;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class SessionReportDrafterTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Draft_StatesOnlyWhatTheBookingRecordKnows()
    {
        var draft = SessionReportDrafter.Draft(new SessionReportDrafter.SessionFacts(
            Start, Start.AddMinutes(50), Start.AddDays(-6), StudentLeftBookingNote: false,
            PriorCompletedSessions: 0, LastCompletedSessionUtc: null));

        draft.Should().StartWith("Session 8 Sep 2026, 10:00\u201310:50 UTC (50 min).");
        draft.Should().Contain("Booked 2 Sep 2026.");
        draft.Should().Contain("First completed session with this counselor.");
    }

    [Fact]
    public void Draft_CountsPriorSessionsAndNamesTheLastOne()
    {
        var draft = SessionReportDrafter.Draft(new SessionReportDrafter.SessionFacts(
            Start, Start.AddMinutes(50), Start.AddDays(-2), StudentLeftBookingNote: true,
            PriorCompletedSessions: 2, LastCompletedSessionUtc: Start.AddDays(-14)));

        draft.Should().Contain("3rd session with this counselor. Previous session 25 Aug 2026.");
    }

    [Fact]
    public void Draft_PointsAtTheStudentsNoteWithoutCopyingIt()
    {
        // The facts carry only whether a note exists. There is no way to pass its text, so the
        // draft cannot quote it; this test pins the wording that sends the counselor to read it.
        var withNote = SessionReportDrafter.Draft(Facts(studentLeftBookingNote: true));
        var withoutNote = SessionReportDrafter.Draft(Facts(studentLeftBookingNote: false));

        withNote.Should().Contain("the student's note is shown above");
        withoutNote.Should().NotContain("student's note");
        typeof(SessionReportDrafter.SessionFacts).GetProperties()
            .Select(property => property.PropertyType)
            .Should().NotContain(typeof(string), "no free text goes in, so none can be echoed out");
    }

    [Fact]
    public void Draft_LeavesEveryJudgementToTheCounselor()
    {
        var draft = SessionReportDrafter.Draft(Facts(studentLeftBookingNote: false));

        foreach (var heading in new[] { "Reason for booking", "What we discussed", "Agreed next steps", "Follow-up" })
            draft.Should().Contain($"{heading}").And.Contain(SessionReportDrafter.Placeholder);

        draft.Split('\n')
            .Where(line => line.Contains(':') && !line.StartsWith("Session", StringComparison.Ordinal))
            .Should().OnlyContain(line => line.TrimEnd().EndsWith(SessionReportDrafter.Placeholder));
    }

    [Fact]
    public void Draft_IsDeterministic()
    {
        var facts = Facts(studentLeftBookingNote: true);
        SessionReportDrafter.Draft(facts).Should().Be(SessionReportDrafter.Draft(facts));
    }

    private static SessionReportDrafter.SessionFacts Facts(bool studentLeftBookingNote)
        => new(Start, Start.AddMinutes(50), Start.AddDays(-1), studentLeftBookingNote, 1, Start.AddDays(-30));
}
