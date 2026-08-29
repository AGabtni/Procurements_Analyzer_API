using Microsoft.EntityFrameworkCore;
using ProcurePortal.API.Data;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Models;

namespace ProcurePortal.API.Services;

public class CompanyService
{
    private readonly ProcurementsDbContext _db;

    public CompanyService(ProcurementsDbContext db)
    {
        _db = db;
    }

    // ── Profile CRUD ──
    public async Task<List<CompanyProfileDto>> GetAllProfilesAsync()
    {
        var profiles = await _db.CompanyProfiles
            .Include(p => p.Preferences)
            .Include(p => p.Users)
            .ToListAsync();

        var labelMap = await BuildLabelMapAsync(
            profiles.SelectMany(p => p.IndustryCodes ?? []).Distinct().ToList());

        return profiles.Select(p => MapToDto(p, labelMap)).ToList();
    }

    public async Task<CompanyProfileDto?> GetProfileByIdAsync(int id)
    {
        var profile = await _db.CompanyProfiles
            .Include(p => p.Preferences)
            .Include(p => p.Users)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (profile is null) return null;
        var labelMap = await BuildLabelMapAsync(profile.IndustryCodes ?? []);
        return MapToDto(profile, labelMap);
    }

    public async Task<CompanyProfileDto?> GetProfileByUserIdAsync(int userId)
    {
        var profile = await _db.CompanyProfiles
            .Include(p => p.Preferences)
            .Include(p => p.Users)
            .FirstOrDefaultAsync(p => p.Users.Any(u => u.Id == userId));

        if (profile is null) return null;
        var labelMap = await BuildLabelMapAsync(profile.IndustryCodes ?? []);
        return MapToDto(profile, labelMap);
    }

    private async Task<Dictionary<string, string>> BuildLabelMapAsync(IEnumerable<string> codes)
    {
        var list = codes.ToList();
        if (list.Count == 0) return [];
        return await _db.Industries
            .Where(i => list.Contains(i.Code))
            .ToDictionaryAsync(i => i.Code, i => i.TitleEn);
    }

    public async Task<CompanyProfileDto> CreateProfileAsync(CreateCompanyProfileRequest request, int? userId = null)
    {
        var profile = new CompanyProfile
        {
            CompanyName = request.CompanyName,
            Province = request.Province,
            ServicesDescription = request.ServicesDescription,
            Keywords = request.Keywords,
            UnspscCodes = request.UnspscCodes,
            GsinCodes = request.GsinCodes,
            Certifications = request.Certifications,
            CompanySize = request.CompanySize,
            CommodityTypes = request.CommodityTypes,
            IndustryCodes = request.IndustryCodes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.CompanyProfiles.Add(profile);
        await _db.SaveChangesAsync();

        if (userId.HasValue)
        {
            var user = await _db.Users.FindAsync(userId.Value);
            if (user is not null)
            {
                user.CompanyId = profile.Id;
                await _db.SaveChangesAsync();
            }
        }

        if (request.Preferences is not null)
        {
            await UpsertPreferencesAsync(profile.Id, request.Preferences);
            await _db.Entry(profile).Reference(p => p.Preferences).LoadAsync();
        }

        await _db.Entry(profile).Collection(p => p.Users).LoadAsync();
        var labelMap = await BuildLabelMapAsync(profile.IndustryCodes ?? []);
        return MapToDto(profile, labelMap);
    }

    public async Task<CompanyProfileDto> AdminCreateProfileAsync(AdminCreateCompanyRequest request)
    {
        return await CreateProfileAsync(request, request.UserId);
    }

    public async Task<CompanyProfileDto?> UpdateProfileAsync(
        int id,
        UpdateCompanyProfileRequest request
    )
    {
        var profile = await _db.CompanyProfiles
            .Include(p => p.Preferences)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (profile is null)
            return null;

        if (request.CompanyName is not null)
            profile.CompanyName = request.CompanyName;
        if (request.Province is not null)
            profile.Province = request.Province;
        if (request.ServicesDescription is not null)
        {
            profile.ServicesDescription = request.ServicesDescription;
            profile.AutoKeywords = null; // Force re-extraction on next match
        }
        if (request.Keywords is not null)
            profile.Keywords = request.Keywords;
        if (request.UnspscCodes is not null)
            profile.UnspscCodes = request.UnspscCodes;
        if (request.GsinCodes is not null)
            profile.GsinCodes = request.GsinCodes;
        if (request.Certifications is not null)
            profile.Certifications = request.Certifications;
        if (request.CompanySize is not null)
            profile.CompanySize = request.CompanySize;

        if (request.CommodityTypes is not null)
            profile.CommodityTypes = request.CommodityTypes;

        if (request.IndustryCodes is not null)
            profile.IndustryCodes = request.IndustryCodes;

        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var labelMap = await BuildLabelMapAsync(profile.IndustryCodes ?? []);
        return MapToDto(profile, labelMap);
    }

    public async Task<(bool Success, string? Error)> DeleteProfileAsync(int id)
    {
        var profile = await _db.CompanyProfiles
            .Include(p => p.Preferences)
            .Include(p => p.Matches)
            .Include(p => p.Users)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (profile is null) return (false, "Company profile not found");
        if (profile.Users.Any()) return (false, "Dissociate all users from this company before deleting");

        if (profile.Preferences is not null) _db.CompanyPreferences.Remove(profile.Preferences);
        _db.CompanyMatches.RemoveRange(profile.Matches);
        _db.CompanyProfiles.Remove(profile);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(CompanyProfileDto? Profile, string? Error)> LinkUserAsync(int companyId, int userId)
    {
        var profile = await _db.CompanyProfiles.Include(p => p.Preferences).Include(p => p.Users)
            .FirstOrDefaultAsync(p => p.Id == companyId);
        if (profile is null) return (null, "Company profile not found");

        var user = await _db.Users.FindAsync(userId);
        if (user is null) return (null, "User not found");

        if (user.CompanyId.HasValue && user.CompanyId != companyId)
            return (null, "This user is already linked to another company");

        user.CompanyId = companyId;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _db.Entry(profile).Collection(p => p.Users).LoadAsync();
        var labelMap = await BuildLabelMapAsync(profile.IndustryCodes ?? []);
        return (MapToDto(profile, labelMap), null);
    }

    public async Task<(CompanyProfileDto? Profile, string? Error)> DissociateUserAsync(int companyId, int userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId);
        if (user is null) return (null, "User not found or not linked to this company");

        user.CompanyId = null;
        await _db.SaveChangesAsync();

        var profile = await GetProfileByIdAsync(companyId);
        return (profile, null);
    }

    // ── Preferences CRUD ──

    public async Task<CompanyPreferencesDto?> GetPreferencesAsync(int companyId)
    {
        var prefs = await _db.CompanyPreferences.FirstOrDefaultAsync(p => p.CompanyId == companyId);

        return prefs is null ? null : MapPrefsToDto(prefs);
    }

    public async Task<CompanyPreferencesDto> UpsertPreferencesAsync(
        int companyId,
        CompanyPreferencesRequest request
    )
    {
        var prefs = await _db.CompanyPreferences.FirstOrDefaultAsync(p => p.CompanyId == companyId);

        if (prefs is null)
        {
            prefs = new CompanyPreferences { CompanyId = companyId, CreatedAt = DateTime.UtcNow };
            _db.CompanyPreferences.Add(prefs);
        }

        prefs.PreferredOrgs = request.PreferredOrgs;
        prefs.PreferredNtTypes = request.PreferredNtTypes;
        prefs.PreferredProvinces = request.PreferredProvinces;
        prefs.MinValue = request.MinValue;
        prefs.MaxValue = request.MaxValue;
        prefs.ExcludeKeywords = request.ExcludeKeywords;
        prefs.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapPrefsToDto(prefs);
    }

    // ── Match status update ──
    public async Task<bool> UpdateMatchStatusAsync(int matchId, int companyId, string status)
    {
        var match = await _db.CompanyMatches.FirstOrDefaultAsync(m =>
            m.Id == matchId && m.CompanyId == companyId
        );

        if (match is null)
            return false;

        match.Status = status;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Matches read (for a company) ──
    public async Task<PagedResult<CompanyMatchDto>> GetMatchesAsync(
        int companyId,
        string? status = null,
        int page = 1,
        int pageSize = 25
    )
    {
        var query = _db.CompanyMatches.Include(m => m.Tender).Where(m => m.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(m => m.Status == status);

        var totalCount = await query.CountAsync();
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var clampedPage = Math.Max(page, 1);

        var items = await query
            .OrderByDescending(m => m.MatchScore)
            .Skip((clampedPage - 1) * clampedPageSize)
            .Take(clampedPageSize)
            .Select(m => new CompanyMatchDto
            {
                Id = m.Id,
                TenderId = m.TenderId,
                NoticeId = m.Tender.NoticeId,
                TenderTitle = m.Tender.Title,
                ProcurementCategory = m.Tender.ProcurementCategory,
                BuyingOrganization = m.Tender.BuyingOrganization,
                ClosingDate =
                    m.Tender.ClosingDate != null
                        ? DateTimeOffset
                            .FromUnixTimeSeconds((long)m.Tender.ClosingDate.Value)
                            .UtcDateTime
                        : null,
                NoticeType = m.Tender.NoticeType,
                NoticeLink = m.Tender.NoticeLink,
                MatchScore = m.MatchScore,
                MatchReason = m.MatchReason,
                MatchedAt = m.MatchedAt,
                Status = m.Status,
            })
            .ToListAsync();

        return new PagedResult<CompanyMatchDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };
    }

    public async Task<MatchStatsDto> GetMatchStatsAsync(int companyId)
    {
        var matches = await _db.CompanyMatches.Where(m => m.CompanyId == companyId).ToListAsync();

        return new MatchStatsDto
        {
            TotalMatches = matches.Count,
            NewCount = matches.Count(m => m.Status == "new"),
            ViewedCount = matches.Count(m => m.Status == "viewed"),
            SavedCount = matches.Count(m => m.Status == "saved"),
            DismissedCount = matches.Count(m => m.Status == "dismissed"),
            AverageScore =
                matches.Count > 0 ? Math.Round(matches.Average(m => m.MatchScore), 1) : 0,
            HighScoreCount = matches.Count(m => m.MatchScore >= 70),
        };
    }

    // ── Matching trigger ──

    public async Task<(bool started, int? retryAfterSeconds)> TriggerMatchAsync(int companyId, bool bypassCooldown = false)
    {
        var profile = await _db.CompanyProfiles.FindAsync(companyId);
        if (profile is null)
            return (false, null);

        // Check 24h cooldown (skip for admin)
        if (!bypassCooldown && profile.LastMatchedAt.HasValue)
        {
            var cooldownEnd = profile.LastMatchedAt.Value.AddHours(24);
            if (DateTime.UtcNow < cooldownEnd)
            {
                var remaining = (int)(cooldownEnd - DateTime.UtcNow).TotalSeconds;
                return (false, remaining);
            }
        }

        // Reject if already pending or running
        if (profile.MatchingStatus is "pending_rematch" or "pending_reset" or "running")
            return (false, null);

        profile.MatchingStatus = "pending_rematch";
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool started, int? retryAfterSeconds)> TriggerResetAsync(int companyId, bool bypassCooldown = false)
    {
        var profile = await _db.CompanyProfiles.FindAsync(companyId);
        if (profile is null)
            return (false, null);

        // Check 24h cooldown (skip for admin)
        if (!bypassCooldown && profile.LastMatchedAt.HasValue)
        {
            var cooldownEnd = profile.LastMatchedAt.Value.AddHours(24);
            if (DateTime.UtcNow < cooldownEnd)
            {
                var remaining = (int)(cooldownEnd - DateTime.UtcNow).TotalSeconds;
                return (false, remaining);
            }
        }

        // Reject if already pending or running
        if (profile.MatchingStatus is "pending_rematch" or "pending_reset" or "running")
            return (false, null);

        profile.MatchingStatus = "pending_reset";
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // ── Mapping helpers ──

    private static CompanyProfileDto MapToDto(CompanyProfile p, Dictionary<string, string> labelMap) =>
        new()
        {
            Id = p.Id,
            Users = p.Users.Select(u => new CompanyUserDto(u.Id, u.FullName, u.Email)).ToArray(),
            CompanyName = p.CompanyName,
            Province = p.Province,
            ServicesDescription = p.ServicesDescription,
            Keywords = p.Keywords,
            UnspscCodes = p.UnspscCodes,
            GsinCodes = p.GsinCodes,
            Certifications = p.Certifications,
            CompanySize = p.CompanySize,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            LastMatchedAt = p.LastMatchedAt,
            MatchingStatus = p.MatchingStatus ?? "idle",
            MatchingStartedAt = p.MatchingStartedAt,
            CommodityTypes = p.CommodityTypes ?? [],
            AutoKeywords = p.AutoKeywords,
            IndustryCodes = p.IndustryCodes ?? [],
            Industries = (p.IndustryCodes ?? [])
                .Select(c => new IndustryLabelDto { Code = c, TitleEn = labelMap.GetValueOrDefault(c, c) })
                .ToArray(),
            Preferences = p.Preferences is null ? null : MapPrefsToDto(p.Preferences),
        };

    private static CompanyPreferencesDto MapPrefsToDto(CompanyPreferences p) =>
        new()
        {
            Id = p.Id,
            PreferredOrgs = p.PreferredOrgs,
            PreferredNtTypes = p.PreferredNtTypes,
            PreferredProvinces = p.PreferredProvinces,
            MinValue = p.MinValue,
            MaxValue = p.MaxValue,
            ExcludeKeywords = p.ExcludeKeywords,
        };
}
