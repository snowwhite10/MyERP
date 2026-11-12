using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

/// <summary>Reads PUR-owned source rows in the active draft transaction.</summary>
public interface IPurchaseInvoiceDraftSourceReader
{
    Task<PurchaseInvoiceOrderIdentity> ReadOrderAsync(long companyId, long supplierId,
        long orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseInvoiceReceiptIdentity>> ReadReceiptsAsync(long companyId,
        IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseInvoiceOrderLinePrice>> ReadPricesAsync(long companyId,
        IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken);
}

public sealed class PurchaseInvoiceDraftSourceReader(MyErpDbContext db)
    : IPurchaseInvoiceDraftSourceReader
{
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Purchase invoice sources require an active transaction.");
    }

    public async Task<PurchaseInvoiceOrderIdentity> ReadOrderAsync(long companyId,
        long supplierId, long orderId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var order = await db.Set<PurchaseOrderSource>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SupplierId == supplierId && x.Id == orderId)
            .SingleOrDefaultAsync(cancellationToken);
        return order is null
            ? new PurchaseInvoiceOrderIdentity(0, 0, 0, "", "", true)
            : new(order.CompanyId, order.SupplierId, order.Id, order.Currency,
                order.Status, order.IsDeleted);
    }

    public async Task<IReadOnlyList<PurchaseInvoiceReceiptIdentity>> ReadReceiptsAsync(
        long companyId, IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var ids = receiptLineIds.Order().Distinct().ToArray();
        var rows = await (from line in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
            join receipt in db.Set<PurchaseReceiptSource>().AsNoTracking()
                on new { line.CompanyId, Id = line.PurchaseReceiptId }
                equals new { receipt.CompanyId, receipt.Id }
            join orderLine in db.Set<PurchaseOrderSourceLine>().AsNoTracking()
                on new { line.CompanyId, Id = line.PurchaseOrderLineId }
                equals new { orderLine.CompanyId, orderLine.Id }
            where line.CompanyId == companyId && ids.Contains(line.Id)
            orderby line.Id
            select new { line, receipt, orderLine }).ToArrayAsync(cancellationToken);
        return rows.Select(x => new PurchaseInvoiceReceiptIdentity(x.line.Id, companyId,
            x.receipt.SupplierId, x.receipt.PurchaseOrderId,
            x.orderLine.PurchaseOrderId, x.line.MaterialId, x.orderLine.MaterialId,
            x.receipt.Currency, x.receipt.Status, x.receipt.IsDeleted)
            { OrderLineId = x.orderLine.Id }).ToArray();
    }

    public async Task<IReadOnlyList<PurchaseInvoiceOrderLinePrice>> ReadPricesAsync(
        long companyId, IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var ids = receiptLineIds.Order().Distinct().ToArray();
        var rows = await (from line in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
            join orderLine in db.Set<PurchaseOrderSourceLine>().AsNoTracking()
                on new { line.CompanyId, Id = line.PurchaseOrderLineId }
                equals new { orderLine.CompanyId, orderLine.Id }
            where line.CompanyId == companyId && ids.Contains(line.Id)
            orderby line.Id
            select new { ReceiptLineId = line.Id, orderLine }).ToArrayAsync(cancellationToken);
        return rows.Select(x => new PurchaseInvoiceOrderLinePrice(x.ReceiptLineId,
            companyId, x.orderLine.PurchaseOrderId, x.orderLine.Id,
            x.orderLine.MaterialId, x.orderLine.UnitPrice,
            x.orderLine.TaxRatePercent)).ToArray();
    }
}

/// <summary>Stable source read order shared by SQL and offline contract tests.</summary>
public static class PurchaseInvoiceDraftSourceSequence
{
    public static async Task<(PurchaseInvoiceOrderIdentity Order,
        IReadOnlyList<PurchaseInvoiceReceiptIdentity> Receipts,
        IReadOnlyList<PurchaseInvoiceOrderLinePrice> Prices)> ReadAsync(
        IPurchaseInvoiceDraftSourceReader sources, long companyId, long supplierId,
        long orderId, IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (companyId <= 0 || supplierId <= 0 || orderId <= 0 ||
            receiptLineIds is null || receiptLineIds.Count == 0 ||
            receiptLineIds.Any(id => id <= 0) || receiptLineIds.Distinct().Count() != receiptLineIds.Count)
            throw new ArgumentException("Draft authority scope is invalid.");
        var ids = receiptLineIds.Order().ToArray();
        var order = await sources.ReadOrderAsync(companyId, supplierId, orderId, cancellationToken);
        var receipts = await sources.ReadReceiptsAsync(companyId, ids, cancellationToken);
        var prices = await sources.ReadPricesAsync(companyId, ids, cancellationToken);
        return (order, receipts, prices);
    }
}

/// <summary>Reads one complete draft authority snapshot inside the session's active transaction.</summary>
public sealed class PurchaseInvoiceDraftLockedReader(
    MyErpDbContext db, IPurchaseInvoiceDraftSourceReader sources,
    long companyId, long supplierId, long orderId,
    IReadOnlyList<long> receiptLineIds, long? invoiceId)
{
    public async Task<PurchaseInvoiceDraftSaveLockedState> ReadAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Draft authority requires an active transaction.");
        if (invoiceId is <= 0) throw new ArgumentException("Draft invoice ID is invalid.");
        var (order, receipts, prices) = await PurchaseInvoiceDraftSourceSequence.ReadAsync(
            sources, companyId, supplierId, orderId, receiptLineIds, cancellationToken);

        // No soft-delete filter: historical claims remain occupied permanently.
        var claims = await db.Set<PurchaseInvoiceNumberClaim>().IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SupplierId == supplierId)
            .OrderBy(x => x.NumberKey).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
        var owners = claims.Select(x => new PurchaseInvoiceNumberOwner(
            x.CompanyId, x.SupplierId, x.InvoiceId, x.NumberKey, false)).ToArray();

        PurchaseInvoice? saved = null;
        PurchaseInvoiceLine[]? lines = null;
        PurchaseInvoiceSavedDraft? savedState = null;
        if (invoiceId.HasValue)
        {
            saved = await db.Set<PurchaseInvoice>().IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invoiceId.Value,
                    cancellationToken);
            if (saved is not null)
            {
                lines = await db.Set<PurchaseInvoiceLine>().IgnoreQueryFilters()
                    .Where(x => x.CompanyId == companyId && x.InvoiceId == invoiceId.Value)
                    .OrderBy(x => x.LineNo).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
                savedState = new(saved.Id, saved.CompanyId, saved.SupplierId,
                    saved.PurchaseOrderId, saved.Currency, saved.Status.ToString(),
                    saved.Version, saved.IsDeleted, saved.NumberKey);
            }
        }

        return new(order, receipts, prices, owners, claims, savedState, saved, lines);
    }
}
