namespace Lodestone.Application.DTOs.Admin;

/// <summary>
/// Details an administrator supplies when creating a peer-support volunteer account.
/// <paramref name="ApproveImmediately"/> records that the creating administrator is also vouching
/// for the volunteer, so the separate approve step can be skipped for accounts they raised
/// themselves while remaining available for any future self-registration route.
/// </summary>
public record CreateVolunteerDto(
    string FullName,
    string Email,
    string? Department,
    string? Skills,
    string? Availability,
    string? Bio,
    bool ApproveImmediately);

public record VolunteerProvisioningResult(
    bool Succeeded,
    string? UserId,
    string? Email,
    int? VolunteerProfileId,
    string? PasswordSetupToken,
    IReadOnlyList<string> Errors);
