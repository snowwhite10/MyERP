using System.Text.Json.Serialization;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>銷項（Output）或進項（Input）；API 以名稱字串傳遞。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaxDirection>))]
public enum TaxDirection { Output, Input }

public sealed record TaxCodeInfo(string Code, string Name, bool IsActive, bool AppliesToOutput, bool AppliesToInput);
public sealed record TaxCalculation(decimal RatePercent, decimal Net, decimal Tax, decimal Gross);

/// <summary>其他模組查詢目前公司稅別與業務日期稅率的唯一入口（ADR-038）。</summary>
public interface ITaxRateLookup
{
    Task<TaxCodeInfo?> FindTaxCodeAsync(string code, CancellationToken token = default);
    Task<decimal> GetRatePercentAsync(string code, TaxDirection direction, DateOnly date, CancellationToken token = default);
    Task<TaxCalculation> CalculateAsync(string code, TaxDirection direction, DateOnly date, decimal amount,
        bool priceIncludesTax, string currencyCode, CancellationToken token = default);
}
