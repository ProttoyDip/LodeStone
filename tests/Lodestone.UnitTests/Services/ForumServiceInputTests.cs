using FluentAssertions;
using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.Exceptions;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

/// <summary>
/// Forum write paths under normal, exceptional and hostile input. The controller validates shape
/// before calling in, but a stale page or a hand-made request can still arrive with an identifier
/// that no longer resolves, so the service is exercised directly here.
/// </summary>
public class ForumServiceInputTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

    // ---------- Normal ----------

    [Fact]
    public async Task CreatePostAsync_TrimsTheTitleAndBodyAndPublishesUnderTheSignedInAuthor()
    {
        ForumPost? stored = null;
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.AddPostAsync(It.IsAny<ForumPost>(), It.IsAny<CancellationToken>()))
            .Callback<ForumPost, CancellationToken>((post, _) => stored = post)
            .Returns(Task.CompletedTask);
        var service = CreateService(repository);

        await service.CreatePostAsync(new CreateForumPostDto(3, "  Exam nerves  ", "  Anyone else?  "));

        stored.Should().NotBeNull();
        stored!.Title.Should().Be("Exam nerves");
        stored.Body.Should().Be("Anyone else?");
        stored.AuthorUserId.Should().Be("student-1");
        stored.Status.Should().Be(ForumPostStatus.Published);
    }

    [Fact]
    public async Task AddCommentAsync_TrimsTheReplyAndAttributesItToTheSignedInAuthor()
    {
        ForumComment? stored = null;
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetPostByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForumPost { Id = 9 });
        repository.Setup(r => r.AddCommentAsync(It.IsAny<ForumComment>(), It.IsAny<CancellationToken>()))
            .Callback<ForumComment, CancellationToken>((comment, _) => stored = comment)
            .Returns(Task.CompletedTask);
        var service = CreateService(repository);

        await service.AddCommentAsync(new CreateForumCommentDto(9, "  You are not alone.  "));

        stored!.Body.Should().Be("You are not alone.");
        stored.ForumPostId.Should().Be(9);
        stored.AuthorUserId.Should().Be("student-1");
    }

    // ---------- Exceptional: no signed-in user ----------

    [Fact]
    public async Task CreatePostAsync_RefusesAnUnauthenticatedCaller()
    {
        var service = CreateService(new Mock<IForumRepository>(), userId: null);

        var create = () => service.CreatePostAsync(new CreateForumPostDto(1, "Title", "Body"));

        await create.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddCommentAsync_RefusesAnUnauthenticatedCaller()
    {
        var service = CreateService(new Mock<IForumRepository>(), userId: null);

        var comment = () => service.AddCommentAsync(new CreateForumCommentDto(1, "Body"));

        await comment.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------- Exceptional: the target row is gone ----------

    [Fact]
    public async Task AddCommentAsync_RefusesAReplyToAPostThatNoLongerExists()
    {
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetPostByIdAsync(4242, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForumPost?)null);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(repository, unitOfWork: unitOfWork);

        // A reply submitted from a page whose post was removed must not reach the database, where
        // the foreign key would fail and surface as an unhandled server error.
        var comment = () => service.AddCommentAsync(new CreateForumCommentDto(4242, "A reply."));

        await comment.Should().ThrowAsync<ForumPostNotFoundException>();
        repository.Verify(r => r.AddCommentAsync(It.IsAny<ForumComment>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FlagPostAsync_ReportsAMissingPostAsAKnownFailureRatherThanACrash()
    {
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetPostByIdAsync(4242, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForumPost?)null);
        var service = CreateService(repository);

        var flag = () => service.FlagPostAsync(4242, "Spam");

        await flag.Should().ThrowAsync<ForumPostNotFoundException>();
        repository.Verify(r => r.AddFlagAsync(It.IsAny<ForumFlag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReviewPostAsync_ReportsFalseForAPostThatIsAlreadyGone()
    {
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetPostByIdAsync(4242, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ForumPost?)null);
        var service = CreateService(repository);

        (await service.ReviewPostAsync(4242, publish: true)).Should().BeFalse();
    }

    // ---------- Invalid: absent text where text is required ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddCommentAsync_RefusesABodyWithNoContent(string? body)
    {
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetPostByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForumPost { Id = 9 });
        var service = CreateService(repository);

        var comment = () => service.AddCommentAsync(new CreateForumCommentDto(9, body!));

        await comment.Should().ThrowAsync<ArgumentException>();
        repository.Verify(r => r.AddCommentAsync(It.IsAny<ForumComment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePostAsync_RefusesATitleWithNoContent(string? title)
    {
        var service = CreateService(new Mock<IForumRepository>());

        var create = () => service.CreatePostAsync(new CreateForumPostDto(1, title!, "Body"));

        await create.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task FlagPostAsync_RefusesAReportWithNoReason(string? reason)
    {
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetPostByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForumPost { Id = 9 });
        var service = CreateService(repository);

        var flag = () => service.FlagPostAsync(9, reason!);

        await flag.Should().ThrowAsync<ArgumentException>();
    }

    private static ForumService CreateService(
        Mock<IForumRepository> repository,
        string? userId = "student-1",
        Mock<IUnitOfWork>? unitOfWork = null)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(userId);
        return new ForumService(
            repository.Object,
            currentUser.Object,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            Mock.Of<IAuditLogService>(),
            new FixedTimeProvider(Now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
