using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>Copies a complete saved quotation snapshot into a new order draft; caller owns the transaction.</summary>
public static class SalesOrderQuotationSnapshot
{
    public static SalesOrder Build(long companyId, string documentNo, SalesQuotation quotation,
        int savedLineCount, bool hasSourceOrder, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(quotation);
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrWhiteSpace(documentNo))
            throw new ArgumentException("Document number is required.", nameof(documentNo));
        if (quotation.Id <= 0 || quotation.IsDeleted || quotation.CompanyId != companyId)
            throw new BusinessRuleException("SAL.DOCUMENT.NOT_FOUND", "Quotation was not found.");
        SalesOrderQuotationSource.Validate(companyId, quotation.CompanyId, quotation.Status,
            quotation.ExpiryDate, hasSourceOrder, clock);

        if (savedLineCount is < 1 or > 1000 || quotation.Lines.Count != savedLineCount ||
            quotation.Lines.Any(line => line.Id <= 0 || line.QuotationId != quotation.Id ||
                line.CompanyId != companyId || line.LineNo <= 0) ||
            quotation.Lines.Select(line => line.Id).Distinct().Count() != savedLineCount ||
            quotation.Lines.Select(line => line.LineNo).Distinct().Count() != savedLineCount)
            throw new BusinessRuleException("SAL.ORDER.SOURCE_INCOMPLETE",
                "Saved quotation lines are incomplete or inconsistent.");

        if (quotation.CustomerId <= 0 || string.IsNullOrWhiteSpace(quotation.CustomerCode) ||
            string.IsNullOrWhiteSpace(quotation.CustomerName) || quotation.BusinessDate == default ||
            !IsCurrency(quotation.CurrencyCode) || !IsCurrency(quotation.BaseCurrencyCode) ||
            quotation.ExchangeRate <= 0 || quotation.ExchangeRate >= 10_000_000_000_000m ||
            decimal.Round(quotation.ExchangeRate, 8) != quotation.ExchangeRate ||
            quotation.Lines.Any(line => line.ItemId <= 0 || string.IsNullOrWhiteSpace(line.ItemCode) ||
                string.IsNullOrWhiteSpace(line.ItemName) || line.TaxRateId <= 0 ||
                !IsNineteenSixPositive(line.Quantity) ||
                line.UnitPrice < 0 || line.UnitPrice >= 10_000_000_000_000m ||
                decimal.Round(line.UnitPrice, 6) != line.UnitPrice ||
                line.DiscountRate < 0 || line.DiscountRate > 100 ||
                decimal.Round(line.DiscountRate, 4) != line.DiscountRate ||
                line.TaxRatePercent < 0 || line.TaxRatePercent > 100 ||
                decimal.Round(line.TaxRatePercent, 4) != line.TaxRatePercent))
            throw new BusinessRuleException("SAL.ORDER.SOURCE_INCOMPLETE",
                "Saved quotation commercial identity is invalid.");

        // Approved quotation amounts are already rounded. Check only that the
        // saved header covers exactly the saved lines; never reprice from MD.
        try
        {
            if (quotation.Lines.Any(line =>
                    checked(line.OriginalNet + line.OriginalTax) != line.OriginalGross ||
                    checked(line.BaseNet + line.BaseTax) != line.BaseGross))
                throw new BusinessRuleException("SAL.ORDER.SOURCE_INCOMPLETE",
                    "Saved quotation line gross does not match its net and tax.");
            if (quotation.OriginalNet != quotation.Lines.Sum(x => x.OriginalNet) ||
                quotation.OriginalTax != quotation.Lines.Sum(x => x.OriginalTax) ||
                quotation.OriginalGross != quotation.Lines.Sum(x => x.OriginalGross) ||
                quotation.BaseNet != quotation.Lines.Sum(x => x.BaseNet) ||
                quotation.BaseTax != quotation.Lines.Sum(x => x.BaseTax) ||
                quotation.BaseGross != quotation.Lines.Sum(x => x.BaseGross))
                throw new BusinessRuleException("SAL.ORDER.SOURCE_INCOMPLETE",
                    "Saved quotation totals do not match its lines.");
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException("SAL.ORDER.SOURCE_INCOMPLETE",
                "Saved quotation line totals exceed the supported range.");
        }

        var order = new SalesOrder
        {
            CompanyId = companyId,
            DocumentNo = documentNo,
            SourceQuotationId = quotation.Id,
            CustomerId = quotation.CustomerId,
            CustomerCode = quotation.CustomerCode,
            CustomerName = quotation.CustomerName,
            BusinessDate = quotation.BusinessDate,
            CurrencyCode = quotation.CurrencyCode,
            BaseCurrencyCode = quotation.BaseCurrencyCode,
            ExchangeRate = quotation.ExchangeRate,
            OriginalNet = quotation.OriginalNet,
            OriginalTax = quotation.OriginalTax,
            OriginalGross = quotation.OriginalGross,
            BaseNet = quotation.BaseNet,
            BaseTax = quotation.BaseTax,
            BaseGross = quotation.BaseGross,
            Remark = quotation.Remark
        };
        foreach (var line in quotation.Lines.OrderBy(line => line.LineNo))
        {
            order.Lines.Add(new SalesOrderLine
            {
                CompanyId = companyId,
                LineNo = line.LineNo,
                SourceQuotationLineId = line.Id,
                ItemId = line.ItemId,
                ItemCode = line.ItemCode,
                ItemName = line.ItemName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountRate = line.DiscountRate,
                TaxRateId = line.TaxRateId,
                TaxRatePercent = line.TaxRatePercent,
                OriginalNet = line.OriginalNet,
                OriginalTax = line.OriginalTax,
                OriginalGross = line.OriginalGross,
                BaseNet = line.BaseNet,
                BaseTax = line.BaseTax,
                BaseGross = line.BaseGross
            });
        }
        return order;
    }

    private static bool IsCurrency(string? code) =>
        code is { Length: 3 } && code.All(c => c is >= 'A' and <= 'Z');

    private static bool IsNineteenSixPositive(decimal value) =>
        value > 0 && value < 10_000_000_000_000m && decimal.Round(value, 6) == value;
}
