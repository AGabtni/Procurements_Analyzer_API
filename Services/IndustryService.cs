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

    // Case-insensitive search on title and code prefix. Returns top `limit` results
    // with an ancestor title chain so the UI can render breadcrumbs.
    public async Task<List<IndustrySearchResultDto>> SearchAsync(string q, int limit = 50)
    {
        var lower = q.ToLowerInvariant();
        var results = await _db.Industries
            .Where(i => i.TitleEn.ToLower().Contains(lower) || i.Code.StartsWith(q))
            .OrderBy(i => i.Level)
            .ThenBy(i => i.Code)
            .Take(limit)
            .ToListAsync();

        if (results.Count == 0)
            return [];

        var ancestorCodes = results
            .SelectMany(r => GetAncestorCodes(r.Code))
            .Distinct()
            .ToList();

        var titleMap = await _db.Industries
            .Where(i => ancestorCodes.Contains(i.Code))
            .ToDictionaryAsync(i => i.Code, i => i.TitleEn);

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
