namespace ProcurePortal.API.DTOs;

public class TenderListDto
{
    public int Id { get; set; }
    public string? NoticeId { get; set; }
    public string? Title { get; set; }
    public string? ProcurementCategory { get; set; }
    public string? BuyingOrganization { get; set; }
    public DateTime? PublicationDate { get; set; }
    public DateTime? ClosingDate { get; set; }
    public string? NoticeType { get; set; }
    public string? ProcurementMethod { get; set; }
    public bool? HasDocuments { get; set; }
}

public class TenderDetailDto
{
    public int Id { get; set; }
    public string? NoticeId { get; set; }
    public string? Title { get; set; }
    public string? ProcurementCategory { get; set; }
    public string? BuyingOrganization { get; set; }
    public DateTime? PublicationDate { get; set; }
    public DateTime? ClosingDate { get; set; }
    public string? NoticeType { get; set; }
    public string? ProcurementMethod { get; set; }
    public string? SelectionCriteria { get; set; }
    public int[]? Unspsc { get; set; }
    public string[]? Gsin { get; set; }
    public string? NoticeLink { get; set; }
    public string? ExternalLink { get; set; }
    public bool? HasDocuments { get; set; }
    public string? Description { get; set; }
    public string? DescriptionMd { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? RegionOfDelivery { get; set; }
    public string? RegionOfOpportunity { get; set; }
    public List<DocumentDto> Documents { get; set; } = [];
}

public class DocumentDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Language { get; set; }
    public string? Type { get; set; }
}

public class TenderSearchParams
{
    public string? Keyword { get; set; }
    public string? Category { get; set; }
    public string? Organization { get; set; }
    public string? NoticeType { get; set; }
    public bool? OpenOnly { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "closing_date";
    public bool SortDesc { get; set; } = false;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// Aggregate dashboard statistics computed directly in the DB.
/// All fields are nullable so future stats can be added without breaking old clients.
/// </summary>
public class TenderStatsDto
{
    public int? NewToday { get; set; }
    public int? ClosingThisWeek { get; set; }
    // Future: OpenCount, NewThisWeek, ClosingToday, ...
}
