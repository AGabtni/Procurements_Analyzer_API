using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProcurePortal.API.Models;

[Table("company_commodity_type")]
[PrimaryKey(nameof(CompanyId), nameof(CommodityCode))]
public class CompanyCommodityType
{
    [Column("company_id")]
    public int CompanyId { get; set; }

    [Column("commodity_code")]
    public string CommodityCode { get; set; } = string.Empty;

    [ForeignKey("CompanyId")]
    public CompanyProfile Company { get; set; } = null!;
}