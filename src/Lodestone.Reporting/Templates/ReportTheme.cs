using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lodestone.Reporting.Templates;

/// <summary>
/// Shared page furniture and styling so the three report types read as one document family.
/// </summary>
internal static class ReportTheme
{
    public const string Ink = "#16202C";
    public const string Muted = "#5C6B72";
    public const string Rule = "#D3DAD6";
    public const string Accent = "#0B6E5F";
    public const string SurfaceAlt = "#F4F6F4";

    public const string Critical = "#A33C3C";
    public const string High = "#B4762A";
    public const string Moderate = "#7A6A2F";
    public const string Low = "#3F7048";

    public static string LevelColor(string level) => level switch
    {
        "Critical" => Critical,
        "High" => High,
        "Moderate" => Moderate,
        _ => Low
    };

    public static void ApplyPage(PageDescriptor page, string title, string subtitle, DateTime generatedAtUtc)
    {
        page.Size(PageSizes.A4);
        page.Margin(1.8f, Unit.Centimetre);
        page.DefaultTextStyle(text => text.FontSize(9.5f).FontColor(Ink).FontFamily(Fonts.Calibri));

        page.Header().Element(container => ComposeHeader(container, title, subtitle));
        page.Footer().Element(container => ComposeFooter(container, generatedAtUtc));
    }

    private static void ComposeHeader(IContainer container, string title, string subtitle)
    {
        container.PaddingBottom(12).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item()
                        .Text("LODESTONE")
                        .FontSize(8).FontColor(Accent).LetterSpacing(0.18f).SemiBold();
                    inner.Item().PaddingTop(2)
                        .Text(title)
                        .FontSize(17).SemiBold().FontColor(Ink);
                    inner.Item().PaddingTop(1)
                        .Text(subtitle)
                        .FontSize(9).FontColor(Muted);
                });

                row.ConstantItem(120).AlignRight().AlignBottom()
                    .Text("Student wellbeing\nearly warning system")
                    .FontSize(7.5f).FontColor(Muted).LineHeight(1.35f);
            });

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Ink);
        });
    }

    private static void ComposeFooter(IContainer container, DateTime generatedAtUtc)
    {
        container.PaddingTop(8).Column(column =>
        {
            column.Item().LineHorizontal(0.5f).LineColor(Rule);
            column.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem()
                    .Text($"Generated {generatedAtUtc:dd MMM yyyy HH:mm} UTC · Confidential — handle under student data policy")
                    .FontSize(7.5f).FontColor(Muted);

                row.ConstantItem(70).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    /// <summary>Section heading used between content blocks.</summary>
    public static void SectionHeading(this IContainer container, string text)
        => container.PaddingTop(14).PaddingBottom(5)
            .Text(text).FontSize(11).SemiBold().FontColor(Ink);

    /// <summary>A labelled figure used in the summary strips.</summary>
    public static void Metric(this IContainer container, string label, string value, string? valueColor = null)
    {
        container.Border(0.5f).BorderColor(Rule).Background(SurfaceAlt).Padding(8).Column(column =>
        {
            column.Item().Text(value).FontSize(15).SemiBold().FontColor(valueColor ?? Ink);
            column.Item().PaddingTop(2).Text(label).FontSize(7.5f).FontColor(Muted).LetterSpacing(0.06f);
        });
    }

    public static IContainer HeaderCell(this IContainer container)
        => container.Background(SurfaceAlt).BorderBottom(1).BorderColor(Ink)
            .PaddingVertical(5).PaddingHorizontal(6);

    public static IContainer BodyCell(this IContainer container)
        => container.BorderBottom(0.5f).BorderColor(Rule)
            .PaddingVertical(4).PaddingHorizontal(6);
}
