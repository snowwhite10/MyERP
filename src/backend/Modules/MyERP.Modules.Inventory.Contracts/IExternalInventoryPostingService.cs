namespace MyERP.Modules.Inventory.Contracts;

/// <summary>外部來源在 INV 異動帳中的固定識別；來源模組不自行組裝過帳字串。</summary>
public static class ExternalInventorySourceTypes
{
    public const string PurchaseInbound = "PURCHASE_INBOUND";
    public const string SalesOutbound = "SALES_OUTBOUND";
}

/// <summary>已保存的 PUR 收貨行；數量為物料基本單位，單價為保存時的成本快照。</summary>
public sealed record PurchaseInboundLine(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, decimal UnitCost, IReadOnlyList<InventoryTrackingLine>? Tracking = null);

/// <summary>已保存的 SD 出貨行；INV 不接受銷售價格作為出庫成本。</summary>
public sealed record SalesOutboundLine(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, IReadOnlyList<InventoryTrackingLine>? Tracking = null);

/// <summary>
/// 僅供 PUR／SD 最終核准處理器使用。呼叫端須先開始包含 SYS 決定與來源狀態的交易，
/// 並從目前公司的已保存單據明細建立行資料；本契約不代表對單據核准狀態的授權。
/// </summary>
public interface IExternalInventoryPostingService
{
    Task<InventoryPostingResult> PostPurchaseInboundAsync(long sourceId, DateOnly businessDate,
        IReadOnlyList<PurchaseInboundLine> lines, CancellationToken token = default);

    Task<InventoryPostingResult> PostSalesOutboundAsync(long sourceId, DateOnly businessDate,
        IReadOnlyList<SalesOutboundLine> lines, CancellationToken token = default);
}
