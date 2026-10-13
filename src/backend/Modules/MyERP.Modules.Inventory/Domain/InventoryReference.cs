using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

/// <summary>INV 持久化來源對 MD 主檔的引用；來源鍵由寫入方穩定提供。</summary>
public sealed class InventoryReference : BusinessEntity
{
    public required string SourceKey { get; set; }
    public long ItemId { get; set; }
    public required string ItemCode { get; set; }
    public long WarehouseId { get; set; }
    public required string WarehouseCode { get; set; }
    public long? LocationId { get; set; }
    public string? LocationCode { get; set; }
}
