using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Lodestone.Infrastructure.Identity;

/// <summary>
/// Creates the first administrator only when a deployment explicitly supplies its identity.
/// Existing administrator accounts are never modified unless development reset is explicitly enabled.
/// </summary>
public static class AdminUserSeeder
{
    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        string? adminEmail,
        string? adminPassword,
        bool resetExistingPassword = false,
        CancellationToken cancellationToken = default)
    {
        if (!await roleManager.RoleExistsAsync(RoleConstants.Admin))
        {
            await roleManager.CreateAsync(new IdentityRole(RoleConstants.Admin));
        }

        var existingAdmins = await userManager.GetUsersInRoleAsync(RoleConstants.Admin);
        if (resetExistingPassword)
        {
            var normalizedEmail = RequireEmail(adminEmail, "reset the configured Admin account");
            var adminToReset = await userManager.FindByEmailAsync(normalizedEmail);
            if (adminToReset is null || !await userManager.IsInRoleAsync(adminToReset, RoleConstants.Admin))
            {
                throw new InvalidOperationException(
                    "SeedData:AdminEmail must identify an existing Administrator before its password can be reset.");
            }

            RequirePassword(adminPassword, "reset the existing Admin account");
            await ResetPasswordAsync(userManager, adminToReset, adminPassword!);
            return;
        }

        // A deployed database may already contain an administrator from an earlier
        // configuration. Do not require or infer a personal bootstrap address then.
        if (existingAdmins.Count > 0)
        {
            return;
        }

        var email = RequireEmail(adminEmail, "create the initial Admin account");
        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            RequirePassword(adminPassword, "create the initial Admin account");
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = "Lodestone Administrator",
                EmailConfirmed = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };

            var createResult = await userManager.CreateAsync(admin, adminPassword!);
            EnsureSucceeded(createResult, "create the initial Admin account");
        }

        if (!await userManager.IsInRoleAsync(admin, RoleConstants.Admin))
        {
            var addToRoleResult = await userManager.AddToRoleAsync(admin, RoleConstants.Admin);
            EnsureSucceeded(addToRoleResult, "assign the Admin role");
        }
    }

    private static async Task ResetPasswordAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser admin,
        string adminPassword)
    {
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(admin);
        var resetResult = await userManager.ResetPasswordAsync(admin, resetToken, adminPassword);
        EnsureSucceeded(resetResult, "reset the existing Admin account password");

        var unlockResult = await userManager.SetLockoutEndDateAsync(admin, null);
        EnsureSucceeded(unlockResult, "unlock the existing Admin account");

        var failedCountResult = await userManager.ResetAccessFailedCountAsync(admin);
        EnsureSucceeded(failedCountResult, "clear the existing Admin account login failures");
    }

    private static string RequireEmail(string? adminEmail, string operation)
    {
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            throw new InvalidOperationException(
                $"Admin seed email is required to {operation}. Set SeedData:AdminEmail or LODESTONE_ADMIN_EMAIL.");
        }

        return adminEmail.Trim();
    }

    private static void RequirePassword(string? adminPassword, string operation)
    {
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException(
                $"Admin seed password is required to {operation}. Set SeedData:AdminPassword or LODESTONE_ADMIN_PASSWORD.");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }
}
