using FluentAssertions;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class PeerChatServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task ResolveRoomAsync_AdmitsOnlyTheTwoParticipants()
    {
        var repo = Repo(Request(SupportRequestStatus.Accepted));
        var service = Service(repo);

        (await service.ResolveRoomAsync("student-1", 9)).Should().NotBeNull();
        (await service.ResolveRoomAsync("volunteer-1", 9))!.IsVolunteer.Should().BeTrue();
        (await service.ResolveRoomAsync("someone-else", 9)).Should().BeNull();
        (await service.ResolveRoomAsync("volunteer-2", 9)).Should().BeNull("another approved volunteer is still not in this conversation");
    }

    [Fact]
    public async Task ResolveRoomAsync_TreatsAMissingRequestLikeAForeignOne()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetRequestByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportRequest?)null);

        (await Service(repo).ResolveRoomAsync("student-1", 404)).Should().BeNull();
    }

    [Theory]
    [InlineData(SupportRequestStatus.Pending, false)]
    [InlineData(SupportRequestStatus.Accepted, true)]
    [InlineData(SupportRequestStatus.Completed, false)]
    [InlineData(SupportRequestStatus.Escalated, false)]
    public async Task ResolveRoomAsync_AllowsSendingOnlyWhileAccepted(SupportRequestStatus status, bool canSend)
    {
        var room = await Service(Repo(Request(status))).ResolveRoomAsync("student-1", 9);
        room!.CanSend.Should().Be(canSend);
    }

    [Fact]
    public async Task PostMessageAsync_PersistsTheStudentsMessageAsAnInteraction()
    {
        var repo = Repo(Request(SupportRequestStatus.Accepted));
        SupportInteraction? saved = null;
        repo.Setup(r => r.AddInteractionAsync(It.IsAny<SupportInteraction>(), It.IsAny<CancellationToken>()))
            .Callback<SupportInteraction, CancellationToken>((interaction, _) => saved = interaction)
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();

        var result = await Service(repo, unitOfWork).PostMessageAsync("student-1", 9, "  Thank you for taking this on.  ");

        result.IsFromVolunteer.Should().BeFalse();
        result.Message.Should().Be("Thank you for taking this on.");
        saved.Should().NotBeNull();
        saved!.VolunteerUserId.Should().BeNull();
        saved.StudentUserId.Should().Be("student-1");
        saved.Type.Should().Be(SupportInteractionType.Message);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PostMessageAsync_MarksTheVolunteersMessageAsTheirs()
    {
        var repo = Repo(Request(SupportRequestStatus.Accepted));
        SupportInteraction? saved = null;
        repo.Setup(r => r.AddInteractionAsync(It.IsAny<SupportInteraction>(), It.IsAny<CancellationToken>()))
            .Callback<SupportInteraction, CancellationToken>((interaction, _) => saved = interaction)
            .Returns(Task.CompletedTask);

        var result = await Service(repo).PostMessageAsync("volunteer-1", 9, "Hello");

        result.IsFromVolunteer.Should().BeTrue();
        saved!.VolunteerUserId.Should().Be("volunteer-1");
    }

    [Fact]
    public async Task PostMessageAsync_RefusesANonParticipantWithoutSavingAnything()
    {
        var repo = Repo(Request(SupportRequestStatus.Accepted));

        var act = () => Service(repo).PostMessageAsync("intruder", 9, "Hi");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        repo.Verify(r => r.AddInteractionAsync(It.IsAny<SupportInteraction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PostMessageAsync_RefusesAClosedConversation()
    {
        var act = () => Service(Repo(Request(SupportRequestStatus.Completed))).PostMessageAsync("student-1", 9, "Hi");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0001byte")]
    public async Task PostMessageAsync_RejectsEmptyOrControlCharacterMessages(string message)
    {
        var act = () => Service(Repo(Request(SupportRequestStatus.Accepted))).PostMessageAsync("student-1", 9, message);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static SupportRequest Request(SupportRequestStatus status)
        => new()
        {
            Id = 9,
            StudentProfileId = 1,
            StudentProfile = new StudentProfile { Id = 1, UserId = "student-1" },
            VolunteerProfileId = status == SupportRequestStatus.Pending ? null : 2,
            VolunteerProfile = status == SupportRequestStatus.Pending
                ? null
                : new VolunteerProfile
                {
                    Id = 2,
                    UserId = "volunteer-1",
                    IsApproved = true,
                    IsActive = true,
                    User = new ApplicationUser { Id = "volunteer-1", IsActive = true }
                },
            Status = status
        };

    private static Mock<IVolunteerSupportRepository> Repo(SupportRequest request)
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        repo.Setup(r => r.GetRequestByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        return repo;
    }

    private static PeerChatService Service(Mock<IVolunteerSupportRepository> repo, Mock<IUnitOfWork>? unitOfWork = null)
        => new(
            repo.Object,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            Mock.Of<IAuditLogService>(),
            new FixedTimeProvider(new DateTimeOffset(Now)));
}
