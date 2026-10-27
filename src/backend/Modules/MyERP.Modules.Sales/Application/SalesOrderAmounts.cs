using MyERP.Shared.Errors;
using MyERP.Shared.Monetary;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesOrderAmountInput(int LineNo, decimal Quantity, decimal UnitPrice,
    decimal DiscountRate, string CurrencyCode, decimal TaxRatePercent, decimal ExchangeRate);

public sealed record SalesOrderLineAmounts(int LineNo, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

public sealed record SalesOrderAmountResult(IReadOnlyList<SalesOrderLineAmounts> Lines,
    decimal BaseNet, decimal BaseTax, decimal BaseGross);

/// <summary>REQ_F4112.2.2: order and quotation use the same commercial rounding policy.</summary>
public static class SalesOrderAmounts
{
    public static SalesOrderAmountResult Calculate(
        IReadOnlyList<SalesOrderAmountInput> inputs, string baseCurrencyCode,
        CurrencyPrecision? precision = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        try
        {
            var result = SalesQuotationAmounts.Calculate(inputs.Select(line =>
                new SalesQuotationAmountInput(line.LineNo, line.Quantity, line.UnitPrice,
                    line.DiscountRate, line.CurrencyCode, line.TaxRatePercent,
                    line.ExchangeRate)).ToArray(), baseCurrencyCode, precision);
            return new(result.Lines.Select(line => new SalesOrderLineAmounts(line.LineNo,
                line.OriginalNet, line.OriginalTax, line.OriginalGross,
                line.BaseNet, line.BaseTax, line.BaseGross)).ToArray(),
                result.BaseNet, result.BaseTax, result.BaseGross);
        }
        catch (BusinessRuleException ex) when (ex.ErrorCode == "SAL.QUOTATION.AMOUNT_OVERFLOW")
        {
            throw new BusinessRuleException("SAL.ORDER.AMOUNT_OVERFLOW",
                "Order amount exceeds DECIMAL(19,4).");
        }
    }
}
