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

    public async Task AddPostAsync(ForumPost post, CancellationToken cancellationToken = default)
        => await Set.AddAsync(post, cancellationToken);

    public async Task AddCommentAsync(ForumComment comment, CancellationToken cancellationToken = default)
        => await Context.ForumComments.AddAsync(comment, cancellationToken);

    public async Task AddFlagAsync(ForumFlag flag, CancellationToken cancellationToken = default)
        => await Context.ForumFlags.AddAsync(flag, cancellationToken);
}
