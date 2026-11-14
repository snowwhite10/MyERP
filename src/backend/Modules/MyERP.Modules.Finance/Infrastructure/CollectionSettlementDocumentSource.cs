using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// Production <see cref="IFinanceCollectionWriteOffSource"/> (WU-FI-005b3b, ADR-128) over FI's own
/// <c>ar_collection_settlement_doc</c> and <c>_line</c>. Reads on the scoped DbContext, which must be enlisted in the
/// supplied transaction (the posting caller's); a soft-deleted row is returned as
/// <see cref="FinanceCollectionWriteOffState.Deleted"/> so posting answers 422 rather than 404. Missing or foreign-company
/// rows return null. Lines are in line number order; the source version is the row version, the base currency is the
/// current MD one and the fingerprint is <see cref="CollectionSettlementFingerprint"/> over the saved header and lines.
/// </summary>
internal sealed class CollectionSettlementDocumentSource(MyErpDbContext db, ICurrencyLookup currencies)
    : IFinanceCollectionWriteOffSource
{
    public async Task<FinanceCollectionWriteOffSnapshot?> ReadWriteOffAsync(long companyId, long settlementId,
        DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (db.Database.CurrentTransaction?.GetDbTransaction() != transaction)
            throw new InvalidOperationException("The settlement source must read inside the caller's transaction.");
        var row = await db.Set<CollectionSettlementDocument>().AsNoTracking()
            .IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .SingleOrDefaultAsync(x => x.Id == settlementId && x.CompanyId == companyId, cancellationToken);
        if (row is null)
            return null;
        var lines = await db.Set<CollectionSettlementDocumentLine>().AsNoTracking()
            .Where(x => x.SettlementDocumentId == row.Id && x.CompanyId == companyId)
            .OrderBy(x => x.LineNo)
            .ToListAsync(cancellationToken);
        // The current MD base currency, not the saved one: a changed base currency makes the settlement unusable (422).
        var baseCurrency = (await currencies.GetBaseCurrencyAsync(cancellationToken))?.Code ?? string.Empty;
        var state = row.IsDeleted
            ? FinanceCollectionWriteOffState.Deleted
            : CollectionSettlementDocumentService.ToState(row.Status);
        return new(row.CompanyId, row.Id, row.Version, CollectionSettlementFingerprint.Create(row, lines), row.CustomerId,
            state, row.BusinessDate, row.Currency, baseCurrency, row.ReceiptId, row.TotalAmount,
            lines.Select(x => new FinanceCollectionWriteOffLine(x.ReceivableId, x.Amount)).ToArray());
    }
}
