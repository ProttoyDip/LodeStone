using Lodestone.Application.DTOs.Reports;
using Lodestone.Reporting.Templates;
using QuestPDF.Fluent;

namespace Lodestone.Reporting.Reports;

/// <summary>Produces an aggregate risk-summary PDF over a date range.</summary>
public class RiskSummaryReportGenerator
{
    public byte[] Generate(RiskSummaryReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new RiskSummaryTemplate(data).GeneratePdf();
    }
}
