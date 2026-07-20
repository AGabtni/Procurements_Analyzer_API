using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProcurePortal.API.Models;

[Table("company_profile")]
public class CompanyProfile
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("company_name")]
    public string CompanyName { get; set; } = string.Empty;

    [Column("province")]
    public string? Province { get; set; }

    [Column("services_description")]
    public string? ServicesDescription { get; set; }

    [Column("keywords")]
    public string[]? Keywords { get; set; }

    [Column("unspsc_codes")]
    public int[]? UnspscCodes { get; set; }

    [Column("gsin_codes")]
    public string[]? GsinCodes { get; set; }

    [Column("certifications")]
    public string[]? Certifications { get; set; }

    [Column("company_size")]
    public string? CompanySize { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("last_matched_at")]
    public DateTime? LastMatchedAt { get; set; }

    [Column("matching_status")]
    public string? MatchingStatus { get; set; }

    [Column("matching_started_at")]
    public DateTime? MatchingStartedAt { get; set; }

    public CompanyPreferences? Preferences { get; set; }

    [Column("commodity_types")]
    public string[]? CommodityTypes { get; set; }

    [Column("auto_keywords")]
    public string[]? AutoKeywords { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    [Column("industry_codes")]
    public string[]? IndustryCodes { get; set; }

    public List<CompanyMatch> Matches { get; set; } = [];
}
