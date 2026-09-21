using FluentValidation;
using Lodestone.Application.DTOs.Forum;

namespace Lodestone.Application.Validators;

public class ForumPostValidator : AbstractValidator<CreateForumPostDto>
{
    public ForumPostValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumTrimmedLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumTrimmedLength(5000);
        RuleFor(x => x.CategoryId).GreaterThan(0);
    }
}
