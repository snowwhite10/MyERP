using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

/// <summary>
/// 其他入庫單表頭（inv_other_receipt，REQ_DS43.136，ADR-054）。草稿與駁回後可修訂；送審後明細凍結，
/// 最終核准與 <c>OTHER_RECEIPT</c> 異動帳過帳同一交易完成，狀態直接成為 <see cref="InventoryDocumentStatus.Posted"/>。
/// </summary>
public sealed class OtherReceipt : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public InventoryDocumentStatus Status { get; set; } = InventoryDocumentStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<OtherReceiptLine> Lines { get; } = [];

    /// <summary>單據金額 = 明細金額加總（不另存；供審批門檻與列表顯示）。</summary>
    public decimal TotalAmount => Lines.Sum(x => x.Amount);
}

/// <summary>其他入庫明細（inv_other_receipt_line，REQ_DS43.137）：數量為物料基本單位；物料、倉庫、儲位跨模組只存 ID。</summary>
public sealed class OtherReceiptLine : BusinessEntity
{
    public long ReceiptId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }

    /// <summary>明細金額 = 數量 × 單價，在明細行取至 4 位小數（ADR-055）。</summary>
    public decimal Amount { get; set; }

    public static decimal AmountOf(decimal quantity, decimal unitCost) =>
        decimal.Round(quantity * unitCost, 4, MidpointRounding.AwayFromZero);

    /// <summary>批號／序號（ADR-060 第 8 點）；草稿可先不填，送審時須與物料追蹤方式相符且合計等於數量。</summary>
    public List<OtherReceiptLineTracking> Tracking { get; } = [];
}

/// <summary>
/// 其他入庫明細的批號或序號（inv_other_receipt_line_tracking，REQ_DS43.145）：擇一填寫；
/// 生產日期與有效期限只用於批號，過帳時建立或核對 <c>inv_lot</c>。
/// </summary>
public sealed class OtherReceiptLineTracking : BusinessEntity
{
    public long LineId { get; set; }
    public int SeqNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly? ManufactureDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}
