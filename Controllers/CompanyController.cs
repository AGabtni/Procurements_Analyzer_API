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

    public CompanyController(CompanyService companyService)
    {
        _companyService = companyService;
    }

    // ── Profile CRUD ──

    [HttpGet]
    [ProducesResponseType(typeof(List<CompanyProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var profiles = await _companyService.GetAllProfilesAsync();
        return Ok(profiles);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var profile = await _companyService.GetProfileByIdAsync(id);
        if (profile is null) return NotFound();
        return Ok(profile);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateCompanyProfileRequest request)
    {
        var profile = await _companyService.CreateProfileAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCompanyProfileRequest request)
    {
        var profile = await _companyService.UpdateProfileAsync(id, request);
        if (profile is null) return NotFound();
        return Ok(profile);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _companyService.DeleteProfileAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    // ── Preferences ──

    [HttpGet("{id:int}/preferences")]
    [ProducesResponseType(typeof(CompanyPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPreferences(int id)
    {
        var prefs = await _companyService.GetPreferencesAsync(id);
        if (prefs is null) return NotFound();
        return Ok(prefs);
    }

    [HttpPut("{id:int}/preferences")]
    [ProducesResponseType(typeof(CompanyPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertPreferences(int id, [FromBody] CompanyPreferencesRequest request)
    {
        var prefs = await _companyService.UpsertPreferencesAsync(id, request);
        return Ok(prefs);
    }

    // ── Matches (read + stats + status update) ──

    [HttpGet("{id:int}/matches")]
    [ProducesResponseType(typeof(List<CompanyMatchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMatches(int id, [FromQuery] string? status, [FromQuery] int limit = 50)
    {
        var matches = await _companyService.GetMatchesAsync(id, status, limit);
        return Ok(matches);
    }

    [HttpGet("{id:int}/matches/stats")]
    [ProducesResponseType(typeof(MatchStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMatchStats(int id)
    {
        var stats = await _companyService.GetMatchStatsAsync(id);
        return Ok(stats);
    }

    [HttpPatch("{id:int}/matches/{matchId:int}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMatchStatus(int id, int matchId, [FromBody] UpdateMatchStatusRequest request)
    {
        var updated = await _companyService.UpdateMatchStatusAsync(matchId, id, request.Status);
        if (!updated) return NotFound();
        return NoContent();
    }

    // ── Matching trigger ──

    [HttpPost("{id:int}/match")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerMatch(int id)
    {
        var (started, retryAfter) = await _companyService.TriggerMatchAsync(id);

        if (retryAfter.HasValue)
        {
            Response.Headers.Append("Retry-After", retryAfter.Value.ToString());
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Cooldown active", retryAfterSeconds = retryAfter.Value });
        }

        if (!started)
        {
            // Either not found or already pending/running
            var profile = await _companyService.GetProfileByIdAsync(id);
            if (profile is null) return NotFound();
            return Conflict(new { message = "Matching is already in progress or pending" });
        }

        return Accepted(new { message = "Matching job queued" });
    }

    [HttpPost("{id:int}/match/reset")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerReset(int id)
    {
        var (started, retryAfter) = await _companyService.TriggerResetAsync(id);

        if (retryAfter.HasValue)
        {
            Response.Headers.Append("Retry-After", retryAfter.Value.ToString());
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Cooldown active", retryAfterSeconds = retryAfter.Value });
        }

        if (!started)
        {
            var profile = await _companyService.GetProfileByIdAsync(id);
            if (profile is null) return NotFound();
            return Conflict(new { message = "Matching is already in progress or pending" });
        }

        return Accepted(new { message = "Reset + matching job queued" });
    }
}
