using Lodestone.Application.DTOs.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lodestone.Reporting.Templates;

/// <summary>QuestPDF document describing an individual student's engagement over a period.</summary>
public class StudentEngagementTemplate : IDocument
{
    private readonly StudentEngagementReportData _data;

    public StudentEngagementTemplate(StudentEngagementReportData data) => _data = data;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            ReportTheme.ApplyPage(
                page,
                "Student engagement",
                $"{_data.StudentReference} · {_data.FromUtc:dd MMM yyyy} – {_data.ToUtc.AddSeconds(-1):dd MMM yyyy} (UTC)",
                _data.GeneratedAtUtc);

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeProfile);
                column.Item().Element(ComposeTotals);
                column.Item().Element(ComposeWeekly);
                column.Item().Element(ComposeSupport);
                column.Item().Element(ComposeInterpretation);
            });
        });
    }

    private void ComposeProfile(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Student");
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(120);
                    columns.RelativeColumn();
                });

                void Detail(string label, string value)
                {
                    table.Cell().BodyCell().Text(label).FontSize(8).FontColor(ReportTheme.Muted);
                    table.Cell().BodyCell().Text(value);
                }

                Detail("Reference", _data.StudentReference);
                Detail("Program", string.IsNullOrWhiteSpace(_data.Program) ? "—" : _data.Program!);
                Detail("Enrollment year", _data.EnrollmentYear > 0 ? _data.EnrollmentYear.ToString() : "—");
            });
        });
    }

    private void ComposeTotals(IContainer container)
    {
        var periodDays = Math.Max(1, (int)Math.Round((_data.ToUtc - _data.FromUtc).TotalDays));
        var activeShare = _data.DaysWithActivity / (double)periodDays;

        container.Column(column =>
        {
            column.Item().SectionHeading("Engagement totals");
            column.Item().Row(row =>
            {
                row.RelativeItem().Metric(
                    "DAYS ACTIVE",
                    $"{_data.DaysWithActivity} / {periodDays}",
                    activeShare < 0.25 ? ReportTheme.High : ReportTheme.Ink);
                row.ConstantItem(8);
                row.RelativeItem().Metric("LOGINS", _data.TotalLogins.ToString("N0"));
                row.ConstantItem(8);
                row.RelativeItem().Metric("COURSE INTERACTIONS", _data.CourseInteractions.ToString("N0"));
                row.ConstantItem(8);
                row.RelativeItem().Metric("FORUM INTERACTIONS", _data.ForumInteractions.ToString("N0"));
                row.ConstantItem(8);
                row.RelativeItem().Metric(
                    "LATE ASSIGNMENTS",
                    _data.LateAssignments.ToString("N0"),
                    _data.LateAssignments > 0 ? ReportTheme.High : ReportTheme.Low);
            });
        });
    }

    private void ComposeWeekly(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Weekly activity");

            if (_data.Weekly.Count == 0)
            {
                column.Item().Text("No recorded activity in this period.")
                    .FontSize(9).FontColor(ReportTheme.Muted).Italic();
                return;
            }

            var peakInteractions = Math.Max(1, _data.Weekly.Max(week => week.CourseInteractions));

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(90);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(48);
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().HeaderCell().Text("Week beginning").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().AlignRight().Text("Days active").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().AlignRight().Text("Logins").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().Text("Course interactions").FontSize(8).SemiBold();
                });

                foreach (var week in _data.Weekly)
                {
                    table.Cell().BodyCell().Text(week.WeekStartUtc.ToString("dd MMM yyyy"));
                    table.Cell().BodyCell().AlignRight().Text($"{week.DaysWithActivity} / 7")
                        .FontColor(week.DaysWithActivity <= 1 ? ReportTheme.High : ReportTheme.Ink);
                    table.Cell().BodyCell().AlignRight().Text(week.Logins.ToString("N0"));
                    table.Cell().BodyCell().Row(row =>
                    {
                        var width = (float)(week.CourseInteractions / (double)peakInteractions * 130);
                        if (width > 0.5f)
                            row.ConstantItem(width).Height(8).Background(ReportTheme.Accent);
                        row.RelativeItem().PaddingLeft(6).AlignMiddle()
                            .Text(week.CourseInteractions.ToString("N0"))
                            .FontSize(8).FontColor(ReportTheme.Muted);
                    });
                }
            });
        });
    }

    private void ComposeSupport(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Support engagement");
            column.Item().Row(row =>
            {
                row.RelativeItem().Metric("JOURNAL ENTRIES", _data.JournalEntries.ToString("N0"));
                row.ConstantItem(8);
                row.RelativeItem().Metric("SESSIONS ATTENDED", _data.BookingsAttended.ToString("N0"));
            });
        });
    }

    private static void ComposeInterpretation(IContainer container)
    {
        container.PaddingTop(16).Border(0.5f).BorderColor(ReportTheme.Rule)
            .Background(ReportTheme.SurfaceAlt).Padding(10).Column(column =>
            {
                column.Item().Text("How to read this report")
                    .FontSize(9).SemiBold().FontColor(ReportTheme.Ink);
                column.Item().PaddingTop(3).Text(
                        "Engagement counts describe platform activity only. Low activity has many " +
                        "benign explanations and is not evidence of distress. Journal entry counts are " +
                        "shown without content: journal notes remain private to the student.")
                    .FontSize(8).FontColor(ReportTheme.Muted).LineHeight(1.4f);
            });
    }
}
