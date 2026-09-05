using Lodestone.Application.DTOs.Reports;
using Lodestone.Reporting.Templates;
using QuestPDF.Fluent;

namespace Lodestone.Reporting.Reports;

/// <summary>Produces a PDF for a single counselor session. Generators live only in Lodestone.Reporting.</summary>
public class CounselorSessionReportGenerator
{
    public byte[] Generate(CounselorSessionReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new CounselorSessionTemplate(report).GeneratePdf();
    }
}
