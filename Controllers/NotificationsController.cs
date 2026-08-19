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

    /// <summary>
    /// Called by the Python worker after saving new matches.
    /// Secured by internal API key (not JWT).
    /// </summary>
    [HttpPost("match-complete")]
    public async Task<IActionResult> MatchComplete([FromBody] MatchCompleteRequest request)
    {
        // Validate internal API key
        var expectedKey = _config["App:InternalApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
            return StatusCode(503, new { message = "Internal API key not configured" });

        var providedKey = Request.Headers["X-Internal-Key"].FirstOrDefault();
        if (providedKey != expectedKey)
            return Unauthorized(new { message = "Invalid internal API key" });

        if (request.NewMatchCount <= 0)
            return Ok(new { message = "No new matches, skipping notification" });

        // Find the company's owner
        var profile = await _db
            .CompanyProfiles.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == request.CompanyId);

        if (profile?.User is null)
        {
            _logger.LogWarning("No user linked to company {CompanyId}", request.CompanyId);
            return Ok(new { message = "No user linked to company" });
        }

        var user = profile.User;
        /*
        if (!user.EmailConfirmed)
        {
            _logger.LogInformation(
                "Skipping notification for company {CompanyId}: email not confirmed",
                request.CompanyId
            );
            return Ok(new { message = "Email not confirmed" });
        }
        */
        if (!user.NotificationsEnabled)
        {
            _logger.LogInformation(
                "Skipping notification for company {CompanyId}: notifications disabled",
                request.CompanyId
            );
            return Ok(new { message = "Notifications disabled" });
        }

        var frontendUrl =
            _config["App:FrontendUrl"]
            ?? throw new InvalidOperationException("App:FrontendUrl must be configured");
        var dashboardUrl = $"{frontendUrl}/my-company?tab=matches";

        try
        {
            await _emailService.SendMatchNotificationAsync(
                user.Email,
                user.FullName,
                profile.CompanyName,
                request.NewMatchCount,
                dashboardUrl
            );
            _logger.LogInformation(
                "Match notification sent to {Email} for company {CompanyId} ({Count} matches)",
                user.Email,
                request.CompanyId,
                request.NewMatchCount
            );
            return Ok(new { message = "Notification sent" });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send match notification for company {CompanyId}",
                request.CompanyId
            );
            return StatusCode(500, new { message = "Failed to send notification" });
        }
    }

    /// <summary>
    /// Admin manually sends a match notification to the user linked to a company.
    /// Uses the count of matches with status 'new'. Bypasses email-confirmed and notifications-enabled guards.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPost("send/{companyId:int}")]
    public async Task<IActionResult> SendManual(int companyId)
    {
        var profile = await _db
            .CompanyProfiles.Include(p => p.User)
            .Include(p => p.Matches)
            .FirstOrDefaultAsync(p => p.Id == companyId);

        if (profile is null)
            return NotFound(new { message = "Company not found" });

        if (profile.User is null)
            return BadRequest(new { message = "No user linked to this company" });

        var newCount = profile.Matches.Count(m => m.Status == "new");
        if (newCount == 0)
            return BadRequest(new { message = "No new matches to notify about" });

        var frontendUrl =
            _config["App:FrontendUrl"]
            ?? throw new InvalidOperationException("App:FrontendUrl must be configured");
        var dashboardUrl = $"{frontendUrl}/my-company?tab=matches";

        try
        {
            await _emailService.SendMatchNotificationAsync(
                profile.User.Email,
                profile.User.FullName,
                profile.CompanyName,
                newCount,
                dashboardUrl
            );
            _logger.LogInformation(
                "Manual notification sent by admin to {Email} for company {CompanyId} ({Count} new matches)",
                profile.User.Email,
                companyId,
                newCount
            );
            return Ok(
                new
                {
                    message = $"Notification sent ({newCount} new matches)",
                    matchCount = newCount,
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send manual notification for company {CompanyId}",
                companyId
            );
            return StatusCode(500, new { message = "Failed to send notification" });
        }
    }
}
