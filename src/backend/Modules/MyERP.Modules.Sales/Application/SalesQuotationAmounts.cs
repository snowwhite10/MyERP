using MyERP.Shared.Errors;
using MyERP.Shared.Monetary;

namespace MyERP.Modules.Sales.Application;

/// <summary>Validated quote line and MD tax/exchange snapshots.</summary>
public sealed record SalesQuotationAmountInput(int LineNo, decimal Quantity, decimal UnitPrice,
    decimal DiscountRate, string CurrencyCode, decimal TaxRatePercent, decimal ExchangeRate);

public sealed record SalesQuotationLineAmounts(int LineNo, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

public sealed record SalesQuotationAmountResult(IReadOnlyList<SalesQuotationLineAmounts> Lines,
    decimal BaseNet, decimal BaseTax, decimal BaseGross);

/// <summary>REQ_F4112.1.2: round each saved original amount before conversion and header sums.</summary>
public static class SalesQuotationAmounts
{
    private const decimal DecimalNineteenFourLimit = 1_000_000_000_000_000m;

    public static SalesQuotationAmountResult Calculate(
        IReadOnlyList<SalesQuotationAmountInput> inputs, string baseCurrencyCode)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var lines = new List<SalesQuotationLineAmounts>(inputs.Count);
        decimal baseNet = 0, baseTax = 0, baseGross = 0;
        try
        {
            foreach (var input in inputs)
            {
                if (input.ExchangeRate <= 0)
                    throw new ArgumentOutOfRangeException(nameof(inputs), "Exchange rate must be positive.");
                var net = Money.Round(checked(input.Quantity * input.UnitPrice *
                    (1m - input.DiscountRate / 100m)), input.CurrencyCode);
                var tax = Money.Round(checked(net * input.TaxRatePercent / 100m), input.CurrencyCode);
                var gross = checked(net + tax);
                var convertedNet = Money.Round(checked(net * input.ExchangeRate), baseCurrencyCode);
                var convertedTax = Money.Round(checked(tax * input.ExchangeRate), baseCurrencyCode);
                var convertedGross = Money.Round(checked(gross * input.ExchangeRate), baseCurrencyCode);
                RequireStorable(net, tax, gross, convertedNet, convertedTax, convertedGross);
                lines.Add(new(input.LineNo, net, tax, gross, convertedNet, convertedTax, convertedGross));
                baseNet = checked(baseNet + convertedNet);
                baseTax = checked(baseTax + convertedTax);
                baseGross = checked(baseGross + convertedGross);
                RequireStorable(baseNet, baseTax, baseGross);
            }
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException("SAL.QUOTATION.AMOUNT_OVERFLOW",
                "Quotation amount exceeds the supported range.");
        }
        return new(lines, baseNet, baseTax, baseGross);
    }

    private static void RequireStorable(params decimal[] amounts)
    {
        if (amounts.Any(value => value < 0 || value >= DecimalNineteenFourLimit))
            throw new BusinessRuleException("SAL.QUOTATION.AMOUNT_OVERFLOW",
                "Quotation amount exceeds DECIMAL(19,4).");
    }
}
