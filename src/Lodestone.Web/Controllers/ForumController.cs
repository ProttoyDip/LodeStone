using FluentValidation;
using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Web.ViewModels.Forum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize]
public class ForumController : Controller
{
    private readonly IForumService _forumService;
    private readonly IValidator<CreateForumPostDto> _postValidator;
    private readonly IValidator<CreateForumCommentDto> _commentValidator;

    public ForumController(
        IForumService forumService,
        IValidator<CreateForumPostDto> postValidator,
        IValidator<CreateForumCommentDto> commentValidator)
    {
        _forumService = forumService;
        _postValidator = postValidator;
        _commentValidator = commentValidator;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new ForumIndexViewModel
        {
            Categories = await _forumService.GetCategoriesAsync(cancellationToken)
        });

    [HttpGet]
    public async Task<IActionResult> Category(int id, CancellationToken cancellationToken)
        => View(new ForumCategoryViewModel
        {
            CategoryId = id,
            Posts = await _forumService.GetPostsAsync(id, cancellationToken),
            NewPost = new CreateForumPostDto(id, string.Empty, string.Empty)
        });

    [HttpGet]
    public async Task<IActionResult> Post(int id, CancellationToken cancellationToken)
    {
        var post = await _forumService.GetPostWithCommentsAsync(id, cancellationToken);
        return post is null
            ? NotFound()
            : View(new ForumPostDetailViewModel
            {
                Post = post,
                NewComment = new CreateForumCommentDto(id, string.Empty)
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(
        [Bind(Prefix = nameof(ForumCategoryViewModel.NewPost))] CreateForumPostDto newPost,
        CancellationToken cancellationToken)
    {
        var validation = await _postValidator.ValidateAsync(newPost, cancellationToken);
        if (!validation.IsValid)
        {
            AddValidationErrors(validation, nameof(ForumCategoryViewModel.NewPost));
            return View("Category", new ForumCategoryViewModel
            {
                CategoryId = newPost.CategoryId,
                Posts = await _forumService.GetPostsAsync(newPost.CategoryId, cancellationToken),
                NewPost = newPost
            });
        }

        var created = await _forumService.CreatePostAsync(newPost, cancellationToken);
        TempData["ForumSuccess"] = "Your discussion has been published.";
        return RedirectToAction(nameof(Post), new { id = created.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(
        [Bind(Prefix = nameof(ForumPostDetailViewModel.NewComment))] CreateForumCommentDto newComment,
        CancellationToken cancellationToken)
    {
        var validation = await _commentValidator.ValidateAsync(newComment, cancellationToken);
        if (!validation.IsValid)
        {
            AddValidationErrors(validation, nameof(ForumPostDetailViewModel.NewComment));
            var post = await _forumService.GetPostWithCommentsAsync(newComment.PostId, cancellationToken);
            if (post is null) return NotFound();
            return View("Post", new ForumPostDetailViewModel { Post = post, NewComment = newComment });
        }

        await _forumService.AddCommentAsync(newComment, cancellationToken);
        TempData["ForumSuccess"] = "Your reply has been added.";
        return RedirectToAction(nameof(Post), new { id = newComment.PostId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Flag(int id, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
        {
            TempData["ForumError"] = "Please provide a brief reason (up to 500 characters) for the report.";
            return RedirectToAction(nameof(Post), new { id });
        }

        await _forumService.FlagPostAsync(id, reason, cancellationToken);
        TempData["ForumSuccess"] = "Thank you. The post has been sent for review.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyConstants.CanModerateForum)]
    [HttpGet]
    public async Task<IActionResult> Moderation(CancellationToken cancellationToken)
        => View(await _forumService.GetFlaggedPostsAsync(cancellationToken));

    [Authorize(Policy = PolicyConstants.CanModerateForum)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int postId, bool publish, CancellationToken cancellationToken)
    {
        var reviewed = await _forumService.ReviewPostAsync(postId, publish, cancellationToken);
        TempData[reviewed ? "ForumSuccess" : "ForumError"] = reviewed
            ? publish ? "The post has been restored." : "The post has been removed."
            : "That post was not found. It may have already been reviewed.";
        return RedirectToAction(nameof(Moderation));
    }

    private void AddValidationErrors(FluentValidation.Results.ValidationResult validation, string prefix)
    {
        foreach (var error in validation.Errors)
            ModelState.AddModelError($"{prefix}.{error.PropertyName}", error.ErrorMessage);
    }
}
