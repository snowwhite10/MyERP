using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Application;

/// <summary>所有 INV 來源建立引用時必經此服務；引用表只追加，單據作廢亦保留歷史引用。</summary>
public sealed class InventoryReferenceService(MyErpDbContext db, IInventoryReferenceGuard guard)
    : IItemUsage, IWarehouseUsage, IStorageLocationUsage
{
    public async Task<InventoryReference> RecordAsync(string sourceKey, long itemId, long warehouseId,
        long? locationId, CancellationToken token = default) =>
        (await RecordWithTrackingAsync(sourceKey, itemId, warehouseId, locationId, token)).Reference;

    /// <summary>同 <see cref="RecordAsync"/>，另回傳在 MD 共享鎖內讀到的物料追蹤方式（ADR-060 第 3 點）。</summary>
    public async Task<(InventoryReference Reference, ItemTrackingMode TrackingMode)> RecordWithTrackingAsync(string sourceKey,
        long itemId, long warehouseId, long? locationId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(sourceKey) || sourceKey.Length > 100)
            throw new RequestValidationException("sourceKey", "Source key must contain 1 to 100 characters.");
        if (db.CurrentCompanyId is not > 0) throw new BusinessRuleException("COMMON.COMPANY.REQUIRED", "Company is required.");
        return await guard.RunWithCodesAsync(itemId, warehouseId, locationId, async codes =>
        {
            if (await db.Set<InventoryReference>().AnyAsync(x => x.SourceKey == sourceKey, token))
                throw new ConflictException("INV.REFERENCE.DUPLICATE", "Source already has an inventory reference.");
            var reference = new InventoryReference { CompanyId = db.CurrentCompanyId.Value, SourceKey = sourceKey,
                ItemId = itemId, ItemCode = codes.ItemCode, WarehouseId = warehouseId,
                WarehouseCode = codes.WarehouseCode, LocationId = locationId, LocationCode = codes.LocationCode };
            db.Add(reference);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            { throw new ConflictException("INV.REFERENCE.DUPLICATE", "Source already has an inventory reference."); }
            return (reference, codes.TrackingMode);
        }, token);
    }

    public Task<bool> IsInUseAsync(string itemCode, CancellationToken token = default) =>
        db.Set<InventoryReference>().AnyAsync(x => x.ItemCode == itemCode, token);

    Task<bool> IWarehouseUsage.IsInUseAsync(string warehouseCode, CancellationToken token) =>
        db.Set<InventoryReference>().AnyAsync(x => x.WarehouseCode == warehouseCode, token);

    public Task<bool> IsInUseAsync(string warehouseCode, string locationCode, CancellationToken token = default) =>
        db.Set<InventoryReference>().AnyAsync(x => x.WarehouseCode == warehouseCode && x.LocationCode == locationCode, token);
}
