namespace MyERP.Shared.Monetary;

/// <summary>單一明細行的未稅、稅額與含稅金額。</summary>
public sealed record TaxAmounts(decimal Net, decimal Tax, decimal Gross);

/// <summary>稅額計算；表頭金額應加總明細結果，不重新計算。</summary>
public static class Tax
{
    public static TaxAmounts Calculate(decimal amount, decimal ratePercent, bool priceIncludesTax, string currency) =>
        Calculate(amount, ratePercent, priceIncludesTax, currency, CurrencyPrecision.Default);

    public static TaxAmounts Calculate(
        decimal amount, decimal ratePercent, bool priceIncludesTax, string currency,
        CurrencyPrecision precision)
    {
        ArgumentNullException.ThrowIfNull(precision);
        if (ratePercent < 0m || ratePercent > 100m || decimal.Round(ratePercent, 4) != ratePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(ratePercent));
        }

        if (priceIncludesTax)
        {
            var gross = Money.Round(amount, currency, precision);
            var net = Money.Round(gross * 100m / (100m + ratePercent), currency, precision);
            return new TaxAmounts(net, gross - net, gross);
        }

        var untaxed = Money.Round(amount, currency, precision);
        var tax = Money.Round(untaxed * ratePercent / 100m, currency, precision);
        return new TaxAmounts(untaxed, tax, untaxed + tax);
    }
}
