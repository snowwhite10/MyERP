using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>REQ_F4112.1.2: quotation input before MD references are checked in a transaction.</summary>
public sealed record SalesQuotationLineInput(int LineNo, long ItemId, decimal Quantity,
    decimal UnitPrice, decimal DiscountRate, string CurrencyCode, long TaxRateId);

public sealed record SalesQuotationInput(long CustomerId, DateOnly BusinessDate,
    DateOnly ExpiryDate, string? Remark, IReadOnlyList<SalesQuotationLineInput>? Lines)
{
    private const decimal DecimalNineteenSixLimit = 10_000_000_000_000m;

    public void Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);

        if (CustomerId <= 0) Fail("customerId", "Customer is required.");
        if (BusinessDate == default) Fail("businessDate", "Business date is required.");
        if (ExpiryDate == default || (BusinessDate != default && ExpiryDate < BusinessDate))
            Fail("expiryDate", "Expiry date must not precede the business date.");
        if (Remark?.Trim().Length > 500) Fail("remark", "Remark must not exceed 500 characters.");
        if (Lines is not { Count: >= 1 and <= 1000 })
            Fail("lines", "A quotation needs 1 to 1000 lines.");

        var lineNumbers = new HashSet<int>();
        foreach (var (line, index) in (Lines ?? []).Select((line, index) => (line, index)))
        {
            var field = $"lines[{index}]";
            if (line is null) { Fail(field, "Line is required."); continue; }
            if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
            if (!lineNumbers.Add(line.LineNo)) Fail("lines", "Line numbers must be unique.");
            if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
            if (line.Quantity <= 0 || line.Quantity >= DecimalNineteenSixLimit ||
                decimal.Round(line.Quantity, 6) != line.Quantity)
                Fail($"{field}.quantity", "Quantity must be positive, below 10^13, with at most 6 decimals.");
            if (line.UnitPrice < 0 || line.UnitPrice >= DecimalNineteenSixLimit ||
                decimal.Round(line.UnitPrice, 6) != line.UnitPrice)
                Fail($"{field}.unitPrice", "Unit price must be nonnegative, below 10^13, with at most 6 decimals.");
            if (line.DiscountRate < 0 || line.DiscountRate > 100 ||
                decimal.Round(line.DiscountRate, 4) != line.DiscountRate)
                Fail($"{field}.discountRate", "Discount must be 0 to 100 with at most 4 decimals.");
            if (string.IsNullOrWhiteSpace(line.CurrencyCode) || line.CurrencyCode.Length != 3 ||
                !line.CurrencyCode.All(c => c is >= 'A' and <= 'Z'))
                Fail($"{field}.currencyCode", "Currency code must be three uppercase letters.");
            if (line.TaxRateId <= 0) Fail($"{field}.taxRateId", "Output tax rate is required.");
        }

        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}
