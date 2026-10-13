using System.Text.Json.Serialization;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>銷項（Output）或進項（Input）；API 以名稱字串傳遞。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaxDirection>))]
public enum TaxDirection { Output, Input }

public sealed record TaxCodeInfo(string Code, string Name, bool IsActive, bool AppliesToOutput, bool AppliesToInput);
public sealed record TaxCalculation(decimal RatePercent, decimal Net, decimal Tax, decimal Gross);

/// <summary>其他模組查詢目前公司稅別與業務日期稅率的唯一入口（ADR-038、ADR-049）。</summary>
public interface ITaxRateLookup
{
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
