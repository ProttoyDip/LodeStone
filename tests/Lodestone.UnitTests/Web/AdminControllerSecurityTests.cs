using FluentAssertions;
using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Email;
using Lodestone.ML.Models;
using Lodestone.Web.Configuration;
using Lodestone.Web.Controllers;
using Lodestone.Web.Services;
using Lodestone.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Web;

public sealed class AdminControllerSecurityTests
{
    [Fact]
    public async Task Failed_counselor_setup_email_does_not_log_the_recipient_token_or_public_link()
    {
        const string email = "counselor@example.edu";
        const string rawToken = "setup-token";
        const string attackerHost = "attacker.example.invalid";
        string? sentBody = null;

        var provisioning = new Mock<ICounselorProvisioningService>();
        provisioning
            .Setup(service => service.CreateAsync(It.IsAny<CreateCounselorDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CounselorProvisioningResult(
                true,
                "counselor-id",
                email,
                rawToken,
                Array.Empty<string>()));

        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(service => service.SendAsync(
                email,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, body, _) => sentBody = body)
            .ThrowsAsync(new InvalidOperationException($"SMTP unavailable for {email}; token={rawToken}"));

        var logger = new CapturingLogger<AdminController>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString(attackerHost);
        var controller = new AdminController(
            Mock.Of<IAdminDashboardService>(),
            Mock.Of<IForumService>(),
            provisioning.Object,
            emailService.Object,
            CreatePublicLinkBuilder(),
            Mock.Of<IRiskSnapshotAdministrationService>(),
            Mock.Of<IRiskModelStatusProvider>(),
            Mock.Of<IStudentNumberVerificationService>(),
            Mock.Of<ICurrentUserService>(),
            logger)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };

        var result = await controller.CreateCounselor(new CreateCounselorViewModel
        {
            FullName = "Counselor Example",
            Email = email
        }, CancellationToken.None);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AdminController.Counselors));
        sentBody.Should().NotBeNull();
        sentBody.Should().Contain("https://portal.example.test/lodestone/Account/ResetPassword");
        sentBody.Should().NotContain(attackerHost);
        logger.Messages.Should().ContainSingle()
            .Which.Should().Be("Failed to send a counselor account setup email.");
        logger.Messages.Should().OnlyContain(message =>
            !message.Contains(email, StringComparison.OrdinalIgnoreCase) &&
            !message.Contains(rawToken, StringComparison.Ordinal));
        logger.Exceptions.Should().OnlyContain(exception => exception == null);
    }

    private static IPublicAccountLinkBuilder CreatePublicLinkBuilder()
        => new PublicAccountLinkBuilder(Options.Create(new PublicUrlSettings
        {
            BaseUrl = "https://portal.example.test/lodestone"
        }));

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
