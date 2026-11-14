using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Reports to MD that an account code is referenced by a saved Posted journal line of the current company
/// (BR-FI-027, ADR-039) or by the company's SD invoice account map (BR-FI-147, WU-FI-002a4b) or PUR purchase invoice
/// account map (BR-FI-150, WU-FI-003a2c), so MD keeps a mapped
/// account deletable-protected and postable (no delete, no direction change, no child). Matching is exact on the saved
/// code; parents and prefixes are not in use by themselves. Only FI tables are read; the company query filter scopes
/// the lookup.
/// </summary>
public sealed class FinanceAccountUsage(MyErpDbContext db) : IAccountUsage
{
    public async Task<bool> IsInUseAsync(string accountCode, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(accountCode)) return false;
        if (await db.Set<SalesInvoiceAccountMapping>().AnyAsync(map => map.ReceivableAccountCode == accountCode
                || map.RevenueAccountCode == accountCode || map.OutputTaxAccountCode == accountCode, token))
            return true;
        if (await db.Set<PurchaseInvoiceAccountMapping>().AnyAsync(map => map.PayableAccountCode == accountCode
                || map.PurchaseAccountCode == accountCode || map.InputTaxAccountCode == accountCode, token))
            return true;
        return await db.Set<GlJournalLine>().AnyAsync(line => line.AccountCode == accountCode &&
            db.Set<GlJournal>().Any(journal => journal.Id == line.JournalId && journal.Status == GlJournalStatus.Posted), token);
    }
}
