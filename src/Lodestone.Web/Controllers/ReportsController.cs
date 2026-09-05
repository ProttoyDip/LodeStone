using Lodestone.Application.DTOs.Reports;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Reporting.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

/// <summary>
/// PDF report downloads. Every action is authorized to staff policies: these documents carry
/// student wellbeing data and are never reachable by the students they describe.
/// </summary>
[Authorize(Policy = PolicyConstants.CanViewRiskQueue)]
public class ReportsController : Controller
{
    /// <summary>Bounds an ad-hoc range so a mistyped date cannot scan the entire history.</summary>
    private const int MaximumRangeDays = 366;

    private readonly IReportService _reportService;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(IReportService reportService, ILogger<ReportsController> logger)
    {
        _reportService = reportService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = PolicyConstants.CanAccessAdmin)]
    public Task<IActionResult> RiskSummary(DateTime? from, DateTime? to, CancellationToken cancellationToken)
        => DownloadAsync(PdfExportService.RiskSummary, from, to, targetId: null, cancellationToken);

    [HttpGet]
    public Task<IActionResult> StudentEngagement(
        int studentProfileId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
        => DownloadAsync(PdfExportService.StudentEngagement, from, to, studentProfileId, cancellationToken);

    [HttpGet]
    public Task<IActionResult> CounselorSession(int id, CancellationToken cancellationToken)
        => DownloadAsync(PdfExportService.CounselorSession, from: null, to: null, targetId: id, cancellationToken);

    private async Task<IActionResult> DownloadAsync(
        string reportType,
        DateTime? from,
        DateTime? to,
        int? targetId,
        CancellationToken cancellationToken)
    {
        var toUtc = (to ?? DateTime.UtcNow).Date;
        var fromUtc = (from ?? toUtc.AddDays(-27)).Date;

        if (fromUtc > toUtc)
            return Problem("The report start date must fall on or before the end date.", statusCode: StatusCodes.Status400BadRequest);
        if ((toUtc - fromUtc).TotalDays > MaximumRangeDays)
            return Problem($"Report ranges are limited to {MaximumRangeDays} days.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var result = await _reportService.GenerateAsync(
                new ReportRequestDto(reportType, fromUtc, toUtc, targetId),
                cancellationToken);

            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not generate the {ReportType} report.", reportType);
            return Problem(
                "The report could not be generated. Please try again.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
