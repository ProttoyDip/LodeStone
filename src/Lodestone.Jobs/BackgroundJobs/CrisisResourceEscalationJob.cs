using System.Globalization;
using Hangfire;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Lodestone.Jobs.BackgroundJobs;

/// <summary>
/// Escalates critical-risk students to counselors / crisis workflows.
/// </summary>
/// <remarks>
/// Escalation here means raising a case to staff, never reaching the student. The job does not
/// contact students, does not resolve queue entries, and sends no student-identifying detail in
/// the notification: it reports how many high and critical cases have gone unreviewed past the
/// staleness threshold, and staff open the queue to see who they are. This keeps the product rule
/// that a human decides every intervention, and keeps names out of notification rows.
/// </remarks>
public class CrisisResourceEscalationJob : IMaintenanceJob
{
    /// <summary>A case older than this without review is treated as overdue.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    private readonly ICounselorQueueService _counselorQueueService;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CrisisResourceEscalationJob> _logger;

    public CrisisResourceEscalationJob(
        ICounselorQueueService counselorQueueService,
        INotificationService notifications,
        TimeProvider timeProvider,
        ILogger<CrisisResourceEscalationJob> logger)
    {
        _counselorQueueService = counselorQueueService;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var queue = await _counselorQueueService.GetQueueAsync(cancellationToken);
        var cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime - StaleAfter;

        var overdue = queue
            .Where(item => !item.IsResolved)
            .Where(item => item.Level is RiskLevel.High or RiskLevel.Critical)
            .Where(item => item.CreatedAtUtc <= cutoffUtc)
            .ToArray();

        if (overdue.Length == 0)
        {
            _logger.LogInformation("Crisis escalation sweep found no overdue high or critical cases.");
            return;
        }

        var criticalCount = overdue.Count(item => item.Level == RiskLevel.Critical);
        var total = overdue.Length.ToString("N0", CultureInfo.InvariantCulture);
        var detail = criticalCount > 0
            ? $"{total} unresolved high-risk {(overdue.Length == 1 ? "case has" : "cases have")} waited over 24 hours, including {criticalCount:N0} at critical level. Open the counselor queue to review."
            : $"{total} unresolved high-risk {(overdue.Length == 1 ? "case has" : "cases have")} waited over 24 hours. Open the counselor queue to review.";

        var notified = await _notifications.NotifyAdministratorsOnceAsync(
            NotificationType.CrisisEscalation,
            "High-risk cases awaiting review",
            detail,
            cancellationToken);

        _logger.LogWarning(
            "Crisis escalation sweep found {OverdueCount} overdue cases ({CriticalCount} critical) and notified {NotifiedCount} staff.",
            overdue.Length,
            criticalCount,
            notified);
    }
}
