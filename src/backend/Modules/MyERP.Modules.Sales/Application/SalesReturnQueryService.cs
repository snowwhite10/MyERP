using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Application;

/// <summary>Billed portion of a return line with the credited invoice's number and the saved snapshot amounts.</summary>
public sealed record SalesReturnAllocationDetail(long Id, long InvoiceId, string InvoiceDocumentNo, long InvoiceLineId,
    decimal Quantity, decimal OriginalNet, decimal OriginalTax, decimal OriginalGross, decimal BaseNet, decimal BaseTax,
    decimal BaseGross);

/// <summary>One return line with its source delivery/order-line references, destination and snapshot amounts.</summary>
public sealed record SalesReturnLineDetail(long Id, int LineNo, long DeliveryId, string DeliveryDocumentNo,
    long DeliveryLineId, long OrderLineId, int OrderLineNo, long ItemId, string ItemCode, string ItemName,
    long WarehouseId, long? LocationId, decimal Quantity, decimal UnbilledQuantity, decimal BilledQuantity,
    long OriginalOutboundMovementId, long? InvMovementId, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross,
    IReadOnlyList<SalesReturnAllocationDetail> Allocations);

/// <summary>A sales return in any stored state; customer code and name are the source order's snapshot.</summary>
public sealed record SalesReturnDetail(long Id, string DocumentNo, string RequestKey, long OrderId,
    string OrderDocumentNo, long CustomerId, string CustomerCode, string CustomerName, DateOnly BusinessDate,
    string CurrencyCode, string BaseCurrencyCode, decimal ExchangeRate, SalesReturnStatus Status, string? Remark,
    long? ApprovalInstanceId, DateTime? PostedAt, long? FiCreditNoteId, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross, int Version,
    IReadOnlyList<SalesReturnLineDetail> Lines);

/// <summary>
/// List filters (WU-SD-008a2b2, ADR-187). <see cref="PagedRequest.Keyword"/> matches the return number, the source
/// order number or the customer name snapshot; From/To bound the return date (inclusive); sort is one of
/// <c>documentNo</c> or <c>businessDate</c> (default <c>-businessDate</c>), the return ID breaking ties in the same
/// direction.
/// </summary>
public sealed record SalesReturnQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public long? OrderId { get; init; }
    public long? CustomerId { get; init; }
    public SalesReturnStatus? Status { get; init; }
}

/// <summary>One return row: source order, customer name snapshot, state, original and base gross, line count.</summary>
public sealed record SalesReturnSummary(long Id, string DocumentNo, long OrderId, string OrderDocumentNo,
    long CustomerId, string CustomerName, DateOnly BusinessDate, string CurrencyCode, string BaseCurrencyCode,
    SalesReturnStatus Status, decimal OriginalGross, decimal BaseGross, DateTime? PostedAt, int LineCount,
    int Version);

/// <summary>
/// WU-SD-008a2b2 (ADR-187): return reads. SAL.RETURN.VIEW applies its SYS data scope to the source order; a return
/// outside it, of another company, soft-deleted or that does not exist is the same 404 SAL.RETURN.NOT_FOUND.
/// </summary>
public sealed class SalesReturnQueryService(MyErpDbContext db, SalesActionAccess access, SalesCompanyAccess company)
{
    public const string ViewPermission = "SAL.RETURN.VIEW";

    /// <summary>
    /// Pages non-deleted returns whose source order is inside the SAL.RETURN.VIEW data scope. The scope is applied
    /// before counting, so hidden returns are neither listed nor counted.
    /// </summary>
    public async Task<PagedResult<SalesReturnSummary>> ListAsync(SalesReturnQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        company.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.OrderId is <= 0)
            throw new RequestValidationException("orderId", "Order must be positive.");
        if (request.CustomerId is <= 0)
            throw new RequestValidationException("customerId", "Customer must be positive.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await access.RequireScopeAsync(ViewPermission, token);

        var query =
            from item in db.Set<SalesReturn>().AsNoTracking()
            join order in scope.Apply(db.Set<SalesOrder>().AsNoTracking()) on item.OrderId equals order.Id
            select new
            {
                item.Id, item.DocumentNo, item.OrderId, OrderDocumentNo = order.DocumentNo, item.CustomerId,
                order.CustomerName, item.BusinessDate, item.CurrencyCode, item.BaseCurrencyCode, item.Status,
                item.OriginalGross, item.BaseGross, item.PostedAt, item.Version
            };
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.OrderDocumentNo.Contains(keyword)
                || x.CustomerName.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.OrderId is { } orderId) query = query.Where(x => x.OrderId == orderId);
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var lines = db.Set<SalesReturnLine>().AsNoTracking();
        var rows = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new { Row = x, LineCount = lines.Count(l => l.ReturnId == x.Id) })
            .ToListAsync(token);
        var items = rows.Select(x => new SalesReturnSummary(x.Row.Id, x.Row.DocumentNo, x.Row.OrderId,
            x.Row.OrderDocumentNo, x.Row.CustomerId, x.Row.CustomerName, x.Row.BusinessDate, x.Row.CurrencyCode,
            x.Row.BaseCurrencyCode, x.Row.Status, x.Row.OriginalGross, x.Row.BaseGross, x.Row.PostedAt, x.LineCount,
            x.Row.Version)).ToList();
        return PagedResult.Create(items, request, count);
    }

    /// <summary>One non-deleted return of a visible order with its lines, allocations and source numbers.</summary>
    public async Task<SalesReturnDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var scope = await access.RequireScopeAsync(ViewPermission, token);
        var visibleOrders = scope.Apply(db.Set<SalesOrder>().AsNoTracking());
        var item = await db.Set<SalesReturn>().AsNoTracking().Include(x => x.Lines)
            .ThenInclude(x => x.InvoiceAllocations)
            .Where(x => x.Id == id && visibleOrders.Any(o => o.Id == x.OrderId))
            .AsSplitQuery().SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(SalesReturnDraftCommandService.NotFoundCode, "Sales return", id);
        return await DescribeAsync(item, token);
    }

    /// <summary>
    /// Maps a return the caller has already authorised (VIEW read, or a command that checked its own scope). Source
    /// order and deliveries are read ignoring soft delete because a saved return keeps referring to them.
    /// </summary>
    public async Task<SalesReturnDetail> DescribeAsync(SalesReturn item, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var order = await db.Set<SalesOrder>().AsNoTracking().IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .Where(x => x.Id == item.OrderId)
            .Select(x => new { x.DocumentNo, x.CustomerCode, x.CustomerName })
            .SingleAsync(token);
        var orderLineIds = item.Lines.Select(x => x.OrderLineId).Distinct().ToList();
        var orderLines = await db.Set<SalesOrderLine>().AsNoTracking()
            .Where(x => x.OrderId == item.OrderId && orderLineIds.Contains(x.Id))
            .Select(x => new { x.Id, x.LineNo, x.ItemCode, x.ItemName })
            .ToDictionaryAsync(x => x.Id, token);
        var deliveryIds = item.Lines.Select(x => x.DeliveryId).Distinct().ToList();
        var deliveries = await db.Set<SalesDelivery>().AsNoTracking().IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .Where(x => deliveryIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DocumentNo })
            .ToDictionaryAsync(x => x.Id, x => x.DocumentNo, token);
        var invoiceIds = item.Lines.SelectMany(x => x.InvoiceAllocations).Select(x => x.InvoiceId).Distinct()
            .ToList();
        var invoices = invoiceIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.Set<SalesInvoice>().AsNoTracking().Where(x => invoiceIds.Contains(x.Id))
                .Select(x => new { x.Id, x.DocumentNo })
                .ToDictionaryAsync(x => x.Id, x => x.DocumentNo, token);
        return new SalesReturnDetail(item.Id, item.DocumentNo, item.RequestKey, item.OrderId, order.DocumentNo,
            item.CustomerId, order.CustomerCode, order.CustomerName, item.BusinessDate, item.CurrencyCode,
            item.BaseCurrencyCode, item.ExchangeRate, item.Status, item.Remark, item.ApprovalInstanceId,
            item.PostedAt, item.FiCreditNoteId, item.OriginalNet, item.OriginalTax, item.OriginalGross, item.BaseNet,
            item.BaseTax, item.BaseGross, item.Version,
            [.. item.Lines.OrderBy(x => x.LineNo).Select(x =>
            {
                var source = orderLines[x.OrderLineId];
                return Line(x, source.LineNo, source.ItemCode, source.ItemName, deliveries[x.DeliveryId], invoices);
            })]);
    }

    private static SalesReturnLineDetail Line(SalesReturnLine x, int orderLineNo, string itemCode, string itemName,
        string deliveryNo, Dictionary<long, string> invoices) =>
        new(x.Id, x.LineNo, x.DeliveryId, deliveryNo, x.DeliveryLineId, x.OrderLineId, orderLineNo, x.ItemId,
            itemCode, itemName, x.WarehouseId, x.LocationId, x.Quantity, x.UnbilledQuantity,
            x.Quantity - x.UnbilledQuantity, x.OriginalOutboundMovementId, x.InvMovementId, x.OriginalNet,
            x.OriginalTax, x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross,
            [.. x.InvoiceAllocations.OrderBy(a => a.InvoiceLineId).Select(a => new SalesReturnAllocationDetail(a.Id,
                a.InvoiceId, invoices[a.InvoiceId], a.InvoiceLineId, a.Quantity, a.OriginalNet, a.OriginalTax,
                a.OriginalGross, a.BaseNet, a.BaseTax, a.BaseGross))]);
}
