namespace Lodestone.Application.DTOs.Reports;

/// <summary>
/// Read models shaped for PDF composition. Generators receive these rather than entities so a
/// template can never trigger a lazy load or reach navigation properties that were not included.
/// </summary>
public record RiskSummaryReportData(
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime GeneratedAtUtc,
    int StudentsScored,
    int ScoresRecorded,
    IReadOnlyList<RiskLevelCountDto> LevelBreakdown,
    int CasesOpened,
    int CasesResolved,
    int CasesStillOpen,
    double? AverageProbability,
    string? ModelVersion,
    IReadOnlyList<RiskSummaryRowDto> HighestRisk)
{
    /// <summary>How resolved cases in the period were closed. Empty for reports over legacy data.</summary>
    public IReadOnlyList<RiskResolutionCountDto> ResolutionBreakdown { get; init; } = Array.Empty<RiskResolutionCountDto>();

    /// <summary>Median hours from a case opening to its resolution, for cases resolved in the period.</summary>
    public double? MedianHoursToResolve { get; init; }
}

public record RiskResolutionCountDto(string Resolution, int Count);

public record RiskLevelCountDto(string Level, int Count);

public record RiskSummaryRowDto(
    string StudentReference,
    string CourseKey,
    string Level,
    double Probability,
    DateTime ScoredAtUtc);

public record StudentEngagementReportData(
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime GeneratedAtUtc,
    string StudentReference,
    string? Program,
    int EnrollmentYear,
    int DaysWithActivity,
    int TotalLogins,
    int ForumInteractions,
    int CourseInteractions,
    int LateAssignments,
    int JournalEntries,
    int BookingsAttended,
    IReadOnlyList<EngagementWeekDto> Weekly);

public record EngagementWeekDto(
    DateTime WeekStartUtc,
    int DaysWithActivity,
    int Logins,
    int CourseInteractions);

public record CounselorSessionReportData(
    int SessionReportId,
    DateTime GeneratedAtUtc,
    string StudentReference,
    string CounselorName,
    DateTime ScheduledForUtc,
    string BookingStatus,
    string Status,
    string Summary,
    string? Recommendations);
