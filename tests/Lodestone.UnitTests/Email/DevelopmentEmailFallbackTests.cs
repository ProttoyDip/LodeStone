using FluentAssertions;
using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Email;

public sealed class DevelopmentEmailFallbackTests
{
    private const string Body =
        """<p>Hello</p><a href="https://localhost:5001/Account/ResetPassword?token=abc">Set password</a>""";

    [Fact]
    public async Task Delegates_to_smtp_when_it_is_configured()
    {
        var smtp = new Mock<IEmailService>();
        var fallback = Create(smtp, Configured());

        await fallback.SendAsync("vol@university.test", "Subject", Body);

        smtp.Verify(
            service => service.SendAsync(
                "vol@university.test", "Subject", Body, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Does_not_send_when_smtp_is_unconfigured()
    {
        var smtp = new Mock<IEmailService>(MockBehavior.Strict);
        var fallback = Create(smtp, Unconfigured());

        await fallback.SendAsync("vol@university.test", "Subject", Body);

        smtp.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Does_not_throw_when_smtp_is_unconfigured()
    {
        var fallback = Create(new Mock<IEmailService>(), Unconfigured());

        // Password reset reports success regardless to avoid disclosing which addresses exist, so
        // an unconfigured machine must not turn that into an unhandled failure.
        var act = async () => await fallback.SendAsync("vol@university.test", "Subject", Body);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("smtp.example.invalid", "user", "secret")]   // reserved TLD can never resolve
    [InlineData("SMTP.EXAMPLE.INVALID", "user", "secret")]   // and the check is case-insensitive
    [InlineData("smtp.gmail.com", "", "secret")]             // credentials incomplete
    [InlineData("smtp.gmail.com", "user", "")]
    [InlineData("", "user", "secret")]
    public async Task Treats_placeholder_or_incomplete_settings_as_unconfigured(
        string host,
        string userName,
        string password)
    {
        var smtp = new Mock<IEmailService>(MockBehavior.Strict);
        var fallback = Create(smtp, new EmailSettings
        {
            SmtpHost = host,
            UserName = userName,
            Password = password
        });

        await fallback.SendAsync("vol@university.test", "Subject", Body);

        smtp.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Logs_a_link_that_can_be_pasted_straight_from_the_console()
    {
        var logger = new CapturingLogger();
        var fallback = new DevelopmentEmailFallback(
            Mock.Of<IEmailService>(),
            Options.Create(Unconfigured()),
            logger);

        await fallback.SendAsync(
            "vol@university.test",
            "Subject",
            """<a href="https://localhost:5001/Reset?email=a@b.test&amp;token=xyz">Set password</a>""");

        // The href is HTML-encoded in the body; a logged "&amp;" would break the copied URL.
        logger.Messages.Should().ContainSingle()
            .Which.Should().Contain("token=xyz").And.NotContain("&amp;");
    }

    private sealed class CapturingLogger : ILogger<DevelopmentEmailFallback>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private static EmailSettings Configured() => new()
    {
        SmtpHost = "smtp.gmail.com",
        SmtpPort = 587,
        UserName = "user@gmail.com",
        Password = "app-password"
    };

    private static EmailSettings Unconfigured() => new()
    {
        SmtpHost = "smtp.example.invalid",
        UserName = string.Empty,
        Password = string.Empty
    };

    private static DevelopmentEmailFallback Create(Mock<IEmailService> smtp, EmailSettings settings)
        => new(
            smtp.Object,
            Options.Create(settings),
            NullLogger<DevelopmentEmailFallback>.Instance);
}
