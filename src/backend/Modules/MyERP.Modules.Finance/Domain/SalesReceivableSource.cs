using System.Text.Json.Serialization;
using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<SalesSourceType>))]
public enum SalesSourceType
{
    /// <summary>SD 發票：增加應收（BR-FI-009、010）。</summary>
    Invoice,

    /// <summary>SD 已開票銷退：信用票據，減少原發票應收（BR-FI-011、012）。</summary>
    Return
}

/// <summary>
/// FI 已過帳的 SD 銷售來源（ar_sales_source，WU-FI-002a1）。保存來源身分、版本、指紋、傳票與
/// 原幣／本位幣淨額、稅額、總額快照。來源鍵 (company, type, source) 永久唯一；保存後不可修改或刪除。
/// SD 發票／銷退 ID 與 MD 客戶 ID 都是純量，不建立跨模組外鍵。
/// </summary>
public sealed class SalesSource : BusinessEntity
{
    public long CustomerId { get; set; }
    public SalesSourceType SourceType { get; set; }
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
/// 信用來源對原發票應收的逐發票行分配（ar_credit_allocation，WU-FI-002a1）。同一信用來源每個發票行只可分配一次；
/// 累計不超過原發票成分由過帳服務在信用鍵鎖下驗證（WU-FI-002a3）。保存後不可修改或刪除。
/// </summary>
public sealed class CreditAllocation : BusinessEntity
{
    public long CreditSourceId { get; set; }
    public long ReceivableId { get; set; }
    public long InvoiceLineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal BaseNetAmount { get; set; }
    public decimal BaseTaxAmount { get; set; }
    public decimal BaseGrossAmount { get; set; }
}
