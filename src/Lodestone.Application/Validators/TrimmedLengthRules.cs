using FluentValidation;

namespace Lodestone.Application.Validators;

/// <summary>
/// Length rules that measure the value the service will actually store.
/// </summary>
/// <remarks>
/// The write paths trim free text before persisting it, so measuring the raw submission rejects
/// input that would have fitted once trimmed. A note pasted with a trailing newline is the common
/// case: the student sees a length error for text that is inside the limit.
/// </remarks>
public static class TrimmedLengthRules
{
    public static IRuleBuilderOptions<T, string?> MaximumTrimmedLength<T>(
        this IRuleBuilder<T, string?> rule,
        int maximumLength)
        => rule
            .Must(value => value is null || value.Trim().Length <= maximumLength)
            .WithMessage($"The length of '{{PropertyName}}' must be {maximumLength} characters or fewer.");
}
