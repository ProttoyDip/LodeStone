namespace Lodestone.Application.DTOs.Admin;

/// <summary>
/// An administrator invites a volunteer by email alone. Everything that describes the volunteer —
/// their name, department, skills and availability — is supplied by the volunteer when they accept,
/// so the administrator never types details on someone else's behalf.
/// </summary>
public record InviteVolunteerDto(string Email);

public record VolunteerProvisioningResult(
    bool Succeeded,
    string? UserId,
    string? Email,
    string? PasswordSetupToken,
    IReadOnlyList<string> Errors);
