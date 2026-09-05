using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Services;

public sealed class CounselorProvisioningService : ICounselorProvisioningService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _context;
    private readonly IAuditLogService _audit;

    public CounselorProvisioningService(UserManager<ApplicationUser> users, ApplicationDbContext context, IAuditLogService audit)
        => (_users, _context, _audit) = (users, context, audit);

    public async Task<CounselorProvisioningResult> CreateAsync(CreateCounselorDto dto, CancellationToken cancellationToken = default)
    {
        var fullName = dto.FullName.Trim();
        var email = dto.Email.Trim().ToLowerInvariant();
        if (fullName.Length is < 2 or > 150 || string.IsNullOrWhiteSpace(email))
            return Failed("Enter a valid name and email address.");
        if (await _users.FindByEmailAsync(email) is not null)
            return Failed("An account with that email already exists.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            CounselorProfile = new CounselorProfile
            {
                Specialization = string.IsNullOrWhiteSpace(dto.Specialization) ? null : dto.Specialization.Trim(),
                IsAcceptingBookings = true,
                CreatedAtUtc = DateTime.UtcNow
            }
        };

        var created = await _users.CreateAsync(user);
        if (!created.Succeeded)
            return Failed(created.Errors.Select(error => error.Description).ToArray());
        var roleResult = await _users.AddToRoleAsync(user, RoleConstants.Counselor);
        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user);
            return Failed(roleResult.Errors.Select(error => error.Description).ToArray());
        }

        _audit.Record("CounselorCreated", nameof(ApplicationUser), user.Id, $"Email={email}");
        await _context.SaveChangesAsync(cancellationToken);
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        return new CounselorProvisioningResult(true, user.Id, email, token, Array.Empty<string>());
    }

    public async Task<CounselorProvisioningResult> CreateSetupTokenAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim();
        var user = await _users.FindByEmailAsync(normalized);
        if (user is null || !await _users.IsInRoleAsync(user, RoleConstants.Counselor))
            return Failed("No counselor account was found for that email.");
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        _audit.Record("CounselorSetupLinkGenerated", nameof(ApplicationUser), user.Id);
        await _context.SaveChangesAsync(cancellationToken);
        return new CounselorProvisioningResult(true, user.Id, user.Email, token, Array.Empty<string>());
    }

    public async Task<IReadOnlyList<StaffReplacementOptionDto>> GetReplacementsAsync(
        int excludingCounselorProfileId,
        CancellationToken cancellationToken = default)
        => await _context.CounselorProfiles
            .AsNoTracking()
            .Where(profile => profile.Id != excludingCounselorProfileId
                              && profile.User != null
                              && profile.User.IsActive)
            .OrderBy(profile => profile.User!.FullName)
            .Select(profile => new StaffReplacementOptionDto(
                profile.Id,
                profile.User!.FullName ?? profile.User.Email ?? "Counselor"))
            .ToListAsync(cancellationToken);

    public async Task<StaffRemovalResult> RemoveAsync(
        int counselorProfileId,
        int? replacementCounselorProfileId,
        CancellationToken cancellationToken = default)
    {
        if (counselorProfileId <= 0)
            return StaffRemovalResult.Failed("Select a counselor to remove.");

        var counselor = await _context.CounselorProfiles
            .Include(profile => profile.User)
            .FirstOrDefaultAsync(profile => profile.Id == counselorProfileId, cancellationToken);
        if (counselor?.User is null)
            return StaffRemovalResult.Failed("That counselor was not found.");

        var bookings = await _context.CounselorBookings
            .Where(booking => booking.CounselorProfileId == counselorProfileId)
            .ToListAsync(cancellationToken);

        // Appointments and their session reports describe students, not the departing counselor,
        // so they are never deleted. Without somewhere to move them the removal cannot proceed.
        if (bookings.Count > 0 && replacementCounselorProfileId is null)
            return StaffRemovalResult.NeedsReplacement(bookings.Count);

        if (bookings.Count > 0)
        {
            if (replacementCounselorProfileId == counselorProfileId)
                return StaffRemovalResult.Failed("Choose a different counselor to receive the appointments.");

            var replacementExists = await _context.CounselorProfiles.AnyAsync(
                profile => profile.Id == replacementCounselorProfileId
                           && profile.User != null
                           && profile.User.IsActive,
                cancellationToken);
            if (!replacementExists)
                return StaffRemovalResult.Failed("The counselor chosen to receive the appointments is not available.");
        }

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        foreach (var booking in bookings)
        {
            booking.CounselorProfileId = replacementCounselorProfileId!.Value;
            // The slot belonged to the departing counselor's own calendar and is about to go. The
            // appointment keeps its own ScheduledForUtc, so the time is not lost with it.
            booking.AvailabilitySlotId = null;
        }

        var slots = await _context.CounselorAvailabilitySlots
            .Where(slot => slot.CounselorProfileId == counselorProfileId)
            .ToListAsync(cancellationToken);
        _context.CounselorAvailabilitySlots.RemoveRange(slots);

        _context.CounselorProfiles.Remove(counselor);
        // The account row is restricted by the profile's foreign key, so the profile must be gone
        // before Identity is asked to delete the user.
        await _context.SaveChangesAsync(cancellationToken);

        var deleted = await _users.DeleteAsync(counselor.User);
        if (!deleted.Succeeded)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return StaffRemovalResult.Failed(deleted.Errors.Select(error => error.Description).ToArray());
        }

        _audit.Record(
            "CounselorRemoved",
            nameof(ApplicationUser),
            counselor.UserId,
            $"TransferredAppointments={bookings.Count}; ReplacementProfileId={replacementCounselorProfileId?.ToString() ?? "none"}");
        await _context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return StaffRemovalResult.Removed(bookings.Count);
    }

    private static CounselorProvisioningResult Failed(params string[] errors)
        => new(false, null, null, null, errors);
}
