using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// Production <see cref="IFinanceCollectionReceiptSource"/> (WU-FI-005b3a, ADR-128) over FI's own
/// <c>ar_collection_receipt_doc</c>. Reads on the scoped DbContext, which must be enlisted in the supplied transaction
/// (the posting caller's); a soft-deleted row is returned as <see cref="FinanceCollectionReceiptState.Deleted"/> so the
/// posting service answers 422 rather than 404. Missing or foreign-company rows return null. The source version is the
/// row version, the base currency is the current MD one, and the fingerprint is <see cref="CollectionReceiptFingerprint"/> over the saved fields.
/// </summary>
internal sealed class CollectionReceiptDocumentSource(MyErpDbContext db, ICurrencyLookup currencies)
    : IFinanceCollectionReceiptSource
{
    public async Task<FinanceCollectionReceiptSnapshot?> ReadReceiptAsync(long companyId, long receiptId,
        DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (db.Database.CurrentTransaction?.GetDbTransaction() != transaction)
            throw new InvalidOperationException("The receipt source must read inside the caller's transaction.");
        var row = await db.Set<CollectionReceiptDocument>().AsNoTracking()
            .IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .SingleOrDefaultAsync(x => x.Id == receiptId && x.CompanyId == companyId, cancellationToken);
        if (row is null)
            return null;
        // The current MD base currency, not the saved one: a changed base currency makes the receipt unusable (422).
        var baseCurrency = (await currencies.GetBaseCurrencyAsync(cancellationToken))?.Code ?? string.Empty;
        var state = row.IsDeleted
            ? FinanceCollectionReceiptState.Deleted
            : CollectionReceiptDocumentService.ToState(row.Status);
        return new(row.CompanyId, row.Id, row.Version, CollectionReceiptFingerprint.Create(row), row.CustomerId, state,
            row.BusinessDate, row.Currency, baseCurrency, row.Amount, row.BankAccountId, row.BankReference);
    }
}
