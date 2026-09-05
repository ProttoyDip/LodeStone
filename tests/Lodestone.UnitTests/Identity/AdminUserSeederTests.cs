using FluentAssertions;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Identity;

public sealed class AdminUserSeederTests
{
    [Fact]
    public async Task Existing_admin_does_not_require_a_password_when_reset_is_disabled()
    {
        var admin = new ApplicationUser { Id = "admin-id" };
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();

        roleManager
            .Setup(manager => manager.RoleExistsAsync(RoleConstants.Admin))
            .ReturnsAsync(true);
        userManager
            .Setup(manager => manager.GetUsersInRoleAsync(RoleConstants.Admin))
            .ReturnsAsync(new List<ApplicationUser> { admin });

        var action = () => AdminUserSeeder.SeedAsync(
            userManager.Object,
            roleManager.Object,
            null,
            string.Empty,
            resetExistingPassword: false);

        await action.Should().NotThrowAsync();
        userManager.Verify(
            manager => manager.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
        userManager.Verify(
            manager => manager.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
        userManager.Verify(
            manager => manager.ResetPasswordAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Missing_admin_requires_a_password_before_creation()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();

        roleManager
            .Setup(manager => manager.RoleExistsAsync(RoleConstants.Admin))
            .ReturnsAsync(true);
        userManager
            .Setup(manager => manager.GetUsersInRoleAsync(RoleConstants.Admin))
            .ReturnsAsync(new List<ApplicationUser>());
        userManager
            .Setup(manager => manager.FindByEmailAsync("admin@example.edu"))
            .ReturnsAsync((ApplicationUser?)null);

        var action = () => AdminUserSeeder.SeedAsync(
            userManager.Object,
            roleManager.Object,
            "admin@example.edu",
            "   ",
            resetExistingPassword: false);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*required to create the initial Admin account*");
        userManager.Verify(
            manager => manager.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Existing_admin_requires_a_password_before_an_explicit_reset()
    {
        var admin = new ApplicationUser { Id = "admin-id" };
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();

        roleManager
            .Setup(manager => manager.RoleExistsAsync(RoleConstants.Admin))
            .ReturnsAsync(true);
        userManager
            .Setup(manager => manager.GetUsersInRoleAsync(RoleConstants.Admin))
            .ReturnsAsync(new List<ApplicationUser> { admin });
        userManager
            .Setup(manager => manager.FindByEmailAsync("admin@example.edu"))
            .ReturnsAsync(admin);
        userManager
            .Setup(manager => manager.IsInRoleAsync(admin, RoleConstants.Admin))
            .ReturnsAsync(true);

        var action = () => AdminUserSeeder.SeedAsync(
            userManager.Object,
            roleManager.Object,
            "admin@example.edu",
            string.Empty,
            resetExistingPassword: true);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*required to reset the existing Admin account*");
        userManager.Verify(
            manager => manager.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
        userManager.Verify(
            manager => manager.ResetPasswordAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static Mock<RoleManager<IdentityRole>> CreateRoleManager()
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        return new Mock<RoleManager<IdentityRole>>(
            store.Object,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);
    }
}
