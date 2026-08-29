using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProcurePortal.API.Data;
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
