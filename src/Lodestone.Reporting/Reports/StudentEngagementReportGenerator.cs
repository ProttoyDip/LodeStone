using Lodestone.Application.DTOs.Reports;
using Lodestone.Reporting.Templates;
using QuestPDF.Fluent;

namespace Lodestone.Reporting.Reports;

/// <summary>Produces an individual student engagement PDF.</summary>
public class StudentEngagementReportGenerator
{
    public byte[] Generate(StudentEngagementReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new StudentEngagementTemplate(data).GeneratePdf();
    }
}
