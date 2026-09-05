using System.Net;
using System.Net.Mail;
using Lodestone.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lodestone.Infrastructure.Email;

/// <summary>SMTP implementation of the Application <see cref="IEmailService"/> contract.</summary>
public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient email is required.", nameof(to));
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost))
            throw new InvalidOperationException("Email SMTP host is not configured (Email:SmtpHost)." );
        if (string.IsNullOrWhiteSpace(_settings.UserName) || string.IsNullOrWhiteSpace(_settings.Password))
            throw new InvalidOperationException("Email SMTP credentials are not configured (Email:UserName/Email:Password)." );

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromAddress, _settings.DisplayName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(to);

        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_settings.UserName, _settings.Password)
        };

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpFailedRecipientException)
        {
            // SMTP exception messages can contain recipient addresses and message content.
            // Keep operational diagnostics useful without routing personal data or secrets to
            // logs that may have a broader retention/access policy than the application data.
            _logger.LogError("SMTP delivery failed for the recipient address supplied by the caller.");
            throw;
        }
        catch (SmtpException)
        {
            _logger.LogError("SMTP transport failed while delivering an application email.");
            throw;
        }
    }
}

