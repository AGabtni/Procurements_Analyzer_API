using Microsoft.EntityFrameworkCore;
using ProcurePortal.API.Data;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Models;

namespace ProcurePortal.API.Services;

public class TenderService
{
    private readonly ProcurementsDbContext _db;

    public TenderService(ProcurementsDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<TenderListDto>> SearchAsync(TenderSearchParams searchParams)
    {
        var query = _db.TenderNotices.AsQueryable();

        // Filter: open tenders only (closing_date > now)
        if (searchParams.OpenOnly == true)
        {
            var nowTimestamp = (float)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            query = query.Where(t => t.ClosingDate > nowTimestamp);
        }

        // Filter: keyword search (title or organization)
        if (!string.IsNullOrWhiteSpace(searchParams.Keyword))
        {
            var kw = searchParams.Keyword.ToLower();
            query = query.Where(t =>
                (t.Title != null && t.Title.ToLower().Contains(kw))
                || (t.BuyingOrganization != null && t.BuyingOrganization.ToLower().Contains(kw))
                || (t.NoticeId != null && t.NoticeId.ToLower().Contains(kw))
            );
        }

        // Filter: category
        if (!string.IsNullOrWhiteSpace(searchParams.Category))
        {
            query = query.Where(t =>
                t.ProcurementCategory != null
                && t.ProcurementCategory.ToLower().Contains(searchParams.Category.ToLower())
            );
        }

        // Filter: organization
        if (!string.IsNullOrWhiteSpace(searchParams.Organization))
        {
            query = query.Where(t =>
                t.BuyingOrganization != null
                && t.BuyingOrganization.ToLower().Contains(searchParams.Organization.ToLower())
            );
        }

        // Filter: notice type
        if (!string.IsNullOrWhiteSpace(searchParams.NoticeType))
        {
            query = query.Where(t =>
                t.NoticeType != null
                && t.NoticeType.ToLower().Contains(searchParams.NoticeType.ToLower())
            );
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Sort
        query = searchParams.SortBy?.ToLower() switch
        {
            "title" => searchParams.SortDesc
                ? query.OrderByDescending(t => t.Title)
                : query.OrderBy(t => t.Title),
            "pub_date" => searchParams.SortDesc
                ? query.OrderByDescending(t => t.PublicationDate)
                : query.OrderBy(t => t.PublicationDate),
            "organization" => searchParams.SortDesc
                ? query.OrderByDescending(t => t.BuyingOrganization!.ToLower())
                : query.OrderBy(t => t.BuyingOrganization!.ToLower()),
            "notice_type" => searchParams.SortDesc
                ? query.OrderByDescending(t => t.NoticeType)
                : query.OrderBy(t => t.NoticeType),
            _ => searchParams.SortDesc
                ? query.OrderByDescending(t => t.ClosingDate)
                : query.OrderBy(t => t.ClosingDate),
        };

        // Paginate
        var pageSize = Math.Clamp(searchParams.PageSize, 1, 100);
        var page = Math.Max(searchParams.Page, 1);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TenderListDto
            {
                Id = t.Id,
                NoticeId = t.NoticeId,
                Title = t.Title,
                ProcurementCategory = t.ProcurementCategory,
                BuyingOrganization = t.BuyingOrganization,
                PublicationDate = TimestampToDateTime(t.PublicationDate),
                ClosingDate = TimestampToDateTime(t.ClosingDate),
                NoticeType = t.NoticeType,
                ProcurementMethod = t.ProcurementMethod,
                HasDocuments = t.HasDocuments,
            })
            .ToListAsync();

        return new PagedResult<TenderListDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<TenderDetailDto?> GetByIdAsync(int id)
    {
        var tender = await _db.TenderNotices.FindAsync(id);
        if (tender is null)
            return null;

        var documents = await _db
            .TenderDocuments.Where(d => d.NoticeId == tender.NoticeId)
            .Select(d => new DocumentDto
            {
                Id = d.Id,
                Title = d.DocTitle,
                Url =
                    !string.IsNullOrWhiteSpace(d.DocUrl) ? d.DocUrl
                    : d.DocContent != null ? $"/api/tenders/documents/{d.Id}/download"
                    : null,
                Language = d.DocLanguage,
                Type = d.DocType,
            })
            .ToListAsync();

        return new TenderDetailDto
        {
            Id = tender.Id,
            NoticeId = tender.NoticeId,
            Title = tender.Title,
            ProcurementCategory = tender.ProcurementCategory,
            BuyingOrganization = tender.BuyingOrganization,
            PublicationDate = TimestampToDateTime(tender.PublicationDate),
            ClosingDate = TimestampToDateTime(tender.ClosingDate),
            NoticeType = tender.NoticeType,
            ProcurementMethod = tender.ProcurementMethod,
            SelectionCriteria = tender.SelectionCriteria,
            Unspsc = tender.Unspsc,
            Gsin = tender.Gsin,
            NoticeLink = tender.NoticeLink,
            ExternalLink = tender.ExternalLink,
            HasDocuments = tender.HasDocuments,
            Description = tender.Description,
            ContactName = tender.ContactName,
            ContactEmail = tender.ContactEmail,
            ContactPhone = tender.ContactPhone,
            RegionOfDelivery = tender.RegionOfDelivery,
            RegionOfOpportunity = tender.RegionOfOpportunity,
            Documents = documents,
        };
    }

    public async Task<TenderDetailDto?> GetByNoticeIdAsync(string noticeId)
    {
        var tender = await _db.TenderNotices.FirstOrDefaultAsync(t => t.NoticeId == noticeId);

        if (tender is null)
            return null;
        return await GetByIdAsync(tender.Id);
    }

    public async Task<TenderDocument?> GetDocumentByIdAsync(int id)
    {
        return await _db.TenderDocuments.FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        return await _db
            .TenderNotices.Where(t => t.ProcurementCategory != null)
            .Select(t => t.ProcurementCategory!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    public async Task<List<string>> GetNoticeTypesAsync()
    {
        return await _db
            .TenderNotices.Where(t => t.NoticeType != null)
            .Select(t => t.NoticeType!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
    }

    public async Task<TenderStatsDto> GetStatsAsync()
    {
        var newToday = await _db
            .Database.SqlQueryRaw<int>(
                @"
            SELECT count(*)::int AS ""Value""
            FROM tender_notice
            WHERE to_timestamp(pub_date)::date = CURRENT_DATE"
            )
            .FirstAsync();

        var closingThisWeek = await _db
            .Database.SqlQueryRaw<int>(
                @"
            SELECT count(*)::int AS ""Value""
            FROM tender_notice
            WHERE to_timestamp(closing_date) >= now()
              AND to_timestamp(closing_date) <  now() + interval '7 days'"
            )
            .FirstAsync();

        return new TenderStatsDto { NewToday = newToday, ClosingThisWeek = closingThisWeek };
    }

    private static DateTime? TimestampToDateTime(float? timestamp)
    {
        if (timestamp is null or 0)
            return null;
        return DateTimeOffset.FromUnixTimeSeconds((long)timestamp.Value).UtcDateTime;
    }
}
