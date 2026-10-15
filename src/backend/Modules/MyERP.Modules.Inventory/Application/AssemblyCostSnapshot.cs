using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Application;

/// <summary>在呼叫端交易中取得物料估值快照；INV 過帳使用相同的交易鎖。</summary>
public sealed class AssemblyCostSnapshot(MyErpDbContext db, IInventoryValuationQuery valuation)
{
    public async Task<InventoryValuation> GetLockedAsync(long itemId, CancellationToken token = default)
    {
        if (itemId <= 0) throw new BusinessRuleException("INV.ASSEMBLY.INVALID_ITEM", "Item id must be positive.");
        if (db.CurrentCompanyId is not long companyId || companyId <= 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Assembly cost snapshot requires a caller transaction.");
        await LockItemAsync(db, companyId, itemId, token);
        return await valuation.GetItemAsync(itemId, token: token);
    }

    internal static Task<int> LockItemAsync(MyErpDbContext db, long companyId, long itemId, CancellationToken token) =>
        db.Database.ExecuteSqlAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = { $"inv_value:{companyId}:{itemId}" },
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, 'Inventory valuation lock was not granted.', 1;
            """, token);
}
