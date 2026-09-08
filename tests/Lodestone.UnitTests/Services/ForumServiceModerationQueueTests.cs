using FluentAssertions;
using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class ForumServiceModerationQueueTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task GetModerationQueueAsync_PutsReportedPostsFirstAndCountsBothTiers()
    {
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetTriageCandidatesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Candidate(1, createdAtUtc: Now.AddDays(-3), commentCount: 0, authorPostCount: 1),
                Candidate(2, createdAtUtc: Now.AddHours(-1), commentCount: 5, authorPostCount: 40, unreviewedFlags: 1),
            });

        var queue = await Service(repository).GetModerationQueueAsync();

        queue.Items.Select(item => item.Post.Id).Should().Equal(2, 1);
        queue.ReportedCount.Should().Be(1);
        queue.SurfacedWithoutFlagCount.Should().Be(1);
        queue.Items[1].Reasons.Should().Contain(reason => reason.StartsWith("No replies after"));
    }

    [Fact]
    public async Task GetModerationQueueAsync_LeavesOutAnUnreportedPostBelowTheSurfacingThreshold()
    {
        // A first post that was answered within the day has a signal, but not enough to need a
        // moderator's time. The ranker returns it; the service decides not to show it.
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetTriageCandidatesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Candidate(1, createdAtUtc: Now.AddHours(-2), commentCount: 3, authorPostCount: 1),
            });

        var queue = await Service(repository).GetModerationQueueAsync();

        queue.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetModerationQueueAsync_AsksTheRepositoryForTheConfiguredLookbackWindow()
    {
        var repository = new Mock<IForumRepository>();
        DateTime? requestedSince = null;
        repository.Setup(r => r.GetTriageCandidatesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, CancellationToken>((since, _) => requestedSince = since)
            .ReturnsAsync(Array.Empty<ForumTriageCandidate>());

        await Service(repository).GetModerationQueueAsync();

        requestedSince.Should().Be(Now - ForumService.TriageLookback);
    }

    [Fact]
    public async Task ReviewPostAsync_RecordsThatAModeratorReadThePost()
    {
        var post = new ForumPost { Id = 7, Title = "Quiet", Status = ForumPostStatus.Published, CreatedAtUtc = Now.AddDays(-2) };
        var repository = new Mock<IForumRepository>();
        repository.Setup(r => r.GetPostByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(post);

        var reviewed = await Service(repository).ReviewPostAsync(7, publish: true);

        reviewed.Should().BeTrue();
        post.LastModeratorReviewAtUtc.Should().Be(Now);
        post.Status.Should().Be(ForumPostStatus.Published);
    }

    private static ForumService Service(Mock<IForumRepository> repository)
        => new(
            repository.Object,
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IAuditLogService>(),
            new FixedTimeProvider(new DateTimeOffset(Now)));

    private static ForumTriageCandidate Candidate(
        int id,
        DateTime createdAtUtc,
        int commentCount,
        int authorPostCount,
        int unreviewedFlags = 0)
        => new(
            id, 1, $"author-{id}", $"Post {id}", new string('x', 120),
            unreviewedFlags > 0 ? ForumPostStatus.Flagged : ForumPostStatus.Published,
            createdAtUtc, commentCount, unreviewedFlags, authorPostCount, 120);
}
