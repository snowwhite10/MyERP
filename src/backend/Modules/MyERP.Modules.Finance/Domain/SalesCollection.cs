using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// FI 已過帳的銷售收款來源（ar_collection_receipt，WU-FI-005a1；BR-FI-033、034）。來源鍵 (company, SalesCollection, source)
/// 與 (company, 銀行, 銀行參考) 永久唯一；過帳時未核銷金額等於收款金額，之後只可因核銷而減少。
/// 銀行 ID 是 MD 純量，不建立跨模組外鍵。本切片只支援公司本位幣（BR-FI-031）。
/// </summary>
public sealed class CollectionReceipt : BusinessEntity
{
    public const string SalesCollectionType = "SalesCollection";

    public long CustomerId { get; set; }
    public string SourceType { get; set; } = SalesCollectionType;
    public long SourceId { get; set; }
    public int SourceVersion { get; set; }
    public string Fingerprint { get; set; } = "";
    public long GlJournalId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public long BankAccountId { get; set; }
    public string BankReference { get; set; } = "";
    public string BaseCurrency { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal UnappliedAmount { get; set; }
}

/// <summary>
/// FI 已過帳的收款核銷來源（ar_collection_settlement，WU-FI-005a1；BR-FI-035、037）。一筆核銷只引用一筆收款，
/// 客戶與幣別必須與收款相同；保存後不可修改或刪除。
/// </summary>
public sealed class CollectionSettlement : BusinessEntity
{
    public const string CollectionWriteOffType = "CollectionWriteOff";

    public long CustomerId { get; set; }
    public string SourceType { get; set; } = CollectionWriteOffType;
    public long SourceId { get; set; }
    public int SourceVersion { get; set; }
    public string Fingerprint { get; set; } = "";
    public long GlJournalId { get; set; }
    public long ReceiptId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string BaseCurrency { get; set; } = "";
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// 核銷對已過帳發票應收的分配行（ar_collection_allocation，WU-FI-005a1；BR-FI-035、037、040）。每筆核銷每個應收一行；
/// 不同核銷可再以同一收款核銷同一發票。保存後不可修改或刪除。
/// </summary>
public sealed class CollectionAllocation : BusinessEntity
{
    public long SettlementId { get; set; }
    public long ReceiptId { get; set; }
    public long ReceivableId { get; set; }
    public decimal Amount { get; set; }
}
