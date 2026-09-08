using FluentAssertions;
using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Identity;
using Lodestone.Jobs.BackgroundJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Jobs;

public sealed class MaintenanceJobTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static TimeProvider Clock() => new FixedTimeProvider(new DateTimeOffset(NowUtc));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ---------- Forum moderation ----------

    [Fact]
    public async Task ForumModeration_notifies_moderators_when_flagged_posts_are_waiting()
    {
        var forum = new Mock<IForumService>();
        forum.Setup(service => service.GetModerationQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Queue(FlaggedPost(1), FlaggedPost(2)));
        var notifications = new Mock<INotificationService>();

        await new ForumModerationJob(forum.Object, notifications.Object, NullLogger<ForumModerationJob>.Instance)
            .ExecuteAsync();

        notifications.Verify(
            service => service.NotifyAdministratorsOnceAsync(
                NotificationType.System,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ForumModeration_stays_silent_when_there_is_no_backlog()
    {
        var forum = new Mock<IForumService>();
        forum.Setup(service => service.GetModerationQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Queue());
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);

        await new ForumModerationJob(forum.Object, notifications.Object, NullLogger<ForumModerationJob>.Instance)
            .ExecuteAsync();

        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForumModeration_never_reviews_posts_itself()
    {
        var forum = new Mock<IForumService>();
        forum.Setup(service => service.GetModerationQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Queue(FlaggedPost(1)));

        await new ForumModerationJob(
                forum.Object,
                new Mock<INotificationService>().Object,
                NullLogger<ForumModerationJob>.Instance)
            .ExecuteAsync();

        // Removing or restoring community content is a moderator decision, never an automated one.
        forum.Verify(
            service => service.ReviewPostAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------- Crisis escalation ----------

    [Fact]
    public async Task CrisisEscalation_alerts_staff_about_overdue_high_risk_cases()
    {
        var queue = new Mock<ICounselorQueueService>();
        queue.Setup(service => service.GetQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                QueueItem(1, RiskLevel.Critical, NowUtc.AddDays(-3)),
                QueueItem(2, RiskLevel.High, NowUtc.AddDays(-2))
            });
        var notifications = new Mock<INotificationService>();

        await new CrisisResourceEscalationJob(
                queue.Object,
                notifications.Object,
                Clock(),
                NullLogger<CrisisResourceEscalationJob>.Instance)
            .ExecuteAsync();

        notifications.Verify(
            service => service.NotifyAdministratorsOnceAsync(
                NotificationType.CrisisEscalation,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CrisisEscalation_ignores_recent_resolved_and_low_level_cases()
    {
        var queue = new Mock<ICounselorQueueService>();
        queue.Setup(service => service.GetQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                QueueItem(1, RiskLevel.Critical, NowUtc.AddHours(-2)),                    // too recent
                QueueItem(2, RiskLevel.Critical, NowUtc.AddDays(-3), isResolved: true),   // already handled
                QueueItem(3, RiskLevel.Moderate, NowUtc.AddDays(-5))                      // below threshold
            });
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);

        await new CrisisResourceEscalationJob(
                queue.Object,
                notifications.Object,
                Clock(),
                NullLogger<CrisisResourceEscalationJob>.Instance)
            .ExecuteAsync();

        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CrisisEscalation_never_resolves_a_case_itself()
    {
        var queue = new Mock<ICounselorQueueService>();
        queue.Setup(service => service.GetQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { QueueItem(1, RiskLevel.Critical, NowUtc.AddDays(-3)) });

        await new CrisisResourceEscalationJob(
                queue.Object,
                new Mock<INotificationService>().Object,
                Clock(),
                NullLogger<CrisisResourceEscalationJob>.Instance)
            .ExecuteAsync();

        queue.Verify(
            service => service.TryResolveAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RiskCaseResolution>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------- Booking reminders ----------

    [Fact]
    public async Task BookingReminder_emails_the_student_and_stamps_the_booking()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(repository => repository.GetBookingsDueForReminderAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Booking(11, "student@university.test") });
        bookings.Setup(repository => repository.TryMarkReminderSentAsync(
                11, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var email = new Mock<IEmailService>();

        await new BookingReminderJob(
                bookings.Object, email.Object, Clock(), NullLogger<BookingReminderJob>.Instance)
            .ExecuteAsync();

        email.Verify(
            service => service.SendAsync(
                "student@university.test",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        bookings.Verify(
            repository => repository.TryMarkReminderSentAsync(11, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BookingReminder_does_not_send_when_another_worker_claimed_the_booking()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(repository => repository.GetBookingsDueForReminderAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Booking(11, "student@university.test") });
        bookings.Setup(repository => repository.TryMarkReminderSentAsync(
                11, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var email = new Mock<IEmailService>(MockBehavior.Strict);

        await new BookingReminderJob(
                bookings.Object, email.Object, Clock(), NullLogger<BookingReminderJob>.Instance)
            .ExecuteAsync();

        // Losing the claim race must mean silence, not a duplicate email to the student.
        email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BookingReminder_skips_a_student_without_an_email_address()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(repository => repository.GetBookingsDueForReminderAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Booking(11, email: null) });
        var email = new Mock<IEmailService>(MockBehavior.Strict);

        await new BookingReminderJob(
                bookings.Object, email.Object, Clock(), NullLogger<BookingReminderJob>.Instance)
            .ExecuteAsync();

        email.VerifyNoOtherCalls();
        bookings.Verify(
            repository => repository.TryMarkReminderSentAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BookingReminder_keeps_going_after_one_send_fails()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(repository => repository.GetBookingsDueForReminderAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Booking(11, "first@university.test"), Booking(12, "second@university.test") });
        bookings.Setup(repository => repository.TryMarkReminderSentAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var email = new Mock<IEmailService>();
        email.Setup(service => service.SendAsync(
                "first@university.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp unavailable"));

        var act = async () => await new BookingReminderJob(
                bookings.Object, email.Object, Clock(), NullLogger<BookingReminderJob>.Instance)
            .ExecuteAsync();

        await act.Should().NotThrowAsync();
        email.Verify(
            service => service.SendAsync(
                "second@university.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------- builders ----------

    private static ForumPostDto FlaggedPost(int id)
        => new(id, 1, "author", "Post", "Body", ForumPostStatus.Flagged, NowUtc.AddDays(-1));

    // ---------- Counselor digest ----------

    [Fact]
    public async Task CounselorDigest_emails_counts_only_and_never_a_student_name()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(repo => repo.GetAllCounselorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Counselor(1, "c-1", "osei@university.test") });
        var queue = new Mock<ICounselorQueueService>();
        queue.Setup(service => service.GetQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { QueueItem(1, RiskLevel.High, NowUtc.AddDays(-2)), QueueItem(2, RiskLevel.Low, NowUtc) });
        var peerSupport = new Mock<IVolunteerSupportRepository>();
        peerSupport.Setup(repo => repo.GetUnhandledEscalationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SupportRequest
                {
                    Id = 9,
                    Status = SupportRequestStatus.Escalated,
                    EscalatedAtUtc = NowUtc.AddDays(-3),
                    StudentProfile = new StudentProfile { User = new ApplicationUser { FullName = "Priya Secret" } }
                }
            });
        var nudges = new Mock<INudgeRepository>();
        nudges.Setup(repo => repo.GetManualByCounselorAsync("c-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Nudge { Status = NudgeStatus.Acknowledged, ExpiresAtUtc = NowUtc.AddDays(5) },
                new Nudge { Status = NudgeStatus.Sent, ExpiresAtUtc = NowUtc.AddDays(5) }
            });
        string? body = null;
        var email = new Mock<IEmailService>();
        email.Setup(service => service.SendAsync("osei@university.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, html, _) => body = html)
            .Returns(Task.CompletedTask);

        await new CounselorDigestJob(
                bookings.Object, queue.Object, peerSupport.Object, nudges.Object, email.Object, Clock(),
                NullLogger<CounselorDigestJob>.Instance)
            .ExecuteAsync();

        body.Should().NotBeNull();
        body.Should().Contain("<strong>2</strong> learning-risk cases are open");
        body.Should().Contain("<strong>1</strong> at high or critical level");
        body.Should().Contain("<strong>1</strong> peer-support escalation");
        body.Should().Contain("1 acknowledged");
        body.Should().Contain("1 still awaiting");
        body.Should().NotContain("Priya", "the digest must never name a student");
        body.Should().NotContain("Student", "not even the placeholder display name");
    }

    [Fact]
    public async Task CounselorDigest_skips_a_counselor_with_a_quiet_week()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(repo => repo.GetAllCounselorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Counselor(1, "c-1", "osei@university.test") });
        var queue = new Mock<ICounselorQueueService>();
        queue.Setup(service => service.GetQueueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<RiskQueueItemDto>());
        var peerSupport = new Mock<IVolunteerSupportRepository>();
        peerSupport.Setup(repo => repo.GetUnhandledEscalationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<SupportRequest>());
        var nudges = new Mock<INudgeRepository>();
        nudges.Setup(repo => repo.GetManualByCounselorAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Nudge>());
        var email = new Mock<IEmailService>(MockBehavior.Strict);

        await new CounselorDigestJob(
                bookings.Object, queue.Object, peerSupport.Object, nudges.Object, email.Object, Clock(),
                NullLogger<CounselorDigestJob>.Instance)
            .ExecuteAsync();

        email.VerifyNoOtherCalls();
    }

    private static CounselorProfile Counselor(int id, string userId, string email)
        => new() { Id = id, UserId = userId, User = new ApplicationUser { Id = userId, Email = email, FullName = "Dr Osei", IsActive = true } };

    private static ForumModerationQueueDto Queue(params ForumPostDto[] reported)
        => new(
            reported.Select(post => new ForumModerationQueueItemDto(
                post, 0.5, new[] { "Reported by a community member and not yet reviewed" }, true, false)).ToArray(),
            reported.Length,
            0);

    private static RiskQueueItemDto QueueItem(
        int id,
        RiskLevel level,
        DateTime createdAtUtc,
        bool isResolved = false)
        => new(id, id, "Student", level, isResolved, createdAtUtc);

    private static CounselorBooking Booking(int id, string? email)
        => new()
        {
            Id = id,
            ScheduledForUtc = NowUtc.AddHours(12),
            Status = BookingStatus.Confirmed,
            StudentProfile = new StudentProfile
            {
                User = new ApplicationUser { Email = email, FullName = "Student" }
            },
            CounselorProfile = new CounselorProfile
            {
                User = new ApplicationUser { Email = "counselor@university.test", FullName = "Dr Osei" }
            }
        };
}
