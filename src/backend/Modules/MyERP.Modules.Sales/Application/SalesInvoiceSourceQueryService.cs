using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// Source-order filters (WU-SD-007b0, ADR-181). <see cref="PagedRequest.Keyword"/> matches the order number or the
/// order's customer code or name snapshot; sort is one of <c>documentNo</c> or <c>businessDate</c> (default
/// <c>-businessDate</c>) with the order ID as tie-breaker in the same direction.
/// </summary>
public sealed record SalesInvoiceSourceOrderQuery : PagedRequest
{
    public long? CustomerId { get; init; }
}

/// <summary>An Approved order with at least one Posted delivery line that still has an uninvoiced quantity.</summary>
public sealed record SalesInvoiceSourceOrderSummary(long Id, string DocumentNo, long CustomerId, string CustomerCode,
    string CustomerName, DateOnly BusinessDate, string CurrencyCode, int OpenLineCount, int Version);

/// <summary>
/// One Posted delivery line with its uninvoiced balance: <c>RemainingQuantity = PostedQuantity − InvoicedQuantity</c>
/// (every saved invoice line of that delivery line; invoices are never deleted). Advisory only: ISSUE rechecks it
/// under the source locks (BR-SD-038).
/// </summary>
public sealed record SalesInvoiceSourceLine(long DeliveryId, string DeliveryDocumentNo, DateOnly DeliveryDate,
    long DeliveryLineId, int DeliveryLineNo, long OrderLineId, int OrderLineNo, long ItemId, string ItemCode,
    string ItemName, decimal TaxRatePercent, decimal PostedQuantity, decimal InvoicedQuantity,
    decimal RemainingQuantity);

/// <summary>The order header the invoice will copy (customer, currency, rate) and its open delivery lines.</summary>
public sealed record SalesInvoiceSourceOrder(long Id, string DocumentNo, long CustomerId, string CustomerCode,
    string CustomerName, DateOnly BusinessDate, string CurrencyCode, string BaseCurrencyCode, decimal ExchangeRate,
    int Version, IReadOnlyList<SalesInvoiceSourceLine> Lines);

/// <summary>
/// WU-SD-007b0 (ADR-181): what the invoice form may issue. Both reads need <c>SAL.INVOICE.ISSUE</c> and apply its SYS
/// data scope to the order, exactly like ISSUE, so the form never offers a source the POST would hide as 404.
/// Nothing is locked or written.
/// </summary>
public sealed class SalesInvoiceSourceQueryService(MyErpDbContext db, SalesActionAccess access,
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
        public decimal Posted { get; init; }
        public decimal Invoiced { get; init; }
    }

    /// <summary>Posted, non-deleted delivery lines whose saved quantity exceeds the quantity already invoiced.</summary>
    private IQueryable<OpenLine> OpenLines()
    {
        var invoiced = db.Set<SalesInvoiceLine>().AsNoTracking();
        return
            from line in db.Set<SalesDeliveryLine>().AsNoTracking()
            join delivery in db.Set<SalesDelivery>().AsNoTracking() on line.DeliveryId equals delivery.Id
            where delivery.Status == SalesDeliveryStatus.Posted
            let billed = invoiced.Where(x => x.DeliveryLineId == line.Id).Sum(x => (decimal?)x.Quantity) ?? 0m
            where line.Quantity > billed
            select new OpenLine
            {
                OrderId = delivery.OrderId, DeliveryId = delivery.Id, DeliveryDocumentNo = delivery.DocumentNo,
                DeliveryDate = delivery.BusinessDate, DeliveryLineId = line.Id, DeliveryLineNo = line.LineNo,
                OrderLineId = line.OrderLineId, Posted = line.Quantity, Invoiced = billed
            };
    }

    /// <summary>
    /// Pages Approved orders in the ISSUE scope that still have an open Posted delivery line. Scope and the open-line
    /// condition are applied before counting, so other orders are neither listed nor counted.
    /// </summary>
    public async Task<PagedResult<SalesInvoiceSourceOrderSummary>> ListOrdersAsync(
        SalesInvoiceSourceOrderQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        company.RequireCompanyId();
        request.EnsureValid();
        if (request.CustomerId is <= 0)
            throw new RequestValidationException("customerId", "Customer must be positive.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await access.RequireScopeAsync(SalesInvoiceIssuing.IssuePermission, token);

        var open = OpenLines();
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
        var items = rows.Select(x => new SalesInvoiceSourceOrderSummary(x.Id, x.DocumentNo, x.CustomerId,
            x.CustomerCode, x.CustomerName, x.BusinessDate, x.CurrencyCode, x.OpenLineCount, x.Version)).ToList();
        return PagedResult.Create(items, request, count);
    }

    /// <summary>
    /// The order and its open Posted delivery lines, ordered by delivery date, delivery and line number. A missing,
    /// deleted, other-company or out-of-scope order is 404 <see cref="SalesInvoiceIssuing.SourceNotFound"/>; an order
    /// that is not Approved is 422 <see cref="SalesInvoiceIssuing.OrderNotApproved"/> (both as ISSUE would answer).
    /// A fully invoiced order returns no lines.
    /// </summary>
    public async Task<SalesInvoiceSourceOrder> GetOrderAsync(long orderId, CancellationToken token = default)
    {
        var companyId = company.RequireCompanyId();
        var scope = await access.RequireScopeAsync(SalesInvoiceIssuing.IssuePermission, token);
        var order = await scope.Apply(db.Set<SalesOrder>().AsNoTracking()).Where(x => x.Id == orderId)
            .Select(x => new
            {
                x.Id, x.DocumentNo, x.CustomerId, x.CustomerCode, x.CustomerName, x.BusinessDate, x.CurrencyCode,
                x.BaseCurrencyCode, x.ExchangeRate, x.Status, x.Version
            })
            .SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(SalesInvoiceIssuing.SourceNotFound, "Sales invoice source", orderId);
        if (order.Status != SalesOrderStatus.Approved)
            throw new BusinessRuleException(SalesInvoiceIssuing.OrderNotApproved,
                "Only deliveries of an approved order can be invoiced.");

        var open = await OpenLines().Where(x => x.OrderId == orderId)
            .OrderBy(x => x.DeliveryDate).ThenBy(x => x.DeliveryId).ThenBy(x => x.DeliveryLineNo)
            .ToListAsync(token);
        var orderLineIds = open.Select(x => x.OrderLineId).Distinct().ToList();
        var orderLines = await db.Set<SalesOrderLine>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.OrderId == orderId && orderLineIds.Contains(x.Id))
            .Select(x => new { x.Id, x.LineNo, x.ItemId, x.ItemCode, x.ItemName, x.TaxRatePercent })
            .ToDictionaryAsync(x => x.Id, token);
        return new SalesInvoiceSourceOrder(order.Id, order.DocumentNo, order.CustomerId, order.CustomerCode,
            order.CustomerName, order.BusinessDate, order.CurrencyCode, order.BaseCurrencyCode, order.ExchangeRate,
            order.Version,
            [.. open.Select(x =>
            {
                var line = orderLines[x.OrderLineId];
                return new SalesInvoiceSourceLine(x.DeliveryId, x.DeliveryDocumentNo, x.DeliveryDate,
                    x.DeliveryLineId, x.DeliveryLineNo, x.OrderLineId, line.LineNo, line.ItemId, line.ItemCode,
                    line.ItemName, line.TaxRatePercent, x.Posted, x.Invoiced, x.Posted - x.Invoiced);
            })]);
    }
}
