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
        string companyName,
        int newMatchCount,
        string dashboardUrl
    )
    {
        var firstName = fullName.Split(' ')[0];
        var html = $"""
            <h2>New Tender Matches</h2>
            <p>Hi {firstName},</p>
            <p><strong>{newMatchCount}</strong> new opportunit{(newMatchCount != 1 ? "ies were" : "y was")} found matching {companyName}'s profile on ProcurePortal.</p>
            <p><a href="{dashboardUrl}" style="display:inline-block;padding:12px 24px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">View Matches</a></p>
            """;

        await SendEmailAsync(
            toEmail,
            $"{newMatchCount} new opportunit{(newMatchCount != 1 ? "ies" : "y")} matching {companyName}",
            html
        );
    }
}
