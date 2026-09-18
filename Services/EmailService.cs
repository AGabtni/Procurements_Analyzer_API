using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;
using Resend;

namespace ProcurePortal.API.Services;

public class EmailService
{
    private readonly IResend _resend;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IStringLocalizer<EmailService> _loc;

    public EmailService(
        IResend resend,
        IConfiguration config,
        ILogger<EmailService> logger,
        IStringLocalizer<EmailService> loc)
    {
        _resend = resend;
        _config = config;
        _logger = logger;
        _loc = loc;
    }

    /// <summary>
    /// Sets the UI culture to the recipient's communications locale for the duration
    /// of the scope so IStringLocalizer resolves the right resx. Unknown/blank locales
    /// fall back to the neutral (English) resources. Restores the previous culture on dispose.
    /// </summary>
    private sealed class CommsCultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        public CommsCultureScope(string? commsLocale)
        {
            _previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture =
                    string.IsNullOrWhiteSpace(commsLocale) ? _previous : new CultureInfo(commsLocale);
            }
            catch (CultureNotFoundException)
            {
                // Leave the previous culture in place; neutral resources are English.
            }
        }

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
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

    public async Task SendConfirmationEmailAsync(string toEmail, string confirmUrl, string? commsLocale)
    {
        string subject, html;
        using (new CommsCultureScope(commsLocale))
        {
            subject = _loc["Confirm_Subject"];
            html = $"""
                <h2>{_loc["Confirm_Heading"]}</h2>
                <p>{_loc["Confirm_Intro"]}</p>
                <p><a href="{confirmUrl}" style="display:inline-block;padding:12px 24px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">{_loc["Confirm_Button"]}</a></p>
                <p>{_loc["Confirm_OrCopy"]} <br/>{confirmUrl}</p>
                <p>{_loc["Confirm_Expires"]}</p>
                """;
        }

        await SendEmailAsync(toEmail, subject, html);
    }

    public async Task SendMatchNotificationAsync(
        string toEmail,
        string fullName,
        string companyName,
        int newMatchCount,
        string dashboardUrl,
        string? commsLocale
    )
    {
        var firstName = fullName.Split(' ')[0];
        var one = newMatchCount == 1;
        string subject, html;
        using (new CommsCultureScope(commsLocale))
        {
            subject = _loc[one ? "Notif_Subject_One" : "Notif_Subject_Other", newMatchCount, companyName];
            html = $"""
                <h2>{_loc["Matches_Heading"]}</h2>
                <p>{_loc["Matches_Greeting", firstName]}</p>
                <p>{_loc[one ? "Notif_Body_One" : "Notif_Body_Other", newMatchCount, companyName]}</p>
                <p><a href="{dashboardUrl}" style="display:inline-block;padding:12px 24px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">{_loc["Matches_Button"]}</a></p>
                """;
        }

        await SendEmailAsync(toEmail, subject, html);
    }

    /// <summary>
    /// End-of-scrape-cycle digest sent to a customer: one email with their total
    /// new matches across all of their company profiles (total only, no breakdown).
    /// </summary>
    public async Task SendMatchDigestAsync(
        string toEmail,
        string fullName,
        int totalMatches,
        string dashboardUrl,
        string? commsLocale
    )
    {
        var firstName = fullName.Split(' ')[0];
        var one = totalMatches == 1;
        string subject, html;
        using (new CommsCultureScope(commsLocale))
        {
            subject = _loc[one ? "Digest_Subject_One" : "Digest_Subject_Other", totalMatches];
            html = $"""
                <h2>{_loc["Matches_Heading"]}</h2>
                <p>{_loc["Matches_Greeting", firstName]}</p>
                <p>{_loc[one ? "Digest_Body_One" : "Digest_Body_Other", totalMatches]}</p>
                <p><a href="{dashboardUrl}" style="display:inline-block;padding:12px 24px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px;">{_loc["Matches_Button"]}</a></p>
                """;
        }

        await SendEmailAsync(toEmail, subject, html);
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
