using System.Text.Json.Serialization;
using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<PurchaseSourceType>))]
public enum PurchaseSourceType
{
    /// <summary>PUR 已三單比對的採購發票：增加應付（BR-FI-015～019）。採購退貨的 FI 票據另行規範（BR-FI-022）。</summary>
    PurchaseInvoice
}

/// <summary>
/// FI 已過帳的 PUR 採購發票來源（ap_purchase_source，WU-FI-003a1）。保存來源身分、版本、指紋、傳票與
/// 原幣／本位幣淨額、進項稅額、總額快照。來源鍵 (company, type, source) 永久唯一；保存後不可修改或刪除。
/// PUR 發票 ID 與 MD 供應商 ID 都是純量，不建立跨模組外鍵。
/// </summary>
public sealed class PurchaseSource : BusinessEntity
{
    public long SupplierId { get; set; }
    public PurchaseSourceType SourceType { get; set; }
    public long SourceId { get; set; }
    public int SourceVersion { get; set; }
    public string Fingerprint { get; set; } = "";
    public long GlJournalId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Currency { get; set; } = "";
    public string BaseCurrency { get; set; } = "";
    public decimal ExchangeRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal BaseNetAmount { get; set; }
    public decimal BaseTaxAmount { get; set; }
    public decimal BaseGrossAmount { get; set; }
}

/// <summary>
/// 採購發票產生的已過帳應付（ap_posted_payable，WU-FI-003a1）。每個來源最多一筆；原始金額、幣別、供應商與
/// 過帳日由 SQL 觸發器驗證等於來源，保存後不可改；之後的付款核銷只減少未付金額（BR-FI-019、022）。
/// </summary>
public sealed class PostedPayable : BusinessEntity
{
    public long SupplierId { get; set; }
    public long PurchaseSourceId { get; set; }
    public DateOnly PostingDate { get; set; }
    public string Currency { get; set; } = "";
    public string BaseCurrency { get; set; } = "";
    public decimal OriginalAmount { get; set; }
    public decimal OpenAmount { get; set; }
    public decimal OriginalBaseAmount { get; set; }
    public decimal OpenBaseAmount { get; set; }
}
