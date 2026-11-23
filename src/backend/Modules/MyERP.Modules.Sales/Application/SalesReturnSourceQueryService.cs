using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// Return source-order filters (WU-SD-008b1, ADR-193). <see cref="PagedRequest.Keyword"/> matches the order number or
/// the order's customer code or name snapshot; sort is one of <c>documentNo</c> or <c>businessDate</c> (default
/// <c>-businessDate</c>) with the order ID as tie-breaker in the same direction.
/// </summary>
public sealed record SalesReturnSourceOrderQuery : PagedRequest
{
    public long? CustomerId { get; init; }
}

/// <summary>An Approved order with at least one untracked Posted delivery line that still has a returnable quantity.</summary>
public sealed record SalesReturnSourceOrderSummary(long Id, string DocumentNo, long CustomerId, string CustomerCode,
    string CustomerName, DateOnly BusinessDate, string CurrencyCode, int OpenLineCount, int Version);

/// <summary>
/// One Posted invoice line of a delivery line that the return may credit: <c>RemainingQuantity = Quantity −
/// CreditedQuantity</c>, where credited counts the billed portions of Posted returns only.
/// </summary>
public sealed record SalesReturnSourceInvoiceLine(long InvoiceLineId, long InvoiceId, string InvoiceDocumentNo,
    DateOnly InvoiceDate, decimal Quantity, decimal CreditedQuantity, decimal RemainingQuantity);

/// <summary>
/// One returnable Posted delivery line (BR-SD-044, advisory): <c>ReturnableQuantity = PostedQuantity −
/// ReturnedQuantity</c> (Posted returns only); <c>UnbilledRemainingQuantity = PostedQuantity − InvoicedQuantity −
/// Posted unbilled returned</c>; <c>InvoiceLines</c> lists the Posted invoice lines with an uncredited quantity. The
/// delivery's warehouse/location are offered as the default destination. <c>TrackingUnsupported</c> marks lot/serial
/// lines that CREATE refuses (SAL.RETURN.TRACKING_UNSUPPORTED). CREATE rechecks every balance under lock.
/// </summary>
public sealed record SalesReturnSourceLine(long DeliveryId, string DeliveryDocumentNo, DateOnly DeliveryDate,
    long DeliveryLineId, int DeliveryLineNo, long OrderLineId, int OrderLineNo, long ItemId, string ItemCode,
    string ItemName, long WarehouseId, long? LocationId, bool TrackingUnsupported, decimal PostedQuantity,
    decimal ReturnedQuantity, decimal ReturnableQuantity, decimal InvoicedQuantity, decimal UnbilledRemainingQuantity,
    IReadOnlyList<SalesReturnSourceInvoiceLine> InvoiceLines);

/// <summary>The order header the return copies (customer, currency, rate) and its returnable delivery lines.</summary>
public sealed record SalesReturnSourceOrder(long Id, string DocumentNo, long CustomerId, string CustomerCode,
    string CustomerName, DateOnly BusinessDate, string CurrencyCode, string BaseCurrencyCode, decimal ExchangeRate,
    int Version, IReadOnlyList<SalesReturnSourceLine> Lines);

/// <summary>
/// WU-SD-008b1 (ADR-193): what the return form may offer. Both reads need <c>SAL.RETURN.CREATE</c> and apply its SYS
/// data scope to the order, exactly like CREATE, so the form never offers a source the POST would hide as 404. Only
/// Posted returns consume balances (Drafts and Pending returns reserve nothing, BR-SD-044). Nothing is locked or
/// written.
/// </summary>
public sealed class SalesReturnSourceQueryService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company)
{
    // A member-initialised class, not a positional record: EF cannot filter or sort after a constructor projection.
    private sealed class OpenLine
    {
        public long OrderId { get; init; }
        public long DeliveryId { get; init; }
        public required string DeliveryDocumentNo { get; init; }
        public DateOnly DeliveryDate { get; init; }
        public long DeliveryLineId { get; init; }
        public int DeliveryLineNo { get; init; }
        public long OrderLineId { get; init; }
        public long WarehouseId { get; init; }
        public long? LocationId { get; init; }
        public decimal Posted { get; init; }
        public decimal Returned { get; init; }
        public decimal ReturnedUnbilled { get; init; }
        public bool Tracked { get; init; }
    }

    private IQueryable<SalesReturnLine> PostedReturnLines() =>
        from line in db.Set<SalesReturnLine>().AsNoTracking()
        join header in db.Set<SalesReturn>().AsNoTracking() on line.ReturnId equals header.Id
        where header.Status == SalesReturnStatus.Posted
        select line;

    /// <summary>Lines of Posted, non-deleted deliveries whose quantity exceeds the quantity of Posted returns.</summary>
    private IQueryable<OpenLine> OpenLines()
    {
        var returned = PostedReturnLines();
        var tracking = db.Set<SalesDeliveryLineTracking>().AsNoTracking();
        return
            from line in db.Set<SalesDeliveryLine>().AsNoTracking()
            join delivery in db.Set<SalesDelivery>().AsNoTracking() on line.DeliveryId equals delivery.Id
            where delivery.Status == SalesDeliveryStatus.Posted
            let back = returned.Where(x => x.DeliveryLineId == line.Id).Sum(x => (decimal?)x.Quantity) ?? 0m
            where line.Quantity > back
            select new OpenLine
            {
                OrderId = delivery.OrderId, DeliveryId = delivery.Id, DeliveryDocumentNo = delivery.DocumentNo,
                DeliveryDate = delivery.BusinessDate, DeliveryLineId = line.Id, DeliveryLineNo = line.LineNo,
                OrderLineId = line.OrderLineId, WarehouseId = line.WarehouseId, LocationId = line.LocationId,
                Posted = line.Quantity, Returned = back,
                ReturnedUnbilled = returned.Where(x => x.DeliveryLineId == line.Id)
                    .Sum(x => (decimal?)x.UnbilledQuantity) ?? 0m,
                Tracked = tracking.Any(x => x.LineId == line.Id)
            };
    }

    /// <summary>
    /// Pages Approved orders in the CREATE scope that still have a returnable, untracked Posted delivery line. Scope
    /// and the open-line condition are applied before counting.
    /// </summary>
    public async Task<PagedResult<SalesReturnSourceOrderSummary>> ListOrdersAsync(SalesReturnSourceOrderQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        company.RequireCompanyId();
        request.EnsureValid();
        if (request.CustomerId is <= 0)
            throw new RequestValidationException("customerId", "Customer must be positive.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await access.RequireScopeAsync(SalesReturnCreateService.CreatePermission, token);

        var open = OpenLines().Where(x => !x.Tracked);
        var query = scope.Apply(db.Set<SalesOrder>().AsNoTracking())
            .Where(x => x.Status == SalesOrderStatus.Approved && open.Any(l => l.OrderId == x.Id));
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.CustomerCode.Contains(keyword)
                || x.CustomerName.Contains(keyword));
        }
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var rows = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.DocumentNo, x.CustomerId, x.CustomerCode, x.CustomerName, x.BusinessDate, x.CurrencyCode,
                OpenLineCount = open.Count(l => l.OrderId == x.Id), x.Version
            })
            .ToListAsync(token);
        var items = rows.Select(x => new SalesReturnSourceOrderSummary(x.Id, x.DocumentNo, x.CustomerId,
            x.CustomerCode, x.CustomerName, x.BusinessDate, x.CurrencyCode, x.OpenLineCount, x.Version)).ToList();
        return PagedResult.Create(items, request, count);
    }

    /// <summary>
    /// The order and its returnable Posted delivery lines (tracked ones flagged), ordered by delivery date, delivery
    /// and line number, each with its Posted invoice lines that still have an uncredited quantity. A missing, deleted,
    /// other-company or out-of-scope order is 404 SAL.RETURN.SOURCE_NOT_FOUND; an order that is not Approved is 422
    /// SAL.RETURN.ORDER_NOT_APPROVED (both as CREATE would answer). A fully returned order returns no lines.
    /// </summary>
    public async Task<SalesReturnSourceOrder> GetOrderAsync(long orderId, CancellationToken token = default)
    {
        var companyId = company.RequireCompanyId();
        var scope = await access.RequireScopeAsync(SalesReturnCreateService.CreatePermission, token);
        var order = await scope.Apply(db.Set<SalesOrder>().AsNoTracking()).Where(x => x.Id == orderId)
            .Select(x => new
            {
                x.Id, x.DocumentNo, x.CustomerId, x.CustomerCode, x.CustomerName, x.BusinessDate, x.CurrencyCode,
                x.BaseCurrencyCode, x.ExchangeRate, x.Status, x.Version
            })
            .SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(SalesReturnCreateService.SourceNotFound, "Sales return source", orderId);
        if (order.Status != SalesOrderStatus.Approved)
            throw new BusinessRuleException(SalesReturnCreateService.OrderNotApproved,
                "Only deliveries of an approved order can be returned.");

        var open = await OpenLines().Where(x => x.OrderId == orderId)
            .OrderBy(x => x.DeliveryDate).ThenBy(x => x.DeliveryId).ThenBy(x => x.DeliveryLineNo)
            .ToListAsync(token);
        var lineIds = open.Select(x => x.DeliveryLineId).ToList();
        var orderLineIds = open.Select(x => x.OrderLineId).Distinct().ToList();
        var orderLines = await db.Set<SalesOrderLine>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.OrderId == orderId && orderLineIds.Contains(x.Id))
            .Select(x => new { x.Id, x.LineNo, x.ItemId, x.ItemCode, x.ItemName })
            .ToDictionaryAsync(x => x.Id, token);
        var invoiced = await db.Set<SalesInvoiceLine>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && lineIds.Contains(x.DeliveryLineId))
            .GroupBy(x => x.DeliveryLineId)
            .Select(g => new { DeliveryLineId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.DeliveryLineId, x => x.Quantity, token);
        var credited = from allocation in db.Set<SalesReturnInvoiceAllocation>().AsNoTracking()
                       join line in PostedReturnLines() on allocation.ReturnLineId equals line.Id
                       select allocation;
        var invoiceLines = (await (
                from line in db.Set<SalesInvoiceLine>().AsNoTracking()
                join invoice in db.Set<SalesInvoice>().AsNoTracking() on line.InvoiceId equals invoice.Id
                where line.CompanyId == companyId && lineIds.Contains(line.DeliveryLineId)
                    && invoice.Status == SalesInvoiceStatus.Posted
                let used = credited.Where(x => x.InvoiceLineId == line.Id).Sum(x => (decimal?)x.Quantity) ?? 0m
                where line.Quantity > used
                orderby invoice.BusinessDate, invoice.Id, line.LineNo
                select new
                {
                    line.DeliveryLineId, LineId = line.Id, InvoiceId = invoice.Id, invoice.DocumentNo,
                    invoice.BusinessDate, line.Quantity, Used = used
                })
            .ToListAsync(token))
            .ToLookup(x => x.DeliveryLineId);

        return new SalesReturnSourceOrder(order.Id, order.DocumentNo, order.CustomerId, order.CustomerCode,
            order.CustomerName, order.BusinessDate, order.CurrencyCode, order.BaseCurrencyCode, order.ExchangeRate,
            order.Version,
            [.. open.Select(x =>
            {
                var orderLine = orderLines[x.OrderLineId];
                var billed = invoiced.GetValueOrDefault(x.DeliveryLineId);
                return new SalesReturnSourceLine(x.DeliveryId, x.DeliveryDocumentNo, x.DeliveryDate,
                    x.DeliveryLineId, x.DeliveryLineNo, x.OrderLineId, orderLine.LineNo, orderLine.ItemId,
                    orderLine.ItemCode, orderLine.ItemName, x.WarehouseId, x.LocationId, x.Tracked, x.Posted,
                    x.Returned, x.Posted - x.Returned, billed, Math.Max(x.Posted - billed - x.ReturnedUnbilled, 0m),
                    [.. invoiceLines[x.DeliveryLineId].Select(i => new SalesReturnSourceInvoiceLine(i.LineId,
                        i.InvoiceId, i.DocumentNo, i.BusinessDate, i.Quantity, i.Used, i.Quantity - i.Used))]);
            })]);
    }
}
