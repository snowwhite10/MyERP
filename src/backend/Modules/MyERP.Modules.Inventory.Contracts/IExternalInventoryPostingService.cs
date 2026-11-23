namespace MyERP.Modules.Inventory.Contracts;

/// <summary>外部來源在 INV 異動帳中的固定識別；來源模組不自行組裝過帳字串。</summary>
public static class ExternalInventorySourceTypes
{
    public const string PurchaseInbound = "PURCHASE_INBOUND";
    public const string SalesOutbound = "SALES_OUTBOUND";
    public const string SalesReturn = "SALES_RETURN";
}

/// <summary>已保存的 PUR 收貨行；數量為物料基本單位，單價為保存時的成本快照。</summary>
public sealed record PurchaseInboundLine(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, decimal UnitCost, IReadOnlyList<InventoryTrackingLine>? Tracking = null);

/// <summary>已保存的 SD 出貨行；INV 不接受銷售價格作為出庫成本。</summary>
public sealed record SalesOutboundLine(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, IReadOnlyList<InventoryTrackingLine>? Tracking = null);

/// <summary>
/// 已保存的 SD 銷退行（ADR-188）：<paramref name="LineNo"/> 為銷退單內永久唯一行號，
/// <paramref name="OriginalMovementId"/> 為原出貨的 <c>SALES_OUTBOUND</c> 出庫異動；數量為物料基本單位。
/// 成本由 INV 以原出庫成本推導，SD 不得傳入售價或成本；批號／序號物料暫不支援（ADR-184 第 6 點）。
/// </summary>
public sealed record SalesReturnInboundLine(int LineNo, long OriginalMovementId, long ItemId, long WarehouseId,
    long? LocationId, decimal Quantity);

/// <summary>銷退入庫證據：新異動 ID 與所採用的單位成本（原出庫成本不明時為 null，按入庫當時平均成本計價）。</summary>
public sealed record SalesReturnInboundMovement(int LineNo, long MovementId, long OriginalMovementId, decimal? UnitCost);

/// <summary>銷退入庫結果；<see cref="Movements"/> 按行號升冪。</summary>
public sealed record SalesReturnInboundResult(bool Replayed, IReadOnlyList<SalesReturnInboundMovement> Movements);

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

    /// <summary>
    /// SD 銷退入庫（ADR-188）：原出庫須屬目前公司、為 <c>SALES_OUTBOUND</c> 出庫且物料相同，同一原出庫的本次合計數量不得超過原數量；
    /// 以 (公司, SALES_RETURN, 銷退 ID, 行號) 冪等，相同內容重送回既有異動，不同內容 409。
    /// 跨多張銷退的累計可退量由 SD 在來源鎖內控制（BR-SD-044）。
    /// </summary>
    Task<SalesReturnInboundResult> PostSalesReturnInboundAsync(long sourceId, DateOnly businessDate,
        IReadOnlyList<SalesReturnInboundLine> lines, CancellationToken token = default);
}
