using MyERP.Modules.Finance.Contracts;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Pure checks of the PUR caller's command and the saved PUR invoice evidence (WU-FI-003a2b, ADR-117; BR-FI-015/017).
/// No I/O. Money, currency, rate and fingerprint primitives are shared with the SD invoice rules.
/// </summary>
public static class PurchaseInvoicePostingRules
{
    public static bool IsWellFormed(PurchaseInvoicePostingCommand? command) =>
        command is { CompanyId: > 0, InvoiceId: > 0, SupplierId: > 0, SourceVersion: > 0 }
        && SalesInvoicePostingRules.IsCanonicalFingerprint(command.Fingerprint);

    /// <summary>
    /// Null when the saved snapshot proves the command and its amounts reconcile; otherwise the HTTP status, code and,
    /// for a line that does not reconcile, its PUR line number. Order: identity (404) → state (422) → caller claim
    /// (409) → amounts (422). Only <see cref="FinancePurchaseSourceState.Matched"/> may create a payable: a Posted source
    /// that FI has no committed source for is not replayable (BR-FI-015).
    /// </summary>
    public static (int Status, string Code, int? SourceLine)? CheckSnapshot(PurchaseInvoicePostingCommand command,
        FinancePurchaseInvoiceSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (snapshot is null || snapshot.CompanyId != command.CompanyId || snapshot.InvoiceId != command.InvoiceId)
            return (404, PurchasePostingErrors.SourceNotFound, null);
        if (snapshot.State != FinancePurchaseSourceState.Matched)
            return (422, PurchasePostingErrors.SourceNotPostable, null);
        if (snapshot.SupplierId != command.SupplierId || snapshot.SourceVersion != command.SourceVersion
            || !string.Equals(snapshot.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return (409, PurchasePostingErrors.SourceMismatch, null);
        if (!HeaderReconciles(snapshot))
            return (422, PurchasePostingErrors.InvalidAmounts, null);
        return CheckLines(snapshot);
    }

    /// <summary>
    /// BR-FI-017 header: each currency reconciles exactly (net + tax = gross, gross &gt; 0, decimal(19,4)); ISO uppercase
    /// currencies; positive decimal(19,8) rate; a base-currency invoice has rate 1 and identical amounts.
    /// </summary>
    private static bool HeaderReconciles(FinancePurchaseInvoiceSnapshot s)
    {
        if (s.BusinessDate == default || !SalesInvoicePostingRules.IsCurrency(s.Currency)
            || !SalesInvoicePostingRules.IsCurrency(s.BaseCurrency)
            || s.ExchangeRate <= 0m || s.ExchangeRate > SalesInvoicePostingRules.MaxRate
            || decimal.Round(s.ExchangeRate, 8) != s.ExchangeRate)
            return false;
        if (!SalesInvoicePostingRules.Reconciles(s.NetAmount, s.TaxAmount, s.GrossAmount)
            || !SalesInvoicePostingRules.Reconciles(s.BaseNetAmount, s.BaseTaxAmount, s.BaseGrossAmount))
            return false;
        return !IsBaseCurrency(s) || (s.ExchangeRate == 1m && s.NetAmount == s.BaseNetAmount
            && s.TaxAmount == s.BaseTaxAmount && s.GrossAmount == s.BaseGrossAmount);
    }

    /// <summary>
    /// BR-FI-017 lines: at least one line; positive unique line numbers; every line reconciles in both currencies (a
    /// zero-tax line is allowed, a zero line is not); a base-currency invoice's line has identical base amounts; and the
    /// lines sum exactly to the header components — no balancing plug absorbs a difference.
    /// </summary>
    private static (int Status, string Code, int? SourceLine)? CheckLines(FinancePurchaseInvoiceSnapshot s)
    {
        if (s.Lines is not { Count: > 0 })
            return (422, PurchasePostingErrors.InvalidAmounts, null);
        var seen = new HashSet<int>();
        decimal net = 0m, tax = 0m, gross = 0m, baseNet = 0m, baseTax = 0m, baseGross = 0m;
        var sameCurrency = IsBaseCurrency(s);
        foreach (var line in s.Lines)
        {
            if (line is null)
                return (422, PurchasePostingErrors.InvalidAmounts, null);
            if (line.LineNo <= 0 || !seen.Add(line.LineNo)
                || !SalesInvoicePostingRules.Reconciles(line.NetAmount, line.TaxAmount, line.GrossAmount)
                || !SalesInvoicePostingRules.Reconciles(line.BaseNetAmount, line.BaseTaxAmount, line.BaseGrossAmount)
                || (sameCurrency && (line.NetAmount != line.BaseNetAmount || line.TaxAmount != line.BaseTaxAmount
                                     || line.GrossAmount != line.BaseGrossAmount)))
                return (422, PurchasePostingErrors.InvalidAmounts, line.LineNo);
            net += line.NetAmount;
            tax += line.TaxAmount;
            gross += line.GrossAmount;
            baseNet += line.BaseNetAmount;
            baseTax += line.BaseTaxAmount;
            baseGross += line.BaseGrossAmount;
        }

        return net == s.NetAmount && tax == s.TaxAmount && gross == s.GrossAmount
               && baseNet == s.BaseNetAmount && baseTax == s.BaseTaxAmount && baseGross == s.BaseGrossAmount
            ? null
            : (422, PurchasePostingErrors.InvalidAmounts, null);
    }

    private static bool IsBaseCurrency(FinancePurchaseInvoiceSnapshot s) =>
        string.Equals(s.Currency, s.BaseCurrency, StringComparison.Ordinal);
}
