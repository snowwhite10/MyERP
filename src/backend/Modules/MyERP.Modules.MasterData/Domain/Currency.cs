using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內的幣別（ISO 4217 代碼）；<see cref="IsBase"/> 標示本位幣，每間公司最多一個（ADR-037）。</summary>
public sealed class Currency : MasterCategory
{
    public const int CodeLength = 3;
    public const int SymbolMaxLength = 10;
    public const int MaxDecimalPlaces = 4;

    public string? Symbol { get; set; }

    /// <summary>金額小數位 0–4。</summary>
    public int DecimalPlaces { get; set; }

    public bool IsBase { get; set; }
}

/// <summary>外幣對本位幣的記帳匯率：1 外幣 = <see cref="Rate"/> 本位幣；同幣別未刪除的期間不可重疊（ADR-037）。</summary>
public sealed class ExchangeRate : BusinessEntity, ISoftDeletable
{
    public const int RateScale = 8;
    public const int RatePrecision = 19;

    public long CurrencyId { get; set; }
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>到期日（含當日）；null 表示持續有效。</summary>
    public DateOnly? EffectiveTo { get; set; }

    public decimal Rate { get; set; }
    public bool IsDeleted { get; set; }
    public Currency? Currency { get; set; }
}
