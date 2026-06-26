using Microsoft.AspNetCore.Mvc;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Services;

namespace ProcurePortal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TendersController : ControllerBase
{
    private readonly TenderService _tenderService;

    public TendersController(TenderService tenderService)
    {
        _tenderService = tenderService;
    }

    /// <summary>
    /// Search and filter tenders with pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TenderListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] TenderSearchParams searchParams)
    {
        var result = await _tenderService.SearchAsync(searchParams);
        return Ok(result);
    }

    /// <summary>
    /// Get a tender by its database ID, including documents.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(TenderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var tender = await _tenderService.GetByIdAsync(id);
        if (tender is null) return NotFound();
        return Ok(tender);
    }

    /// <summary>
    /// Get a tender by its notice ID (e.g. PW-24-01234567), including documents.
    /// </summary>
    [HttpGet("by-notice/{noticeId}")]
    [ProducesResponseType(typeof(TenderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByNoticeId(string noticeId)
    {
        var tender = await _tenderService.GetByNoticeIdAsync(noticeId);
        if (tender is null) return NotFound();
        return Ok(tender);
    }

    /// <summary>
    /// Download a tender document stored either as an external URL or binary content.
    /// </summary>
    [HttpGet("documents/{id:int}/download")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(int id)
    {
        var document = await _tenderService.GetDocumentByIdAsync(id);
        if (document is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(document.DocUrl))
            return Redirect(document.DocUrl);

        if (document.DocContent is { Length: > 0 })
            return File(
                document.DocContent,
                "application/octet-stream",
                document.DocTitle ?? $"document-{id}"
            );

        return NotFound();
    }

    /// <summary>
    /// Aggregate dashboard statistics (new today, closing this week, …).
    /// Computed directly in the DB — accurate regardless of result-page limits.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(TenderStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats()
    {
        var stats = await _tenderService.GetStatsAsync();
        return Ok(stats);
    }

    /// <summary>
    /// Get all distinct procurement categories.
    /// </summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _tenderService.GetCategoriesAsync();
        return Ok(categories);
    }

    /// <summary>
    /// Get all distinct notice types.
    /// </summary>
    [HttpGet("notice-types")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNoticeTypes()
    {
        var types = await _tenderService.GetNoticeTypesAsync();
        return Ok(types);
    }
}
