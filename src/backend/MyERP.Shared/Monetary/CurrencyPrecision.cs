using System.Collections.Frozen;

namespace MyERP.Shared.Monetary;

/// <summary>
/// 各幣別金額小數位與捨入方式。預設 TWD / JPY 0 位、其他 2 位、四捨五入（AwayFromZero）。
/// 不可變：<see cref="With"/> 回傳新實例。幣別主檔（MD）完成後可由主檔資料建立。
/// </summary>
public sealed record CurrencyPrecision
{
    public const int MaxDecimals = 8;

    public static CurrencyPrecision Default { get; } = new(
        new Dictionary<string, int>(StringComparer.Ordinal) { ["TWD"] = 0, ["JPY"] = 0 }.ToFrozenDictionary(StringComparer.Ordinal),
        defaultDecimals: 2);

    private readonly FrozenDictionary<string, int> _decimals;

    private CurrencyPrecision(FrozenDictionary<string, int> decimals, int defaultDecimals)
    {
        _decimals = decimals;
        DefaultDecimals = defaultDecimals;
    }

    /// <summary>未特別設定的幣別使用的小數位。</summary>
    public int DefaultDecimals { get; }

    /// <summary>中間值的捨入方式。</summary>
    public MidpointRounding Rounding { get; init; } = MidpointRounding.AwayFromZero;

    public int DecimalsFor(string currency) =>
        _decimals.TryGetValue(Money.NormalizeCurrency(currency), out var decimals) ? decimals : DefaultDecimals;

    /// <summary>回傳設定了指定幣別小數位的新實例。</summary>
    public CurrencyPrecision With(string currency, int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, MaxDecimals);

        var copy = new Dictionary<string, int>(_decimals, StringComparer.Ordinal) { [Money.NormalizeCurrency(currency)] = decimals };
        return new CurrencyPrecision(copy.ToFrozenDictionary(StringComparer.Ordinal), DefaultDecimals) { Rounding = Rounding };
    }
}
