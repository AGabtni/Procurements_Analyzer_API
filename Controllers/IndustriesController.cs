using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Services;

namespace ProcurePortal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IndustriesController : ControllerBase
{
    private readonly IndustryService _industryService;

    public IndustriesController(IndustryService industryService)
    {
        _industryService = industryService;
    }

    // GET /api/industries          → root sectors (parent_code IS NULL)
    // GET /api/industries?parent=23 → children of sector 23
    [HttpGet]
    [ProducesResponseType(typeof(List<IndustryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChildren([FromQuery] string? parent)
    {
        var children = await _industryService.GetChildrenAsync(parent);
        return Ok(children);
    }

    // GET /api/industries/search?q=plumb
    [HttpGet("search")]
    [ProducesResponseType(typeof(List<IndustrySearchResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] string? q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<IndustrySearchResultDto>());

        var results = await _industryService.SearchAsync(q.Trim());
        return Ok(results);
    }
}
