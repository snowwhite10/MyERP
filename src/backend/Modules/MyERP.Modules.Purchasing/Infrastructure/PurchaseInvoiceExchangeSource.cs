using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

/// <summary>Reads the posting exchange snapshot of an invoice's purchase order (WU-PUR-008b2, ADR-206).</summary>
public interface IPurchaseInvoiceExchangeSource
{
    /// <summary>
    /// Returns the order's saved base currency and rate, validated against the current company base currency
    /// (MD <see cref="ICurrencyLookup"/>; the caller's company context must be <paramref name="companyId"/>).
    /// Null when the order does not exist in <paramref name="companyId"/>. Must run inside the caller's transaction
    /// after the order header lock (BR-PUR-043 lock order).
    /// </summary>
    Task<PurchaseInvoiceExchangeSnapshot?> ReadAsync(long companyId, long purchaseOrderId, string invoiceCurrency,
        CancellationToken cancellationToken);
}

public sealed class PurchaseInvoiceExchangeSource(MyErpDbContext db, ICurrencyLookup currencies)
    : IPurchaseInvoiceExchangeSource
{
    public async Task<PurchaseInvoiceExchangeSnapshot?> ReadAsync(long companyId, long purchaseOrderId,
        string invoiceCurrency, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Purchase invoice exchange snapshot requires an active transaction.");

        // Soft-deleted orders are still read: the invoice preflight (008a9) decides postability, not the rate source.
        var order = await db.Set<PurchaseOrderSource>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Id == purchaseOrderId)
            .Select(x => new PurchaseInvoiceOrderRateSnapshot(x.Currency, x.BaseCurrencyCode, x.ExchangeRate))
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
            return null;

        var baseCurrency = (await currencies.GetBaseCurrencyAsync(cancellationToken))?.Code;
        return PurchaseInvoiceExchangeRules.Resolve(invoiceCurrency, order, baseCurrency);
    }
}
