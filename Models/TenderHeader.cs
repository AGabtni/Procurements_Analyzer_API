using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProcurePortal.API.Models;

[Table("tender_header")]
public class TenderHeader
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nt_id")]
    public string? NoticeId { get; set; }

    [Column("nt_title")]
    public string? Title { get; set; }

    [Column("pub_date")]
    public float? PublicationDate { get; set; }

    [Column("closing_date")]
    public float? ClosingDate { get; set; }

    [Column("buying_org")]
    public string? BuyingOrganization { get; set; }

    [Column("nt_type")]
    public string? NoticeType { get; set; }
}
