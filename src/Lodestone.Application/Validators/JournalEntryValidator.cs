using FluentValidation;
using Lodestone.Application.DTOs.Journal;

namespace Lodestone.Application.Validators;

public class JournalEntryValidator : AbstractValidator<CreateJournalEntryDto>
{
    public JournalEntryValidator()
    {
        RuleFor(x => x.MoodRating).InclusiveBetween(1, 5);
        RuleFor(x => x.Note).MaximumTrimmedLength(2000);
    }
}
