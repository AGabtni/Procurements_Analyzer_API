using System.ComponentModel.DataAnnotations;

namespace ProcurePortal.API.DTOs;

// ── Response DTOs ──

public class CompanyProfileDto
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? Province { get; set; }
    public string? ServicesDescription { get; set; }
    public string[]? Keywords { get; set; }
    public int[]? UnspscCodes { get; set; }
    public string[]? GsinCodes { get; set; }
    public string[]? Certifications { get; set; }
    public string? CompanySize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public CompanyPreferencesDto? Preferences { get; set; }
}

public class CompanyPreferencesDto
{
    public int Id { get; set; }
    public string[]? PreferredProcCats { get; set; }
    public string[]? PreferredOrgs { get; set; }
    public string[]? PreferredNtTypes { get; set; }
    public string[]? PreferredProvinces { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string[]? ExcludeKeywords { get; set; }
}

public class CompanyMatchDto
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public string? TenderTitle { get; set; }
    public string? BuyingOrganization { get; set; }
    public DateTime? ClosingDate { get; set; }
    public int MatchScore { get; set; }
    public string? MatchReason { get; set; }
    public DateTime MatchedAt { get; set; }
    public string Status { get; set; } = "new";
}

// ── Request DTOs ──

public class CreateCompanyProfileRequest
{
    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Industry { get; set; }

    [MaxLength(50)]
    public string? Province { get; set; }

    [MaxLength(2000)]
    public string? ServicesDescription { get; set; }

    public string[]? Keywords { get; set; }
    public int[]? UnspscCodes { get; set; }
    public string[]? GsinCodes { get; set; }
    public string[]? Certifications { get; set; }

    [MaxLength(50)]
    public string? CompanySize { get; set; }

    public CompanyPreferencesRequest? Preferences { get; set; }
}

public class UpdateCompanyProfileRequest
{
    [MaxLength(200)]
    public string? CompanyName { get; set; }

    [MaxLength(100)]
    public string? Industry { get; set; }

    [MaxLength(50)]
    public string? Province { get; set; }

    [MaxLength(2000)]
    public string? ServicesDescription { get; set; }

    public string[]? Keywords { get; set; }
    public int[]? UnspscCodes { get; set; }
    public string[]? GsinCodes { get; set; }
    public string[]? Certifications { get; set; }

    [MaxLength(50)]
    public string? CompanySize { get; set; }
}

public class CompanyPreferencesRequest
{
    public string[]? PreferredProcCats { get; set; }
    public string[]? PreferredOrgs { get; set; }
    public string[]? PreferredNtTypes { get; set; }
    public string[]? PreferredProvinces { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string[]? ExcludeKeywords { get; set; }
}

public class UpdateMatchStatusRequest
{
    [Required]
    [RegularExpression("^(new|viewed|saved|dismissed)$", ErrorMessage = "Status must be: new, viewed, saved, or dismissed")]
    public string Status { get; set; } = string.Empty;
}
