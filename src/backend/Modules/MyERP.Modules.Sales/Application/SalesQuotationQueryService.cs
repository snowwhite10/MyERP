using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesQuotationLineDetail(long Id, int LineNo, long ItemId, string ItemCode,
    string ItemName, decimal Quantity, decimal UnitPrice, decimal DiscountRate,
    long TaxRateId, decimal TaxRatePercent, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

public sealed record SalesQuotationDetail(long Id, string DocumentNo, long CustomerId,
    string CustomerCode, string CustomerName, DateOnly BusinessDate, DateOnly ExpiryDate,
    string CurrencyCode, string BaseCurrencyCode, decimal ExchangeRate,
    SalesQuotationStatus Status, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross,
    string? Remark, int Version, IReadOnlyList<SalesQuotationLineDetail> Lines,
    long? ApprovalInstanceId = null, bool IsExpired = false,
    IReadOnlyList<ApprovalDocumentHistory>? ApprovalHistory = null);

public sealed record SalesQuotationQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public SalesQuotationStatus? Status { get; init; }
    public long? CustomerId { get; init; }
}

public sealed record SalesQuotationSummary(long Id, string DocumentNo, long CustomerId,
    string CustomerName, DateOnly BusinessDate, DateOnly ExpiryDate,
    SalesQuotationStatus Status, decimal OriginalGross, int Version);

/// <summary>Reads a quotation after applying the exact VIEW permission and data scope.</summary>
public sealed class SalesQuotationQueryService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, IClock clock, IApprovalHistoryReader history)
{
    public async Task<PagedResult<SalesQuotationSummary>> ListAsync(SalesQuotationQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        company.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.CustomerId is <= 0)
            throw new RequestValidationException("customerId", "Customer must be positive.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate", "customerId"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.VIEW", token);
        var query = scope.Apply(db.Set<SalesQuotation>().AsNoTracking());
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.CustomerCode.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } state) query = query.Where(x => x.Status == state);
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            ("customerId", false) => query.OrderBy(x => x.CustomerId).ThenBy(x => x.Id),
            ("customerId", true) => query.OrderByDescending(x => x.CustomerId).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new SalesQuotationSummary(x.Id, x.DocumentNo, x.CustomerId,
                x.CustomerName, x.BusinessDate, x.ExpiryDate, x.Status, x.OriginalGross, x.Version))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<SalesQuotationDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.VIEW", token);
        var saved = await scope.Apply(db.Set<SalesQuotation>().AsNoTracking())
            .Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("SAL.QUOTATION.NOT_FOUND", "Sales quotation", id);
        return Map(saved) with
        {
            IsExpired = SalesQuotationLifecycle.IsExpired(saved.ExpiryDate, clock),
            ApprovalHistory = await history.GetHistoryAsync("SAL.QUOTATION", saved.Id, token)
        };
    }

    public static SalesQuotationDetail Map(SalesQuotation saved) => new(
        saved.Id, saved.DocumentNo, saved.CustomerId, saved.CustomerCode, saved.CustomerName,
        saved.BusinessDate, saved.ExpiryDate, saved.CurrencyCode, saved.BaseCurrencyCode,
        saved.ExchangeRate, saved.Status, saved.OriginalNet, saved.OriginalTax,
        saved.OriginalGross, saved.BaseNet, saved.BaseTax, saved.BaseGross,
        saved.Remark, saved.Version,
        [.. saved.Lines.OrderBy(x => x.LineNo).Select(x => new SalesQuotationLineDetail(
            x.Id, x.LineNo, x.ItemId, x.ItemCode, x.ItemName, x.Quantity, x.UnitPrice,
            x.DiscountRate, x.TaxRateId, x.TaxRatePercent, x.OriginalNet, x.OriginalTax,
            x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross))], saved.ApprovalInstanceId);
}
