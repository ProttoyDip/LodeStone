using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Nudges;

/// <summary>Only the neutral templates approved for counselor-originated in-app prompts.</summary>
public enum ManualNudgeTemplate
{
    CheckIn = 0,
    BookingFollowUp = 1,
    SupportResources = 2
}

public enum NudgeResponseAction
{
    Acknowledge = 0,
    Snooze = 1,
    Dismiss = 2
}

public enum NudgeMutationResult
{
    Updated = 0,
    NotFound = 1,
    NotActionable = 2,
    PreferenceDisabled = 3,
    CooldownActive = 4,
    NotEligible = 5,
    InvalidRequest = 6
}

public sealed record StudentNudgeDto(
    int Id,
    string Message,
    NudgeStatus Status,
    DateTime AvailableAtUtc,
    DateTime ExpiresAtUtc,
    bool CanRespond);

public sealed record StudentNudgeStateDto(
    bool IsInAppNudgesEnabled,
    IReadOnlyList<StudentNudgeDto> ActiveNudges);
