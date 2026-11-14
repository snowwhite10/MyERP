using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// FI 收款核銷單據（ar_collection_settlement_doc，WU-FI-005b2；REQ_F4116.5.1–3、BR-FI-035～040、ADR-127）。草稿與審批狀態
/// 的唯一來源；最終核准後由 FI 核銷過帳服務寫入已過帳來源 <see cref="CollectionSettlement"/>（其 <c>source_id</c> 即本單 ID）。
/// <see cref="ReceiptId"/> 是 FI 收款單據 ID（已過帳收款的 <c>source_id</c>）；客戶取自該已過帳收款。金額只限公司本位幣，
/// 表頭合計＝明細合計。刪除為軟刪除（只限 Draft／Rejected），保留單號歷史；明細保留。
/// </summary>
public sealed class CollectionSettlementDocument : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public long CustomerId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string Currency { get; set; }
    public long ReceiptId { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Remark { get; set; }

    /// <summary>刪除以 <see cref="IsDeleted"/> 表示（來源埠映射為 Deleted）。</summary>
    public CollectionSettlementDocumentStatus Status { get; set; } = CollectionSettlementDocumentStatus.Draft;

    public string? CustomFields { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// 核銷單據分配行（ar_collection_settlement_doc_line）。每張單據每個應收一行，行號依應收 ID 升冪 1..n；
/// 應收為同公司 FI 已過帳應收（<c>ar_posted_receivable</c>，模組內外鍵）。草稿修改時整批替換。
/// </summary>
public sealed class CollectionSettlementDocumentLine : BusinessEntity
{
    public long SettlementDocumentId { get; set; }
    public int LineNo { get; set; }
    public long ReceivableId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>核銷單據保存狀態（BR-FI-032）；以字串儲存。對外以 Application 的 FinanceCollectionWriteOffState 表示。</summary>
public enum CollectionSettlementDocumentStatus { Draft, Pending, Rejected, Approved, Posted }
