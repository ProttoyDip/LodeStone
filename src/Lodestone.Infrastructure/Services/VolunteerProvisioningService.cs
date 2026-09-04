using System.ComponentModel.DataAnnotations;
using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace Lodestone.Infrastructure.Services;

/// <summary>
/// Invites volunteers by email and grants them the Volunteer role.
/// </summary>
/// <remarks>
/// Registration always assigns the Student role, so without this service no account could ever
/// hold the Volunteer role and no student request could reach a volunteer.
/// <para>
/// The invitation deliberately creates the account without a volunteer profile. The administrator
/// knows only an email address, so the profile is left for the volunteer to complete after they
/// set a password; until they do, they hold the role but cannot take requests. This also gives the
/// administrator a real profile to read before approving, rather than approving details they
/// typed themselves.
/// </para>
/// </remarks>
public sealed class VolunteerProvisioningService : IVolunteerProvisioningService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _context;
    private readonly IAuditLogService _audit;

    public VolunteerProvisioningService(
        UserManager<ApplicationUser> users,
        ApplicationDbContext context,
        IAuditLogService audit)
        => (_users, _context, _audit) = (users, context, audit);

    public async Task<VolunteerProvisioningResult> InviteAsync(
        InviteVolunteerDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length == 0 || !new EmailAddressAttribute().IsValid(email))
            return Failed("Enter a valid email address.");
        if (await _users.FindByEmailAsync(email) is not null)
            return Failed("An account with that email already exists.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            // The volunteer sets their own name when completing the profile.
            FullName = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
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

        _audit.Record("VolunteerInvited", nameof(ApplicationUser), user.Id, $"Email={email}");
        await _context.SaveChangesAsync(cancellationToken);

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        return new VolunteerProvisioningResult(
            Succeeded: true,
            UserId: user.Id,
            Email: email,
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
            PasswordSetupToken: token,
            Errors: Array.Empty<string>());
    }

    private static VolunteerProvisioningResult Failed(params string[] errors)
        => new(false, null, null, null, errors);
}
