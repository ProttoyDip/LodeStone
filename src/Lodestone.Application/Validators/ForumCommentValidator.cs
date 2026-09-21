using FluentValidation;
using Lodestone.Application.DTOs.Forum;

namespace Lodestone.Application.Validators;

public class ForumCommentValidator : AbstractValidator<CreateForumCommentDto>
{
    public ForumCommentValidator()
    {
        RuleFor(x => x.PostId).GreaterThan(0);
        RuleFor(x => x.Body).NotEmpty().MaximumTrimmedLength(2000);
    }
}
