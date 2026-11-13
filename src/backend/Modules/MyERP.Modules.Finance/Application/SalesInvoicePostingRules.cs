using MyERP.Modules.Finance.Contracts;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// FI account mapping for SD invoices (BR-FI-009: journal mapping is FI-owned). The version must equal the one the
/// GL posting path reads for <c>SD/Invoice</c> through <see cref="IGlPostingMappingVersionSource"/>.
/// </summary>
public sealed record SalesInvoiceAccountMap(int MappingVersion, string ReceivableAccountCode,
    string RevenueAccountCode, string OutputTaxAccountCode);

/// <summary>Reads the company's current SD-invoice account map; null means no usable mapping (422).</summary>
public interface ISalesInvoiceAccountMapSource
{
    Task<SalesInvoiceAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken);
}

/// <summary>Pure checks of the SD caller's command and the saved SD invoice evidence (WU-FI-002a2b). No I/O.</summary>
public static class SalesInvoicePostingRules
{
    public const decimal MaxAmount = 999_999_999_999_999.9999m;
    public const decimal MaxRate = 99_999_999_999.99999999m;
    public const int MaxFingerprintLength = 128;

    public static bool IsWellFormed(SalesInvoicePostingCommand? command) =>
        command is { CompanyId: > 0, InvoiceId: > 0, CustomerId: > 0, SourceVersion: > 0 }
        && IsCanonicalFingerprint(command.Fingerprint);

    /// <summary>
    /// Null when the saved snapshot proves the command and its amounts reconcile; otherwise the HTTP status and code.
    /// Order: identity (404) → state (422) → caller claim (409) → amounts (422).
    /// </summary>
    public static (int Status, string Code)? CheckSnapshot(SalesInvoicePostingCommand command,
        FinanceSalesInvoiceSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (snapshot is null || snapshot.CompanyId != command.CompanyId || snapshot.InvoiceId != command.InvoiceId)
            return (404, SalesPostingErrors.SourceNotFound);
        if (snapshot.State is not (FinanceSalesSourceState.Issuing or FinanceSalesSourceState.Posted))
            return (422, SalesPostingErrors.SourceNotPostable);
        if (snapshot.CustomerId != command.CustomerId || snapshot.SourceVersion != command.SourceVersion
            || !string.Equals(snapshot.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return (409, SalesPostingErrors.SourceMismatch);
        return AmountsReconcile(snapshot) ? null : (422, SalesPostingErrors.InvalidAmounts);
    }

    /// <summary>
    /// BR-FI-009: each currency reconciles exactly (net + tax = gross, gross &gt; 0, decimal(19,4)); ISO uppercase
    /// currencies; positive decimal(19,8) rate; a base-currency invoice has rate 1 and identical amounts.
    /// </summary>
    public static bool AmountsReconcile(FinanceSalesInvoiceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.BusinessDate == default || !IsCurrency(snapshot.Currency) || !IsCurrency(snapshot.BaseCurrency)
            || snapshot.ExchangeRate <= 0m || snapshot.ExchangeRate > MaxRate
            || decimal.Round(snapshot.ExchangeRate, 8) != snapshot.ExchangeRate)
            return false;
        if (!Reconciles(snapshot.NetAmount, snapshot.TaxAmount, snapshot.GrossAmount)
            || !Reconciles(snapshot.BaseNetAmount, snapshot.BaseTaxAmount, snapshot.BaseGrossAmount))
            return false;
        return !string.Equals(snapshot.Currency, snapshot.BaseCurrency, StringComparison.Ordinal)
            || (snapshot.ExchangeRate == 1m && snapshot.NetAmount == snapshot.BaseNetAmount
                && snapshot.TaxAmount == snapshot.BaseTaxAmount && snapshot.GrossAmount == snapshot.BaseGrossAmount);
    }

    internal static bool Reconciles(decimal net, decimal tax, decimal gross) =>
        IsMoney(net) && IsMoney(tax) && IsMoney(gross) && gross > 0m && net + tax == gross;

    internal static bool IsMoney(decimal amount) =>
        amount >= 0m && amount <= MaxAmount && decimal.Round(amount, 4) == amount;

    internal static bool IsCurrency(string? code) =>
        code is { Length: 3 } && code.All(static c => c is >= 'A' and <= 'Z');

    internal static bool IsCanonicalFingerprint(string? fingerprint) =>
        !string.IsNullOrWhiteSpace(fingerprint) && fingerprint.Length <= MaxFingerprintLength
        && fingerprint.Trim().Length == fingerprint.Length && fingerprint.All(static c => c is > ' ' and < (char)127);
}
