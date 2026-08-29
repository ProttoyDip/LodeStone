namespace Lodestone.Application.DTOs.Admin;

public record CreateCounselorDto(string FullName, string Email, string? Specialization);

public record CounselorProvisioningResult(
    bool Succeeded,
    string? UserId,
    string? Email,
    string? PasswordSetupToken,
    IReadOnlyList<string> Errors);
