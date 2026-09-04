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

    // ---------- helpers ----------

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
        Mock<INotificationService>? notifications = null)
        => new(
            repo.Object,
            currentUser.Object,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            (audit ?? new Mock<IAuditLogService>()).Object,
            (notifications ?? new Mock<INotificationService>()).Object,
            NullLogger<VolunteerSupportService>.Instance);
}
