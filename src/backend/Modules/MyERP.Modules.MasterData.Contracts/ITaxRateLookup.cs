using System.Text.Json.Serialization;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>銷項（Output）或進項（Input）；API 以名稱字串傳遞。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaxDirection>))]
public enum TaxDirection { Output, Input }

public sealed record TaxCodeInfo(string Code, string Name, bool IsActive, bool AppliesToOutput, bool AppliesToInput);
/// <summary>單行稅額試算結果；<paramref name="DecimalPlaces"/> 是捨入所用的幣別小數位（供顯示補零）。</summary>
public sealed record TaxCalculation(decimal RatePercent, decimal Net, decimal Tax, decimal Gross, int DecimalPlaces);
/// <summary>指定進項／銷項稅率 ID 在業務日期的有效稅率證據。</summary>
public sealed record TaxRateSnapshot(long RateId, decimal RatePercent, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

/// <summary>其他模組查詢目前公司稅別與業務日期稅率的唯一入口（ADR-038、ADR-049）。</summary>
public interface ITaxRateLookup
{
    /// <summary>驗證目前公司指定日期的有效稅率 ID 與進／銷項方向；不存在或不適用時拋出業務錯誤。</summary>
    Task RequireRateAsync(long rateId, TaxDirection direction, DateOnly date, CancellationToken token = default);
    /// <summary>按目前公司、方向及有效日期驗證稅率，返回比價所需百分比及有效期間。</summary>
    Task<TaxRateSnapshot> GetRateSnapshotAsync(long rateId, TaxDirection direction, DateOnly date, CancellationToken token = default);
    /// <summary>依代碼找稅別，含停用；不存在回 null。</summary>
    Task<TaxCodeInfo?> FindTaxCodeAsync(string code, CancellationToken token = default);

    /// <summary>
    /// 業務日期的稅率百分比。稅別不存在 404 <c>MD.TAX_CODE.NOT_FOUND</c>；停用 422 <c>MD.TAX_CODE.INACTIVE</c>
    /// （重算歷史單據時傳 <paramref name="includeInactive"/> = true）；方向不符 422 <c>MD.TAX_RATE.DIRECTION_MISMATCH</c>；
    /// 沒有稅率 422 <c>MD.TAX_RATE.NOT_FOUND</c>。
    /// </summary>
    Task<decimal> GetRatePercentAsync(string code, TaxDirection direction, DateOnly date, bool includeInactive = false,
        CancellationToken token = default);

    /// <summary>
    /// 以 <see cref="GetRatePercentAsync"/> 的稅率試算單行稅額；幣別須存在於幣別主檔（可為停用），
    /// 否則 404 <c>MD.CURRENCY.NOT_FOUND</c>；金額按該幣別小數位捨入。
    /// </summary>
    Task<TaxCalculation> CalculateAsync(string code, TaxDirection direction, DateOnly date, decimal amount,
        bool priceIncludesTax, string currencyCode, bool includeInactive = false, CancellationToken token = default);
}

/// <summary>由引用稅別的模組（SD、PUR、FI）實作並註冊；MD 刪除稅別、修改或刪除稅率前逐一詢問（ADR-049）。</summary>
public interface ITaxCodeUsage
{
    /// <summary>
    /// 目前公司是否有單據使用此稅別且業務日期落在 <paramref name="from"/>～<paramref name="to"/>（含兩端；
    /// <paramref name="to"/> 為 null 表示不設上限）。
    /// </summary>
    Task<bool> IsInUseAsync(string taxCode, DateOnly from, DateOnly? to, CancellationToken token = default);
}
