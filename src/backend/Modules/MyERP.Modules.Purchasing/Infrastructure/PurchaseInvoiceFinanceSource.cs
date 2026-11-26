using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

/// <summary>
/// WU-PUR-008b3 (ADR-205/207): PUR's implementation of the FI-owned <see cref="IFinancePurchaseInvoiceSource"/>.
/// FI calls it inside the PUR POST transaction (008b4) on the shared scoped DbContext, after PUR has taken its locks
/// (BR-PUR-043); this port takes no lock of its own. It reads only PUR tables: the saved invoice and lines, the order
/// line prices and RESOLVE rows for the 008a10 fingerprint, and the order rate snapshot (008b2). A missing or
/// foreign-company invoice is null; a soft-deleted one is reported as Deleted. Only a Matched invoice carries posting
/// evidence; other states return <see cref="PurchaseInvoiceFinanceSnapshot.NotPostable"/>. Unusable saved evidence
/// (missing rate snapshot, inconsistent differences) throws the PUR <see cref="BusinessRuleException"/> and FI writes
/// nothing; the caller rolls back.
/// </summary>
internal sealed class PurchaseInvoiceFinanceSource(MyErpDbContext db, IPurchaseInvoiceDraftSourceReader sources,
    IPurchaseInvoiceExchangeSource exchanges, ICurrencyLookup currencies) : IFinancePurchaseInvoiceSource
{
    public async Task<FinancePurchaseInvoiceSnapshot?> ReadInvoiceAsync(long companyId, long invoiceId,
        DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(db.Database.CurrentTransaction?.GetDbTransaction(), transaction))
            throw new InvalidOperationException(
                "The PUR invoice source must be read inside the caller's transaction on the shared DbContext.");
        if (companyId <= 0 || invoiceId <= 0 || db.CurrentCompanyId != companyId)
            return null;

        var invoice = await db.Set<PurchaseInvoice>().AsNoTracking()
            .IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .Where(x => x.CompanyId == companyId && x.Id == invoiceId)
            .Select(x => new
            {
                x.Version, x.SupplierId, x.PurchaseOrderId, x.Status, x.IsDeleted, x.BusinessDate, x.Currency,
                x.TotalNet, x.TotalTax, x.TotalGross
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (invoice is null)
            return null;

        var lines = await db.Set<PurchaseInvoiceLine>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.InvoiceId == invoiceId)
            .OrderBy(x => x.LineNo)
            .ToArrayAsync(cancellationToken);
        var saved = new PurchaseInvoiceFinanceSaved(companyId, invoiceId, invoice.Version, invoice.SupplierId,
            invoice.Status, invoice.IsDeleted, invoice.BusinessDate, invoice.Currency,
            invoice.TotalNet, invoice.TotalTax, invoice.TotalGross,
            lines.Select(x => new PurchaseInvoiceFinanceSavedLine(x.LineNo, x.Net, x.Tax, x.Gross)).ToArray());
        if (invoice.IsDeleted || invoice.Status != PurchaseInvoiceStatus.Matched)
            return PurchaseInvoiceFinanceSnapshot.NotPostable(saved);

        var fingerprint = await FingerprintAsync(companyId, invoiceId, invoice.SupplierId, invoice.Currency,
            invoice.BusinessDate, lines, cancellationToken);
        var exchange = await exchanges.ReadAsync(companyId, invoice.PurchaseOrderId, invoice.Currency,
                cancellationToken)
            ?? throw new BusinessRuleException("PUR.INVOICE.INVALID_POSTING_SNAPSHOT",
                "Saved invoice purchase order is missing.");
        var precision = await currencies.GetPrecisionAsync(cancellationToken);
        return PurchaseInvoiceFinanceSnapshot.Build(saved, fingerprint, exchange, precision);
    }

    private async Task<string> FingerprintAsync(long companyId, long invoiceId, long supplierId, string currency,
        DateOnly businessDate, IReadOnlyList<PurchaseInvoiceLine> lines, CancellationToken cancellationToken)
    {
        var receiptLineIds = lines.Select(x => x.ReceiptLineId).Distinct().Order().ToArray();
        var prices = await sources.ReadPricesAsync(companyId, receiptLineIds, cancellationToken);
        if (prices.Count != receiptLineIds.Length || prices.Any(x => x is null || x.ReceiptLineId <= 0)
            || prices.Select(x => x.ReceiptLineId).Distinct().Count() != prices.Count)
            throw new BusinessRuleException("PUR.INVOICE.INVALID_SOURCE_IDENTITY",
                "Saved receipt prices are incomplete or duplicated.");
        var priceByReceipt = prices.ToDictionary(x => x.ReceiptLineId);
        var match = lines.Select(line =>
        {
            var price = priceByReceipt[line.ReceiptLineId];
            return new PurchaseInvoiceMatchLine(line.ReceiptLineId, new PurchaseInvoiceAmountLine(line.LineNo,
                line.Quantity, price.UnitPrice, price.TaxRatePercent, line.UnitPrice, line.TaxRatePercent,
                line.Net, line.Tax, line.Gross));
        }).ToArray();

        var resolutions = await db.Set<PurchaseInvoiceResolutionRecord>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.InvoiceId == invoiceId)
            .Select(x => new PurchaseInvoiceSavedResolution(x.InvoiceVersion, x.LineNo, x.Action, x.Reason,
                x.NetDifference, x.TaxDifference, x.GrossDifference))
            .ToArrayAsync(cancellationToken);
        var amount = PurchaseInvoiceAmountMatch.Check(match.Select(x => x.Amount).ToArray());
        var accepted = PurchaseInvoiceFinanceEvidence.Accepted(amount, resolutions);
        return PurchaseInvoiceSnapshotFingerprint.Create(companyId, invoiceId, supplierId, currency, businessDate,
            match, accepted);
    }
}
