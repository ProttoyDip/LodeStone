using Lodestone.Application.DTOs.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Lodestone.Reporting.Templates;

/// <summary>QuestPDF document describing the aggregate risk-summary report layout.</summary>
public class RiskSummaryTemplate : IDocument
{
    private readonly RiskSummaryReportData _data;

    public RiskSummaryTemplate(RiskSummaryReportData data) => _data = data;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            ReportTheme.ApplyPage(
                page,
                "Risk summary",
                $"{_data.FromUtc:dd MMM yyyy} – {_data.ToUtc.AddSeconds(-1):dd MMM yyyy} (UTC)",
                _data.GeneratedAtUtc);

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeOverview);
                column.Item().Element(ComposeLevels);
                column.Item().Element(ComposeCases);
                column.Item().Element(ComposeHighestRisk);
                column.Item().Element(ComposeInterpretation);
            });
        });
    }

    private void ComposeOverview(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Overview");
            column.Item().Row(row =>
            {
                row.RelativeItem().Metric("STUDENTS SCORED", _data.StudentsScored.ToString("N0"));
                row.ConstantItem(8);
                row.RelativeItem().Metric("SCORES RECORDED", _data.ScoresRecorded.ToString("N0"));
                row.ConstantItem(8);
                row.RelativeItem().Metric(
                    "MEAN RISK SCORE",
                    _data.AverageProbability is null ? "—" : _data.AverageProbability.Value.ToString("F3"));
                row.ConstantItem(8);
                row.RelativeItem().Metric("CASES STILL OPEN", _data.CasesStillOpen.ToString("N0"));
            });

            if (!string.IsNullOrWhiteSpace(_data.ModelVersion))
            {
                column.Item().PaddingTop(6)
                    .Text($"Most recent model version in range: {_data.ModelVersion}")
                    .FontSize(8).FontColor(ReportTheme.Muted);
            }
        });
    }

    private void ComposeLevels(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Risk level distribution");

            if (_data.ScoresRecorded == 0)
            {
                column.Item().Text("No scores were recorded in this period.")
                    .FontSize(9).FontColor(ReportTheme.Muted).Italic();
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.ConstantColumn(70);
                    columns.RelativeColumn(5);
                });

                table.Header(header =>
                {
                    header.Cell().HeaderCell().Text("Level").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().AlignRight().Text("Scores").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().Text("Share").FontSize(8).SemiBold();
                });

                foreach (var level in _data.LevelBreakdown)
                {
                    var share = _data.ScoresRecorded == 0 ? 0 : level.Count / (double)_data.ScoresRecorded;

                    table.Cell().BodyCell().Text(level.Level)
                        .FontColor(ReportTheme.LevelColor(level.Level)).SemiBold();
                    table.Cell().BodyCell().AlignRight().Text(level.Count.ToString("N0"));
                    table.Cell().BodyCell().Row(row =>
                    {
                        // Proportional bar: width is a share of a fixed 160pt track.
                        var width = (float)Math.Max(share * 160, share > 0 ? 1.5 : 0);
                        if (width > 0)
                        {
                            row.ConstantItem(width).Height(8)
                                .Background(ReportTheme.LevelColor(level.Level));
                        }
                        row.RelativeItem().PaddingLeft(6).AlignMiddle()
                            .Text(share.ToString("P1")).FontSize(8).FontColor(ReportTheme.Muted);
                    });
                }
            });
        });
    }

    private void ComposeCases(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Counselor case flow");
            column.Item().Row(row =>
            {
                row.RelativeItem().Metric("CASES OPENED", _data.CasesOpened.ToString("N0"));
                row.ConstantItem(8);
                row.RelativeItem().Metric("CASES RESOLVED", _data.CasesResolved.ToString("N0"), ReportTheme.Low);
                row.ConstantItem(8);
                row.RelativeItem().Metric(
                    "NET CHANGE",
                    (_data.CasesOpened - _data.CasesResolved).ToString("+#;-#;0"),
                    _data.CasesOpened > _data.CasesResolved ? ReportTheme.High : ReportTheme.Low);
            });
        });
    }

    private void ComposeHighestRisk(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().SectionHeading("Highest scoring students in period");

            if (_data.HighestRisk.Count == 0)
            {
                column.Item().Text("No scored students in this period.")
                    .FontSize(9).FontColor(ReportTheme.Muted).Italic();
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(24);
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(52);
                    columns.ConstantColumn(78);
                });

                table.Header(header =>
                {
                    header.Cell().HeaderCell().Text("#").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().Text("Student").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().Text("Course").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().Text("Level").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().AlignRight().Text("Score").FontSize(8).SemiBold();
                    header.Cell().HeaderCell().Text("Scored").FontSize(8).SemiBold();
                });

                var index = 1;
                foreach (var row in _data.HighestRisk)
                {
                    table.Cell().BodyCell().Text(index.ToString()).FontColor(ReportTheme.Muted);
                    table.Cell().BodyCell().Text(row.StudentReference);
                    table.Cell().BodyCell().Text(string.IsNullOrWhiteSpace(row.CourseKey) ? "—" : row.CourseKey);
                    table.Cell().BodyCell().Text(row.Level)
                        .FontColor(ReportTheme.LevelColor(row.Level)).SemiBold();
                    table.Cell().BodyCell().AlignRight().Text(row.Probability.ToString("F3"));
                    table.Cell().BodyCell().Text(row.ScoredAtUtc.ToString("dd MMM HH:mm"))
                        .FontSize(8).FontColor(ReportTheme.Muted);
                    index++;
                }
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
                        "Scores rank relative risk; they are not calibrated probabilities and must not be " +
                        "read as a percentage chance of withdrawal. This report supports counselor " +
                        "triage and review. It is not a basis for automated contact, and no student " +
                        "should be contacted solely because they appear here.")
                    .FontSize(8).FontColor(ReportTheme.Muted).LineHeight(1.4f);
            });
    }
}
