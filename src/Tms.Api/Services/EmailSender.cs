using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Tms.Api.Services;

public record EmailAttachment(string FileName, byte[] Content, string ContentType);

/// <summary>
/// The one place this codebase sends real email from — a plain SMTP client (MailKit,
/// pure C#, no vendor SDK) rather than a specific provider's API, since every real
/// provider (SES, SendGrid, Postmark, a corporate Exchange server, Gmail) accepts SMTP,
/// so the choice of provider stays entirely a deployment-time config decision (the
/// "Email" appsettings section) rather than a code one. Config left blank —
/// the default, since no real mail account exists in this environment — means sending
/// is a documented no-op rather than a startup failure or a fabricated success: every
/// call still logs exactly what would have been sent, so the calling code path (and
/// its own tests) behaves identically whether or not a real SMTP account is configured.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments, CancellationToken ct);
}

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments, CancellationToken ct)
    {
        var host = _configuration["Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation("Email:SmtpHost is not configured — skipping send to {ToEmail} ({Subject}).", toEmail, subject);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _configuration["Email:FromName"] ?? "TMS",
            _configuration["Email:FromAddress"] ?? "noreply@example.com"));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        if (attachments is not null)
        {
            foreach (var attachment in attachments)
                builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var port = _configuration.GetValue("Email:SmtpPort", 587);
        await client.ConnectAsync(host, port, SecureSocketOptions.Auto, ct);

        var username = _configuration["Email:SmtpUsername"];
        if (!string.IsNullOrEmpty(username))
            await client.AuthenticateAsync(username, _configuration["Email:SmtpPassword"] ?? string.Empty, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
