using System.Globalization;
using System.Net;
using Hangfire;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Lodestone.Jobs.BackgroundJobs;

/// <summary>
/// Emails each active counselor a weekly summary of what is waiting for them.
/// </summary>
/// <remarks>
/// The digest is deliberately count-only. It names no student and quotes no message: it tells a
/// counselor that work is waiting and where, and they sign in to see who. Email is the least
/// protected channel in the system, so it carries the least information. Nothing here contacts a
/// student, reads a risk score for a decision, or changes any record.
/// </remarks>
public class CounselorDigestJob : IMaintenanceJob
{
    /// <summary>Prompts sent within this window are summarised as "this week".</summary>
    private static readonly TimeSpan LookBack = TimeSpan.FromDays(7);

    private readonly IBookingRepository _bookings;
    private readonly ICounselorQueueService _queue;
    private readonly IVolunteerSupportRepository _peerSupport;
    private readonly INudgeRepository _nudges;
    private readonly IEmailService _emailService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CounselorDigestJob> _logger;

    public CounselorDigestJob(
        IBookingRepository bookings,
        ICounselorQueueService queue,
        IVolunteerSupportRepository peerSupport,
        INudgeRepository nudges,
        IEmailService emailService,
        TimeProvider timeProvider,
        ILogger<CounselorDigestJob> logger)
    {
        _bookings = bookings;
        _queue = queue;
        _peerSupport = peerSupport;
        _nudges = nudges;
        _emailService = emailService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 15 * 60)]
    [AutomaticRetry(Attempts = 1, DelaysInSeconds = new[] { 300 })]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var counselors = await _bookings.GetAllCounselorsAsync(cancellationToken);
        if (counselors.Count == 0)
        {
            _logger.LogInformation("Counselor digest found no active counselors to email.");
            return;
        }

        // Shared across recipients: the queue and escalations are team-wide, not per counselor.
        var openCases = (await _queue.GetQueueAsync(cancellationToken)).Where(item => !item.IsResolved).ToArray();
        var urgentCases = openCases.Count(item => item.Level is RiskLevel.High or RiskLevel.Critical);
        var staleCases = openCases.Count(item => item.CreatedAtUtc <= nowUtc.AddDays(-1));
        var escalations = await _peerSupport.GetUnhandledEscalationsAsync(cancellationToken);
        var oldestEscalationDays = escalations.Count == 0
            ? 0
            : (int)Math.Floor((nowUtc - escalations.Min(request => request.EscalatedAtUtc ?? request.CreatedAtUtc)).TotalDays);

        var sent = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var counselor in counselors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recipient = counselor.User?.Email;
            if (string.IsNullOrWhiteSpace(recipient))
            {
                skipped++;
                continue;
            }

            var prompts = await _nudges.GetManualByCounselorAsync(counselor.UserId, nowUtc - LookBack, cancellationToken);
            var promptSummary = new PromptSummary(
                Sent: prompts.Count,
                Acknowledged: prompts.Count(nudge => nudge.Status == NudgeStatus.Acknowledged),
                Snoozed: prompts.Count(nudge => nudge.Status == NudgeStatus.Snoozed),
                Dismissed: prompts.Count(nudge => nudge.Status == NudgeStatus.Dismissed),
                Awaiting: prompts.Count(nudge => nudge.Status is NudgeStatus.Pending or NudgeStatus.Sent && nudge.ExpiresAtUtc > nowUtc));

            // Nothing waiting and nothing sent: a silent week is better than an empty email.
            if (openCases.Length == 0 && escalations.Count == 0 && promptSummary.Sent == 0)
            {
                skipped++;
                continue;
            }

            try
            {
                await _emailService.SendAsync(
                    recipient,
                    "Your weekly Lodestone support digest",
                    BuildBody(counselor.User?.FullName, nowUtc, openCases.Length, urgentCases, staleCases, escalations.Count, oldestEscalationDays, promptSummary),
                    cancellationToken);
                sent++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                _logger.LogError(exception, "Could not send the weekly digest to counselor profile {CounselorProfileId}.", counselor.Id);
            }
        }

        _logger.LogInformation(
            "Counselor digest sent {SentCount}, skipped {SkippedCount}, failed {FailedCount} of {CounselorCount} counselors.",
            sent,
            skipped,
            failed,
            counselors.Count);
    }

    private sealed record PromptSummary(int Sent, int Acknowledged, int Snoozed, int Dismissed, int Awaiting);

    private static string BuildBody(
        string? counselorName,
        DateTime nowUtc,
        int openCases,
        int urgentCases,
        int staleCases,
        int escalations,
        int oldestEscalationDays,
        PromptSummary prompts)
    {
        var greeting = string.IsNullOrWhiteSpace(counselorName)
            ? "Hello,"
            : $"Hello {WebUtility.HtmlEncode(counselorName.Split(' ', 2)[0])},";
        var weekOf = nowUtc.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);

        var caseLine = openCases == 0
            ? "<li>No learning-risk cases are waiting for review.</li>"
            : $"<li><strong>{openCases:N0}</strong> learning-risk {Plural(openCases, "case is", "cases are")} open" +
              (urgentCases > 0 ? $", <strong>{urgentCases:N0}</strong> at high or critical level" : string.Empty) +
              (staleCases > 0 ? $"; {staleCases:N0} {Plural(staleCases, "has", "have")} waited more than a day" : string.Empty) +
              ".</li>";

        var escalationLine = escalations == 0
            ? "<li>No peer-support escalations are waiting for a counselor.</li>"
            : $"<li><strong>{escalations:N0}</strong> peer-support {Plural(escalations, "escalation", "escalations")} from volunteers {Plural(escalations, "is", "are")} waiting for a counselor to take {Plural(escalations, "it", "them")}" +
              (oldestEscalationDays >= 1 ? $" (oldest: {oldestEscalationDays:N0} {Plural(oldestEscalationDays, "day", "days")})" : string.Empty) +
              ".</li>";

        var promptLine = prompts.Sent == 0
            ? "<li>You sent no optional support prompts this week.</li>"
            : $"<li>You sent <strong>{prompts.Sent:N0}</strong> optional {Plural(prompts.Sent, "prompt", "prompts")} this week: " +
              $"{prompts.Acknowledged:N0} acknowledged, {prompts.Snoozed:N0} snoozed, {prompts.Dismissed:N0} dismissed, {prompts.Awaiting:N0} still awaiting a response.</li>";

        return $"""
            <p>{greeting}</p>
            <p>Here is what is waiting for you in Lodestone as of {weekOf}:</p>
            <ul>
                {caseLine}
                {escalationLine}
                {promptLine}
            </ul>
            <p>Sign in and open <strong>Support queue</strong> and <strong>Appointments</strong> to see the details. This email deliberately names no student.</p>
            <p>— Lodestone</p>
            """;
    }

    private static string Plural(int count, string one, string many) => count == 1 ? one : many;
}
