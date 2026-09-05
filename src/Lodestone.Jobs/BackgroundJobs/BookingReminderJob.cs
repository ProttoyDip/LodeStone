using System.Globalization;
using System.Net;
using Hangfire;
using Lodestone.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Lodestone.Jobs.BackgroundJobs;

/// <summary>
/// Emails students a reminder about a counseling session they booked.
/// </summary>
/// <remarks>
/// This is transactional contact about the student's own confirmed booking, not risk-driven
/// outreach: nothing here reads a risk score, and no student is contacted because a model flagged
/// them. The reminder is stamped before it is sent, so a transport failure leaves the session
/// un-reminded rather than repeatedly emailed — for a wellbeing service, a missed reminder is a
/// much smaller harm than a loop of duplicates.
/// </remarks>
public class BookingReminderJob : IMaintenanceJob
{
    /// <summary>Sessions starting inside this window ahead of the run are reminded.</summary>
    private static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(24);

    private readonly IBookingRepository _bookings;
    private readonly IEmailService _emailService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookingReminderJob> _logger;

    public BookingReminderJob(
        IBookingRepository bookings,
        IEmailService emailService,
        TimeProvider timeProvider,
        ILogger<BookingReminderJob> logger)
    {
        _bookings = bookings;
        _emailService = emailService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 15 * 60)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var due = await _bookings.GetBookingsDueForReminderAsync(
            nowUtc,
            nowUtc + ReminderLeadTime,
            cancellationToken);

        if (due.Count == 0)
        {
            _logger.LogInformation("Booking reminder sweep found no sessions needing a reminder.");
            return;
        }

        var sent = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var booking in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recipient = booking.StudentProfile?.User?.Email;
            if (string.IsNullOrWhiteSpace(recipient))
            {
                skipped++;
                _logger.LogWarning(
                    "Booking {BookingId} has no student email address and was skipped.",
                    booking.Id);
                continue;
            }

            // Claim the booking before sending. If another worker already claimed it, skip.
            if (!await _bookings.TryMarkReminderSentAsync(booking.Id, nowUtc, cancellationToken))
            {
                skipped++;
                continue;
            }

            try
            {
                await _emailService.SendAsync(
                    recipient,
                    "Reminder: your Lodestone counseling session",
                    BuildBody(booking.ScheduledForUtc, booking.CounselorProfile?.User?.FullName),
                    cancellationToken);
                sent++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Left stamped on purpose: retrying risks duplicate mail to a student who may
                // already have received it, which is worse than a single missed reminder.
                failed++;
                _logger.LogError(
                    exception,
                    "Could not send the session reminder for booking {BookingId}.",
                    booking.Id);
            }
        }

        _logger.LogInformation(
            "Booking reminder sweep sent {SentCount}, skipped {SkippedCount}, failed {FailedCount} of {DueCount} due sessions.",
            sent,
            skipped,
            failed,
            due.Count);
    }

    private static string BuildBody(DateTime scheduledForUtc, string? counselorName)
    {
        var when = scheduledForUtc.ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);
        var withCounselor = string.IsNullOrWhiteSpace(counselorName)
            ? string.Empty
            : $" with {WebUtility.HtmlEncode(counselorName)}";

        return $"""
            <p>Hello,</p>
            <p>This is a reminder of your counseling session{withCounselor} on
            <strong>{WebUtility.HtmlEncode(when)} UTC</strong>.</p>
            <p>If you can no longer attend, please cancel in Lodestone so the slot can be offered
            to another student.</p>
            <p>If you need urgent support before then, please use the crisis resources listed in
            Lodestone rather than waiting for this session.</p>
            <p>— Lodestone</p>
            """;
    }
}
