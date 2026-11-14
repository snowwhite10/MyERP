using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// FI 銷售收款單據（ar_collection_receipt_doc，WU-FI-005b1；REQ_F4116.1.1–3、BR-FI-031～034、ADR-126）。草稿與審批狀態
/// 的唯一來源；最終核准後由 FI 過帳服務寫入已過帳來源 <see cref="CollectionReceipt"/>（其 <c>source_id</c> 即本單 ID）。
/// 金額只限公司本位幣；客戶與資金帳戶是 MD 純量 ID，不建跨模組外鍵。同公司同資金帳戶的銀行參考在未刪除單據中唯一。
/// 刪除為軟刪除（只限 Draft／Rejected），保留單號歷史並釋出銀行參考。
/// </summary>
public sealed class CollectionReceiptDocument : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public long CustomerId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public long BankAccountId { get; set; }
    public required string BankReference { get; set; }
    public string? Remark { get; set; }

    /// <summary>刪除以 <see cref="IsDeleted"/> 表示（來源埠映射為 Deleted）。</summary>
    public CollectionReceiptDocumentStatus Status { get; set; } = CollectionReceiptDocumentStatus.Draft;

    public string? CustomFields { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>收款單據保存狀態（BR-FI-032）；以字串儲存。對外以 Application 的 FinanceCollectionReceiptState 表示。</summary>
public enum CollectionReceiptDocumentStatus { Draft, Pending, Rejected, Approved, Posted }
