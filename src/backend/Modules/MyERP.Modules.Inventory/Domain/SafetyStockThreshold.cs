using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

/// <summary>公司、物料及倉庫的永久唯一最低現存量設定。</summary>
public sealed class SafetyStockThreshold : BusinessEntity, ISoftDeletable
{
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public decimal MinimumQuantity { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public long? DeletedBy { get; set; }
}
