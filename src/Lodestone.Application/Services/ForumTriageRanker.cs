using System.Globalization;
using Lodestone.Application.DTOs.Forum;

namespace Lodestone.Application.Services;

/// <summary>
/// Orders forum posts by how soon a moderator should read them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> Moderation currently begins when someone reports a post. That works
/// for content people object to and fails for the case that matters most here: a student writes
/// something quietly worrying, nobody replies, nobody reports it, and it ages off the front page
/// unread. Reactive flagging cannot reach a post precisely because nobody engaged with it.
/// </para>
/// <para>
/// <b>What this deliberately is not.</b> It is not a content classifier, and it never assigns a
/// category such as "distress" or "self-harm concern". There is no labelled data in this project to
/// train such a classifier or to measure it, and the costly error would be the false negative --
/// which is exactly the number that could not be measured. Worse, a post shown to a moderator
/// without a concern label would be read as cleared, so an unmeasurable classifier would make the
/// quiet cases <em>less</em> visible than no classifier at all. Every signal below is a fact about
/// a post's history that a moderator could have noticed unaided; the ranker only notices them all
/// at once, on every post, every day.
/// </para>
/// <para>
/// <b>Triage means surfacing, never deciding.</b> Nothing here changes a post's status, hides it,
/// or notifies its author. It reorders a list a human reads.
/// </para>
/// </remarks>
public static class ForumTriageRanker
{
    /// <summary>A post is treated as unanswered once this long has passed with no reply.</summary>
    public static readonly TimeSpan UnansweredAfter = TimeSpan.FromHours(24);

    /// <summary>Beyond this age an unattended post has aged as much as the score reflects.</summary>
    private static readonly TimeSpan AgeSaturatesAt = TimeSpan.FromDays(7);

    /// <summary>Authors with no more posts than this are treated as new to the forum.</summary>
    private const int NewAuthorPostCount = 2;

    /// <summary>How many times the author's own typical length counts as a marked departure.</summary>
    private const double LengthAnomalyMultiple = 2.5;

    /// <summary>Below this, "twice as long as usual" is a sentence instead of a word, and means nothing.</summary>
    private const int MinimumMeaningfulLength = 200;

    private const double FlaggedWeight = 0.40;
    private const double UnansweredWeight = 0.25;
    private const double NewAuthorWeight = 0.15;
    private const double LengthAnomalyWeight = 0.10;
    private const double AgeWeight = 0.10;

    /// <summary>
    /// Ranks posts most-urgent-first. Posts with no signal at all score zero and sort last; they are
    /// returned rather than dropped so the caller decides what a moderator sees.
    /// </summary>
    public static IReadOnlyList<ForumTriageResult> Rank(
        IReadOnlyList<ForumTriageInput> posts,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(posts);

        // Reported posts form their own tier ahead of everything the ranker inferred. Relying on
        // weights to achieve that would leave the guarantee one tuning change away from silently
        // breaking, and the thing it protects -- a human having explicitly asked for review -- is
        // not something the ranker should be able to outvote.
        return posts
            .Select(post => Score(post, nowUtc))
            .OrderByDescending(result => result.WasReported)
            .ThenByDescending(result => result.Priority)
            .ThenBy(result => result.PostId)
            .ToArray();
    }

    private static ForumTriageResult Score(ForumTriageInput post, DateTime nowUtc)
    {
        var reasons = new List<string>();
        var score = 0d;

        if (post.UnreviewedFlagCount > 0)
        {
            // Someone already asked for this to be looked at. Nothing the ranker infers should
            // ever push an explicit human request for review down the list.
            score += FlaggedWeight;
            reasons.Add(post.UnreviewedFlagCount == 1
                ? "Reported by a community member and not yet reviewed"
                : $"Reported by {post.UnreviewedFlagCount.ToString(CultureInfo.InvariantCulture)} community members and not yet reviewed");
        }

        var age = nowUtc - post.CreatedAtUtc;
        var unanswered = post.CommentCount == 0 && age >= UnansweredAfter;
        if (unanswered)
        {
            score += UnansweredWeight;
            reasons.Add($"No replies after {FormatAge(age)}");
        }

        if (post.AuthorPostCount <= NewAuthorPostCount)
        {
            score += NewAuthorWeight;
            reasons.Add(post.AuthorPostCount <= 1
                ? "The author's first post"
                : "The author has posted only rarely");
        }

        if (IsMarkedlyLongerThanUsual(post))
        {
            score += LengthAnomalyWeight;
            reasons.Add("Much longer than this author's usual posts");
        }

        if (age > TimeSpan.Zero)
        {
            var aged = Math.Min(1d, age.TotalSeconds / AgeSaturatesAt.TotalSeconds);
            score += aged * AgeWeight;
        }

        return new ForumTriageResult(
            post.PostId,
            post.Title,
            Math.Round(Math.Min(1d, score), 4),
            reasons)
        {
            WasReported = post.UnreviewedFlagCount > 0,
            // The value of this ranker is the post nobody reported. Marking them lets a moderator
            // filter for exactly the cases the old flag-driven sweep could never show them.
            SurfacedWithoutAFlag = post.UnreviewedFlagCount == 0 && reasons.Count > 0
        };
    }

    /// <summary>
    /// Compares a post against its own author's typical length, not the forum's.
    /// </summary>
    /// <remarks>
    /// A change in how someone writes is a fact about the post. It is not evidence of anything, and
    /// the reason shown to a moderator says only that -- it prompts a look, it does not report a
    /// finding.
    /// </remarks>
    private static bool IsMarkedlyLongerThanUsual(ForumTriageInput post)
        => post.BodyLength >= MinimumMeaningfulLength
           && post.AuthorMedianBodyLength > 0
           && post.BodyLength >= post.AuthorMedianBodyLength * LengthAnomalyMultiple;

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalDays >= 2) return $"{(int)age.TotalDays} days";
        if (age.TotalDays >= 1) return "a day";
        return $"{(int)age.TotalHours} hours";
    }
}
