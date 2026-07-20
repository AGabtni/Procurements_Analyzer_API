using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace ProcurePortal.API.Models;

[Table("industries")]
public class Industry
{
    [Key]
    [Column("code")]
    public string Code { get; set; } = string.Empty;

    [Column("title_en")]
    public string TitleEn { get; set; } = string.Empty;

    [Column("title_fr")]
    public string? TitleFr { get; set; }

    [Column("level")]
    public short Level { get; set; }

    [Column("parent_code")]
    public string? ParentCode { get; set; }

    [Column("elements", TypeName = "jsonb")]
    public JsonDocument? Elements { get; set; }

    public Industry? Parent { get; set; }
    public List<Industry> Children { get; set; } = [];
}
