using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

public sealed record SalesSourceQuery : PagedRequest
{
    public long? CustomerId { get; init; }
    public SalesSourceType? SourceType { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record SalesSourceSummary(long Id, long CustomerId, SalesSourceType SourceType, long SourceId,
    int SourceVersion, DateOnly BusinessDate, string Currency, string BaseCurrency, decimal GrossAmount,
    decimal BaseGrossAmount, long GlJournalId, long? ReceivableId, decimal? OpenBaseAmount);

public sealed record SalesCreditAllocationRead(long Id, long CreditSourceId, long ReceivableId,
    long OriginalSourceId, long InvoiceLineId, decimal Quantity, decimal GrossAmount, decimal BaseGrossAmount);

public sealed record SalesSourceDetail(SalesSourceSummary Source, decimal NetAmount, decimal TaxAmount,
    decimal BaseNetAmount, decimal BaseTaxAmount, decimal ExchangeRate,
    IReadOnlyList<SalesCreditAllocationRead> Allocations);

/// <summary>Committed FI sales sources. The source has no branch, department or owner dimension, so the
/// granted SYS scope permits a company-wide read, as for FI GL journals.</summary>
public sealed class SalesSourceReadService(MyErpDbContext db, ICurrentCompany company,
    ICurrentAuthorization authorization)
{
    public const string ViewPermission = "AR.SALES_SOURCE.VIEW";
    public const string NotFound = "FI.SALES_SOURCE.NOT_FOUND";

    public async Task<PagedResult<SalesSourceSummary>> ListAsync(SalesSourceQuery request, CancellationToken token)
    {
        await FinanceAccess.RequireAsync(company, authorization, ViewPermission, token);
        request.EnsureValid();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            throw new RequestValidationException("keyword", "Keyword search is not supported for sales sources.");
        if (request.CustomerId is <= 0) throw new RequestValidationException("customerId", "Customer must be positive.");
        if (request.SourceType is { } type && !Enum.IsDefined(type))
            throw new RequestValidationException("sourceType", "Unknown source type.");
        if (request.From > request.To) throw new RequestValidationException("to", "End date precedes start date.");
        var sorts = SortSpec.Parse(request.Sort, ["businessDate", "id"], "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<SalesSource>().AsNoTracking();
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        if (request.SourceType is { } sourceType) query = query.Where(x => x.SourceType == sourceType);
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("id", false) => query.OrderBy(x => x.Id),
            ("id", true) => query.OrderByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new SalesSourceSummary(x.Id, x.CustomerId, x.SourceType, x.SourceId, x.SourceVersion,
                x.BusinessDate, x.Currency, x.BaseCurrency, x.GrossAmount, x.BaseGrossAmount, x.GlJournalId,
                db.Set<PostedReceivable>().Where(r => r.SourceType == "Invoice" && r.SourceId == x.SourceId &&
                    x.SourceType == SalesSourceType.Invoice).Select(r => (long?)r.Id).FirstOrDefault(),
                db.Set<PostedReceivable>().Where(r => r.SourceType == "Invoice" && r.SourceId == x.SourceId &&
                    x.SourceType == SalesSourceType.Invoice).Select(r => (decimal?)r.OpenBaseAmount).FirstOrDefault()))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<SalesSourceDetail> GetAsync(long id, CancellationToken token)
    {
        await FinanceAccess.RequireAsync(company, authorization, ViewPermission, token);
        var source = await db.Set<SalesSource>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(NotFound, "Sales source", id);
        var receivable = source.SourceType == SalesSourceType.Invoice
            ? await db.Set<PostedReceivable>().AsNoTracking().SingleOrDefaultAsync(
                x => x.SourceType == "Invoice" && x.SourceId == source.SourceId, token) : null;
        var receivableId = receivable?.Id;
        var allocations = await (from allocation in db.Set<CreditAllocation>().AsNoTracking()
            join original in db.Set<PostedReceivable>().AsNoTracking() on allocation.ReceivableId equals original.Id
            where allocation.CreditSourceId == source.Id ||
                (receivableId != null && allocation.ReceivableId == receivableId)
            orderby allocation.Id
            select new SalesCreditAllocationRead(allocation.Id, allocation.CreditSourceId, allocation.ReceivableId,
                original.SourceId, allocation.InvoiceLineId, allocation.Quantity, allocation.GrossAmount,
                allocation.BaseGrossAmount)).ToListAsync(token);
        var summary = new SalesSourceSummary(source.Id, source.CustomerId, source.SourceType, source.SourceId,
            source.SourceVersion, source.BusinessDate, source.Currency, source.BaseCurrency, source.GrossAmount,
            source.BaseGrossAmount, source.GlJournalId, receivable?.Id, receivable?.OpenBaseAmount);
        return new SalesSourceDetail(summary, source.NetAmount, source.TaxAmount, source.BaseNetAmount,
            source.BaseTaxAmount, source.ExchangeRate, allocations);
    }

    public async Task<SalesSourceDetail> GetByKeyAsync(SalesSourceType sourceType, long sourceId, CancellationToken token)
    {
        await FinanceAccess.RequireAsync(company, authorization, ViewPermission, token);
        if (!Enum.IsDefined(sourceType)) throw new RequestValidationException("sourceType", "Unknown source type.");
        if (sourceId <= 0) throw new RequestValidationException("sourceId", "Source must be positive.");
        var id = await db.Set<SalesSource>().AsNoTracking()
            .Where(x => x.SourceType == sourceType && x.SourceId == sourceId)
            .Select(x => x.Id).SingleOrDefaultAsync(token);
        if (id == 0) throw new NotFoundException(NotFound, "Sales source", sourceId);
        return await GetAsync(id, token);
    }
}
