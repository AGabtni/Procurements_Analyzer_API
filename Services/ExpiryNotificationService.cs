using System.Web;
using Microsoft.EntityFrameworkCore;
using ProcurePortal.API.Data;
using ProcurePortal.API.Models;

namespace ProcurePortal.API.Services;

/// <summary>
/// Sends one daily digest per user listing every matched tender closing in the
/// next 7 days. Fires on the first worker tick at/after the user's notify_hour
/// (UTC) and is deduplicated via match_expiry_notifications (user_id, digest_date)
/// so each user receives at most one email per calendar day.
/// </summary>
public class ExpiryNotificationService
{
    private const int DigestWindowDays = 7;

    private readonly ProcurementsDbContext _db;
    private readonly EmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<ExpiryNotificationService> _logger;

    public ExpiryNotificationService(
        ProcurementsDbContext db,
        EmailService emailService,
        IConfiguration config,
        ILogger<ExpiryNotificationService> logger
    )
    {
        _db = db;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    private string FrontendUrl =>
        _config["App:FrontendUrl"]
        ?? throw new InvalidOperationException("App:FrontendUrl must be configured");

    public async Task<int> RunAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var currentHour = nowUtc.Hour;
        var today = DateOnly.FromDateTime(nowUtc);
        var nowTs = (float)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var maxTs = (float)DateTimeOffset.UtcNow.AddDays(DigestWindowDays).ToUnixTimeSeconds();

        // Expiry alerts are independent of the match-notification toggle.
        var eligibleUsers = await _db
            .Users.Include(u => u.Preferences)
            .Include(u => u.CompanyProfile)
            .Where(u =>
                u.CompanyId != null
                && u.Preferences != null
                && u.Preferences.NotifyExpiryEnabled
                && u.Preferences.NotifyHour <= currentHour
                && !_db.MatchExpiryNotifications.Any(n => n.UserId == u.Id && n.DigestDate == today)
            )
            .ToListAsync();

        if (eligibleUsers.Count == 0)
        {
            _logger.LogInformation(
                "ExpiryCheck: no eligible users at hour {Hour} UTC",
                currentHour
            );
            return 0;
        }

        var companyIds = eligibleUsers.Select(u => u.CompanyId!.Value).Distinct().ToList();

        var matchesByCompany = await _db
            .CompanyMatches.Include(m => m.Tender)
            .Where(m =>
                companyIds.Contains(m.CompanyId)
                && m.Tender.ClosingDate != null
                && m.Tender.ClosingDate > nowTs
                && m.Tender.ClosingDate <= maxTs
            )
            .GroupBy(m => m.CompanyId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList());

        var emailsSent = 0;

        foreach (var user in eligibleUsers)
        {
            try
            {
                // No expiring matches → skip without stamping so a later match today can still trigger a send.
                if (
                    !matchesByCompany.TryGetValue(user.CompanyId!.Value, out var userMatches)
                    || userMatches.Count == 0
                )
                    continue;

                var rows = BuildRows(userMatches);
                if (rows.Count == 0)
                    continue;

                var matchesUrl = BuildMatchesUrl();

                await _emailService.SendExpiryDigestAsync(
                    user.Email,
                    user.FullName,
                    user.CompanyProfile!.CompanyName,
                    rows,
                    matchesUrl,
                    user.Preferences!.CommsLocale
                );

                _db.MatchExpiryNotifications.Add(
                    new MatchExpiryNotification
                    {
                        UserId = user.Id,
                        DigestDate = today,
                        SentAt = nowUtc,
                    }
                );
                await _db.SaveChangesAsync();
                emailsSent++;

                _logger.LogInformation(
                    "ExpiryCheck: sent digest to {Email} ({Count} match rows)",
                    user.Email,
                    rows.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ExpiryCheck: failed to send digest to user {UserId}",
                    user.Id
                );
            }
        }

        _logger.LogInformation("ExpiryCheck: complete — {Count} digest(s) sent", emailsSent);
        return emailsSent;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private IReadOnlyList<ExpiryMatchRow> BuildRows(IEnumerable<CompanyMatch> matches)
    {
        var rows = new List<ExpiryMatchRow>();

        foreach (var match in matches)
        {
            if (match.Tender?.ClosingDate is not float closingTs || closingTs <= 0)
                continue;

            var closingDt = DateTimeOffset.FromUnixTimeSeconds((long)closingTs).UtcDateTime;
            var daysLeft = Math.Max(0, (int)Math.Ceiling((closingDt - DateTime.UtcNow).TotalDays));

            rows.Add(
                new ExpiryMatchRow(
                    match.Id,
                    match.TenderId,
                    match.Tender.Title ?? $"Tender #{match.TenderId}",
                    match.Tender.BuyingOrganization,
                    closingDt,
                    daysLeft,
                    $"{FrontendUrl}/tenders/{match.TenderId}"
                )
            );
        }

        return rows.OrderBy(r => r.DaysLeft).ToList();
    }

    private string BuildMatchesUrl()
    {
        var url = $"{FrontendUrl}/my-company?tab=matches&sortBy=closingDate&sortDir=asc";
        //url += $"&q={HttpUtility.UrlEncode("tenders about to expire")}";
        return url;
    }
}
