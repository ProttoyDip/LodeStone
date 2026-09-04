using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace Lodestone.Infrastructure.Services;

/// <summary>
/// Creates volunteer accounts with their profile and role in one operation.
/// </summary>
/// <remarks>
/// Registration always assigns the Student role, so without this service no account could ever
/// hold the Volunteer role and no student request could reach a volunteer. The account and its
/// profile are created together: a user holding the role without a profile would pass
/// authorization and then be refused by every dashboard check, which reads as a broken account
/// rather than a pending one.
/// </remarks>
public sealed class VolunteerProvisioningService : IVolunteerProvisioningService
{
    private const int MaximumNameLength = 150;
    private const int MaximumDepartmentLength = 200;
    private const int MaximumSkillsLength = 500;
    private const int MaximumFreeTextLength = 2000;

    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _context;
    private readonly IAuditLogService _audit;

    public VolunteerProvisioningService(
        UserManager<ApplicationUser> users,
        ApplicationDbContext context,
        IAuditLogService audit)
        => (_users, _context, _audit) = (users, context, audit);

    public async Task<VolunteerProvisioningResult> CreateAsync(
        CreateVolunteerDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var fullName = (dto.FullName ?? string.Empty).Trim();
        var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (fullName.Length is < 2 or > MaximumNameLength || string.IsNullOrWhiteSpace(email))
            return Failed("Enter a valid name and email address.");
        if (await _users.FindByEmailAsync(email) is not null)
            return Failed("An account with that email already exists.");

        var nowUtc = DateTime.UtcNow;
        var profile = new VolunteerProfile
        {
            Department = Normalize(dto.Department, MaximumDepartmentLength),
            Skills = Normalize(dto.Skills, MaximumSkillsLength),
            Availability = Normalize(dto.Availability, MaximumFreeTextLength),
            Bio = Normalize(dto.Bio, MaximumFreeTextLength),
            IsApproved = dto.ApproveImmediately,
            IsActive = true,
            CreatedAtUtc = nowUtc
        };

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            CreatedAtUtc = nowUtc,
            IsActive = true,
            VolunteerProfile = profile
        };

        var created = await _users.CreateAsync(user);
        if (!created.Succeeded)
            return Failed(created.Errors.Select(error => error.Description).ToArray());

        var roleResult = await _users.AddToRoleAsync(user, RoleConstants.Volunteer);
        if (!roleResult.Succeeded)
        {
            // Without the role the account is unusable, so do not leave a half-provisioned user.
            await _users.DeleteAsync(user);
            return Failed(roleResult.Errors.Select(error => error.Description).ToArray());
        }

        _audit.Record(
            "VolunteerCreated",
            nameof(ApplicationUser),
            user.Id,
            $"Email={email}; Approved={profile.IsApproved}");
        await _context.SaveChangesAsync(cancellationToken);

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        return new VolunteerProvisioningResult(
            Succeeded: true,
            UserId: user.Id,
            Email: email,
            VolunteerProfileId: profile.Id,
            PasswordSetupToken: token,
            Errors: Array.Empty<string>());
    }

    public async Task<VolunteerProvisioningResult> CreateSetupTokenAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return Failed("Enter the volunteer's email address.");

        var user = await _users.FindByEmailAsync(normalized);
        if (user is null || !await _users.IsInRoleAsync(user, RoleConstants.Volunteer))
            return Failed("No volunteer account was found for that email.");

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        _audit.Record("VolunteerSetupLinkGenerated", nameof(ApplicationUser), user.Id);
        await _context.SaveChangesAsync(cancellationToken);

        return new VolunteerProvisioningResult(
            Succeeded: true,
            UserId: user.Id,
            Email: user.Email,
            VolunteerProfileId: null,
            PasswordSetupToken: token,
            Errors: Array.Empty<string>());
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    private static VolunteerProvisioningResult Failed(params string[] errors)
        => new(false, null, null, null, null, errors);
}
