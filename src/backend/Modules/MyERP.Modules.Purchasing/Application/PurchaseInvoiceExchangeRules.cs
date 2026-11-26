using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Order currency and the rate snapshot saved on the purchase order (both null before WU-PUR-006a).</summary>
public sealed record PurchaseInvoiceOrderRateSnapshot(string OrderCurrency, string? BaseCurrencyCode, decimal? ExchangeRate);

/// <summary>
/// ADR-206 / BR-PUR-043b: the posting rate is the saved purchase-order snapshot, never today's rate. The snapshot's
/// base currency must still be the company base currency read through MD <c>ICurrencyLookup</c>.
/// Inconsistent data (currency mismatch, half snapshot, invalid rate) is a programming/data error
/// (<c>PUR.INVOICE.INVALID_POSTING_SNAPSHOT</c>); missing snapshot or base currency are 422 user-facing reasons.
/// </summary>
public static class PurchaseInvoiceExchangeRules
{
    private const decimal MaxRate = 99_999_999_999.99999999m;

    public static PurchaseInvoiceExchangeSnapshot Resolve(string invoiceCurrency, PurchaseInvoiceOrderRateSnapshot order,
        string? companyBaseCurrency)
    {
        if (order is null || !IsCurrency(invoiceCurrency) || !IsCurrency(order.OrderCurrency)
            || !string.Equals(invoiceCurrency, order.OrderCurrency, StringComparison.Ordinal)
            || (order.BaseCurrencyCode is null) != (order.ExchangeRate is null))
            throw Invalid();

        if (order.BaseCurrencyCode is null || order.ExchangeRate is not { } rate)
            throw new BusinessRuleException("PUR.INVOICE.RATE_SNAPSHOT_MISSING",
                "The purchase order has no saved exchange-rate snapshot, so the invoice cannot be posted.");

        if (string.IsNullOrWhiteSpace(companyBaseCurrency))
            throw new BusinessRuleException("PUR.INVOICE.BASE_CURRENCY_NOT_SET",
                "The company has no base currency, so the invoice cannot be posted.");

        var snapshotBase = order.BaseCurrencyCode;
        if (!IsCurrency(snapshotBase) || rate <= 0m || rate > MaxRate || decimal.Round(rate, 8) != rate
            || (string.Equals(order.OrderCurrency, snapshotBase, StringComparison.Ordinal) && rate != 1m))
            throw Invalid();

        var companyBase = companyBaseCurrency.Trim().ToUpperInvariant();
        if (!string.Equals(snapshotBase, companyBase, StringComparison.Ordinal))
            throw new BusinessRuleException("PUR.INVOICE.BASE_CURRENCY_CHANGED",
                "The purchase order rate was captured for a different base currency than the company's current base currency.");

        return new PurchaseInvoiceExchangeSnapshot(snapshotBase, rate);
    }

    private static bool IsCurrency(string? code) =>
        code is { Length: 3 } && code.All(static c => c is >= 'A' and <= 'Z');

    private static BusinessRuleException Invalid() =>
        new("PUR.INVOICE.INVALID_POSTING_SNAPSHOT", "Saved purchase order rate snapshot is inconsistent.");
}
