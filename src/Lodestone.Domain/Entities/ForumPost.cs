using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

public class ForumPost : SoftDeleteEntity
{
    public int ForumCategoryId { get; set; }
    public ForumCategory? Category { get; set; }

    public string AuthorUserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public ForumPostStatus Status { get; set; } = ForumPostStatus.Published;

    /// <summary>
    /// When a moderator last looked at this post. Set by review regardless of outcome, so a post
    /// surfaced by triage without a report stops resurfacing once a human has read it.
    /// </summary>
    public DateTime? LastModeratorReviewAtUtc { get; set; }

    public ICollection<ForumComment> Comments { get; set; } = new List<ForumComment>();
    public ICollection<ForumFlag> Flags { get; set; } = new List<ForumFlag>();
}
