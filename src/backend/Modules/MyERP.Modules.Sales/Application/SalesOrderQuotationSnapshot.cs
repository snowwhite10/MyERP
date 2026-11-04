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

        // Approved quotation amounts are already rounded. Check only that the
        // saved header covers exactly the saved lines; never reprice from MD.
        try
        {
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
}
