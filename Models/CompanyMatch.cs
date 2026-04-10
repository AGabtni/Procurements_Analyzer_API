using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProcurePortal.API.Models;

[Table("company_matches")]
public class CompanyMatch
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_id")]
    public int CompanyId { get; set; }

    [Column("tender_id")]
    public int TenderId { get; set; }

    [Column("match_score")]
    public int MatchScore { get; set; }

    [Column("match_reason")]
    public string? MatchReason { get; set; }

    [Column("matched_at")]
    public DateTime MatchedAt { get; set; }

    [Column("status")]
    public string Status { get; set; } = "new";

    [ForeignKey("CompanyId")]
    public CompanyProfile Company { get; set; } = null!;

    [ForeignKey("TenderId")]
    public TenderNotice Tender { get; set; } = null!;
}
