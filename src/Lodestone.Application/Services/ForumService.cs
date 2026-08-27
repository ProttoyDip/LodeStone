using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

public class ForumService : IForumService
{
    private readonly IForumRepository _forumRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLog;

    public ForumService(
        IForumRepository forumRepository,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLog)
    {
        _forumRepository = forumRepository;
        _currentUser     = currentUser;
        _unitOfWork      = unitOfWork;
        _auditLog        = auditLog;
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

        var post = new ForumPost
        {
            ForumCategoryId = dto.CategoryId,
            AuthorUserId    = userId,
            Title           = dto.Title.Trim(),
            Body            = dto.Body.Trim(),
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

        var comment = new ForumComment
        {
            ForumPostId  = dto.PostId,
            AuthorUserId = userId,
            Body         = dto.Body.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };

        await _forumRepository.AddCommentAsync(comment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ForumCommentDto(comment.Id, comment.ForumPostId, comment.AuthorUserId, comment.Body, comment.CreatedAtUtc);
    }

    public async Task FlagPostAsync(int postId, string reason, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");

        var post = await _forumRepository.GetPostByIdAsync(postId, cancellationToken)
            ?? throw new InvalidOperationException($"Post {postId} not found.");

        var flag = new ForumFlag
        {
            ForumPostId  = postId,
            RaisedByUserId = userId,
            Reason       = reason.Trim(),
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

    public async Task<bool> ReviewPostAsync(int postId, bool publish, CancellationToken cancellationToken = default)
    {
        var post = await _forumRepository.GetPostByIdAsync(postId, cancellationToken);
        if (post is null)
        {
            return false;
        }

        var reviewedAtUtc = DateTime.UtcNow;
        post.Status = publish ? ForumPostStatus.Published : ForumPostStatus.Removed;
        post.ModifiedAtUtc = reviewedAtUtc;

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

    private static ForumPostDto MapToDto(ForumPost p)
        => new(p.Id, p.ForumCategoryId, p.AuthorUserId, p.Title, p.Body, p.Status, p.CreatedAtUtc);
}
