using Microsoft.EntityFrameworkCore;
using ProcurePortal.API.Data;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Models;

namespace ProcurePortal.API.Services;

public class CompanyService
{
    private readonly ProcurementsDbContext _db;
    private readonly int _trialDays;

    public CompanyService(ProcurementsDbContext db, IConfiguration config)
    {
        _db = db;
        _trialDays = int.TryParse(config["App:TrialDays"], out var d) ? d : 14;
    }

    // ── Subscription access ──

    /// Effective status, computing trial expiry on read (no cron needed).
    public static string EffectiveStatus(CompanyProfile c)
    {
        if (c.SubscriptionStatus == "active") return "active";
        if (c.SubscriptionStatus == "expired") return "expired";
        if (c.TrialEndsAt.HasValue && c.TrialEndsAt.Value < DateTime.UtcNow) return "expired";
        return "trialing";
    }

    public record MatchAccess(int CompanyId, string Status, bool CanSeeFull, string Locale);

    /// Resolves whether a user may see full match payloads:
    /// trialing → yes; active → only seated users; expired → counts only.
    public async Task<MatchAccess?> GetMatchAccessAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.CompanyProfile is null) return null;

        var status = EffectiveStatus(user.CompanyProfile);
        var canSeeFull = status switch
        {
            "trialing" => true,
            "active" => user.HasSeat,
            _ => false,
        };
        // Locale (UI language) drives on-screen match-reason language; comms_locale
        // is for emails only.
        return new MatchAccess(user.CompanyProfile.Id, status, canSeeFull, user.Locale);
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
        // Trial clock is company-level: starts at company creation + TrialDays.
        var creator = userId.HasValue ? await _db.Users.FindAsync(userId.Value) : null;

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
            SubscriptionStatus = "trialing",
            TrialEndsAt = DateTime.UtcNow.AddDays(_trialDays),
        };

        _db.CompanyProfiles.Add(profile);
        await _db.SaveChangesAsync();

        if (creator is not null)
        {
            creator.CompanyId = profile.Id;
            await _db.SaveChangesAsync();
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

        if (status == "viewed" && match.ViewedAt is null)
            match.ViewedAt = DateTime.UtcNow;

        match.Status = status;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Matches read (for a company) ──
    public async Task<PagedResult<CompanyMatchDto>> GetMatchesAsync(
        int companyId,
        string[]? statuses = null,
        string? search = null,
        string[]? organizations = null,
        string[]? noticeTypes = null,
        int page = 1,
        int pageSize = 25,
        string? displayLocale = null,
        bool includeDescription = false,
        string? sortBy = null,
        string? sortDir = null,
        bool expiredOnly = false,
        bool openedOnly = false,
        string[]? provinces = null
    )
    {
        var wantFr = displayLocale == "fr-CA";
        var unixNow = (float)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var query = _db.CompanyMatches.Include(m => m.Tender).Where(m => m.CompanyId == companyId);

        // Scope by expiry before any other filter
        if (expiredOnly)
            query = query.Where(m => m.Tender.ClosingDate != null && m.Tender.ClosingDate < unixNow);
        else
            query = query.Where(m => m.Tender.ClosingDate == null || m.Tender.ClosingDate >= unixNow);

        if (openedOnly)
            query = query.Where(m => m.ViewedAt != null);

        if (provinces is { Length: > 0 })
            query = query.Where(m => m.Tender.Province != null && provinces.Contains(m.Tender.Province));

        if (statuses is { Length: > 0 })
        {
            var hasNew = statuses.Contains("new");
            var others = statuses.Where(s => s != "new").ToArray();

            if (hasNew && others.Length > 0)
                // "new" = unviewed (excl. dismissed) combined with explicit other statuses
                query = query.Where(m =>
                    (m.ViewedAt == null && m.Status != "dismissed") || others.Contains(m.Status));
            else if (hasNew)
                query = query.Where(m => m.ViewedAt == null && m.Status != "dismissed");
            else
                query = query.Where(m => others.Contains(m.Status));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(m =>
                (m.Tender.Title != null && m.Tender.Title.ToLower().Contains(s)) ||
                (m.Tender.NoticeId != null && m.Tender.NoticeId.ToLower().Contains(s)));
        }

        if (organizations is { Length: > 0 })
            query = query.Where(m => m.Tender.BuyingOrganization != null && organizations.Contains(m.Tender.BuyingOrganization));

        if (noticeTypes is { Length: > 0 })
            query = query.Where(m => m.Tender.NoticeType != null && noticeTypes.Contains(m.Tender.NoticeType));

        var totalCount = await query.CountAsync();
        var clampedPageSize = Math.Clamp(pageSize, 1, 1000);
        var clampedPage = Math.Max(page, 1);

        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<CompanyMatch> ordered = sortBy switch
        {
            "matchedAt"    => asc ? query.OrderBy(m => m.MatchedAt)           : query.OrderByDescending(m => m.MatchedAt),
            "closingDate"  => asc ? query.OrderBy(m => m.Tender.ClosingDate)  : query.OrderByDescending(m => m.Tender.ClosingDate),
            "organization" => asc ? query.OrderBy(m => m.Tender.BuyingOrganization) : query.OrderByDescending(m => m.Tender.BuyingOrganization),
            _              => asc ? query.OrderBy(m => m.MatchScore)          : query.OrderByDescending(m => m.MatchScore),
        };

        var items = await ordered
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
                // Resolve to the reader's language: fr → en → legacy match_reason.
                MatchReason =
                    (wantFr && m.MatchReasonFr != null && m.MatchReasonFr != "")
                        ? m.MatchReasonFr
                        : (m.MatchReasonEn ?? m.MatchReason),
                MatchReasonEn = m.MatchReasonEn ?? m.MatchReason,
                MatchReasonFr = m.MatchReasonFr,
                TenderDescription = includeDescription ? (m.Tender.DescriptionMd ?? m.Tender.Description) : null,
                MatchedAt = m.MatchedAt,
                ViewedAt = m.ViewedAt,
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
        var unixNow = (float)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var matches = await _db.CompanyMatches
            .Include(m => m.Tender)
            .Where(m => m.CompanyId == companyId)
            .ToListAsync();

        var active  = matches.Where(m => m.Tender?.ClosingDate == null || m.Tender.ClosingDate >= unixNow).ToList();
        var expired = matches.Where(m => m.Tender?.ClosingDate != null && m.Tender.ClosingDate < unixNow).ToList();

        return new MatchStatsDto
        {
            TotalMatches   = active.Count,
            ExpiredCount   = expired.Count,
            NewCount       = active.Count(m => m.ViewedAt == null && m.Status != "dismissed"),
            ViewedCount    = active.Count(m => m.ViewedAt != null),
            SavedCount     = active.Count(m => m.Status == "saved"),
            DismissedCount = active.Count(m => m.Status == "dismissed"),
            AverageScore   = active.Count > 0 ? Math.Round(active.Average(m => m.MatchScore), 1) : 0,
            HighScoreCount = active.Count(m => m.MatchScore >= 70),
        };
    }

    public async Task<MatchFiltersDto> GetMatchFiltersAsync(int companyId)
    {
        var base_q = _db.CompanyMatches.Include(m => m.Tender).Where(m => m.CompanyId == companyId);

        var orgs = await base_q
            .Where(m => m.Tender.BuyingOrganization != null)
            .Select(m => m.Tender.BuyingOrganization!)
            .Distinct().OrderBy(o => o).ToListAsync();

        var types = await base_q
            .Where(m => m.Tender.NoticeType != null)
            .Select(m => m.Tender.NoticeType!)
            .Distinct().OrderBy(t => t).ToListAsync();

        var provinces = await base_q
            .Where(m => m.Tender.Province != null)
            .Select(m => m.Tender.Province!)
            .Distinct().OrderBy(p => p).ToListAsync();

        return new MatchFiltersDto(orgs.ToArray(), types.ToArray(), provinces.ToArray());
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
            Users = p.Users.Select(u => new CompanyUserDto(u.Id, u.FullName, u.Email, u.HasSeat)).ToArray(),
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
            SubscriptionStatus = EffectiveStatus(p),
            TrialEndsAt = p.TrialEndsAt,
            MaxSeats = p.MaxSeats,
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
