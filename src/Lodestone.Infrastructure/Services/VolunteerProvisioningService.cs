using System.ComponentModel.DataAnnotations;
using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

    public async Task<IReadOnlyList<StaffReplacementOptionDto>> GetReplacementsAsync(
        int excludingVolunteerProfileId,
        CancellationToken cancellationToken = default)
        => await _context.VolunteerProfiles
            .AsNoTracking()
            .Where(profile => profile.Id != excludingVolunteerProfileId
                              && profile.IsApproved
                              && profile.IsActive
                              && profile.User != null
                              && profile.User.IsActive)
            .OrderBy(profile => profile.User!.FullName)
            .Select(profile => new StaffReplacementOptionDto(
                profile.Id,
                profile.User!.FullName ?? profile.User.Email ?? "Volunteer"))
            .ToListAsync(cancellationToken);

    public async Task<StaffRemovalResult> RemoveAsync(
        int volunteerProfileId,
        int? replacementVolunteerProfileId,
        CancellationToken cancellationToken = default)
    {
        if (volunteerProfileId <= 0)
            return StaffRemovalResult.Failed("Select a volunteer to remove.");

        var volunteer = await _context.VolunteerProfiles
            .Include(profile => profile.User)
            .FirstOrDefaultAsync(profile => profile.Id == volunteerProfileId, cancellationToken);
        if (volunteer?.User is null)
            return StaffRemovalResult.Failed("That volunteer was not found.");

        var requests = await _context.SupportRequests
            .Where(request => request.VolunteerProfileId == volunteerProfileId)
            .ToListAsync(cancellationToken);

        // A support request and its messages belong to the student who raised it.
        if (requests.Count > 0 && replacementVolunteerProfileId is null)
            return StaffRemovalResult.NeedsReplacement(requests.Count);

        if (requests.Count > 0)
        {
            if (replacementVolunteerProfileId == volunteerProfileId)
                return StaffRemovalResult.Failed("Choose a different volunteer to receive the support requests.");

            var replacementExists = await _context.VolunteerProfiles.AnyAsync(
                profile => profile.Id == replacementVolunteerProfileId
                           && profile.IsApproved
                           && profile.IsActive
                           && profile.User != null
                           && profile.User.IsActive,
                cancellationToken);
            if (!replacementExists)
                return StaffRemovalResult.Failed("The volunteer chosen to receive the support requests is not available.");
        }

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        foreach (var request in requests)
            request.VolunteerProfileId = replacementVolunteerProfileId;

        var assignments = await _context.VolunteerAssignments
            .Where(assignment => assignment.VolunteerProfileId == volunteerProfileId)
            .ToListAsync(cancellationToken);

        if (replacementVolunteerProfileId is { } replacementId)
        {
            // (VolunteerProfileId, StudentProfileId) is unique, so an assignment for a student the
            // replacement already mentors cannot be moved across; drop it rather than duplicate it.
            var alreadyMentored = await _context.VolunteerAssignments
                .Where(assignment => assignment.VolunteerProfileId == replacementId)
                .Select(assignment => assignment.StudentProfileId)
                .ToListAsync(cancellationToken);

            foreach (var assignment in assignments)
            {
                if (alreadyMentored.Contains(assignment.StudentProfileId))
                    _context.VolunteerAssignments.Remove(assignment);
                else
                    assignment.VolunteerProfileId = replacementId;
            }
        }
        else
        {
            // No student work to hand over, so the assignments are just admin plumbing.
            _context.VolunteerAssignments.RemoveRange(assignments);
        }

        _context.VolunteerProfiles.Remove(volunteer);
        // The account row is restricted by the profile's foreign key, so the profile must be gone
        // before Identity is asked to delete the user.
        await _context.SaveChangesAsync(cancellationToken);

        var deleted = await _users.DeleteAsync(volunteer.User);
        if (!deleted.Succeeded)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return StaffRemovalResult.Failed(deleted.Errors.Select(error => error.Description).ToArray());
        }

        _audit.Record(
            "VolunteerRemoved",
            nameof(ApplicationUser),
            volunteer.UserId,
            $"TransferredRequests={requests.Count}; ReplacementProfileId={replacementVolunteerProfileId?.ToString() ?? "none"}");
        await _context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return StaffRemovalResult.Removed(requests.Count);
    }

    private static VolunteerProvisioningResult Failed(params string[] errors)
        => new(false, null, null, null, errors);
}
