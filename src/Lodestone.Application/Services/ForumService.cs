using Lodestone.Application.Exceptions;
using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

public class ForumService : IForumService
{
    /// <summary>How far back triage looks for unreported posts. Older posts have left the front page.</summary>
    public static readonly TimeSpan TriageLookback = TimeSpan.FromDays(14);

    /// <summary>
    /// Minimum ranker priority for an unreported post to enter the queue. Set at the weight of the
    /// "no replies after 24 hours" signal, which is the case the ranker exists to catch; a first
    /// post that was answered does not on its own need a moderator.
    /// </summary>
    public const double SurfacingThreshold = 0.25;

    private readonly IForumRepository _forumRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLog;
    private readonly TimeProvider _clock;

    public ForumService(
        IForumRepository forumRepository,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLog,
        TimeProvider clock)
    {
        _forumRepository = forumRepository;
        _currentUser     = currentUser;
        _unitOfWork      = unitOfWork;
        _auditLog        = auditLog;
        _clock           = clock;
    }

    public async Task<IReadOnlyList<ForumCategoryDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await _forumRepository.GetCategoriesAsync(cancellationToken);
        return categories
            .Select(c => new ForumCategoryDto(
                c.Id, c.Name, c.Description,
                c.Posts.Count(p => p.Status == ForumPostStatus.Published && !p.IsDeleted)))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<ForumPostDto>> GetPostsAsync(
        int categoryId, CancellationToken cancellationToken = default)
    {
        var posts = await _forumRepository.GetPostsByCategoryAsync(categoryId, cancellationToken);
        return posts.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ForumPostDetailDto?> GetPostWithCommentsAsync(
        int postId, CancellationToken cancellationToken = default)
    {
        var post = await _forumRepository.GetPostWithCommentsAsync(postId, cancellationToken);
        if (post is null) return null;

        var comments = post.Comments
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new ForumCommentDto(c.Id, c.ForumPostId, c.AuthorUserId, c.Body, c.CreatedAtUtc))
            .ToList()
            .AsReadOnly();

        return new ForumPostDetailDto(
            post.Id, post.ForumCategoryId, post.Category?.Name ?? string.Empty,
            post.AuthorUserId, post.Title, post.Body, post.CreatedAtUtc, comments);
    }

    public async Task<ForumPostDto> CreatePostAsync(
        CreateForumPostDto dto, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var title = Required(dto.Title, nameof(dto.Title));
        var body = Required(dto.Body, nameof(dto.Body));

        var post = new ForumPost
        {
            ForumCategoryId = dto.CategoryId,
            AuthorUserId    = userId,
            Title           = title,
            Body            = body,
            Status          = ForumPostStatus.Published,
            CreatedAtUtc    = DateTime.UtcNow,
        };

        await _forumRepository.AddPostAsync(post, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(post);
    }

    public async Task<ForumCommentDto> AddCommentAsync(
        CreateForumCommentDto dto, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var body = Required(dto.Body, nameof(dto.Body));

        // The post is read before the reply is staged: without this, a reply from a page whose post
        // has since been removed reaches the database and fails on the foreign key as a 500.
        _ = await _forumRepository.GetPostByIdAsync(dto.PostId, cancellationToken)
            ?? throw new ForumPostNotFoundException(dto.PostId);

        var comment = new ForumComment
        {
            ForumPostId  = dto.PostId,
            AuthorUserId = userId,
            Body         = body,
            CreatedAtUtc = DateTime.UtcNow,
        };

        await _forumRepository.AddCommentAsync(comment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ForumCommentDto(comment.Id, comment.ForumPostId, comment.AuthorUserId, comment.Body, comment.CreatedAtUtc);
    }

    public async Task FlagPostAsync(int postId, string reason, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var trimmedReason = Required(reason, nameof(reason));

        var post = await _forumRepository.GetPostByIdAsync(postId, cancellationToken)
            ?? throw new ForumPostNotFoundException(postId);

        var flag = new ForumFlag
        {
            ForumPostId  = postId,
            RaisedByUserId = userId,
            Reason       = trimmedReason,
            CreatedAtUtc = DateTime.UtcNow,
        };

        await _forumRepository.AddFlagAsync(flag, cancellationToken);

        // Automatically move to UnderReview when flagged.
        post.Status = ForumPostStatus.Flagged;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ForumPostDto>> GetFlaggedPostsAsync(CancellationToken cancellationToken = default)
        => (await _forumRepository.GetFlaggedPostsAsync(cancellationToken))
            .Select(MapToDto)
            .ToList()
            .AsReadOnly();

    public async Task<ForumModerationQueueDto> GetModerationQueueAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var candidates = await _forumRepository.GetTriageCandidatesAsync(nowUtc - TriageLookback, cancellationToken);
        var byId = candidates.ToDictionary(candidate => candidate.PostId);

        var ranked = ForumTriageRanker.Rank(
            candidates.Select(candidate => new ForumTriageInput(
                candidate.PostId,
                candidate.Title,
                candidate.CreatedAtUtc,
                candidate.Body.Length,
                candidate.AuthorPostCount,
                candidate.CommentCount,
                candidate.UnreviewedFlagCount,
                candidate.AuthorMedianBodyLength)).ToArray(),
            nowUtc);

        var items = ranked
            .Where(result => result.WasReported
                          || (result.SurfacedWithoutAFlag && result.Priority >= SurfacingThreshold))
            .Select(result =>
            {
                var candidate = byId[result.PostId];
                var post = new ForumPostDto(
                    candidate.PostId, candidate.CategoryId, candidate.AuthorUserId,
                    candidate.Title, candidate.Body, candidate.Status, candidate.CreatedAtUtc);
                return new ForumModerationQueueItemDto(
                    post, result.Priority, result.Reasons, result.WasReported, result.SurfacedWithoutAFlag);
            })
            .ToList()
            .AsReadOnly();

        return new ForumModerationQueueDto(
            items,
            items.Count(item => item.WasReported),
            items.Count(item => item.SurfacedWithoutAFlag));
    }

    public async Task<bool> ReviewPostAsync(int postId, bool publish, CancellationToken cancellationToken = default)
    {
        var post = await _forumRepository.GetPostByIdAsync(postId, cancellationToken);
        if (post is null)
        {
            return false;
        }

        var reviewedAtUtc = _clock.GetUtcNow().UtcDateTime;
        post.Status = publish ? ForumPostStatus.Published : ForumPostStatus.Removed;
        post.ModifiedAtUtc = reviewedAtUtc;
        post.LastModeratorReviewAtUtc = reviewedAtUtc;

        foreach (var flag in post.Flags.Where(flag => !flag.IsReviewed))
        {
            flag.IsReviewed = true;
            flag.ModifiedAtUtc = reviewedAtUtc;
        }

        _auditLog.Record(
            action: publish ? "ForumPost.Approve" : "ForumPost.Remove",
            entityName: "ForumPost",
            entityId: postId.ToString(),
            details: $"Post \"{post.Title}\" {(publish ? "restored to" : "removed from")} the community.");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Text the caller must supply, trimmed. Blank input is refused here as well as in the request
    /// validator, so a caller that bypasses the MVC pipeline cannot store an empty post or reply,
    /// and a null never reaches <c>Trim</c> as a null reference.
    /// </summary>
    private static string Required(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("This field cannot be empty.", parameterName)
            : value.Trim();

    private static ForumPostDto MapToDto(ForumPost p)
        => new(p.Id, p.ForumCategoryId, p.AuthorUserId, p.Title, p.Body, p.Status, p.CreatedAtUtc);
}
