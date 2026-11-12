using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

/// <summary>Source storage is supplied by PUR when order and receipt tables are introduced.</summary>
public interface IPurchaseInvoiceDraftSourceReader
{
    Task<PurchaseInvoiceOrderIdentity> ReadOrderAsync(long companyId, long supplierId,
        long orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseInvoiceReceiptIdentity>> ReadReceiptsAsync(long companyId,
        IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseInvoiceOrderLinePrice>> ReadPricesAsync(long companyId,
        IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken);
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
