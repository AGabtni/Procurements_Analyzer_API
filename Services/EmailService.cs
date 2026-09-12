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

    /// <summary>
    /// End-of-scrape-cycle digest sent to a customer: one email with their total
    /// new matches across all of their company profiles (total only, no breakdown).
    /// </summary>
    public async Task SendMatchDigestAsync(
        string toEmail,
        string fullName,
        int totalMatches,
        string dashboardUrl
    )
    {
        var firstName = fullName.Split(' ')[0];
        var noun = totalMatches != 1 ? "opportunities were" : "opportunity was";
        var html = $"""
            <h2>New Tender Matches</h2>
            <p>Hi {firstName},</p>
            <p><strong>{totalMatches}</strong> new {noun} found matching your profile on ProcurePortal.</p>
            <p><a href="{dashboardUrl}" style="display:inline-block;padding:12px 24px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">View Matches</a></p>
            """;

        await SendEmailAsync(
            toEmail,
            $"{totalMatches} new opportunit{(totalMatches != 1 ? "ies" : "y")} matching your profile",
            html
        );
    }

    /// <summary>
    /// Operational summary sent to admins after a full scrape cycle: per-company
    /// total matches with a per-portal breakdown, plus grand totals.
    /// </summary>
    public async Task SendScrapeSummaryAsync(
        string toEmail,
        int newTenderCount,
        IReadOnlyList<ScrapeSummaryRow> companies,
        int grandTotalMatches,
        IReadOnlyDictionary<string, int> grandByPortal
    )
    {
        static string Portals(IReadOnlyDictionary<string, int> byPortal) =>
            byPortal.Count == 0
                ? "&mdash;"
                : string.Join(", ",
                    byPortal.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));

        string rows = companies.Count == 0
            ? "<tr><td colspan=\"3\" style=\"padding:6px 10px;\">No matches this cycle.</td></tr>"
            : string.Join("", companies
                .OrderByDescending(c => c.Total)
                .Select(c => $"""
                    <tr>
                      <td style="padding:6px 10px;border-top:1px solid #ddd;">{c.Company}</td>
                      <td style="padding:6px 10px;border-top:1px solid #ddd;text-align:right;">{c.Total}</td>
                      <td style="padding:6px 10px;border-top:1px solid #ddd;">{Portals(c.ByPortal)}</td>
                    </tr>
                    """));

        var html = $"""
            <h2>Scrape Cycle Summary</h2>
            <p><strong>{newTenderCount}</strong> new tender(s) scraped this cycle.</p>
            <p><strong>{grandTotalMatches}</strong> total new match(es) across <strong>{companies.Count}</strong> compan{(companies.Count != 1 ? "ies" : "y")}.</p>
            <p>By portal: {Portals(grandByPortal)}</p>
            <table style="border-collapse:collapse;font-family:sans-serif;font-size:14px;">
              <thead>
                <tr>
                  <th style="padding:6px 10px;text-align:left;">Company</th>
                  <th style="padding:6px 10px;text-align:right;">Matches</th>
                  <th style="padding:6px 10px;text-align:left;">By portal</th>
                </tr>
              </thead>
              <tbody>{rows}</tbody>
            </table>
            """;

        await SendEmailAsync(
            toEmail,
            $"Scrape summary — {grandTotalMatches} new match(es), {newTenderCount} new tender(s)",
            html
        );
    }
}

/// <summary>One company's line in the admin scrape summary.</summary>
public record ScrapeSummaryRow(string Company, int Total, IReadOnlyDictionary<string, int> ByPortal);
