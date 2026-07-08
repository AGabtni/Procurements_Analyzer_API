using System.Text.RegularExpressions;
using Resend;

namespace ProcurePortal.API.Services;

public class EmailService
{
    private readonly IResend _resend;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IResend resend, IConfiguration config, ILogger<EmailService> logger)
    {
        _resend = resend;
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var from =
            $"{_config["Resend:FromName"] ?? "ProcurePortal"} <{_config["Resend:FromEmail"] ?? "noreply@procureportal.ca"}>";

        var message = new EmailMessage
        {
            From = from,
            Subject = subject,
            HtmlBody = htmlBody,
        };
        message.To.Add(toEmail);

        await _resend.EmailSendAsync(message);
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

    public async Task SendMatchNotificationAsync(
        string toEmail,
        string fullName,
        int newMatchCount,
        string dashboardUrl
    )
    {
        var html = $"""
            <h2>New Tender Matches</h2>
            <p>Hi {fullName},</p>
            <p>You have <strong>{newMatchCount}</strong> new tender match{(
                newMatchCount != 1 ? "es" : ""
            )} on ProcurePortal.</p>
            <p><a href="{dashboardUrl}" style="display:inline-block;padding:12px 24px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">View Matches</a></p>
            """;

        await SendEmailAsync(
            toEmail,
            $"{newMatchCount} new tender match{(newMatchCount != 1 ? "es" : "")} — ProcurePortal",
            html
        );
    }
}
