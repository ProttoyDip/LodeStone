namespace Lodestone.Application.DTOs.Forum;

/// <summary>
/// A post as the triage ranker sees it: structure and history, never an interpretation of content.
/// </summary>
/// <param name="AuthorPostCount">How many posts this author has written, including this one.</param>
/// <param name="CommentCount">Replies the post has received.</param>
/// <param name="UnreviewedFlagCount">Flags raised by community members and not yet reviewed.</param>
/// <param name="AuthorMedianBodyLength">
/// The author's own typical post length. Used as their baseline, not the forum's: a normally terse
/// person writing at length is a change worth a moderator's eye, and comparing them against a
/// chattier population would hide that.
/// </param>
public sealed record ForumTriageInput(
    int PostId,
    string Title,
    DateTime CreatedAtUtc,
    int BodyLength,
    int AuthorPostCount,
    int CommentCount,
    int UnreviewedFlagCount,
    int AuthorMedianBodyLength);

/// <summary>
/// A post's place in the moderator's queue, with the reasons that put it there.
/// </summary>
/// <remarks>
/// <para>
/// The score answers one question: how soon should a human read this? It is not a judgement about
/// the post, the author, or their wellbeing, and it carries no category. Every reason is a fact
/// about the post's history that a moderator could have observed themselves.
/// </para>
/// <para>
/// This is deliberately not a content classifier. See <c>ForumTriageRanker</c> for why.
/// </para>
/// </remarks>
public sealed record ForumTriageResult(
    int PostId,
    string Title,
    double Priority,
    IReadOnlyList<string> Reasons)
{
    /// <summary>
    /// True when a community member reported this post and no moderator has reviewed it. These are
    /// ordered ahead of everything the ranker inferred on its own.
    /// </summary>
    public bool WasReported { get; init; }

    /// <summary>
    /// True when nobody reported this post and it surfaced only because it went unanswered. These
    /// are the cases reactive flagging cannot reach, and the reason the ranker exists.
    /// </summary>
    public bool SurfacedWithoutAFlag { get; init; }
}
