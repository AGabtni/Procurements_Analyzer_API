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
        return await _db.CompanyProfiles
            .Include(p => p.Preferences)
            .Select(p => MapToDto(p))
            .ToListAsync();
    }

    public async Task<CompanyProfileDto?> GetProfileByIdAsync(int id)
    {
        var profile = await _db.CompanyProfiles
            .Include(p => p.Preferences)
            .FirstOrDefaultAsync(p => p.Id == id);

        return profile is null ? null : MapToDto(profile);
    }

    public async Task<CompanyProfileDto> CreateProfileAsync(CreateCompanyProfileRequest request)
    {
        var profile = new CompanyProfile
        {
            CompanyName = request.CompanyName,
            Industry = request.Industry,
            Province = request.Province,
            ServicesDescription = request.ServicesDescription,
            Keywords = request.Keywords,
            UnspscCodes = request.UnspscCodes,
            GsinCodes = request.GsinCodes,
            Certifications = request.Certifications,
            CompanySize = request.CompanySize,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.CompanyProfiles.Add(profile);
        await _db.SaveChangesAsync();

        if (request.Preferences is not null)
        {
            await UpsertPreferencesAsync(profile.Id, request.Preferences);
            await _db.Entry(profile).Reference(p => p.Preferences).LoadAsync();
        }

        return MapToDto(profile);
    }

    public async Task<CompanyProfileDto?> UpdateProfileAsync(int id, UpdateCompanyProfileRequest request)
    {
        var profile = await _db.CompanyProfiles
            .Include(p => p.Preferences)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (profile is null) return null;

        if (request.CompanyName is not null) profile.CompanyName = request.CompanyName;
        if (request.Industry is not null) profile.Industry = request.Industry;
        if (request.Province is not null) profile.Province = request.Province;
        if (request.ServicesDescription is not null) profile.ServicesDescription = request.ServicesDescription;
        if (request.Keywords is not null) profile.Keywords = request.Keywords;
        if (request.UnspscCodes is not null) profile.UnspscCodes = request.UnspscCodes;
        if (request.GsinCodes is not null) profile.GsinCodes = request.GsinCodes;
        if (request.Certifications is not null) profile.Certifications = request.Certifications;
        if (request.CompanySize is not null) profile.CompanySize = request.CompanySize;

        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return MapToDto(profile);
    }

    public async Task<bool> DeleteProfileAsync(int id)
    {
        var profile = await _db.CompanyProfiles.FindAsync(id);
        if (profile is null) return false;

        _db.CompanyProfiles.Remove(profile);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Preferences CRUD ──

    public async Task<CompanyPreferencesDto?> GetPreferencesAsync(int companyId)
    {
        var prefs = await _db.CompanyPreferences
            .FirstOrDefaultAsync(p => p.CompanyId == companyId);

        return prefs is null ? null : MapPrefsToDto(prefs);
    }

    public async Task<CompanyPreferencesDto> UpsertPreferencesAsync(int companyId, CompanyPreferencesRequest request)
    {
        var prefs = await _db.CompanyPreferences
            .FirstOrDefaultAsync(p => p.CompanyId == companyId);

        if (prefs is null)
        {
            prefs = new CompanyPreferences
            {
                CompanyId = companyId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.CompanyPreferences.Add(prefs);
        }

        prefs.PreferredProcCats = request.PreferredProcCats;
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
        var match = await _db.CompanyMatches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.CompanyId == companyId);

        if (match is null) return false;

        match.Status = status;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Matches read (for a company) ──

    public async Task<List<CompanyMatchDto>> GetMatchesAsync(int companyId, string? status = null, int limit = 50)
    {
        var query = _db.CompanyMatches
            .Include(m => m.Tender)
            .Where(m => m.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(m => m.Status == status);

        return await query
            .OrderByDescending(m => m.MatchScore)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(m => new CompanyMatchDto
            {
                Id = m.Id,
                TenderId = m.TenderId,
                TenderTitle = m.Tender.Title,
                BuyingOrganization = m.Tender.BuyingOrganization,
                ClosingDate = m.Tender.ClosingDate != null
                    ? DateTimeOffset.FromUnixTimeSeconds((long)m.Tender.ClosingDate.Value).UtcDateTime
                    : null,
                MatchScore = m.MatchScore,
                MatchReason = m.MatchReason,
                MatchedAt = m.MatchedAt,
                Status = m.Status,
            })
            .ToListAsync();
    }

    // ── Mapping helpers ──

    private static CompanyProfileDto MapToDto(CompanyProfile p) => new()
    {
        Id = p.Id,
        CompanyName = p.CompanyName,
        Industry = p.Industry,
        Province = p.Province,
        ServicesDescription = p.ServicesDescription,
        Keywords = p.Keywords,
        UnspscCodes = p.UnspscCodes,
        GsinCodes = p.GsinCodes,
        Certifications = p.Certifications,
        CompanySize = p.CompanySize,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        Preferences = p.Preferences is null ? null : MapPrefsToDto(p.Preferences),
    };

    private static CompanyPreferencesDto MapPrefsToDto(CompanyPreferences p) => new()
    {
        Id = p.Id,
        PreferredProcCats = p.PreferredProcCats,
        PreferredOrgs = p.PreferredOrgs,
        PreferredNtTypes = p.PreferredNtTypes,
        PreferredProvinces = p.PreferredProvinces,
        MinValue = p.MinValue,
        MaxValue = p.MaxValue,
        ExcludeKeywords = p.ExcludeKeywords,
    };
}
