using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Reports to MD that an account code is referenced by a saved Posted journal line of the current company
/// (BR-FI-027, ADR-039). Matching is exact on the saved account code snapshot; parents and prefixes are not
/// in use by themselves. Only FI tables are read; the company query filter scopes the lookup.
/// </summary>
public sealed class FinanceAccountUsage(MyErpDbContext db) : IAccountUsage
{
    public Task<bool> IsInUseAsync(string accountCode, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(accountCode)) return Task.FromResult(false);
        return db.Set<GlJournalLine>().AnyAsync(line => line.AccountCode == accountCode &&
            db.Set<GlJournal>().Any(journal => journal.Id == line.JournalId && journal.Status == GlJournalStatus.Posted), token);
    }
}
