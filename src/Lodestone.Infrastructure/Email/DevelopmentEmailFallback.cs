using System.Net;
using System.Text.RegularExpressions;
using Lodestone.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lodestone.Infrastructure.Email;

/// <summary>
/// Sends through SMTP when it is configured, and otherwise writes the message to the log.
/// </summary>
/// <remarks>
/// Registered only for the Development environment. Without it, an unconfigured developer machine
/// cannot exercise any flow that depends on a link sent by email: password reset silently reports
/// success to avoid disclosing which addresses are registered, so a failed send leaves no trace
/// the developer can act on.
/// <para>
/// The logged message includes the link, which carries a single-use token. That is the point of
/// the fallback, and the reason it must never be registered outside Development.
/// </para>
/// </remarks>
public sealed partial class DevelopmentEmailFallback : IEmailService
{
    private readonly IEmailService _smtp;
    private readonly EmailSettings _settings;
    private readonly ILogger<DevelopmentEmailFallback> _logger;

    public DevelopmentEmailFallback(
        IEmailService smtp,
        IOptions<EmailSettings> settings,
        ILogger<DevelopmentEmailFallback> logger)
    {
        _smtp = smtp;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (IsSmtpConfigured())
        {
            await _smtp.SendAsync(to, subject, htmlBody, cancellationToken);
            return;
        }

        // Decode the href: it comes from an HTML body, so a query string arrives as "&amp;" and a
        // link copied straight from the log would otherwise be broken.
        var links = LinkPattern()
            .Matches(htmlBody ?? string.Empty)
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        _logger.LogWarning(
            "SMTP is not configured, so this email was not sent. To: {Recipient}. Subject: {Subject}. Links: {Links}",
            to,
            subject,
            links.Length == 0 ? "(none)" : string.Join(" | ", links));
    }

    /// <summary>
    /// A host under the reserved .invalid TLD can never resolve, so the shipped placeholder counts
    /// as unconfigured even though the setting is populated.
    /// </summary>
    private bool IsSmtpConfigured()
        => !string.IsNullOrWhiteSpace(_settings.SmtpHost)
           && !_settings.SmtpHost.Trim().EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(_settings.UserName)
           && !string.IsNullOrWhiteSpace(_settings.Password);

    [GeneratedRegex("""href\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
