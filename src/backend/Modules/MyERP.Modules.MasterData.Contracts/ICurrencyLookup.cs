using MyERP.Shared.Monetary;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>目前公司的幣別資料；<see cref="DecimalPlaces"/> 為金額小數位（ADR-037）。</summary>
public sealed record CurrencyInfo(string Code, string Name, string? Symbol, int DecimalPlaces, bool IsBase, bool IsActive);
/// <summary>比價使用的有效匯率證據；本位幣 RateId 為 null，EffectiveFrom 為查詢日。</summary>
public sealed record ExchangeRateSnapshot(decimal Rate, DateOnly EffectiveFrom, DateOnly? EffectiveTo, long? RateId);

/// <summary>其他模組取得本位幣、幣別與匯率的唯一入口（不得直接讀 md_currency／md_exchange_rate，ADR-037）。</summary>
public interface ICurrencyLookup
{
    /// <summary>目前公司的本位幣；尚未設定時回 null。</summary>
    Task<CurrencyInfo?> GetBaseCurrencyAsync(CancellationToken token = default);

    /// <summary>依 ISO 4217 代碼（不分大小寫）找目前公司的幣別，含停用；不存在回 null。</summary>
    Task<CurrencyInfo?> FindCurrencyAsync(string currencyCode, CancellationToken token = default);

    /// <summary>
    /// 業務日期 <paramref name="date"/> 適用的匯率：1 外幣 = 回傳值 本位幣；本位幣回 1。
    /// 幣別不存在回 404 <c>MD.CURRENCY.NOT_FOUND</c>；公司沒有本位幣回 422 <c>MD.EXCHANGE_RATE.NO_BASE_CURRENCY</c>；
    /// 沒有涵蓋該日的匯率回 422 <c>MD.EXCHANGE_RATE.NOT_FOUND</c>。停用幣別仍可查詢（歷史單據）。
    /// </summary>
    Task<decimal> GetRateAsync(string currencyCode, DateOnly date, CancellationToken token = default);

    /// <summary>取得同一有效期間的匯率與生效日，供採購比價及決標快照使用。</summary>
    Task<ExchangeRateSnapshot> GetRateSnapshotAsync(string currencyCode, DateOnly date, CancellationToken token = default);

    /// <summary>以 <see cref="CurrencyPrecision.Default"/> 為基礎、套用目前公司幣別小數位的捨入設定，供 <c>Money.Round</c> 使用。</summary>
    Task<CurrencyPrecision> GetPrecisionAsync(CancellationToken token = default);
}
