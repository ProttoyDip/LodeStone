using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Repositories;

/// <summary>Forum-specific queries: categories, posts with comments, flags.</summary>
public class ForumRepository : GenericRepository<ForumPost>, IForumRepository
{
    public ForumRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IReadOnlyList<ForumCategory>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
        => await Context.ForumCategories
            .Include(c => c.Posts.Where(p => p.Status == ForumPostStatus.Published && !p.IsDeleted))
            .OrderBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ForumPost>> GetPostsByCategoryAsync(
        int categoryId, CancellationToken cancellationToken = default)
        => await Set
            .Where(p => p.ForumCategoryId == categoryId
                     && p.Status == ForumPostStatus.Published
                     && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<ForumPost?> GetPostWithCommentsAsync(
        int postId, CancellationToken cancellationToken = default)
        => await Set
            .Include(p => p.Comments.Where(comment =>
                comment.Status == ForumPostStatus.Published && !comment.IsDeleted))
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted, cancellationToken);

    public async Task<ForumPost?> GetPostByIdAsync(int postId, CancellationToken cancellationToken = default)
        => await Set
            .Include(post => post.Flags)
            .FirstOrDefaultAsync(post => post.Id == postId, cancellationToken);

    public async Task<IReadOnlyList<ForumPost>> GetFlaggedPostsAsync(CancellationToken cancellationToken = default)
        => await Set
            .Where(post => post.Status == ForumPostStatus.Flagged && !post.IsDeleted)
            .OrderBy(post => post.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ForumTriageCandidate>> GetTriageCandidatesAsync(
        DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var posts = await Set
            .Where(post => !post.IsDeleted
                        && post.Status != ForumPostStatus.Removed
                        && (post.Flags.Any(flag => !flag.IsReviewed)
                            || (post.CreatedAtUtc >= sinceUtc && post.LastModeratorReviewAtUtc == null)))
            .Select(post => new
            {
                post.Id,
                post.ForumCategoryId,
                post.AuthorUserId,
                post.Title,
                post.Body,
                post.Status,
                post.CreatedAtUtc,
                CommentCount = post.Comments.Count(comment =>
                    !comment.IsDeleted && comment.Status == ForumPostStatus.Published),
                UnreviewedFlagCount = post.Flags.Count(flag => !flag.IsReviewed)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (posts.Count == 0) return Array.Empty<ForumTriageCandidate>();

        var authorIds = posts.Select(post => post.AuthorUserId).Distinct().ToList();
        var authorHistory = await Set
            .Where(post => !post.IsDeleted && authorIds.Contains(post.AuthorUserId))
            .Select(post => new { post.AuthorUserId, Length = post.Body.Length })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var authorStats = authorHistory
            .GroupBy(entry => entry.AuthorUserId)
            .ToDictionary(
                group => group.Key,
                group => (Count: group.Count(), Median: Median(group.Select(entry => entry.Length))));

        return posts
            .Select(post =>
            {
                var stats = authorStats.TryGetValue(post.AuthorUserId, out var found) ? found : (Count: 1, Median: 0);
                return new ForumTriageCandidate(
                    post.Id,
                    post.ForumCategoryId,
                    post.AuthorUserId,
                    post.Title,
                    post.Body,
                    post.Status,
                    post.CreatedAtUtc,
                    post.CommentCount,
                    post.UnreviewedFlagCount,
                    stats.Count,
                    stats.Median);
            })
            .ToList()
            .AsReadOnly();
    }

    private static int Median(IEnumerable<int> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0) return 0;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    public async Task AddPostAsync(ForumPost post, CancellationToken cancellationToken = default)
        => await Set.AddAsync(post, cancellationToken);

    public async Task AddCommentAsync(ForumComment comment, CancellationToken cancellationToken = default)
        => await Context.ForumComments.AddAsync(comment, cancellationToken);

    public async Task AddFlagAsync(ForumFlag flag, CancellationToken cancellationToken = default)
        => await Context.ForumFlags.AddAsync(flag, cancellationToken);
}
