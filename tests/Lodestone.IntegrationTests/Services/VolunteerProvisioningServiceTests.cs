using FluentAssertions;
using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Lodestone.IntegrationTests.Services;

/// <summary>
/// Volunteer provisioning is the only path that grants the Volunteer role, so these tests cover
/// what happens to the account when each step of that path succeeds or fails.
/// </summary>
public sealed class VolunteerProvisioningServiceTests
{
    [Fact]
    public async Task CreateAsync_GrantsTheVolunteerRoleAndReturnsASetupToken()
    {
        var users = CreateUserManager();
        users.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        users.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleConstants.Volunteer))
            .ReturnsAsync(IdentityResult.Success);
        users.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync("setup-token");

        await using var context = CreateContext();
        var service = new VolunteerProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.CreateAsync(Dto());

        result.Succeeded.Should().BeTrue();
        result.Email.Should().Be("vol@university.test");
        result.PasswordSetupToken.Should().Be("setup-token");
        users.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleConstants.Volunteer), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_AttachesAVolunteerProfileToTheNewAccount()
    {
        ApplicationUser? created = null;
        var users = CreateUserManager();
        users.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>()))
            .Callback<ApplicationUser>(user => created = user)
            .ReturnsAsync(IdentityResult.Success);
        users.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        users.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("t");

        await using var context = CreateContext();
        var service = new VolunteerProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        await service.CreateAsync(Dto());

        // Role without profile would pass authorization and then be refused by every dashboard
        // check, so the two must be created together.
        created.Should().NotBeNull();
        created!.VolunteerProfile.Should().NotBeNull();
        created.VolunteerProfile!.Department.Should().Be("Computer Science");
        created.VolunteerProfile.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_HonoursTheApprovalChoice(bool approveImmediately)
    {
        ApplicationUser? created = null;
        var users = CreateUserManager();
        users.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>()))
            .Callback<ApplicationUser>(user => created = user)
            .ReturnsAsync(IdentityResult.Success);
        users.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        users.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("t");

        await using var context = CreateContext();
        var service = new VolunteerProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        await service.CreateAsync(Dto(approveImmediately: approveImmediately));

        created!.VolunteerProfile!.IsApproved.Should().Be(approveImmediately);
    }

    [Fact]
    public async Task CreateAsync_DeletesTheAccountWhenTheRoleCannotBeGranted()
    {
        var users = CreateUserManager();
        users.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        users.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role missing." }));
        users.Setup(m => m.DeleteAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        await using var context = CreateContext();
        var service = new VolunteerProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.CreateAsync(Dto());

        // An account without the role can never sign in as a volunteer, so it must not be left behind.
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain("Role missing.");
        users.Verify(m => m.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RejectsAnEmailThatIsAlreadyRegistered()
    {
        var users = CreateUserManager();
        users.Setup(m => m.FindByEmailAsync("vol@university.test"))
            .ReturnsAsync(new ApplicationUser { Id = "existing", Email = "vol@university.test" });

        await using var context = CreateContext();
        var service = new VolunteerProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.CreateAsync(Dto());

        result.Succeeded.Should().BeFalse();
        users.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Theory]
    [InlineData("", "vol@university.test")]
    [InlineData("A", "vol@university.test")]
    [InlineData("Valid Name", "")]
    [InlineData("Valid Name", "   ")]
    public async Task CreateAsync_RejectsInvalidIdentityDetails(string fullName, string email)
    {
        var users = CreateUserManager();
        await using var context = CreateContext();
        var service = new VolunteerProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.CreateAsync(
            new CreateVolunteerDto(fullName, email, null, null, null, null, true));

        result.Succeeded.Should().BeFalse();
        users.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task CreateSetupTokenAsync_RefusesAnAccountThatIsNotAVolunteer()
    {
        var user = new ApplicationUser { Id = "u1", Email = "student@university.test" };
        var users = CreateUserManager();
        users.Setup(m => m.FindByEmailAsync("student@university.test")).ReturnsAsync(user);
        users.Setup(m => m.IsInRoleAsync(user, RoleConstants.Volunteer)).ReturnsAsync(false);

        await using var context = CreateContext();
        var service = new VolunteerProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.CreateSetupTokenAsync("student@university.test");

        result.Succeeded.Should().BeFalse();
        users.Verify(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task CreateSetupTokenAsync_IssuesAFreshTokenForAVolunteer()
    {
        var user = new ApplicationUser { Id = "u1", Email = "vol@university.test" };
        var users = CreateUserManager();
        users.Setup(m => m.FindByEmailAsync("vol@university.test")).ReturnsAsync(user);
        users.Setup(m => m.IsInRoleAsync(user, RoleConstants.Volunteer)).ReturnsAsync(true);
        users.Setup(m => m.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("fresh-token");

        await using var context = CreateContext();
        var service = new VolunteerProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.CreateSetupTokenAsync("  vol@university.test  ");

        result.Succeeded.Should().BeTrue();
        result.PasswordSetupToken.Should().Be("fresh-token");
    }

    // ---------- helpers ----------

    private static CreateVolunteerDto Dto(bool approveImmediately = true)
        => new(
            "Volunteer A",
            "  Vol@University.test  ",
            "Computer Science",
            "Study planning",
            "Weekday evenings",
            "Second-year student mentor.",
            approveImmediately);

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"volunteer-provisioning-{Guid.NewGuid()}")
            .Options);

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
        => new(
            new Mock<IUserStore<ApplicationUser>>().Object,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
}
