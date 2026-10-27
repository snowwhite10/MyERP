using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>Resolves MD identities and dated commercial evidence for a manual order.</summary>
public sealed class SalesOrderSnapshotBuilder(
    ICustomerLookup customers, ISalesItemLookup items,
    ICurrencyLookup currencies, ITaxRateLookup taxes)
{
    public async Task<SalesOrder> BuildManualAsync(long companyId, string documentNo,
        SalesOrderDraftInput input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        if (companyId <= 0) throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
        if (string.IsNullOrWhiteSpace(documentNo)) throw new ArgumentException("Document number is required.", nameof(documentNo));

        var customer = await customers.RequireApprovedAsync(input.CustomerId, token);
        var currency = await currencies.FindCurrencyAsync(input.CurrencyCode, token)
            ?? throw new NotFoundException("MD.CURRENCY.NOT_FOUND", "Currency", input.CurrencyCode);
        if (!currency.IsActive)
            throw new BusinessRuleException("MD.CURRENCY.INACTIVE", "Currency is inactive.");
        var baseCurrency = await currencies.GetBaseCurrencyAsync(token)
            ?? throw new BusinessRuleException("MD.EXCHANGE_RATE.NO_BASE_CURRENCY", "The company has no base currency.");
        var exchange = await currencies.GetRateSnapshotAsync(currency.Code, input.BusinessDate, token);
        var precision = await currencies.GetPrecisionAsync(token);

        var resolved = new List<(SalesOrderLineInput Input, SalesItemIdentity Item, TaxRateSnapshot Tax)>();
        foreach (var line in input.Lines!)
        {
            var item = await items.RequireActiveAsync(line.ItemId, token);
            var tax = await taxes.GetRateSnapshotAsync(line.TaxRateId, TaxDirection.Output,
                input.BusinessDate, token);
            resolved.Add((line, item, tax));
        }
        var amounts = SalesOrderAmounts.Calculate(resolved.Select(x =>
            new SalesOrderAmountInput(x.Input.LineNo, x.Input.Quantity, x.Input.UnitPrice,
                x.Input.DiscountRate, currency.Code, x.Tax.RatePercent, exchange.Rate))
            .ToArray(), baseCurrency.Code, precision);
        decimal originalNet, originalTax, originalGross;
        try
        {
            originalNet = checked(amounts.Lines.Sum(x => x.OriginalNet));
            originalTax = checked(amounts.Lines.Sum(x => x.OriginalTax));
            originalGross = checked(amounts.Lines.Sum(x => x.OriginalGross));
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException("SAL.ORDER.AMOUNT_OVERFLOW", "Order amount exceeds DECIMAL(19,4).");
        }
        if (originalNet >= 1_000_000_000_000_000m || originalTax >= 1_000_000_000_000_000m ||
            originalGross >= 1_000_000_000_000_000m)
            throw new BusinessRuleException("SAL.ORDER.AMOUNT_OVERFLOW", "Order amount exceeds DECIMAL(19,4).");

        var order = new SalesOrder
        {
            CompanyId = companyId,
            DocumentNo = documentNo,
            CustomerId = customer.Id,
            CustomerCode = customer.Code,
            CustomerName = customer.Name,
            BusinessDate = input.BusinessDate,
            CurrencyCode = currency.Code,
            BaseCurrencyCode = baseCurrency.Code,
            ExchangeRate = exchange.Rate,
            OriginalNet = originalNet,
            OriginalTax = originalTax,
            OriginalGross = originalGross,
            BaseNet = amounts.BaseNet,
            BaseTax = amounts.BaseTax,
            BaseGross = amounts.BaseGross,
            Remark = input.Remark?.Trim()
        };
        for (var index = 0; index < resolved.Count; index++)
        {
            var (line, item, tax) = resolved[index];
            var amount = amounts.Lines[index];
            order.Lines.Add(new SalesOrderLine
            {
                CompanyId = companyId,
                LineNo = line.LineNo,
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemName = item.Name,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountRate = line.DiscountRate,
                TaxRateId = tax.RateId,
                TaxRatePercent = tax.RatePercent,
                OriginalNet = amount.OriginalNet,
                OriginalTax = amount.OriginalTax,
                OriginalGross = amount.OriginalGross,
                BaseNet = amount.BaseNet,
                BaseTax = amount.BaseTax,
                BaseGross = amount.BaseGross
            });
        }
        return order;
    }
}
