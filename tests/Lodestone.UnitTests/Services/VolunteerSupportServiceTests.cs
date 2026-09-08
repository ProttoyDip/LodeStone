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

public sealed class VolunteerSupportServiceTests
{
    [Fact]
    public async Task CreateSupportRequestAsync_CreatesUnassignedPendingRequest()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var audit = new Mock<IAuditLogService>();
        var notifications = new Mock<INotificationService>();
        var currentUser = Student("student-42");

        var student = new StudentProfile
        {
            Id = 7,
            UserId = "student-42",
            User = new ApplicationUser { Id = "student-42", FullName = "Student A" }
        };

        SupportRequest? captured = null;
        repo.Setup(r => r.GetStudentProfileByUserIdAsync("student-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(student);
        repo.Setup(r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SupportRequest, CancellationToken>((request, _) => captured = request)
            .Returns(Task.CompletedTask);

        var service = CreateService(repo, currentUser, unitOfWork, audit, notifications);

        var result = await service.CreateSupportRequestAsync(
            new CreateSupportRequestDto(
                SupportRequestCategory.AcademicGuidance,
                "I need advice on choosing an elective and planning my schedule.",
                "Weekday afternoons"),
            CancellationToken.None);

        result.Category.Should().Be(SupportRequestCategory.AcademicGuidance);
        result.Status.Should().Be(SupportRequestStatus.Pending);

        // Requests are raised unassigned; routing happens through admin volunteer assignments.
        captured.Should().NotBeNull();
        captured!.VolunteerProfileId.Should().BeNull();
        captured.IsVisibleToVolunteers.Should().BeTrue();
        captured.Title.Should().NotBeNullOrWhiteSpace("the service derives a title from the category");

        repo.Verify(r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSupportRequestAsync_RejectsAnUndefinedCategory()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var service = CreateService(repo, Student("student-42"));

        var act = async () => await service.CreateSupportRequestAsync(
            new CreateSupportRequestDto((SupportRequestCategory)99, "Message", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        repo.Verify(
            r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateSupportRequestAsync_RefusesACallerWhoIsNotAStudent()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns("volunteer-1");
        currentUser.Setup(user => user.IsInRole(It.IsAny<string>())).Returns(false);

        var service = CreateService(repo, currentUser);

        var act = async () => await service.CreateSupportRequestAsync(
            new CreateSupportRequestDto(SupportRequestCategory.GeneralSupport, "Message", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetVolunteerDashboardAsync_BlocksAVolunteerAwaitingApproval()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetVolunteerProfileByUserIdAsync("vol-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VolunteerProfile
            {
                Id = 4,
                UserId = "vol-1",
                IsApproved = false,
                IsActive = true,
                User = new ApplicationUser { Id = "vol-1", FullName = "Volunteer A" }
            });

        var service = CreateService(repo, Volunteer("vol-1"));

        var dashboard = await service.GetVolunteerDashboardAsync(CancellationToken.None);

        dashboard.CanHandleRequests.Should().BeFalse();
        dashboard.AccessMessage.Should().NotBeNullOrWhiteSpace();
        dashboard.PendingRequests.Should().BeEmpty();
        repo.Verify(
            r => r.GetRequestsForVolunteerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetVolunteerDashboardAsync_ExplainsWhenNoProfileExists()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetVolunteerProfileByUserIdAsync("vol-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((VolunteerProfile?)null);

        var service = CreateService(repo, Volunteer("vol-1"));

        var dashboard = await service.GetVolunteerDashboardAsync(CancellationToken.None);

        dashboard.CanHandleRequests.Should().BeFalse();
        dashboard.AccessMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateVolunteerProfileAsync_SetsTheNameTheInvitationCouldNotSupply()
    {
        var account = new ApplicationUser { Id = "vol-1", Email = "vol@university.test", FullName = string.Empty };
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetVolunteerProfileByUserIdAsync("vol-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((VolunteerProfile?)null);
        repo.Setup(r => r.GetTrackedUserAsync("vol-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        VolunteerProfile? captured = null;
        repo.Setup(r => r.CreateVolunteerProfileAsync(It.IsAny<VolunteerProfile>(), It.IsAny<CancellationToken>()))
            .Callback<VolunteerProfile, CancellationToken>((profile, _) => captured = profile)
            .Returns(Task.CompletedTask);

        var service = CreateService(repo, Volunteer("vol-1"));

        await service.CreateVolunteerProfileAsync(
            new CreateVolunteerProfileDto("  Volunteer A  ", "Computer Science", "Study planning", "Evenings", "Hello."),
            CancellationToken.None);

        account.FullName.Should().Be("Volunteer A");
        captured.Should().NotBeNull();
        captured!.Department.Should().Be("Computer Science");
        captured.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateVolunteerProfileAsync_LeavesTheProfileWaitingForApproval()
    {
        VolunteerProfile? captured = null;
        var repo = ProfileCreationRepo(profile => captured = profile);
        var service = CreateService(repo, Volunteer("vol-1"));

        await service.CreateVolunteerProfileAsync(Profile(), CancellationToken.None);

        // A volunteer describing themselves is not the same as an administrator vouching for them.
        captured!.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task CreateVolunteerProfileAsync_TellsAdministratorsAProfileIsWaiting()
    {
        var repo = ProfileCreationRepo();
        var notifications = new Mock<INotificationService>();
        var service = CreateService(repo, Volunteer("vol-1"), notifications: notifications);

        await service.CreateVolunteerProfileAsync(Profile(), CancellationToken.None);

        notifications.Verify(
            n => n.NotifyAdministratorsAsync(
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public async Task CreateVolunteerProfileAsync_RequiresAUsableName(string fullName)
    {
        var repo = ProfileCreationRepo();
        var service = CreateService(repo, Volunteer("vol-1"));

        var act = async () => await service.CreateVolunteerProfileAsync(
            Profile(fullName),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        repo.Verify(
            r => r.CreateVolunteerProfileAsync(It.IsAny<VolunteerProfile>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateVolunteerProfileAsync_RefusesASecondProfile()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetVolunteerProfileByUserIdAsync("vol-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VolunteerProfile { Id = 4, UserId = "vol-1" });

        var service = CreateService(repo, Volunteer("vol-1"));

        var act = async () => await service.CreateVolunteerProfileAsync(Profile(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateSupportRequestAsync_SignalsOnlyTheVolunteersAssignedToThatStudent()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetStudentProfileByUserIdAsync("student-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StudentProfile
            {
                Id = 7,
                UserId = "student-42",
                User = new ApplicationUser { Id = "student-42", FullName = "Student A" }
            });
        repo.Setup(r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetAssignedVolunteerUserIdsAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "vol-assigned" });

        IReadOnlyCollection<string>? signalled = null;
        var peerSupport = new Mock<IPeerSupportNotifier>();
        peerSupport.Setup(n => n.NotifyChangedAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, CancellationToken>((ids, _) => signalled = ids)
            .Returns(Task.CompletedTask);

        var service = CreateService(repo, Student("student-42"), peerSupport: peerSupport);

        await service.CreateSupportRequestAsync(
            new CreateSupportRequestDto(SupportRequestCategory.GeneralSupport, "Help please.", null),
            CancellationToken.None);

        // Only the student and the volunteers actually assigned to them; a request is private to
        // the people already entitled to see it.
        signalled.Should().NotBeNull();
        signalled.Should().BeEquivalentTo(new[] { "student-42", "vol-assigned" });
    }

    [Fact]
    public async Task CreateSupportRequestAsync_SucceedsEvenIfTheRealtimeSignalFails()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetStudentProfileByUserIdAsync("student-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StudentProfile { Id = 7, UserId = "student-42" });
        repo.Setup(r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetAssignedVolunteerUserIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var peerSupport = new Mock<IPeerSupportNotifier>();
        peerSupport.Setup(n => n.NotifyChangedAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub unavailable"));

        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(repo, Student("student-42"), unitOfWork, peerSupport: peerSupport);

        // The request is already committed by the time the signal is sent; a transport failure
        // must not turn a saved request into an error for the student.
        var act = async () => await service.CreateSupportRequestAsync(
            new CreateSupportRequestDto(SupportRequestCategory.GeneralSupport, "Help please.", null),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateVolunteerProfileAsync_TellsAdministratorsTheirVolunteerListChanged()
    {
        var repo = ProfileCreationRepo(_ => { });
        var roster = new Mock<IVolunteerRosterNotifier>();
        var service = CreateService(repo, Volunteer("vol-1"), roster: roster);

        await service.CreateVolunteerProfileAsync(Profile(), CancellationToken.None);

        // This is the one roster change no administrator causes themselves, so it is the one their
        // open list would otherwise never learn about.
        roster.Verify(n => n.NotifyRosterChangedAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateVolunteerProfileAsync_SucceedsEvenIfTheRosterSignalFails()
    {
        var repo = ProfileCreationRepo(_ => { });
        var unitOfWork = new Mock<IUnitOfWork>();
        var roster = new Mock<IVolunteerRosterNotifier>();
        roster.Setup(n => n.NotifyRosterChangedAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub unavailable"));

        var service = CreateService(repo, Volunteer("vol-1"), unitOfWork, roster: roster);

        // The profile is committed before the signal is sent; a volunteer who has just filled in
        // their details must not be told it failed because a hub was unreachable.
        var act = async () => await service.CreateVolunteerProfileAsync(Profile(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- helpers ----------

    private static CreateVolunteerProfileDto Profile(string fullName = "Volunteer A")
        => new(fullName, "Computer Science", "Study planning", "Evenings", "Hello.");

    private static Mock<IVolunteerSupportRepository> ProfileCreationRepo(Action<VolunteerProfile>? onCreate = null)
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetVolunteerProfileByUserIdAsync("vol-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((VolunteerProfile?)null);
        repo.Setup(r => r.GetTrackedUserAsync("vol-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUser { Id = "vol-1", FullName = string.Empty });
        repo.Setup(r => r.CreateVolunteerProfileAsync(It.IsAny<VolunteerProfile>(), It.IsAny<CancellationToken>()))
            .Callback<VolunteerProfile, CancellationToken>((profile, _) => onCreate?.Invoke(profile))
            .Returns(Task.CompletedTask);
        return repo;
    }

    [Fact]
    public async Task GetRequestRoutingAsync_RanksVolunteersForEachUnroutedRequestAndNeverQuotesTheStudent()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var studentMessage = "I keep failing my python labs and my laptop will not connect to the portal.";
        repo.Setup(r => r.GetUnroutedPendingRequestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SupportRequest
                {
                    Id = 11,
                    StudentProfileId = 5,
                    StudentProfile = new StudentProfile { Id = 5, UserId = "s-5", User = new ApplicationUser { Id = "s-5", FullName = "Student Five" } },
                    Category = SupportRequestCategory.TechnicalHelp,
                    Title = "Technical help",
                    Message = studentMessage,
                    Availability = "Tuesday evenings",
                    Status = SupportRequestStatus.Pending,
                    IsVisibleToVolunteers = true,
                    CreatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            });
        repo.Setup(r => r.GetAvailableVolunteersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Volunteer(1, "Ada", skills: "python, programming, laptops", availability: "Tuesday evenings"),
                Volunteer(2, "Ben", skills: "essay writing, revision", availability: "Weekends"),
                Volunteer(3, "Cal", skills: "python", availability: "Monday"),
                Volunteer(4, "Dee", skills: null, availability: null)
            });
        repo.Setup(r => r.GetActiveAssignmentCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int> { [1] = 1, [3] = 4 });

        var routing = await CreateService(repo, InRole("admin", RoleConstants.Admin)).GetRequestRoutingAsync();

        routing.AvailableVolunteers.Should().Be(4);
        var item = routing.Requests.Should().ContainSingle().Subject;
        item.Suggestions.Should().HaveCount(VolunteerSupportService.SuggestionsPerRequest);
        item.Suggestions[0].FullName.Should().Be("Ada");
        item.Suggestions.SelectMany(match => match.Reasons)
            .Should().NotContain(reason => reason.Contains("failing", StringComparison.OrdinalIgnoreCase));
        item.GetType().GetProperties().Select(property => property.Name).Should().NotContain("Message");
    }

    [Fact]
    public async Task GetRequestRoutingAsync_DoesNotScoreWhenNothingIsWaiting()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetUnroutedPendingRequestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SupportRequest>());
        repo.Setup(r => r.GetAvailableVolunteersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Volunteer(1, "Ada", "python", "Tuesday") });

        var routing = await CreateService(repo, InRole("admin", RoleConstants.Admin)).GetRequestRoutingAsync();

        routing.Requests.Should().BeEmpty();
        routing.AvailableVolunteers.Should().Be(1);
        repo.Verify(r => r.GetActiveAssignmentCountsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRequestRoutingAsync_RefusesANonAdministrator()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var act = async () => await CreateService(repo, Volunteer("v-1")).GetRequestRoutingAsync();
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static VolunteerProfile Volunteer(int id, string name, string? skills, string? availability)
        => new()
        {
            Id = id,
            UserId = $"v-{id}",
            User = new ApplicationUser { Id = $"v-{id}", FullName = name, IsActive = true },
            Skills = skills,
            Availability = availability,
            IsApproved = true,
            IsActive = true
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
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IAuditLogService>? audit = null,
        Mock<INotificationService>? notifications = null,
        Mock<IPeerSupportNotifier>? peerSupport = null,
        Mock<IVolunteerRosterNotifier>? roster = null)
        => new(
            repo.Object,
            currentUser.Object,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            (audit ?? new Mock<IAuditLogService>()).Object,
            (notifications ?? new Mock<INotificationService>()).Object,
            (peerSupport ?? new Mock<IPeerSupportNotifier>()).Object,
            (roster ?? new Mock<IVolunteerRosterNotifier>()).Object,
            NullLogger<VolunteerSupportService>.Instance);
}
