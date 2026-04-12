using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProcurePortal.API.Models;

[Table("tender_notice")]
public class TenderNotice
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nt_id")]
    public string? NoticeId { get; set; }

    [Column("nt_title")]
    public string? Title { get; set; }

    [Column("proc_cat")]
    public string? ProcurementCategory { get; set; }

    [Column("buying_org")]
    public string? BuyingOrganization { get; set; }

    [Column("pub_date")]
    public float? PublicationDate { get; set; }

    [Column("closing_date")]
    public float? ClosingDate { get; set; }

    [Column("nt_type")]
    public string? NoticeType { get; set; }

    [Column("proc_method")]
    public string? ProcurementMethod { get; set; }

    [Column("sel_criteria")]
    public string? SelectionCriteria { get; set; }

    [Column("unspsc")]
    public int[]? Unspsc { get; set; }

    [Column("gsin")]
    public string[]? Gsin { get; set; }

    [Column("nt_link")]
    public string? NoticeLink { get; set; }

    [Column("ext_link")]
    public string? ExternalLink { get; set; }

    [Column("has_documents")]
    public bool? HasDocuments { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("contact_name")]
    public string? ContactName { get; set; }

    [Column("contact_email")]
    public string? ContactEmail { get; set; }

    [Column("contact_phone")]
    public string? ContactPhone { get; set; }
}
