namespace MyERP.Shared.Monetary;

/// <summary>
/// 金額捨入（MASTER_PROMPT 6.3）：一律在明細行層級呼叫，表頭金額 = 明細加總，不得各自計算。
/// </summary>
public static class Money
{
    public static int Decimals(string currency) => CurrencyPrecision.Default.DecimalsFor(currency);

    public static decimal Round(decimal value, string currency) => Round(value, currency, CurrencyPrecision.Default);

    public static decimal Round(decimal value, string currency, CurrencyPrecision precision)
    {
        ArgumentNullException.ThrowIfNull(precision);
        return Math.Round(value, precision.DecimalsFor(currency), precision.Rounding);
    }

    /// <summary>驗證並轉為大寫 ISO 4217 三碼。</summary>
    internal static string NormalizeCurrency(string currency)
    {
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetter))
        {
            throw new ArgumentException($"Invalid ISO 4217 currency code '{currency}'.", nameof(currency));
        }

        return currency.ToUpperInvariant();
    }
}
