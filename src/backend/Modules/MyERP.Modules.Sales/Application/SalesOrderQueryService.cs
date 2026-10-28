using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesOrderLineDetail(long Id, int LineNo, long ItemId, string ItemCode,
    string ItemName, decimal Quantity, decimal UnitPrice, decimal DiscountRate,
    long TaxRateId, decimal TaxRatePercent, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

public sealed record SalesOrderDetail(long Id, string DocumentNo, long CustomerId,
    string CustomerCode, string CustomerName, DateOnly BusinessDate, string CurrencyCode,
    string BaseCurrencyCode, decimal ExchangeRate, SalesOrderStatus Status,
    long? SourceQuotationId, decimal OriginalNet, decimal OriginalTax, decimal OriginalGross,
    decimal BaseNet, decimal BaseTax, decimal BaseGross, string? Remark, int Version,
    IReadOnlyList<SalesOrderLineDetail> Lines);

public sealed record SalesOrderQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public SalesOrderStatus? Status { get; init; }
    public long? CustomerId { get; init; }
}

public sealed record SalesOrderSummary(long Id, string DocumentNo, long CustomerId,
    string CustomerName, DateOnly BusinessDate, SalesOrderStatus Status,
    decimal OriginalGross, int Version);

/// <summary>Reads saved orders only after the exact VIEW action and its SYS data scope are applied.</summary>
public sealed class SalesOrderQueryService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company)
{
    public async Task<PagedResult<SalesOrderSummary>> ListAsync(SalesOrderQuery request,
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
        var scope = await access.RequireScopeAsync("SAL.ORDER.VIEW", token);
        var query = scope.Apply(db.Set<SalesOrder>().AsNoTracking());
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
            .Select(x => new SalesOrderSummary(x.Id, x.DocumentNo, x.CustomerId,
                x.CustomerName, x.BusinessDate, x.Status, x.OriginalGross, x.Version)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<SalesOrderDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var scope = await access.RequireScopeAsync("SAL.ORDER.VIEW", token);
        var order = await scope.Apply(db.Set<SalesOrder>().AsNoTracking())
            .Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", id);
        return Map(order);
    }

    public static SalesOrderDetail Map(SalesOrder order) => new(order.Id, order.DocumentNo,
        order.CustomerId, order.CustomerCode, order.CustomerName, order.BusinessDate,
        order.CurrencyCode, order.BaseCurrencyCode, order.ExchangeRate, order.Status,
        order.SourceQuotationId, order.OriginalNet, order.OriginalTax, order.OriginalGross,
        order.BaseNet, order.BaseTax, order.BaseGross, order.Remark, order.Version,
        [.. order.Lines.OrderBy(x => x.LineNo).Select(x => new SalesOrderLineDetail(
            x.Id, x.LineNo, x.ItemId, x.ItemCode, x.ItemName, x.Quantity, x.UnitPrice,
            x.DiscountRate, x.TaxRateId, x.TaxRatePercent, x.OriginalNet, x.OriginalTax,
            x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross))]);
}
