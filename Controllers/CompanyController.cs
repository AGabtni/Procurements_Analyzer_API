using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Services;

namespace ProcurePortal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CompanyController : ControllerBase
{
    private readonly CompanyService _companyService;
    private readonly AuthService _authService;

    public CompanyController(CompanyService companyService, AuthService authService)
    {
        _companyService = companyService;
        _authService = authService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin() =>
        User.IsInRole("admin");

    // ── My Profile (current user) ──

    [HttpGet("me")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyProfile()
    {
        var profile = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (profile is null) return NotFound();
        return Ok(profile);
    }

    [HttpPost("me")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMyProfile([FromBody] CreateCompanyProfileRequest request)
    {
        var existing = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (existing is not null)
            return Conflict(new { message = "You already have a company profile" });

        var profile = await _companyService.CreateProfileAsync(request, GetUserId());
        return CreatedAtAction(nameof(GetMyProfile), profile);
    }

    [HttpPut("me")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateCompanyProfileRequest request)
    {
        var profile = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (profile is null) return NotFound();
        var updated = await _companyService.UpdateProfileAsync(profile.Id, request);
        return Ok(updated);
    }

    [HttpGet("me/preferences")]
    [ProducesResponseType(typeof(CompanyPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyPreferences()
    {
        var profile = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (profile is null) return NotFound();
        var prefs = await _companyService.GetPreferencesAsync(profile.Id);
        if (prefs is null) return Ok(new CompanyPreferencesDto());
        return Ok(prefs);
    }

    [HttpPut("me/preferences")]
    [ProducesResponseType(typeof(CompanyPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertMyPreferences([FromBody] CompanyPreferencesRequest request)
    {
        var profile = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (profile is null) return NotFound();
        var prefs = await _companyService.UpsertPreferencesAsync(profile.Id, request);
        return Ok(prefs);
    }

    [HttpGet("me/matches")]
    [ProducesResponseType(typeof(PagedResult<CompanyMatchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyMatches([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var profile = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (profile is null) return NotFound();
        var matches = await _companyService.GetMatchesAsync(profile.Id, status, page, pageSize);
        return Ok(matches);
    }

    [HttpGet("me/matches/stats")]
    [ProducesResponseType(typeof(MatchStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyMatchStats()
    {
        var profile = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (profile is null) return NotFound();
        var stats = await _companyService.GetMatchStatsAsync(profile.Id);
        return Ok(stats);
    }

    [HttpPatch("me/matches/{matchId:int}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyMatchStatus(int matchId, [FromBody] UpdateMatchStatusRequest request)
    {
        var profile = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (profile is null) return NotFound();
        var updated = await _companyService.UpdateMatchStatusAsync(matchId, profile.Id, request.Status);
        if (!updated) return NotFound();
        return NoContent();
    }

    [HttpPost("me/match")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerMyMatch()
    {
        var profile = await _companyService.GetProfileByUserIdAsync(GetUserId());
        if (profile is null) return NotFound();
        return await HandleTrigger(profile.Id, false);
    }

    // ── Admin: All Profiles ──

    [HttpGet]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(List<CompanyProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var profiles = await _companyService.GetAllProfilesAsync();
        return Ok(profiles);
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AdminCreate([FromBody] AdminCreateCompanyRequest request)
    {
        if (request.UserId.HasValue)
        {
            var existing = await _companyService.GetProfileByUserIdAsync(request.UserId.Value);
            if (existing is not null)
                return BadRequest(new { message = "This user already has a company profile" });
        }
        var profile = await _companyService.CreateProfileAsync(request, request.UserId);
        return Created("", profile);
    }

    [HttpPatch("{id:int}/link-user")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LinkUser(int id, [FromBody] LinkUserRequest request)
    {
        var (profile, error) = await _companyService.LinkUserAsync(id, request.UserId);
        if (error is not null)
            return profile is null ? NotFound(new { message = error }) : BadRequest(new { message = error });
        return Ok(profile);
    }

    [HttpPost("{id:int}/dissociate")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DissociateUser(int id, [FromBody] AdminPasswordConfirmRequest request)
    {
        var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await _authService.VerifyPasswordAsync(adminId, request.Password))
            return Unauthorized(new { message = "Incorrect password" });

        var (profile, error) = await _companyService.LinkUserAsync(id, null);
        if (error is not null)
            return profile is null ? NotFound(new { message = error }) : BadRequest(new { message = error });
        return Ok(profile);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, [FromBody] AdminPasswordConfirmRequest request)
    {
        var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await _authService.VerifyPasswordAsync(adminId, request.Password))
            return Unauthorized(new { message = "Incorrect password" });

        var (success, error) = await _companyService.DeleteProfileAsync(id);
        if (!success)
            return error == "Company profile not found" ? NotFound(new { message = error }) : BadRequest(new { message = error });
        return NoContent();
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var profile = await _companyService.GetProfileByIdAsync(id);
        if (profile is null) return NotFound();
        return Ok(profile);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCompanyProfileRequest request)
    {
        var profile = await _companyService.UpdateProfileAsync(id, request);
        if (profile is null) return NotFound();
        return Ok(profile);
    }

    [HttpGet("{id:int}/preferences")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CompanyPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPreferences(int id)
    {
        var prefs = await _companyService.GetPreferencesAsync(id);
        if (prefs is null) return NotFound();
        return Ok(prefs);
    }

    [HttpPut("{id:int}/preferences")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CompanyPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertPreferences(int id, [FromBody] CompanyPreferencesRequest request)
    {
        var prefs = await _companyService.UpsertPreferencesAsync(id, request);
        return Ok(prefs);
    }

    [HttpGet("{id:int}/matches")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(PagedResult<CompanyMatchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMatches(int id, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var matches = await _companyService.GetMatchesAsync(id, status, page, pageSize);
        return Ok(matches);
    }

    [HttpGet("{id:int}/matches/stats")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(MatchStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMatchStats(int id)
    {
        var stats = await _companyService.GetMatchStatsAsync(id);
        return Ok(stats);
    }

    [HttpPatch("{id:int}/matches/{matchId:int}/status")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMatchStatus(int id, int matchId, [FromBody] UpdateMatchStatusRequest request)
    {
        var updated = await _companyService.UpdateMatchStatusAsync(matchId, id, request.Status);
        if (!updated) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/match")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerMatch(int id)
    {
        var profile = await _companyService.GetProfileByIdAsync(id);
        if (profile is null) return NotFound();
        return await HandleTrigger(id, true);
    }

    [HttpPost("{id:int}/match/reset")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerReset(int id)
    {
        var profile = await _companyService.GetProfileByIdAsync(id);
        if (profile is null) return NotFound();

        var (started, retryAfter) = await _companyService.TriggerResetAsync(id, bypassCooldown: true);

        if (retryAfter.HasValue)
        {
            Response.Headers.Append("Retry-After", retryAfter.Value.ToString());
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Cooldown active", retryAfterSeconds = retryAfter.Value });
        }

        if (!started)
            return Conflict(new { message = "Matching is already in progress or pending" });

        return Accepted(new { message = "Reset + matching job queued" });
    }

    // ── Shared trigger helper ──

    private async Task<IActionResult> HandleTrigger(int companyId, bool bypassCooldown)
    {
        var (started, retryAfter) = await _companyService.TriggerMatchAsync(companyId, bypassCooldown);

        if (retryAfter.HasValue)
        {
            Response.Headers.Append("Retry-After", retryAfter.Value.ToString());
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Cooldown active", retryAfterSeconds = retryAfter.Value });
        }

        if (!started)
            return Conflict(new { message = "Matching is already in progress or pending" });

        return Accepted(new { message = "Matching job queued" });
    }
}
