using Lodestone.Application.DTOs.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lodestone.Reporting.Templates;

/// <summary>QuestPDF document describing the layout of a counselor session report.</summary>
public class CounselorSessionTemplate : IDocument
{
    private readonly CounselorSessionReportData _report;

    public CounselorSessionTemplate(CounselorSessionReportData report) => _report = report;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            ReportTheme.ApplyPage(
                page,
                "Counselor session report",
                $"Session on {_report.ScheduledForUtc:dd MMM yyyy HH:mm} UTC",
                _report.GeneratedAtUtc);

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeDetails);
                column.Item().Element(ComposeSummary);
                column.Item().Element(ComposeRecommendations);
                column.Item().Element(ComposeSignOff);
            });
        });
    }

    private void ComposeDetails(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Session details");
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

                Detail("Report reference", $"CSR-{_report.SessionReportId:D6}");
                Detail("Student", _report.StudentReference);
                Detail("Counselor", _report.CounselorName);
                Detail("Scheduled for", $"{_report.ScheduledForUtc:dd MMM yyyy HH:mm} UTC");
                Detail("Booking status", _report.BookingStatus);
                Detail("Report status", _report.Status);
            });
        });
    }

    private void ComposeSummary(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Session summary");
            column.Item().Text(string.IsNullOrWhiteSpace(_report.Summary)
                    ? "No summary was recorded for this session."
                    : _report.Summary)
                .LineHeight(1.45f)
                .FontColor(string.IsNullOrWhiteSpace(_report.Summary) ? ReportTheme.Muted : ReportTheme.Ink);
        });
    }

    private void ComposeRecommendations(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Recommendations");
            column.Item().Text(string.IsNullOrWhiteSpace(_report.Recommendations)
                    ? "No recommendations were recorded."
                    : _report.Recommendations)
                .LineHeight(1.45f)
                .FontColor(string.IsNullOrWhiteSpace(_report.Recommendations) ? ReportTheme.Muted : ReportTheme.Ink);
        });
    }

    private static void ComposeSignOff(IContainer container)
    {
        container.PaddingTop(24).Column(column =>
        {
            column.Item().LineHorizontal(0.5f).LineColor(ReportTheme.Rule);
            column.Item().PaddingTop(8).Text(
                    "This report contains confidential student wellbeing information. Share only with " +
                    "staff who have a legitimate need to know, and retain it in line with the " +
                    "institution's data-retention policy.")
                .FontSize(8).FontColor(ReportTheme.Muted).LineHeight(1.4f);
        });
    }
}
