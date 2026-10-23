using MyERP.Shared.Errors;
using MyERP.Shared.Monetary;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>已驗證的人工訂單行與 MD 匯率／進項稅率快照。</summary>
public sealed record PurchaseOrderAmountInput(int LineNo, decimal Quantity, decimal UnitPrice,
    string CurrencyCode, decimal TaxRatePercent, decimal ExchangeRate);

public sealed record PurchaseOrderLineAmounts(int LineNo, decimal OriginalNet,
    decimal OriginalTax, decimal OriginalGross, decimal BaseNet, decimal BaseTax,
    decimal BaseGross);

public sealed record PurchaseOrderAmountResult(IReadOnlyList<PurchaseOrderLineAmounts> Lines,
    decimal BaseNet, decimal BaseTax, decimal BaseGross);

/// <summary>REQ_F4114.1.2～3：各行先按幣別取整，表頭只累加已取整行值。</summary>
public static class PurchaseOrderAmounts
{
    private const decimal DecimalNineteenFourLimit = 1_000_000_000_000_000m;

    public static PurchaseOrderAmountResult Calculate(
        IReadOnlyList<PurchaseOrderAmountInput> inputs, string baseCurrencyCode)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var lines = new List<PurchaseOrderLineAmounts>(inputs.Count);
        decimal baseNet = 0m, baseTax = 0m, baseGross = 0m;
        try
        {
            foreach (var input in inputs)
            {
                if (input.ExchangeRate <= 0m)
                    throw new ArgumentOutOfRangeException(nameof(inputs), "Exchange rate must be positive.");
                var originalNet = Money.Round(checked(input.Quantity * input.UnitPrice), input.CurrencyCode);
                var originalTax = Money.Round(checked(originalNet * input.TaxRatePercent / 100m), input.CurrencyCode);
                var originalGross = checked(originalNet + originalTax);
                var lineBaseNet = Money.Round(
                    checked(input.Quantity * input.UnitPrice * input.ExchangeRate), baseCurrencyCode);
                var lineBaseTax = Money.Round(
                    checked(lineBaseNet * input.TaxRatePercent / 100m), baseCurrencyCode);
                var lineBaseGross = checked(lineBaseNet + lineBaseTax);
                RequireStorable(originalNet, originalTax, originalGross, lineBaseNet, lineBaseTax, lineBaseGross);
                lines.Add(new PurchaseOrderLineAmounts(input.LineNo, originalNet, originalTax,
                    originalGross, lineBaseNet, lineBaseTax, lineBaseGross));
                baseNet = checked(baseNet + lineBaseNet);
                baseTax = checked(baseTax + lineBaseTax);
                baseGross = checked(baseGross + lineBaseGross);
                RequireStorable(baseNet, baseTax, baseGross);
            }
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException("PUR.ORDER.AMOUNT_OVERFLOW",
                "Purchase order amount exceeds the supported range.");
        }

        return new PurchaseOrderAmountResult(lines, baseNet, baseTax, baseGross);
    }

    private static void RequireStorable(params decimal[] values)
    {
        if (values.Any(value => value < 0m || value >= DecimalNineteenFourLimit))
            throw new BusinessRuleException("PUR.ORDER.AMOUNT_OVERFLOW",
                "Purchase order amount exceeds DECIMAL(19,4).");
    }
}
