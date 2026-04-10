using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProcurePortal.API.Models;

[Table("company_preferences")]
public class CompanyPreferences
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_id")]
    public int CompanyId { get; set; }

    [Column("preferred_proc_cats")]
    public string[]? PreferredProcCats { get; set; }

    [Column("preferred_orgs")]
    public string[]? PreferredOrgs { get; set; }

    [Column("preferred_nt_types")]
    public string[]? PreferredNtTypes { get; set; }

    [Column("preferred_provinces")]
    public string[]? PreferredProvinces { get; set; }

    [Column("min_value")]
    public decimal? MinValue { get; set; }

    [Column("max_value")]
    public decimal? MaxValue { get; set; }

    [Column("exclude_keywords")]
    public string[]? ExcludeKeywords { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("CompanyId")]
    public CompanyProfile Company { get; set; } = null!;
}
