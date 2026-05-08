using MailKit.Net.Smtp;
using MimeKit;

namespace ProcurePortal.API.Services;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var smtpHost = _config["Smtp:Host"];
        if (string.IsNullOrEmpty(smtpHost))
        {
            _logger.LogWarning("SMTP not configured — skipping email to {Email}", toEmail);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _config["Smtp:FromName"] ?? "ProcurePortal",
            _config["Smtp:FromEmail"] ?? "noreply@procureportal.ca"));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            smtpHost,
            int.Parse(_config["Smtp:Port"] ?? "587"),
            MailKit.Security.SecureSocketOptions.StartTls);

        var user = _config["Smtp:Username"];
        if (!string.IsNullOrEmpty(user))
            await client.AuthenticateAsync(user, _config["Smtp:Password"]);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
    }

    public async Task SendConfirmationEmailAsync(string toEmail, string confirmUrl)
    {
        var html = $"""
            <h2>Confirm your email</h2>
            <p>Click the link below to confirm your email address for ProcurePortal:</p>
            <p><a href="{confirmUrl}" style="display:inline-block;padding:12px 24px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">Confirm Email</a></p>
            <p>Or copy this link: <br/>{confirmUrl}</p>
            <p>This link expires in 48 hours.</p>
            """;

        await SendEmailAsync(toEmail, "Confirm your email — ProcurePortal", html);
    }

    public async Task SendMatchNotificationAsync(string toEmail, string fullName, int newMatchCount, string dashboardUrl)
    {
        var html = $"""
            <h2>New Tender Matches</h2>
            <p>Hi {fullName},</p>
            <p>You have <strong>{newMatchCount}</strong> new tender match{(newMatchCount != 1 ? "es" : "")} on ProcurePortal.</p>
            <p><a href="{dashboardUrl}" style="display:inline-block;padding:12px 24px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">View Matches</a></p>
            """;

        await SendEmailAsync(toEmail, $"{newMatchCount} new tender match{(newMatchCount != 1 ? "es" : "")} — ProcurePortal", html);
    }
}
