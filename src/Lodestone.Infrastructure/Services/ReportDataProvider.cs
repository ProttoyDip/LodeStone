using Lodestone.Application.DTOs.Reports;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Services;

/// <summary>
/// Materialises report read models. Every query is AsNoTracking and projects to a DTO before
/// leaving this class, so PDF composition can never trigger a database round trip.
/// </summary>
public sealed class ReportDataProvider : IReportDataProvider
{
    private const int HighestRiskRowLimit = 20;

    private readonly ApplicationDbContext _context;

    public ReportDataProvider(ApplicationDbContext context) => _context = context;

    public async Task<RiskSummaryReportData> GetRiskSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var scores = _context.RiskScores
            .AsNoTracking()
            .Where(score => score.ScoredAtUtc >= fromUtc && score.ScoredAtUtc < toUtc);

        var scoresRecorded = await scores.CountAsync(cancellationToken);
        var studentsScored = await scores
            .Select(score => score.StudentProfileId)
            .Distinct()
            .CountAsync(cancellationToken);

        var levelBreakdown = await scores
            .GroupBy(score => score.Level)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // Averaging an empty sequence throws in SQL translation, so guard on the count first.
        var averageProbability = scoresRecorded == 0
            ? (double?)null
            : await scores.AverageAsync(score => score.Probability, cancellationToken);

        var modelVersion = await scores
            .OrderByDescending(score => score.ScoredAtUtc)
            .Select(score => score.ModelVersion)
            .FirstOrDefaultAsync(cancellationToken);

        var highestRisk = await scores
            .OrderByDescending(score => score.Probability)
            .Take(HighestRiskRowLimit)
            .Select(score => new
            {
                StudentNumber = score.StudentProfile!.StudentNumber,
                score.StudentProfileId,
                score.CourseKey,
                score.Level,
                score.Probability,
                score.ScoredAtUtc
            })
            .ToListAsync(cancellationToken);

        var casesOpened = await _context.RiskQueueEntries
            .AsNoTracking()
            .CountAsync(
                entry => entry.CreatedAtUtc >= fromUtc && entry.CreatedAtUtc < toUtc,
                cancellationToken);

        var casesResolved = await _context.RiskQueueEntries
            .AsNoTracking()
            .CountAsync(
                entry => entry.ResolvedAtUtc != null &&
                         entry.ResolvedAtUtc >= fromUtc &&
                         entry.ResolvedAtUtc < toUtc,
                cancellationToken);

        var casesStillOpen = await _context.RiskQueueEntries
            .AsNoTracking()
            .CountAsync(entry => !entry.IsResolved, cancellationToken);

        return new RiskSummaryReportData(
            FromUtc: fromUtc,
            ToUtc: toUtc,
            GeneratedAtUtc: DateTime.UtcNow,
            StudentsScored: studentsScored,
            ScoresRecorded: scoresRecorded,
            LevelBreakdown: Enum.GetValues<RiskLevel>()
                .Select(level => new RiskLevelCountDto(
                    level.ToString(),
                    levelBreakdown.FirstOrDefault(item => item.Key == level)?.Count ?? 0))
                .ToArray(),
            CasesOpened: casesOpened,
            CasesResolved: casesResolved,
            CasesStillOpen: casesStillOpen,
            AverageProbability: averageProbability,
            ModelVersion: string.IsNullOrWhiteSpace(modelVersion) ? null : modelVersion,
            HighestRisk: highestRisk
                .Select(row => new RiskSummaryRowDto(
                    Reference(row.StudentNumber, row.StudentProfileId),
                    row.CourseKey,
                    row.Level.ToString(),
                    row.Probability,
                    row.ScoredAtUtc))
                .ToArray());
    }

    public async Task<StudentEngagementReportData?> GetStudentEngagementAsync(
        int studentProfileId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var profile = await _context.StudentProfiles
            .AsNoTracking()
            .Where(candidate => candidate.Id == studentProfileId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.StudentNumber,
                candidate.Program,
                candidate.EnrollmentYear
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null) return null;

        var activity = await _context.ActivityLogs
            .AsNoTracking()
            .Where(log => log.StudentProfileId == studentProfileId &&
                          log.OccurredAtUtc >= fromUtc &&
                          log.OccurredAtUtc < toUtc)
            .Select(log => new
            {
                log.OccurredAtUtc,
                log.LoginCount,
                log.ForumInteractions,
                log.CourseInteractions,
                log.AssignmentsLateCount
            })
            .ToListAsync(cancellationToken);

        var journalEntries = await _context.MoodJournalEntries
            .AsNoTracking()
            .CountAsync(
                entry => entry.StudentProfileId == studentProfileId &&
                         entry.EntryDateUtc >= fromUtc &&
                         entry.EntryDateUtc < toUtc,
                cancellationToken);

        var bookingsAttended = await _context.CounselorBookings
            .AsNoTracking()
            .CountAsync(
                booking => booking.StudentProfileId == studentProfileId &&
                           booking.ScheduledForUtc >= fromUtc &&
                           booking.ScheduledForUtc < toUtc &&
                           booking.Status == BookingStatus.Completed,
                cancellationToken);

        // Weeks are anchored to the range start rather than a calendar weekday so the first
        // bucket always begins on fromUtc and no partial leading week appears.
        var weekly = activity
            .GroupBy(log => (int)Math.Floor((log.OccurredAtUtc - fromUtc).TotalDays / 7))
            .OrderBy(group => group.Key)
            .Select(group => new EngagementWeekDto(
                fromUtc.AddDays(group.Key * 7),
                group.Select(log => log.OccurredAtUtc.Date).Distinct().Count(),
                group.Sum(log => log.LoginCount),
                group.Sum(log => log.CourseInteractions)))
            .ToArray();

        return new StudentEngagementReportData(
            FromUtc: fromUtc,
            ToUtc: toUtc,
            GeneratedAtUtc: DateTime.UtcNow,
            StudentReference: Reference(profile.StudentNumber, profile.Id),
            Program: profile.Program,
            EnrollmentYear: profile.EnrollmentYear,
            DaysWithActivity: activity.Select(log => log.OccurredAtUtc.Date).Distinct().Count(),
            TotalLogins: activity.Sum(log => log.LoginCount),
            ForumInteractions: activity.Sum(log => log.ForumInteractions),
            CourseInteractions: activity.Sum(log => log.CourseInteractions),
            LateAssignments: activity.Sum(log => log.AssignmentsLateCount),
            JournalEntries: journalEntries,
            BookingsAttended: bookingsAttended,
            Weekly: weekly);
    }

    public async Task<CounselorSessionReportData?> GetCounselorSessionAsync(
        int sessionReportId,
        CancellationToken cancellationToken = default)
    {
        var report = await _context.CounselorSessionReports
            .AsNoTracking()
            .Where(candidate => candidate.Id == sessionReportId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Summary,
                candidate.Recommendations,
                candidate.Status,
                StudentNumber = candidate.Booking!.StudentProfile!.StudentNumber,
                StudentProfileId = candidate.Booking!.StudentProfileId,
                CounselorName = candidate.Booking!.CounselorProfile!.User!.FullName,
                candidate.Booking!.ScheduledForUtc,
                BookingStatus = candidate.Booking!.Status
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (report is null) return null;

        return new CounselorSessionReportData(
            SessionReportId: report.Id,
            GeneratedAtUtc: DateTime.UtcNow,
            StudentReference: Reference(report.StudentNumber, report.StudentProfileId),
            CounselorName: string.IsNullOrWhiteSpace(report.CounselorName)
                ? "Counselor"
                : report.CounselorName,
            ScheduledForUtc: report.ScheduledForUtc,
            BookingStatus: report.BookingStatus.ToString(),
            Status: report.Status.ToString(),
            Summary: report.Summary,
            Recommendations: report.Recommendations);
    }

    /// <summary>Prefers the verified LMS identifier and falls back to an internal reference.</summary>
    private static string Reference(string? studentNumber, int studentProfileId)
        => string.IsNullOrWhiteSpace(studentNumber)
            ? $"Student #{studentProfileId}"
            : studentNumber;
}
