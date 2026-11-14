using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Reports to MD that a fund account is referenced by a posted sales collection receipt of the current company
/// (<c>ar_collection_receipt.bank_account_id</c>; BR-FI-155, ADR-124), so MD refuses to delete it. FI stores the stable
/// MD ID, so matching is by ID and the code is ignored; deactivation stays allowed. Only FI tables are read; the company
/// query filter scopes the lookup and receipts exist only once posted (a rolled-back or rejected posting leaves none).
/// </summary>
public sealed class FinanceFundAccountUsage(MyErpDbContext db) : IFundAccountUsage
{
    public Task<bool> IsInUseAsync(long fundAccountId, string fundAccountCode, CancellationToken token = default) =>
        fundAccountId <= 0
            ? Task.FromResult(false)
            : db.Set<CollectionReceipt>().AnyAsync(x => x.BankAccountId == fundAccountId, token);
}
