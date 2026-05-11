using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Services;
using System.Security.Claims;

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
            return Unauthorized(new { message = error });
        return Ok(result);
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var (user, error) = await _authService.RegisterAsync(request);
        if (error is not null)
            return BadRequest(new { message = error });
        return Created("", user);
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public IActionResult Me()
    {
        return Ok(new
        {
            id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            fullName = User.Identity?.Name,
            role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
        });
    }

    // ── Email confirmation ──

    [Authorize]
    [HttpPost("send-confirmation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendConfirmation()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var (token, error) = await _authService.SendConfirmationAsync(userId);
        if (error is not null)
            return BadRequest(new { message = error });

        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:5173";
        var confirmUrl = $"{frontendUrl}/confirm-email?token={token}";
        var settings = await _authService.GetSettingsAsync(userId);
        var email = settings!.Email;

        await _emailService.SendConfirmationEmailAsync(email, confirmUrl);
        return Ok(new { message = "Confirmation email sent" });
    }

    [HttpGet("confirm-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string token)
    {
        if (string.IsNullOrEmpty(token))
            return BadRequest(new { message = "Token is required" });

        var success = await _authService.ConfirmEmailAsync(token);
        if (!success)
            return BadRequest(new { message = "Invalid or expired token" });

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
        if (settings is null) return NotFound();
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
            return BadRequest(new { message = error });
        return Ok(settings);
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateUser(int id)
    {
        return await _authService.SetActiveAsync(id, true) ? NoContent() : NotFound();
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("users/{id:int}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateUser(int id)
    {
        return await _authService.SetActiveAsync(id, false) ? NoContent() : NotFound();
    }
}
