using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

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

    private static CounselorProvisioningResult Failed(params string[] errors)
        => new(false, null, null, null, errors);
}
