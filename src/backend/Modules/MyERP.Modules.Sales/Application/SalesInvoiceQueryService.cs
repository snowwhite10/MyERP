using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

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
/// WU-SD-007a3b (ADR-179): invoice reads. SAL.INVOICE.VIEW applies its SYS data scope to the source order; an invoice
/// outside it, of another company or that does not exist is the same 404 SAL.INVOICE.NOT_FOUND.
/// </summary>
public sealed class SalesInvoiceQueryService(MyErpDbContext db, SalesActionAccess access, SalesCompanyAccess company)
{
    public const string ViewPermission = "SAL.INVOICE.VIEW";
    public const string NotFound = "SAL.INVOICE.NOT_FOUND";

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
