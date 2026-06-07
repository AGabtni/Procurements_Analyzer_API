using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProcurePortal.API.Models;

[Table("tender_documents")]
public class TenderDocument
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nt_id")]
    public string NoticeId { get; set; } = string.Empty;

    [Column("doc_title")]
    public string? DocTitle { get; set; }

    [Column("doc_url")]
    public string? DocUrl { get; set; }

    [Column("doc_lang")]
    public string? DocLanguage { get; set; }

    [Column("pub_date")]
    public float? PublicationDate { get; set; }

    [Column("doc_type")]
    public string? DocType { get; set; }

    [Column("doc_content")]
    public byte[]? DocContent { get; set; }
}
