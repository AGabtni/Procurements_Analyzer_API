using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Services;

namespace ProcurePortal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly EmailService _emailService;
    private readonly IConfiguration _config;

    public AuthController(AuthService authService, EmailService emailService, IConfiguration config)
    {
        _authService = authService;
        _emailService = emailService;
        _config = config;
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (result, error) = await _authService.LoginAsync(request);
        if (result is null)
            return Unauthorized(new { code = error });
        return Ok(result);
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var (user, error) = await _authService.RegisterAsync(request);
        if (error is not null)
            return BadRequest(new { code = error });
        return Created("", user);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var (activatedAt, trialDays, subscriptionStatus, trialEndsAt, companyId, locale, commsLocale) = await _authService.GetSessionMetaAsync(userId);
        return Ok(new
        {
            id = userId,
            email = User.FindFirst(ClaimTypes.Email)?.Value,
            fullName = User.Identity?.Name,
            role = User.FindFirst(ClaimTypes.Role)?.Value,
            activatedAt,
            trialDays,
            subscriptionStatus,
            trialEndsAt,
            companyId,
            locale,
            commsLocale,
        });
    }

    [Authorize]
    [HttpPatch("me/locale")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateLocale([FromBody] UpdateLocaleRequest request)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var (found, locale, error) = await _authService.UpdateLocaleAsync(userId, request.Locale);
        if (!found) return BadRequest(new { code = error });
        return Ok(new { locale });
    }

    [Authorize]
    [HttpPatch("me/comms-locale")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateCommsLocale([FromBody] UpdateCommsLocaleRequest request)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var (found, commsLocale, error) = await _authService.UpdateCommsLocaleAsync(userId, request.CommsLocale);
        if (!found) return BadRequest(new { code = error });
        return Ok(new { commsLocale });
    }

    // ── Email confirmation ──

    [Authorize]
    [HttpPost("send-confirmation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendConfirmation()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var (token, commsLocale, error) = await _authService.SendConfirmationAsync(userId);
        if (error is not null)
            return BadRequest(new { code = error });

        var frontendUrl =
            _config["App:FrontendUrl"]
            ?? throw new InvalidOperationException("App:FrontendUrl must be configured");
        var confirmUrl = $"{frontendUrl}/confirm-email?token={token}";
        var settings = await _authService.GetSettingsAsync(userId);
        var email = settings!.Email;

        await _emailService.SendConfirmationEmailAsync(email, confirmUrl, commsLocale);
        return Ok(new { message = "Confirmation email sent" });
    }

    [HttpGet("confirm-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string token)
    {
        if (string.IsNullOrEmpty(token))
            return BadRequest(new { code = "tokenRequired" });

        var success = await _authService.ConfirmEmailAsync(token);
        if (!success)
            return BadRequest(new { code = "invalidOrExpiredToken" });

        return Ok(new { message = "Email confirmed successfully" });
    }

    // ── User settings ──

    [Authorize]
    [HttpGet("settings")]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var settings = await _authService.GetSettingsAsync(userId);
        if (settings is null)
            return NotFound();
        return Ok(settings);
    }

    [Authorize]
    [HttpPut("settings")]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var (settings, error) = await _authService.UpdateSettingsAsync(userId, request);
        if (error is not null)
            return BadRequest(new { code = error });
        return Ok(settings);
    }

    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return BadRequest(new { code = "passwordTooShort" });

        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var error = await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        if (error is not null)
            return BadRequest(new { code = error });

        return NoContent();
    }

    // ── Admin-only: user management ──

    [Authorize(Roles = "admin")]
    [HttpGet("users")]
    [ProducesResponseType(typeof(List<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers()
    {
        return Ok(await _authService.GetAllUsersAsync());
    }

    [Authorize(Roles = "admin")]
    [HttpGet("users/unlinked")]
    [ProducesResponseType(typeof(List<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnlinkedUsers()
    {
        return Ok(await _authService.GetUnlinkedUsersAsync());
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("users/{id:int}/activate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateUser(int id)
    {
        var (found, activatedAt) = await _authService.SetActiveAsync(id, true);
        if (!found) return NotFound();
        return Ok(new { activatedAt });
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("users/{id:int}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateUser(int id)
    {
        var (found, _) = await _authService.SetActiveAsync(id, false);
        return found ? NoContent() : NotFound();
    }
}
