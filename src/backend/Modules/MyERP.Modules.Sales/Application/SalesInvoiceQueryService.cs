using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Application;

/// <summary>One invoice line with its source delivery/order-line references and allocated snapshot amounts.</summary>
public sealed record SalesInvoiceLineDetail(long Id, int LineNo, long DeliveryId, string DeliveryDocumentNo,
    long DeliveryLineId, long OrderLineId, int OrderLineNo, long ItemId, string ItemCode, string ItemName,
    long TaxRateId, decimal TaxRatePercent, decimal Quantity, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

/// <summary>
/// A committed (Posted) sales invoice with the FI/AR references returned by the FI posting contract (ADR-179).
/// Customer code and name are the source order's snapshot.
/// </summary>
public sealed record SalesInvoiceDetail(long Id, string DocumentNo, string RequestKey, long OrderId,
    string OrderDocumentNo, long CustomerId, string CustomerCode, string CustomerName, DateOnly BusinessDate,
    string CurrencyCode, string BaseCurrencyCode, decimal ExchangeRate, SalesInvoiceStatus Status,
    DateTime? PostedAt, long? FiSalesSourceId, long? FiReceivableId, long? FiJournalId, decimal OriginalNet,
    decimal OriginalTax, decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross, int Version,
    IReadOnlyList<SalesInvoiceLineDetail> Lines);

/// <summary>
/// List filters (WU-SD-007a3b2, ADR-180). <see cref="PagedRequest.Keyword"/> matches the invoice or source order
/// number; From/To bound the invoice date (inclusive); sort is one of <c>documentNo</c> or <c>businessDate</c>
/// (default <c>-businessDate</c>), with the invoice ID as tie-breaker in the same direction.
/// </summary>
public sealed record SalesInvoiceQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public long? OrderId { get; init; }
    public long? CustomerId { get; init; }
}

/// <summary>One Posted invoice row: source order, customer snapshot name, invoice and base amounts, FI journal.</summary>
public sealed record SalesInvoiceSummary(long Id, string DocumentNo, long OrderId, string OrderDocumentNo,
    long CustomerId, string CustomerName, DateOnly BusinessDate, string CurrencyCode, string BaseCurrencyCode,
    decimal OriginalGross, decimal BaseGross, DateTime? PostedAt, long? FiJournalId, int LineCount, int Version);

/// <summary>
/// WU-SD-007a3b (ADR-179): invoice reads. SAL.INVOICE.VIEW applies its SYS data scope to the source order; an invoice
/// outside it, of another company or that does not exist is the same 404 SAL.INVOICE.NOT_FOUND.
/// </summary>
public sealed class SalesInvoiceQueryService(MyErpDbContext db, SalesActionAccess access, SalesCompanyAccess company)
{
    public const string ViewPermission = "SAL.INVOICE.VIEW";
    public const string NotFound = "SAL.INVOICE.NOT_FOUND";

    /// <summary>
    /// Pages Posted invoices whose source order is inside the SAL.INVOICE.VIEW data scope. The scope is applied
    /// before counting, so hidden invoices are neither listed nor counted (ADR-180).
    /// </summary>
    public async Task<PagedResult<SalesInvoiceSummary>> ListAsync(SalesInvoiceQuery request,
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
            from invoice in db.Set<SalesInvoice>().AsNoTracking()
            join order in scope.Apply(db.Set<SalesOrder>().AsNoTracking()) on invoice.OrderId equals order.Id
            where invoice.Status == SalesInvoiceStatus.Posted
            select new
            {
                invoice.Id, invoice.DocumentNo, invoice.OrderId, OrderDocumentNo = order.DocumentNo,
                invoice.CustomerId, order.CustomerName, invoice.BusinessDate, invoice.CurrencyCode,
                invoice.BaseCurrencyCode, invoice.OriginalGross, invoice.BaseGross, invoice.PostedAt,
                invoice.FiJournalId, invoice.Version
            };
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.OrderDocumentNo.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.OrderId is { } orderId) query = query.Where(x => x.OrderId == orderId);
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var lines = db.Set<SalesInvoiceLine>().AsNoTracking();
        var rows = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new { Row = x, LineCount = lines.Count(l => l.InvoiceId == x.Id) })
            .ToListAsync(token);
        var items = rows.Select(x => new SalesInvoiceSummary(x.Row.Id, x.Row.DocumentNo, x.Row.OrderId,
            x.Row.OrderDocumentNo, x.Row.CustomerId, x.Row.CustomerName, x.Row.BusinessDate, x.Row.CurrencyCode,
            x.Row.BaseCurrencyCode, x.Row.OriginalGross, x.Row.BaseGross, x.Row.PostedAt, x.Row.FiJournalId,
            x.LineCount, x.Row.Version)).ToList();
        return PagedResult.Create(items, request, count);
    }

    public async Task<SalesInvoiceDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var scope = await access.RequireScopeAsync(ViewPermission, token);
        var visibleOrders = scope.Apply(db.Set<SalesOrder>().AsNoTracking());
        // Issuing exists only inside the ISSUE transaction; Read Committed never sees it, the filter is a guard.
        var invoice = await db.Set<SalesInvoice>().AsNoTracking().Include(x => x.Lines)
            .Where(x => x.Id == id && x.Status == SalesInvoiceStatus.Posted
                && visibleOrders.Any(o => o.Id == x.OrderId))
            .AsSplitQuery().SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(NotFound, "Sales invoice", id);
        return await DescribeAsync(invoice, token);
    }

    /// <summary>
    /// Reads back an invoice the ISSUE command has just committed or replayed (its own scope already checked), so the
    /// response shows the stored values (for example PostedAt at SQL precision and the row version) exactly as a later
    /// GET will.
    /// </summary>
    public async Task<SalesInvoiceDetail> DescribeCommittedAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var invoice = await db.Set<SalesInvoice>().AsNoTracking().Include(x => x.Lines)
            .Where(x => x.Id == id && x.Status == SalesInvoiceStatus.Posted)
            .AsSplitQuery().SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(NotFound, "Sales invoice", id);
        return await DescribeAsync(invoice, token);
    }

    /// <summary>
    /// Maps an invoice the caller has already authorised (VIEW read, or the ISSUE command that checked its own
    /// scope). An issuer does not need VIEW to see the invoice it just issued or replayed.
    /// </summary>
    public async Task<SalesInvoiceDetail> DescribeAsync(SalesInvoice invoice, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var order = await db.Set<SalesOrder>().AsNoTracking().IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .Where(x => x.Id == invoice.OrderId)
            .Select(x => new { x.DocumentNo, x.CustomerCode, x.CustomerName })
            .SingleAsync(token);
        var orderLineIds = invoice.Lines.Select(x => x.OrderLineId).Distinct().ToList();
        var orderLines = await db.Set<SalesOrderLine>().AsNoTracking()
            .Where(x => x.OrderId == invoice.OrderId && orderLineIds.Contains(x.Id))
            .Select(x => new { x.Id, x.LineNo, x.ItemCode, x.ItemName })
            .ToDictionaryAsync(x => x.Id, token);
        var deliveryIds = invoice.Lines.Select(x => x.DeliveryId).Distinct().ToList();
        var deliveries = await db.Set<SalesDelivery>().AsNoTracking().IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .Where(x => deliveryIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DocumentNo })
            .ToDictionaryAsync(x => x.Id, x => x.DocumentNo, token);
        return new SalesInvoiceDetail(invoice.Id, invoice.DocumentNo, invoice.RequestKey, invoice.OrderId,
            order.DocumentNo, invoice.CustomerId, order.CustomerCode, order.CustomerName, invoice.BusinessDate,
            invoice.CurrencyCode, invoice.BaseCurrencyCode, invoice.ExchangeRate, invoice.Status, invoice.PostedAt,
            invoice.FiSalesSourceId, invoice.FiReceivableId, invoice.FiJournalId, invoice.OriginalNet,
            invoice.OriginalTax, invoice.OriginalGross, invoice.BaseNet, invoice.BaseTax, invoice.BaseGross,
            invoice.Version,
            [.. invoice.Lines.OrderBy(x => x.LineNo).Select(x => new SalesInvoiceLineDetail(x.Id, x.LineNo,
                x.DeliveryId, deliveries[x.DeliveryId], x.DeliveryLineId, x.OrderLineId,
                orderLines[x.OrderLineId].LineNo, x.ItemId, orderLines[x.OrderLineId].ItemCode,
                orderLines[x.OrderLineId].ItemName, x.TaxRateId, x.TaxRatePercent, x.Quantity, x.OriginalNet,
                x.OriginalTax, x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross))]);
    }
}
