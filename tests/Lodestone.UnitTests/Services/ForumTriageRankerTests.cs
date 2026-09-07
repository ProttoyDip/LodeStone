using FluentAssertions;
using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.Services;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class ForumTriageRankerTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Rank_KeepsAReportedPostAheadOfAnythingItMerelyInferred()
    {
        // A community member explicitly asked for review. No signal the ranker derives on its own
        // should ever push that request down the queue.
        var results = ForumTriageRanker.Rank(
            [
                Post(1, unreviewedFlags: 1, createdAtUtc: Now.AddHours(-1), commentCount: 4, authorPostCount: 50),
                Post(2, createdAtUtc: Now.AddDays(-6), commentCount: 0, authorPostCount: 1, bodyLength: 3000,
                    authorMedianBodyLength: 100)
            ],
            Now);

        results[0].PostId.Should().Be(1);
    }

    [Fact]
    public void Rank_SurfacesTheQuietPostNobodyReported()
    {
        // The case reactive flagging cannot reach: written, unanswered, unreported, ageing away.
        var results = ForumTriageRanker.Rank(
            [
                Post(1, createdAtUtc: Now.AddHours(-1), commentCount: 6, authorPostCount: 40),
                Post(2, createdAtUtc: Now.AddDays(-3), commentCount: 0, authorPostCount: 1)
            ],
            Now);

        results[0].PostId.Should().Be(2);
        results[0].SurfacedWithoutAFlag.Should().BeTrue();
        results[0].Reasons.Should().Contain(reason => reason.StartsWith("No replies after"));
        results[0].Reasons.Should().Contain("The author's first post");
    }

    [Fact]
    public void Rank_DoesNotCallAPostUnansweredBeforeAnyoneHadTimeToAnswer()
    {
        var results = ForumTriageRanker.Rank(
            [Post(1, createdAtUtc: Now.AddHours(-2), commentCount: 0, authorPostCount: 30)],
            Now);

        results[0].Reasons.Should().NotContain(reason => reason.StartsWith("No replies"));
    }

    [Fact]
    public void Rank_NoticesAPostFarLongerThanItsOwnAuthorsNorm()
    {
        var results = ForumTriageRanker.Rank(
            [Post(1, createdAtUtc: Now.AddHours(-1), commentCount: 3, authorPostCount: 30,
                bodyLength: 1500, authorMedianBodyLength: 120)],
            Now);

        results[0].Reasons.Should().Contain("Much longer than this author's usual posts");
    }

    [Fact]
    public void Rank_ComparesAnAuthorAgainstThemselvesRatherThanTheForum()
    {
        // A habitually long-winded author writing at their usual length is not a change. Judging
        // them against a chattier population average would bury the terse person's outlier post.
        var results = ForumTriageRanker.Rank(
            [Post(1, createdAtUtc: Now.AddHours(-1), commentCount: 3, authorPostCount: 30,
                bodyLength: 2000, authorMedianBodyLength: 1900)],
            Now);

        results[0].Reasons.Should().NotContain("Much longer than this author's usual posts");
    }

    [Fact]
    public void Rank_IgnoresLengthChangesTooSmallToMeanAnything()
    {
        // Fifty characters against a twenty-character norm is one sentence instead of one word.
        var results = ForumTriageRanker.Rank(
            [Post(1, createdAtUtc: Now.AddHours(-1), commentCount: 3, authorPostCount: 30,
                bodyLength: 50, authorMedianBodyLength: 10)],
            Now);

        results[0].Reasons.Should().NotContain("Much longer than this author's usual posts");
    }

    [Fact]
    public void Rank_AssignsNoCategoryAndMakesNoClaimAboutTheAuthor()
    {
        // Every reason must be a fact about the post's history that a moderator could observe.
        // A category such as "distress" would be a clinical judgement with nothing behind it.
        var results = ForumTriageRanker.Rank(
            [Post(1, unreviewedFlags: 2, createdAtUtc: Now.AddDays(-2), commentCount: 0,
                authorPostCount: 1, bodyLength: 4000, authorMedianBodyLength: 100)],
            Now);

        foreach (var reason in results[0].Reasons)
        {
            reason.ToLowerInvariant().Should().NotContainAny(
                "distress", "crisis", "self-harm", "risk", "concern", "mental", "suicide");
        }
    }

    [Fact]
    public void Rank_LeavesAnOrdinaryPostWithNoSignalAndNoReasons()
    {
        var results = ForumTriageRanker.Rank(
            [Post(1, createdAtUtc: Now, commentCount: 5, authorPostCount: 40)],
            Now);

        results[0].Priority.Should().Be(0);
        results[0].Reasons.Should().BeEmpty();
        results[0].SurfacedWithoutAFlag.Should().BeFalse("a post with nothing to say about it was not surfaced");
    }

    [Fact]
    public void Rank_RaisesAnUnattendedPostAsItAges()
    {
        var results = ForumTriageRanker.Rank(
            [
                Post(1, createdAtUtc: Now.AddDays(-6), commentCount: 0, authorPostCount: 40),
                Post(2, createdAtUtc: Now.AddDays(-2), commentCount: 0, authorPostCount: 40)
            ],
            Now);

        results[0].PostId.Should().Be(1);
    }

    [Fact]
    public void Rank_NeverExceedsAFullScore()
    {
        var results = ForumTriageRanker.Rank(
            [Post(1, unreviewedFlags: 9, createdAtUtc: Now.AddDays(-30), commentCount: 0,
                authorPostCount: 1, bodyLength: 9000, authorMedianBodyLength: 50)],
            Now);

        results[0].Priority.Should().BeLessThanOrEqualTo(1d);
    }

    [Fact]
    public void Rank_ReturnsEveryPostRatherThanDecidingWhatAModeratorMaySee()
    {
        var results = ForumTriageRanker.Rank(
            [
                Post(1, createdAtUtc: Now, commentCount: 5, authorPostCount: 40),
                Post(2, createdAtUtc: Now.AddDays(-3), commentCount: 0, authorPostCount: 1)
            ],
            Now);

        results.Should().HaveCount(2);
    }

    private static ForumTriageInput Post(
        int id,
        DateTime createdAtUtc,
        int commentCount = 0,
        int authorPostCount = 10,
        int unreviewedFlags = 0,
        int bodyLength = 300,
        int authorMedianBodyLength = 300)
        => new(id, $"Post {id}", createdAtUtc, bodyLength, authorPostCount, commentCount,
            unreviewedFlags, authorMedianBodyLength);
}
