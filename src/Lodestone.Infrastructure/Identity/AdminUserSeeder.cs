using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Lodestone.Infrastructure.Identity;

public static class AdminUserSeeder
{
    private const string AdminEmail = "rashid.cse.20230104102@aust.edu";

    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        string adminPassword,
        bool resetExistingPassword = false,
        CancellationToken cancellationToken = default)
    {
        if (!await roleManager.RoleExistsAsync(RoleConstants.Admin))
        {
            await roleManager.CreateAsync(new IdentityRole(RoleConstants.Admin));
        }

        var admin = await userManager.FindByEmailAsync(AdminEmail);
        if (admin is null)
        {
            RequirePassword(adminPassword, "create the initial Admin account");
            admin = new ApplicationUser
            {
                UserName = AdminEmail,
                Email = AdminEmail,
                FullName = "System Admin",
                EmailConfirmed = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };

            var createResult = await userManager.CreateAsync(admin, adminPassword);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Unable to seed admin user: {errors}");
            }
        }
        else if (resetExistingPassword)
        {
            RequirePassword(adminPassword, "reset the existing Admin account");
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(admin);
            var resetResult = await userManager.ResetPasswordAsync(admin, resetToken, adminPassword);
            if (!resetResult.Succeeded)
            {
                var errors = string.Join("; ", resetResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Unable to reset admin user password: {errors}");
            }

            var unlockResult = await userManager.SetLockoutEndDateAsync(admin, null);
            if (!unlockResult.Succeeded)
            {
                var errors = string.Join("; ", unlockResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Unable to unlock admin user: {errors}");
            }

            var failedCountResult = await userManager.ResetAccessFailedCountAsync(admin);
            if (!failedCountResult.Succeeded)
            {
                var errors = string.Join("; ", failedCountResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Unable to clear admin login failures: {errors}");
            }
        }

        if (!await userManager.IsInRoleAsync(admin, RoleConstants.Admin))
        {
            await userManager.AddToRoleAsync(admin, RoleConstants.Admin);
        }
    }

    private static void RequirePassword(string adminPassword, string operation)
    {
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException(
                $"Admin seed password is required to {operation}. Set SeedData:AdminPassword or LODESTONE_ADMIN_PASSWORD.");
        }
    }
}
