using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProcurePortal.API.Data;
using ProcurePortal.API.Models;
using ProcurePortal.API.Services;

namespace ProcurePortal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly ProcurementsDbContext _db;
    private readonly EmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        ProcurementsDbContext db,
        EmailService emailService,
        IConfiguration config,
        ILogger<NotificationsController> logger
    )
    {
        _db = db;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public record MatchCompleteRequest(int CompanyId, int NewMatchCount);

    private string GetDashboardUrl() =>
        $"{_config["App:FrontendUrl"] ?? throw new InvalidOperationException("App:FrontendUrl must be configured")}/my-company?tab=matches";

    /// <summary>
    /// Called by the Python worker after saving new matches.
    /// Secured by internal API key (not JWT).
    /// </summary>
    [HttpPost("match-complete")]
    public async Task<IActionResult> MatchComplete([FromBody] MatchCompleteRequest request)
    {
        var expectedKey = _config["App:InternalApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
            return StatusCode(503, new { message = "Internal API key not configured" });

        var providedKey = Request.Headers["X-Internal-Key"].FirstOrDefault();
        if (providedKey != expectedKey)
            return Unauthorized(new { message = "Invalid internal API key" });

        if (request.NewMatchCount <= 0)
            return Ok(new { message = "No new matches, skipping notification" });

        var profile = await _db.CompanyProfiles
            .Include(p => p.Users)
            .FirstOrDefaultAsync(p => p.Id == request.CompanyId);

        if (profile is null || profile.Users.Count == 0)
        {
            _logger.LogWarning("No users linked to company {CompanyId}", request.CompanyId);
            return Ok(new { message = "No users linked to company" });
        }

        var dashboardUrl = GetDashboardUrl();
        var sent = 0;

        foreach (var user in profile.Users)
        {
            if (!user.NotificationsEnabled)
            {
                _logger.LogInformation("Skipping notification for user {UserId}: notifications disabled", user.Id);
                continue;
            }

            try
            {
                await _emailService.SendMatchNotificationAsync(
                    user.Email, user.FullName, profile.CompanyName, request.NewMatchCount, dashboardUrl);
                _logger.LogInformation(
                    "Match notification sent to {Email} for company {CompanyId} ({Count} matches)",
                    user.Email, request.CompanyId, request.NewMatchCount);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send match notification to {Email}", user.Email);
            }
        }

        return Ok(new { message = $"Notification sent to {sent} user(s)" });
    }

    public record CompanyMatchSummary(int CompanyId, int TotalMatches, Dictionary<string, int>? ByPortal);
    public record ScrapeCompleteRequest(int NewTenderCount, List<CompanyMatchSummary> Companies);

    /// <summary>
    /// Called by the scraper once at the end of a full scrape cycle with aggregated
    /// per-company match results. Sends (1) a per-user customer digest with each
    /// user's total new matches, and (2) an operational summary to every admin with
    /// per-company totals and a per-portal breakdown. Secured by internal API key.
    /// </summary>
    [HttpPost("scrape-complete")]
    public async Task<IActionResult> ScrapeComplete([FromBody] ScrapeCompleteRequest request)
    {
        var expectedKey = _config["App:InternalApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
            return StatusCode(503, new { message = "Internal API key not configured" });

        var providedKey = Request.Headers["X-Internal-Key"].FirstOrDefault();
        if (providedKey != expectedKey)
            return Unauthorized(new { message = "Invalid internal API key" });

        var companies = request.Companies ?? new List<CompanyMatchSummary>();
        var companyIds = companies.Select(c => c.CompanyId).Distinct().ToList();

        var profiles = await _db.CompanyProfiles
            .Include(p => p.Users)
            .Where(p => companyIds.Contains(p.Id))
            .ToListAsync();
        var profileById = profiles.ToDictionary(p => p.Id);

        var dashboardUrl = GetDashboardUrl();

        // ── (1) Per-user customer digest: sum a user's matches across their companies ──
        var userTotals = new Dictionary<int, (AppUser User, int Total)>();
        foreach (var c in companies)
        {
            if (c.TotalMatches <= 0 || !profileById.TryGetValue(c.CompanyId, out var profile))
                continue;
            foreach (var user in profile.Users)
            {
                var prev = userTotals.TryGetValue(user.Id, out var e) ? e.Total : 0;
                userTotals[user.Id] = (user, prev + c.TotalMatches);
            }
        }

        var digestsSent = 0;
        foreach (var (user, total) in userTotals.Values)
        {
            if (!user.NotificationsEnabled || total <= 0)
                continue;
            try
            {
                await _emailService.SendMatchDigestAsync(user.Email, user.FullName, total, dashboardUrl);
                digestsSent++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send match digest to {Email}", user.Email);
            }
        }

        // ── (2) Admin operational summary (sent regardless of notification prefs) ──
        var rows = companies
            .Where(c => c.TotalMatches > 0)
            .Select(c => new ScrapeSummaryRow(
                profileById.TryGetValue(c.CompanyId, out var p) ? p.CompanyName : $"Company #{c.CompanyId}",
                c.TotalMatches,
                (IReadOnlyDictionary<string, int>)(c.ByPortal ?? new Dictionary<string, int>())))
            .ToList();

        var grandTotal = rows.Sum(r => r.Total);
        var grandByPortal = new Dictionary<string, int>();
        foreach (var c in companies)
            foreach (var kv in c.ByPortal ?? new Dictionary<string, int>())
                grandByPortal[kv.Key] = grandByPortal.GetValueOrDefault(kv.Key) + kv.Value;

        var admins = await _db.Users.Where(u => u.Role == "admin" && u.IsActive).ToListAsync();
        var adminsSent = 0;
        foreach (var admin in admins)
        {
            try
            {
                await _emailService.SendScrapeSummaryAsync(
                    admin.Email, request.NewTenderCount, rows, grandTotal, grandByPortal);
                adminsSent++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send scrape summary to {Email}", admin.Email);
            }
        }

        _logger.LogInformation(
            "Scrape-complete: {NewTenders} new tenders, {GrandTotal} matches; {Digests} digest(s), {Admins} admin summary(ies) sent",
            request.NewTenderCount, grandTotal, digestsSent, adminsSent);

        return Ok(new { message = $"Sent {digestsSent} digest(s) and {adminsSent} admin summary(ies)" });
    }

    /// <summary>
    /// Admin manually sends a match notification to one specific user linked to a company.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPost("send/{companyId:int}/{userId:int}")]
    public async Task<IActionResult> SendManualToUser(int companyId, int userId)
    {
        var profile = await _db.CompanyProfiles
            .Include(p => p.Users)
            .Include(p => p.Matches)
            .FirstOrDefaultAsync(p => p.Id == companyId);

        if (profile is null)
            return NotFound(new { message = "Company not found" });

        var user = profile.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return NotFound(new { message = "User not found or not linked to this company" });

        var newCount = profile.Matches.Count(m => m.Status == "new");
        if (newCount == 0)
            return BadRequest(new { message = "No new matches to notify about" });

        var dashboardUrl = GetDashboardUrl();
        try
        {
            await _emailService.SendMatchNotificationAsync(
                user.Email, user.FullName, profile.CompanyName, newCount, dashboardUrl);
            _logger.LogInformation(
                "Manual notification sent by admin to {Email} for company {CompanyId} ({Count} new matches)",
                user.Email, companyId, newCount);
            return Ok(new { message = $"Notification sent ({newCount} new matches)", matchCount = newCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send manual notification to {Email}", user.Email);
            return StatusCode(500, new { message = "Failed to send notification" });
        }
    }

    /// <summary>
    /// Admin manually sends match notifications to all users linked to a company.
    /// Uses the count of matches with status 'new'. Bypasses notifications-enabled guard.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPost("send/{companyId:int}")]
    public async Task<IActionResult> SendManual(int companyId)
    {
        var profile = await _db.CompanyProfiles
            .Include(p => p.Users)
            .Include(p => p.Matches)
            .FirstOrDefaultAsync(p => p.Id == companyId);

        if (profile is null)
            return NotFound(new { message = "Company not found" });

        if (profile.Users.Count == 0)
            return BadRequest(new { message = "No users linked to this company" });

        var newCount = profile.Matches.Count(m => m.Status == "new");
        if (newCount == 0)
            return BadRequest(new { message = "No new matches to notify about" });

        var dashboardUrl = GetDashboardUrl();
        var sent = 0;

        foreach (var user in profile.Users)
        {
            try
            {
                await _emailService.SendMatchNotificationAsync(
                    user.Email, user.FullName, profile.CompanyName, newCount, dashboardUrl);
                _logger.LogInformation(
                    "Manual notification sent by admin to {Email} for company {CompanyId} ({Count} new matches)",
                    user.Email, companyId, newCount);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send manual notification to {Email}", user.Email);
            }
        }

        return Ok(new { message = $"Notification sent to {sent} user(s) ({newCount} new matches)", matchCount = newCount });
    }
}
