using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Monetary;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One saved invoice line's original-currency amounts.</summary>
public sealed record PurchaseInvoiceFinanceSavedLine(int LineNo, decimal Net, decimal Tax, decimal Gross);

/// <summary>Saved invoice header and lines as read inside the caller's transaction (WU-PUR-008b1).</summary>
public sealed record PurchaseInvoiceFinanceSaved(long CompanyId, long InvoiceId, int Version, long SupplierId,
    PurchaseInvoiceStatus Status, bool IsDeleted, DateOnly BusinessDate, string Currency,
    decimal TotalNet, decimal TotalTax, decimal TotalGross,
    IReadOnlyList<PurchaseInvoiceFinanceSavedLine> Lines);

/// <summary>Company base currency and the saved purchase-order exchange-rate snapshot (BR-PUR-043).</summary>
public sealed record PurchaseInvoiceExchangeSnapshot(string BaseCurrency, decimal ExchangeRate);

/// <summary>
/// ADR-205: pure mapping of a saved PUR invoice to the FI-owned <see cref="FinancePurchaseInvoiceSnapshot"/>.
/// Original amounts are passed through unchanged (FI reconciles them); base amounts are converted per line with
/// <see cref="Money.Round(decimal, string, CurrencyPrecision)"/> and summed for the header. Invalid input never yields
/// a partial snapshot.
/// </summary>
public static class PurchaseInvoiceFinanceSnapshot
{
    private const decimal MaxAmount = 999_999_999_999_999.9999m;
    private const decimal MaxRate = 99_999_999_999.99999999m;
    private const int MaxFingerprintLength = 128;

    public static FinancePurchaseInvoiceSnapshot Build(PurchaseInvoiceFinanceSaved saved, string fingerprint,
        PurchaseInvoiceExchangeSnapshot exchange, CurrencyPrecision? precision = null)
    {
        if (saved is null || exchange is null || saved.Lines is null || saved.Lines.Count == 0
            || saved.CompanyId <= 0 || saved.InvoiceId <= 0 || saved.Version <= 0 || saved.SupplierId <= 0
            || !IsCurrency(saved.Currency) || !IsCurrency(exchange.BaseCurrency)
            || exchange.ExchangeRate <= 0m || exchange.ExchangeRate > MaxRate
            || decimal.Round(exchange.ExchangeRate, 8) != exchange.ExchangeRate
            || string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > MaxFingerprintLength
            || fingerprint.Trim().Length != fingerprint.Length)
            throw Invalid();

        var sameCurrency = string.Equals(saved.Currency, exchange.BaseCurrency, StringComparison.Ordinal);
        if (sameCurrency && exchange.ExchangeRate != 1m)
            throw Invalid();

        precision ??= CurrencyPrecision.Default;
        var source = saved.Lines.ToArray();
        if (source.Any(x => x is null || x.LineNo <= 0)
            || source.Select(x => x.LineNo).Distinct().Count() != source.Length)
            throw Invalid();

        var lines = new List<FinancePurchaseInvoiceLine>(source.Length);
        decimal baseNet = 0m, baseTax = 0m;
        try
        {
            foreach (var line in source.OrderBy(x => x.LineNo))
            {
                var net = sameCurrency ? line.Net : Convert(line.Net, exchange, precision);
                var tax = sameCurrency ? line.Tax : Convert(line.Tax, exchange, precision);
                var gross = checked(net + tax);
                if (gross > MaxAmount)
                    throw Invalid();
                lines.Add(new(line.LineNo, line.Net, line.Tax, line.Gross, net, tax, gross));
                baseNet = checked(baseNet + net);
                baseTax = checked(baseTax + tax);
            }
        }
        catch (OverflowException)
        {
            throw Invalid();
        }

        var baseGross = baseNet + baseTax;
        if (baseGross > MaxAmount)
            throw Invalid();

        return new FinancePurchaseInvoiceSnapshot(saved.CompanyId, saved.InvoiceId, saved.Version, fingerprint,
            saved.SupplierId, MapState(saved), saved.BusinessDate, saved.Currency, exchange.BaseCurrency,
            exchange.ExchangeRate, saved.TotalNet, saved.TotalTax, saved.TotalGross,
            baseNet, baseTax, baseGross, lines);
    }

    private static decimal Convert(decimal amount, PurchaseInvoiceExchangeSnapshot exchange,
        CurrencyPrecision precision)
    {
        var converted = Money.Round(checked(amount * exchange.ExchangeRate), exchange.BaseCurrency, precision);
        return converted > MaxAmount ? throw Invalid() : converted;
    }

    private static FinancePurchaseSourceState MapState(PurchaseInvoiceFinanceSaved saved) =>
        saved.IsDeleted
            ? FinancePurchaseSourceState.Deleted
            : saved.Status switch
            {
                PurchaseInvoiceStatus.Draft => FinancePurchaseSourceState.Draft,
                PurchaseInvoiceStatus.Matched => FinancePurchaseSourceState.Matched,
                PurchaseInvoiceStatus.Disputed => FinancePurchaseSourceState.Disputed,
                PurchaseInvoiceStatus.Rejected => FinancePurchaseSourceState.Rejected,
                PurchaseInvoiceStatus.Posted => FinancePurchaseSourceState.Posted,
                _ => throw Invalid()
            };

    private static bool IsCurrency(string? code) =>
        code is { Length: 3 } && code.All(static c => c is >= 'A' and <= 'Z');

    private static BusinessRuleException Invalid() =>
        new("PUR.INVOICE.INVALID_POSTING_SNAPSHOT", "Saved invoice cannot be mapped to the FI payable snapshot.");
}
