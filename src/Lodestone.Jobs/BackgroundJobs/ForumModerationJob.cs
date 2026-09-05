using System.Globalization;
using Hangfire;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Lodestone.Jobs.BackgroundJobs;

/// <summary>
/// Sweeps flagged forum content for automated moderation triage.
/// </summary>
/// <remarks>
/// Triage means surfacing, never deciding. This job deliberately does not call
/// <see cref="IForumService.ReviewPostAsync"/>: removing or restoring community content is a
/// moderator judgement about a distressed person's post, and automating it would hide content
/// from the author with no human having read it. The job only tells moderators that a backlog
/// exists, and repeats itself no more than once per unread alert.
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
        var flagged = await _forumService.GetFlaggedPostsAsync(cancellationToken);
        if (flagged.Count == 0)
        {
            _logger.LogInformation("Forum moderation sweep found no flagged posts awaiting review.");
            return;
        }

        var count = flagged.Count.ToString("N0", CultureInfo.InvariantCulture);
        var notified = await _notifications.NotifyAdministratorsOnceAsync(
            NotificationType.System,
            "Flagged discussions awaiting review",
            $"{count} flagged {(flagged.Count == 1 ? "discussion is" : "discussions are")} waiting for moderator review.",
            cancellationToken);

        _logger.LogInformation(
            "Forum moderation sweep found {FlaggedCount} flagged posts and notified {NotifiedCount} moderators.",
            flagged.Count,
            notified);
    }
}
