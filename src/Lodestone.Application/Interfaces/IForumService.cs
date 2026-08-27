using Lodestone.Application.DTOs.Forum;

namespace Lodestone.Application.Interfaces;

public interface IForumService
{
    Task<IReadOnlyList<ForumCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ForumPostDto>> GetPostsAsync(int categoryId, CancellationToken cancellationToken = default);
    Task<ForumPostDetailDto?> GetPostWithCommentsAsync(int postId, CancellationToken cancellationToken = default);
    Task<ForumPostDto> CreatePostAsync(CreateForumPostDto dto, CancellationToken cancellationToken = default);
    Task<ForumCommentDto> AddCommentAsync(CreateForumCommentDto dto, CancellationToken cancellationToken = default);
    Task FlagPostAsync(int postId, string reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ForumPostDto>> GetFlaggedPostsAsync(CancellationToken cancellationToken = default);
    Task<bool> ReviewPostAsync(int postId, bool publish, CancellationToken cancellationToken = default);
}
