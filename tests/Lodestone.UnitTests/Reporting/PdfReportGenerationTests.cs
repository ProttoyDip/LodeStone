using System.Text;
using FluentAssertions;
using Lodestone.Application.DTOs.Reports;
using Lodestone.Reporting.Reports;
using QuestPDF.Infrastructure;
using Xunit;

namespace Lodestone.UnitTests.Reporting;

/// <summary>
/// QuestPDF resolves layout at composition time, so a template that compiles can still throw when
/// rendered. These tests render each report end to end and assert a real PDF came back.
/// </summary>
public sealed class PdfReportGenerationTests
{
    static PdfReportGenerationTests()
        => QuestPDF.Settings.License = LicenseType.Community;

    private static readonly DateTime FromUtc = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToUtc = new(2026, 8, 29, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RiskSummary_renders_a_pdf()
    {
        var pdf = new RiskSummaryReportGenerator().Generate(SampleRiskSummary());

        AssertIsPdf(pdf);
    }

    [Fact]
    public void RiskSummary_renders_when_the_period_has_no_scores()
    {
        var empty = SampleRiskSummary() with
        {
            StudentsScored = 0,
            ScoresRecorded = 0,
            AverageProbability = null,
            ModelVersion = null,
            LevelBreakdown = new[]
            {
                new RiskLevelCountDto("Low", 0),
                new RiskLevelCountDto("Moderate", 0),
                new RiskLevelCountDto("High", 0),
                new RiskLevelCountDto("Critical", 0)
            },
            HighestRisk = Array.Empty<RiskSummaryRowDto>()
        };

        AssertIsPdf(new RiskSummaryReportGenerator().Generate(empty));
    }

    [Fact]
    public void StudentEngagement_renders_a_pdf()
    {
        var pdf = new StudentEngagementReportGenerator().Generate(SampleEngagement());

        AssertIsPdf(pdf);
    }

    [Fact]
    public void StudentEngagement_renders_when_there_is_no_activity()
    {
        var quiet = SampleEngagement() with
        {
            DaysWithActivity = 0,
            TotalLogins = 0,
            ForumInteractions = 0,
            CourseInteractions = 0,
            LateAssignments = 0,
            Weekly = Array.Empty<EngagementWeekDto>()
        };

        AssertIsPdf(new StudentEngagementReportGenerator().Generate(quiet));
    }

    [Fact]
    public void CounselorSession_renders_a_pdf()
    {
        var pdf = new CounselorSessionReportGenerator().Generate(SampleSession());

        AssertIsPdf(pdf);
    }

    [Fact]
    public void CounselorSession_renders_when_optional_narrative_is_missing()
    {
        var sparse = SampleSession() with { Summary = string.Empty, Recommendations = null };

        AssertIsPdf(new CounselorSessionReportGenerator().Generate(sparse));
    }

    [Fact]
    public void Generators_reject_null_data()
    {
        var risk = new RiskSummaryReportGenerator();
        var engagement = new StudentEngagementReportGenerator();
        var session = new CounselorSessionReportGenerator();

        risk.Invoking(generator => generator.Generate(null!)).Should().Throw<ArgumentNullException>();
        engagement.Invoking(generator => generator.Generate(null!)).Should().Throw<ArgumentNullException>();
        session.Invoking(generator => generator.Generate(null!)).Should().Throw<ArgumentNullException>();
    }

    private static void AssertIsPdf(byte[] content)
    {
        content.Should().NotBeNullOrEmpty();
        // Every PDF begins with the %PDF- header; anything else is not a renderable document.
        Encoding.ASCII.GetString(content, 0, 5).Should().Be("%PDF-");
        content.Length.Should().BeGreaterThan(1000, "a composed report is more than a stub document");
    }

    private static RiskSummaryReportData SampleRiskSummary() => new(
        FromUtc,
        ToUtc,
        DateTime.UtcNow,
        StudentsScored: 3726,
        ScoresRecorded: 108335,
        LevelBreakdown: new[]
        {
            new RiskLevelCountDto("Low", 71000),
            new RiskLevelCountDto("Moderate", 24000),
            new RiskLevelCountDto("High", 11335),
            new RiskLevelCountDto("Critical", 2000)
        },
        CasesOpened: 46,
        CasesResolved: 31,
        CasesStillOpen: 58,
        AverageProbability: 0.0731,
        ModelVersion: "withdrawal-28d-v3-20260901T073501229Z",
        HighestRisk: new[]
        {
            new RiskSummaryRowDto("S1042993", "AAA/2026J", "Critical", 0.9124, ToUtc.AddDays(-2)),
            new RiskSummaryRowDto("Student #418", "BBB/2026J", "High", 0.8471, ToUtc.AddDays(-3)),
            new RiskSummaryRowDto("S1099210", string.Empty, "High", 0.8309, ToUtc.AddDays(-5))
        });

    private static StudentEngagementReportData SampleEngagement() => new(
        FromUtc,
        ToUtc,
        DateTime.UtcNow,
        StudentReference: "S1042993",
        Program: "BSc Software Engineering",
        EnrollmentYear: 2024,
        DaysWithActivity: 9,
        TotalLogins: 21,
        ForumInteractions: 4,
        CourseInteractions: 173,
        LateAssignments: 2,
        JournalEntries: 6,
        BookingsAttended: 1,
        Weekly: new[]
        {
            new EngagementWeekDto(FromUtc, 5, 11, 96),
            new EngagementWeekDto(FromUtc.AddDays(7), 3, 7, 51),
            new EngagementWeekDto(FromUtc.AddDays(14), 1, 3, 26),
            new EngagementWeekDto(FromUtc.AddDays(21), 0, 0, 0)
        });

    private static CounselorSessionReportData SampleSession() => new(
        SessionReportId: 4821,
        GeneratedAtUtc: DateTime.UtcNow,
        StudentReference: "S1042993",
        CounselorName: "Dr Amara Osei",
        ScheduledForUtc: ToUtc.AddDays(-4).AddHours(14),
        BookingStatus: "Completed",
        Status: "Submitted",
        Summary: "Student described sustained difficulty keeping up with coursework following a "
                 + "period of illness, and reported that catching up felt unmanageable.",
        Recommendations: "Agreed a staged catch-up plan with the module lead and a follow-up "
                         + "session in two weeks.");
}
