using FluentAssertions;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Web.Configuration;
using Lodestone.Web.Controllers;
using Lodestone.Web.Services;
using Lodestone.Web.ViewModels.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Web;

public sealed class AccountControllerSecurityTests
{
    [Fact]
    public async Task Failed_reset_email_does_not_log_the_recipient_or_reset_url()
    {
        const string email = "student@example.edu";
        const string rawToken = "raw-reset-token";
        var user = new ApplicationUser { Id = "student-id", Email = email, IsActive = true };
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.FindByEmailAsync(email)).ReturnsAsync(user);
        userManager.Setup(manager => manager.GeneratePasswordResetTokenAsync(user)).ReturnsAsync(rawToken);

        string? sentBody = null;
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(service => service.SendAsync(
                email,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, body, _) => sentBody = body)
            .ThrowsAsync(new InvalidOperationException($"SMTP unavailable for {email}; token={rawToken}"));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("attacker.example.invalid");

        var logger = new CapturingLogger<AccountController>();
        var controller = new AccountController(
            CreateSignInManager(userManager.Object).Object,
            userManager.Object,
            emailService.Object,
            CreatePublicLinkBuilder(),
            Mock.Of<IActivityLogService>(),
            Mock.Of<IRiskMonitoringConsentService>(),
            Mock.Of<IStudentNumberVerificationService>(),
            logger)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.ForgotPassword(new ForgotPasswordViewModel { Email = email });

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AccountController.ForgotPasswordConfirmation));
        logger.Messages.Should().ContainSingle()
            .Which.Should().Be("Failed to send a password reset email.");
        logger.Messages.Should().OnlyContain(message =>
            !message.Contains(email, StringComparison.OrdinalIgnoreCase) &&
            !message.Contains(rawToken, StringComparison.Ordinal) &&
            !message.Contains("encoded-secret-token", StringComparison.Ordinal));
        logger.Exceptions.Should().OnlyContain(exception => exception == null);
        sentBody.Should().NotBeNull();
        sentBody.Should().Contain("https://portal.example.test/lodestone/Account/ResetPassword");
        sentBody.Should().NotContain("attacker.example.invalid");
    }

    [Fact]
    public void Reset_password_response_is_not_cacheable_and_never_sends_a_referrer()
    {
        var controller = CreateController();

        var result = controller.ResetPassword("student@example.edu", "encoded-token");

        result.Should().BeOfType<ViewResult>();
        controller.Response.Headers["Cache-Control"].ToString()
            .Should().Be("no-store, no-cache, max-age=0, must-revalidate");
        controller.Response.Headers["Pragma"].ToString().Should().Be("no-cache");
        controller.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        controller.Response.Headers["X-Robots-Tag"].ToString().Should().Be("noindex, nofollow");
    }

    [Fact]
    public void Public_account_links_use_the_configured_https_origin_and_preserve_its_path_base()
    {
        var link = CreatePublicLinkBuilder().BuildPasswordResetUrl(
            "student+tag@example.edu",
            "encoded-token");
        var uri = new Uri(link);

        uri.Scheme.Should().Be(Uri.UriSchemeHttps);
        uri.Host.Should().Be("portal.example.test");
        uri.AbsolutePath.Should().Be("/lodestone/Account/ResetPassword");
        var query = QueryHelpers.ParseQuery(uri.Query);
        query["email"].ToString().Should().Be("student+tag@example.edu");
        query["token"].ToString().Should().Be("encoded-token");
    }

    [Theory]
    [InlineData("http://portal.example.test")]
    [InlineData("https://user:password@portal.example.test")]
    [InlineData("https://portal.example.test?next=attacker")]
    [InlineData("https://portal.example.test#fragment")]
    [InlineData("not a url")]
    public void Public_url_validation_rejects_noncanonical_or_insecure_origins(string baseUrl)
    {
        PublicUrlSettings.IsValid(new PublicUrlSettings { BaseUrl = baseUrl }).Should().BeFalse();
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

    private static Mock<SignInManager<ApplicationUser>> CreateSignInManager(
        UserManager<ApplicationUser> userManager)
        => new(
            userManager,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<ApplicationUser>>());

    private static IPublicAccountLinkBuilder CreatePublicLinkBuilder()
        => new PublicAccountLinkBuilder(Options.Create(new PublicUrlSettings
        {
            BaseUrl = "https://portal.example.test/lodestone"
        }));

    private static AccountController CreateController()
    {
        var userManager = CreateUserManager();
        return new AccountController(
            CreateSignInManager(userManager.Object).Object,
            userManager.Object,
            Mock.Of<IEmailService>(),
            CreatePublicLinkBuilder(),
            Mock.Of<IActivityLogService>(),
            Mock.Of<IRiskMonitoringConsentService>(),
            Mock.Of<IStudentNumberVerificationService>(),
            NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
