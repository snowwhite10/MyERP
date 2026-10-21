using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

/// <summary>不可變的逐行決標證據；轉單於後續工作單元建立獨立來源連結。</summary>
public sealed class RfqAward : BusinessEntity
{
    public long RfqId { get; set; }
    public long RfqLineId { get; set; }
    public long QuotationId { get; set; }
    public long QuotationLineId { get; set; }
    public long SupplierId { get; set; }
    public required string Reason { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public required string CurrencyCode { get; set; }
    public required string BaseCurrencyCode { get; set; }
    public long TaxRateId { get; set; }
    public decimal TaxRatePercent { get; set; }
    public DateOnly TaxRateFrom { get; set; }
    public DateOnly? TaxRateTo { get; set; }
    public decimal ExchangeRate { get; set; }
    public long? ExchangeRateId { get; set; }
    public DateOnly ExchangeRateFrom { get; set; }
    public DateOnly? ExchangeRateTo { get; set; }
    public DateOnly ComparisonDate { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
}
