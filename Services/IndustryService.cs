using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProcurePortal.API.Data;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Models;

namespace ProcurePortal.API.Services;

public class IndustryService
{
    private readonly ProcurementsDbContext _db;

    public IndustryService(ProcurementsDbContext db)
    {
        _db = db;
    }

    // Returns direct children of parentCode, or root sectors when parentCode is null.
    public async Task<List<IndustryDto>> GetChildrenAsync(string? parentCode)
    {
        var children = await _db.Industries
            .Where(i => i.ParentCode == parentCode)
            .OrderBy(i => i.Code)
            .ToListAsync();

        if (children.Count == 0)
            return [];

        var childCodes = children.Select(c => c.Code).ToList();
        var codesWithChildren = (await _db.Industries
            .Where(i => i.ParentCode != null && childCodes.Contains(i.ParentCode))
            .Select(i => i.ParentCode!)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        return children.Select(c => ToDto(c, codesWithChildren.Contains(c.Code))).ToList();
    }

    // Case-insensitive search on title (both languages) and code prefix. Returns
    // top `limit` results with an ancestor title chain so the UI can render
    // breadcrumbs. When `locale` is French, current-locale title matches are ranked
    // first and breadcrumbs resolve to French titles (falling back to English).
    public async Task<List<IndustrySearchResultDto>> SearchAsync(string q, string? locale = null, int limit = 50)
    {
        var fr = IsFrench(locale);
        var lower = q.ToLowerInvariant();

        var matches = await _db.Industries
            .Where(i => i.TitleEn.ToLower().Contains(lower)
                || (i.TitleFr != null && i.TitleFr.ToLower().Contains(lower))
                || i.Code.StartsWith(q))
            .ToListAsync();

        if (matches.Count == 0)
            return [];

        // Prefer rows whose current-locale title matched, then shallower/lower codes.
        var results = matches
            .OrderByDescending(i => LocaleTitleMatches(i, lower, fr))
            .ThenBy(i => i.Level)
            .ThenBy(i => i.Code)
            .Take(limit)
            .ToList();

        var ancestorCodes = results
            .SelectMany(r => GetAncestorCodes(r.Code))
            .Distinct()
            .ToList();

        var titleMap = (await _db.Industries
            .Where(i => ancestorCodes.Contains(i.Code))
            .Select(i => new { i.Code, i.TitleEn, i.TitleFr })
            .ToListAsync())
            .ToDictionary(i => i.Code, i => fr ? (i.TitleFr ?? i.TitleEn) : i.TitleEn);

        return results.Select(r => new IndustrySearchResultDto
        {
            Code = r.Code,
            TitleEn = r.TitleEn,
            TitleFr = r.TitleFr,
            Level = r.Level,
            ParentCode = r.ParentCode,
            HasChildren = false,
            Elements = ParseElements(r.Elements),
            AncestorTitles = GetAncestorCodes(r.Code)
                .Where(c => titleMap.ContainsKey(c))
                .Select(c => titleMap[c])
                .ToArray(),
        }).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsFrench(string? locale) =>
        locale is not null && locale.StartsWith("fr", StringComparison.OrdinalIgnoreCase);

    // True when the title in the requested locale contains the query term.
    private static bool LocaleTitleMatches(Industry i, string lowerQuery, bool fr) =>
        fr
            ? i.TitleFr is not null && i.TitleFr.ToLowerInvariant().Contains(lowerQuery)
            : i.TitleEn.ToLowerInvariant().Contains(lowerQuery);

    private static IndustryDto ToDto(Industry i, bool hasChildren) => new()
    {
        Code = i.Code,
        TitleEn = i.TitleEn,
        TitleFr = i.TitleFr,
        Level = i.Level,
        ParentCode = i.ParentCode,
        HasChildren = hasChildren,
        Elements = ParseElements(i.Elements),
    };

    private static IndustryElementsDto? ParseElements(JsonDocument? doc)
    {
        if (doc is null) return null;
        try
        {
            return JsonSerializer.Deserialize<IndustryElementsDto>(
                doc.RootElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    // Returns ancestor codes from root down to direct parent.
    // e.g. "236110" → ["23", "236", "2361", "23611"]
    private static IEnumerable<string> GetAncestorCodes(string code)
    {
        for (var len = 2; len < code.Length; len++)
            yield return code[..len];
    }
}
