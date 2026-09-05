namespace Lodestone.Application.DTOs.Admin;

/// <summary>
/// Outcome of removing a counselor or volunteer account.
/// </summary>
/// <param name="RequiresReplacement">
/// True when the person still holds work that belongs to students and no replacement was named.
/// The removal is refused rather than destroying records about people who are not being removed.
/// </param>
/// <param name="TransferredItems">
/// How many student-owned records moved to the replacement — appointments for a counselor,
/// support requests for a volunteer.
/// </param>
public record StaffRemovalResult(
    bool Succeeded,
    bool RequiresReplacement,
    int TransferredItems,
    IReadOnlyList<string> Errors)
{
    public static StaffRemovalResult Failed(params string[] errors)
        => new(false, false, 0, errors);

    public static StaffRemovalResult NeedsReplacement(int outstandingItems)
        => new(false, true, outstandingItems, Array.Empty<string>());

    public static StaffRemovalResult Removed(int transferredItems)
        => new(true, false, transferredItems, Array.Empty<string>());
}

/// <summary>A person who can receive the work of someone being removed.</summary>
public record StaffReplacementOptionDto(int ProfileId, string DisplayName);
