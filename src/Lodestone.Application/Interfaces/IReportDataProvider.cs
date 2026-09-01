using Lodestone.Application.DTOs.Reports;

namespace Lodestone.Application.Interfaces;

/// <summary>
/// Gathers the read models a PDF report needs. Implemented in Infrastructure so the Reporting
/// project composes documents without taking a dependency on EF Core or the database.
/// </summary>
public interface IReportDataProvider
{
    Task<RiskSummaryReportData> GetRiskSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns null when the student profile does not exist.</summary>
    Task<StudentEngagementReportData?> GetStudentEngagementAsync(
        int studentProfileId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns null when the session report does not exist.</summary>
    Task<CounselorSessionReportData?> GetCounselorSessionAsync(
        int sessionReportId,
        CancellationToken cancellationToken = default);
}
