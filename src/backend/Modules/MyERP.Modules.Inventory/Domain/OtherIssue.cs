using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

/// <summary>
/// 其他出庫單表頭（inv_other_issue，REQ_DS43.138，ADR-057）。草稿與駁回後可修訂；送審後明細凍結，
/// 最終核准與 <c>OTHER_ISSUE</c> 異動帳過帳同一交易完成，狀態直接成為 <see cref="InventoryDocumentStatus.Posted"/>。
/// </summary>
public sealed class OtherIssue : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public InventoryDocumentStatus Status { get; set; } = InventoryDocumentStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<OtherIssueLine> Lines { get; } = [];

    /// <summary>單據金額 = 明細金額加總（不另存；供審批門檻與列表顯示）。</summary>
    public decimal TotalAmount => Lines.Sum(x => x.Amount);
}

/// <summary>其他出庫明細（inv_other_issue_line，REQ_DS43.139）：數量為物料基本單位；物料、倉庫、儲位跨模組只存 ID。</summary>
public sealed class OtherIssueLine : BusinessEntity
{
    public long IssueId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }

    /// <summary>審批估算金額 = 數量 × 估算單價，在明細行取至 4 位小數（ADR-057），不代表已入帳成本。</summary>
    public decimal Amount { get; set; }

    public static decimal AmountOf(decimal quantity, decimal unitCost) =>
        decimal.Round(quantity * unitCost, 4, MidpointRounding.AwayFromZero);

    /// <summary>要出庫的批號／序號（ADR-060 第 8 點）；草稿可先不填，送審時須與物料追蹤方式相符且合計等於數量。</summary>
    public List<OtherIssueLineTracking> Tracking { get; } = [];
}

/// <summary>其他出庫明細的批號或序號（inv_other_issue_line_tracking，REQ_DS43.146）：擇一填寫，過帳時核對該結存鍵的批序號結存。</summary>
public sealed class OtherIssueLineTracking : BusinessEntity
{
    public long LineId { get; set; }
    public int SeqNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public decimal Quantity { get; set; }
}
