using System.Globalization;
using Hangfire;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Lodestone.Jobs.BackgroundJobs;

/// <summary>
/// Sweeps the forum and tells moderators when their triage queue has a backlog.
/// </summary>
/// <remarks>
/// Triage means surfacing, never deciding. This job deliberately does not call
/// <see cref="IForumService.ReviewPostAsync"/>: removing or restoring community content is a
/// moderator judgement about a distressed person's post, and automating it would hide content
/// from the author with no human having read it. The job only tells moderators that a backlog
/// exists, and repeats itself no more than once per unread alert. The queue it counts comes from
/// <c>ForumTriageRanker</c>, so posts nobody reported are included when they went unanswered.
/// </remarks>
public class ForumModerationJob : IMaintenanceJob
{
    private readonly IForumService _forumService;
    private readonly INotificationService _notifications;
    private readonly ILogger<ForumModerationJob> _logger;

    public ForumModerationJob(
        IForumService forumService,
        INotificationService notifications,
        ILogger<ForumModerationJob> logger)
    {
        _forumService = forumService;
        _notifications = notifications;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var queue = await _forumService.GetModerationQueueAsync(cancellationToken);
        if (queue.Items.Count == 0)
        {
            _logger.LogInformation("Forum moderation sweep found no posts awaiting review.");
            return;
        }

        var reported = queue.ReportedCount.ToString("N0", CultureInfo.InvariantCulture);
        var surfaced = queue.SurfacedWithoutFlagCount.ToString("N0", CultureInfo.InvariantCulture);
        var message = queue.SurfacedWithoutFlagCount == 0
            ? $"{reported} reported {(queue.ReportedCount == 1 ? "discussion is" : "discussions are")} waiting for moderator review."
            : $"{reported} reported and {surfaced} unanswered {(queue.Items.Count == 1 ? "discussion is" : "discussions are")} waiting for moderator review.";

        var notified = await _notifications.NotifyAdministratorsOnceAsync(
            NotificationType.System,
            "Discussions awaiting review",
            message,
            cancellationToken);

        _logger.LogInformation(
            "Forum moderation sweep found {ReportedCount} reported and {SurfacedCount} surfaced posts and notified {NotifiedCount} moderators.",
            queue.ReportedCount,
            queue.SurfacedWithoutFlagCount,
            notified);
    }
}
