using FluentAssertions;
using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

/// <summary>
/// Direct conversations, volunteer availability, unread markers, and the escalation handoff.
/// </summary>
public sealed class VolunteerSupportServiceHandoffTests
{
    // ---------- Direct conversations ----------

    [Fact]
    public async Task StartConversation_CreatesAcceptedRequestBoundToAssignedVolunteer()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var student = StudentProfile(7, "student-1");
        var volunteer = VolunteerProfile(3, "Vera");
        SupportRequest? captured = null;

        repo.Setup(r => r.GetStudentProfileByUserIdAsync("student-1", It.IsAny<CancellationToken>())).ReturnsAsync(student);
        repo.Setup(r => r.GetActiveAssignmentsForStudentAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Assignment(volunteer, 7) });
        repo.Setup(r => r.GetRequestsForStudentAsync("student-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SupportRequest>());
        repo.Setup(r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SupportRequest, CancellationToken>((request, _) => { captured = request; request.Id = 99; })
            .Returns(Task.CompletedTask);

        var notifications = new Mock<INotificationService>();
        var service = CreateService(repo, Student("student-1"), notifications: notifications);

        var requestId = await service.StartConversationWithVolunteerAsync(3);

        requestId.Should().Be(99);
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(SupportRequestStatus.Accepted);
        captured.VolunteerProfileId.Should().Be(3);
        captured.VolunteerProfile.Should().BeNull("untracked graphs must not be attached or EF re-inserts them");
        captured.IsVisibleToVolunteers.Should().BeFalse();
        notifications.Verify(n => n.CreateAsync("v-3", NotificationType.System, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartConversation_ReusesAnOpenConversationWithTheSameVolunteer()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var student = StudentProfile(7, "student-1");
        var volunteer = VolunteerProfile(3, "Vera");

        repo.Setup(r => r.GetStudentProfileByUserIdAsync("student-1", It.IsAny<CancellationToken>())).ReturnsAsync(student);
        repo.Setup(r => r.GetActiveAssignmentsForStudentAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Assignment(volunteer, 7) });
        repo.Setup(r => r.GetRequestsForStudentAsync("student-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SupportRequest { Id = 41, StudentProfileId = 7, VolunteerProfileId = 3, Status = SupportRequestStatus.Accepted, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
                new SupportRequest { Id = 40, StudentProfileId = 7, VolunteerProfileId = 3, Status = SupportRequestStatus.Completed, CreatedAtUtc = DateTime.UtcNow.AddDays(-9) }
            });

        var requestId = await CreateService(repo, Student("student-1")).StartConversationWithVolunteerAsync(3);

        requestId.Should().Be(41);
        repo.Verify(r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartConversation_RefusesAVolunteerWhoIsNotAssigned()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetStudentProfileByUserIdAsync("student-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StudentProfile(7, "student-1"));
        repo.Setup(r => r.GetActiveAssignmentsForStudentAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VolunteerAssignment>());

        var act = () => CreateService(repo, Student("student-1")).StartConversationWithVolunteerAsync(3);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------- Unread markers ----------

    [Fact]
    public async Task GetRequestForStudent_MarksReadAndCountsOnlyVolunteerMessagesSinceLastRead()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var request = new SupportRequest
        {
            Id = 5,
            StudentProfileId = 7,
            Status = SupportRequestStatus.Accepted,
            StudentLastReadAtUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            Interactions =
            {
                new SupportInteraction { VolunteerUserId = "v-3", Message = "old", CreatedAtUtc = new DateTime(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc) },
                new SupportInteraction { VolunteerUserId = "v-3", Message = "new", CreatedAtUtc = new DateTime(2026, 9, 2, 11, 0, 0, DateTimeKind.Utc) },
                new SupportInteraction { StudentUserId = "student-1", Message = "mine", CreatedAtUtc = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc) }
            }
        };
        repo.Setup(r => r.GetRequestForStudentAsync(5, "student-1", It.IsAny<CancellationToken>())).ReturnsAsync(request);
        repo.Setup(r => r.GetRequestsForStudentAsync("student-1", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { request });

        var service = CreateService(repo, Student("student-1"));

        // The list view counts one volunteer message newer than the last read marker; the student's own reply never counts.
        var listed = await service.GetRequestsForStudentAsync();
        listed.Should().ContainSingle().Which.UnreadForStudent.Should().Be(1);

        var dto = await service.GetRequestForStudentAsync(5);

        dto.Should().NotBeNull();
        dto!.UnreadForStudent.Should().Be(0, "opening the conversation marks it read");
        repo.Verify(r => r.MarkConversationReadAsync(5, false, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- Availability ----------

    [Fact]
    public async Task SetAvailability_MarksAwayWithDefaultWindowAndAuditsIt()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var volunteer = VolunteerProfile(3, "Vera");
        repo.Setup(r => r.GetVolunteerProfileByUserIdAsync("v-3", It.IsAny<CancellationToken>())).ReturnsAsync(volunteer);
        var audit = new Mock<IAuditLogService>();

        await CreateService(repo, Volunteer("v-3"), audit: audit)
            .SetAvailabilityAsync(new SetVolunteerAvailabilityDto(true, null, "  Exams  "));

        volunteer.AwayUntilUtc.Should().NotBeNull();
        volunteer.AwayUntilUtc!.Value.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
        volunteer.AwayMessage.Should().Be("Exams");
        volunteer.IsAwayAt(DateTime.UtcNow).Should().BeTrue();
        audit.Verify(a => a.Record("VolunteerProfile.Away", nameof(VolunteerProfile), "3", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SetAvailability_RejectsReturnDatesOutsideNinetyDays()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetVolunteerProfileByUserIdAsync("v-3", It.IsAny<CancellationToken>())).ReturnsAsync(VolunteerProfile(3, "Vera"));

        var act = () => CreateService(repo, Volunteer("v-3"))
            .SetAvailabilityAsync(new SetVolunteerAvailabilityDto(true, DateTime.UtcNow.AddDays(120), null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetRequestRouting_SkipsAwayVolunteers()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var available = VolunteerProfile(1, "Avail");
        var away = VolunteerProfile(2, "Away");
        away.AwayUntilUtc = DateTime.UtcNow.AddDays(3);

        repo.Setup(r => r.GetUnroutedPendingRequestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SupportRequest { Id = 1, StudentProfileId = 7, Category = SupportRequestCategory.GeneralSupport, Title = "General", StudentProfile = StudentProfile(7, "s") } });
        repo.Setup(r => r.GetAvailableVolunteersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { available, away });
        repo.Setup(r => r.GetActiveAssignmentCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int>());

        var routing = await CreateService(repo, InRole("admin", RoleConstants.Admin)).GetRequestRoutingAsync();

        routing.Requests.Should().ContainSingle();
        routing.Requests[0].Suggestions.Select(s => s.VolunteerProfileId).Should().Equal(1);
    }

    // ---------- Escalation handoff ----------

    [Fact]
    public async Task AcknowledgeEscalation_StampsCounselorAndNotifiesStudent()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var request = new SupportRequest
        {
            Id = 9,
            StudentProfileId = 7,
            StudentProfile = StudentProfile(7, "student-1"),
            Status = SupportRequestStatus.Escalated,
            EscalatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        repo.Setup(r => r.GetRequestByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        var notifications = new Mock<INotificationService>();

        var handled = await CreateService(repo, InRole("c-1", RoleConstants.Counselor), notifications: notifications)
            .AcknowledgeEscalationAsync(9, " Emailed the student ");

        handled.Should().BeTrue();
        request.EscalationHandledAtUtc.Should().NotBeNull();
        request.EscalationHandledByUserId.Should().Be("c-1");
        request.EscalationHandledNote.Should().Be("Emailed the student");
        notifications.Verify(n => n.CreateAsync("student-1", NotificationType.System, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeEscalation_IsIdempotentAndRefusesNonEscalatedRequests()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetRequestByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportRequest { Id = 1, Status = SupportRequestStatus.Escalated, EscalationHandledAtUtc = DateTime.UtcNow });
        repo.Setup(r => r.GetRequestByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportRequest { Id = 2, Status = SupportRequestStatus.Accepted });

        var service = CreateService(repo, InRole("c-1", RoleConstants.Counselor));

        (await service.AcknowledgeEscalationAsync(1, null)).Should().BeFalse("already handled");
        (await service.AcknowledgeEscalationAsync(2, null)).Should().BeFalse("not escalated");
    }

    [Fact]
    public async Task GetOpenEscalations_RefusesVolunteersAndStudents()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        await FluentActions.Awaiting(() => CreateService(repo, Volunteer("v-1")).GetOpenEscalationsAsync())
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => CreateService(repo, Student("s-1")).GetOpenEscalationsAsync())
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetOpenEscalations_ExposesVolunteerNoteButNeverTheConversation()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var volunteer = VolunteerProfile(3, "Vera");
        repo.Setup(r => r.GetUnhandledEscalationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SupportRequest
                {
                    Id = 9,
                    StudentProfileId = 7,
                    StudentProfile = StudentProfile(7, "student-1"),
                    VolunteerProfileId = 3,
                    VolunteerProfile = volunteer,
                    Status = SupportRequestStatus.Escalated,
                    Title = "Peer discussion",
                    EscalatedAtUtc = DateTime.UtcNow.AddHours(-1),
                    Interactions =
                    {
                        new SupportInteraction { StudentUserId = "student-1", Message = "private chat line", Type = SupportInteractionType.Message, CreatedAtUtc = DateTime.UtcNow.AddHours(-3) },
                        new SupportInteraction { VolunteerUserId = "v-3", Message = "Needs a counselor; exam anxiety", Type = SupportInteractionType.Escalated, EscalatedToCounselor = true, CreatedAtUtc = DateTime.UtcNow.AddHours(-1) }
                    }
                }
            });

        var escalations = await CreateService(repo, InRole("c-1", RoleConstants.Counselor)).GetOpenEscalationsAsync();

        escalations.Should().ContainSingle();
        escalations[0].EscalationMessage.Should().Be("Needs a counselor; exam anxiety");
        escalations[0].VolunteerDisplayName.Should().Be("Vera");
        escalations[0].ToString().Should().NotContain("private chat line");
    }

    // ---------- helpers ----------

    private static StudentProfile StudentProfile(int id, string userId)
        => new() { Id = id, UserId = userId, User = new ApplicationUser { Id = userId, FullName = $"Student {id}", IsActive = true } };

    private static VolunteerProfile VolunteerProfile(int id, string name)
        => new()
        {
            Id = id,
            UserId = $"v-{id}",
            User = new ApplicationUser { Id = $"v-{id}", FullName = name, IsActive = true },
            IsApproved = true,
            IsActive = true
        };

    private static VolunteerAssignment Assignment(VolunteerProfile volunteer, int studentProfileId)
        => new()
        {
            Id = volunteer.Id * 10,
            VolunteerProfileId = volunteer.Id,
            VolunteerProfile = volunteer,
            StudentProfileId = studentProfileId,
            Role = "Peer Mentor",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
        };

    private static Mock<ICurrentUserService> Student(string userId) => InRole(userId, RoleConstants.Student);
    private static Mock<ICurrentUserService> Volunteer(string userId) => InRole(userId, RoleConstants.Volunteer);

    private static Mock<ICurrentUserService> InRole(string userId, string role)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        currentUser.Setup(user => user.IsInRole(role)).Returns(true);
        return currentUser;
    }

    private static VolunteerSupportService CreateService(
        Mock<IVolunteerSupportRepository> repo,
        Mock<ICurrentUserService> currentUser,
        Mock<IAuditLogService>? audit = null,
        Mock<INotificationService>? notifications = null)
        => new(
            repo.Object,
            currentUser.Object,
            new Mock<IUnitOfWork>().Object,
            (audit ?? new Mock<IAuditLogService>()).Object,
            (notifications ?? new Mock<INotificationService>()).Object,
            new Mock<IPeerSupportNotifier>().Object,
            new Mock<IVolunteerRosterNotifier>().Object,
            NullLogger<VolunteerSupportService>.Instance);
}
