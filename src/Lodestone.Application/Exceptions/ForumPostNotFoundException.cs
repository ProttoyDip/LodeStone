namespace Lodestone.Application.Exceptions;

/// <summary>
/// Raised when a forum write names a post that no longer resolves. A reply or report submitted
/// from a stale page must fail as a known, handled condition rather than reaching the database
/// and surfacing a foreign-key error to the student.
/// </summary>
public sealed class ForumPostNotFoundException : InvalidOperationException
{
    public ForumPostNotFoundException(int postId)
        : base("That discussion is no longer available. It may have been removed.")
        => PostId = postId;

    public int PostId { get; }
}
