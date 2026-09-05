using System.Globalization;
using Lodestone.Application.DTOs.Reports;
using Lodestone.Application.Interfaces;
using Lodestone.Reporting.Reports;

namespace Lodestone.Reporting.Export;

/// <summary>Implements the Application <see cref="IReportService"/> using QuestPDF generators.</summary>
public class PdfExportService : IReportService
{
    public const string RiskSummary = "risk-summary";
    public const string StudentEngagement = "student-engagement";
    public const string CounselorSession = "counselor-session";

    private const string PdfContentType = "application/pdf";

    private readonly CounselorSessionReportGenerator _sessionGenerator;
    private readonly RiskSummaryReportGenerator _riskGenerator;
    private readonly StudentEngagementReportGenerator _engagementGenerator;
    private readonly IReportDataProvider _dataProvider;

    public PdfExportService(
        CounselorSessionReportGenerator sessionGenerator,
        RiskSummaryReportGenerator riskGenerator,
        StudentEngagementReportGenerator engagementGenerator,
        IReportDataProvider dataProvider)
    {
        _sessionGenerator = sessionGenerator;
        _riskGenerator = riskGenerator;
        _engagementGenerator = engagementGenerator;
        _dataProvider = dataProvider;
    }

    public async Task<ReportResultDto> GenerateAsync(
        ReportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reportType = (request.ReportType ?? string.Empty).Trim().ToLowerInvariant();
        return reportType switch
        {
            RiskSummary => await GenerateRiskSummaryAsync(request, cancellationToken),
            StudentEngagement => await GenerateStudentEngagementAsync(request, cancellationToken),
            CounselorSession => await GenerateCounselorSessionAsync(request, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.ReportType,
                "Unsupported report type.")
        };
    }

    private async Task<ReportResultDto> GenerateRiskSummaryAsync(
        ReportRequestDto request,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = NormalizeRange(request);
        var data = await _dataProvider.GetRiskSummaryAsync(fromUtc, toUtc, cancellationToken);
        return new ReportResultDto(
            FileName($"lodestone-risk-summary-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}"),
            PdfContentType,
            _riskGenerator.Generate(data));
    }

    private async Task<ReportResultDto> GenerateStudentEngagementAsync(
        ReportRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.TargetId is not { } studentProfileId || studentProfileId <= 0)
        {
            throw new ArgumentException(
                "A student profile identifier is required for the student engagement report.",
                nameof(request));
        }

        var (fromUtc, toUtc) = NormalizeRange(request);
        var data = await _dataProvider.GetStudentEngagementAsync(
            studentProfileId,
            fromUtc,
            toUtc,
            cancellationToken);
        if (data is null)
            throw new KeyNotFoundException($"Student profile {studentProfileId} was not found.");

        return new ReportResultDto(
            FileName($"lodestone-engagement-{studentProfileId}-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}"),
            PdfContentType,
            _engagementGenerator.Generate(data));
    }

    private async Task<ReportResultDto> GenerateCounselorSessionAsync(
        ReportRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.TargetId is not { } sessionReportId || sessionReportId <= 0)
        {
            throw new ArgumentException(
                "A session report identifier is required for the counselor session report.",
                nameof(request));
        }

        var data = await _dataProvider.GetCounselorSessionAsync(sessionReportId, cancellationToken);
        if (data is null)
            throw new KeyNotFoundException($"Session report {sessionReportId} was not found.");

        return new ReportResultDto(
            FileName($"lodestone-session-{sessionReportId:D6}"),
            PdfContentType,
            _sessionGenerator.Generate(data));
    }

    /// <summary>
    /// Treats the requested range as inclusive of the whole end day and rejects an inverted range,
    /// so a caller passing the same date twice still receives that day's data.
    /// </summary>
    private static (DateTime FromUtc, DateTime ToUtc) NormalizeRange(ReportRequestDto request)
    {
        var fromUtc = DateTime.SpecifyKind(request.FromUtc.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(request.ToUtc.Date, DateTimeKind.Utc).AddDays(1);
        if (toUtc <= fromUtc)
        {
            throw new ArgumentException(
                "The report end date cannot fall before the start date.",
                nameof(request));
        }

        return (fromUtc, toUtc);
    }

    private static string FileName(string stem)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{stem}.pdf");
}
