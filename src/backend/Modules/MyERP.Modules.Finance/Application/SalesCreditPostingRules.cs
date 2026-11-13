using MyERP.Modules.Finance.Contracts;

namespace MyERP.Modules.Finance.Application;

/// <summary>FI-saved original invoice posting and its receivable, as read under the credit key lock (WU-FI-002a3).</summary>
public sealed record SalesCreditOriginalInvoice(
    long InvoiceId, long CustomerId, string Currency, string BaseCurrency,
    decimal NetAmount, decimal TaxAmount, decimal BaseNetAmount, decimal BaseTaxAmount,
    long ReceivableId, decimal OpenBaseAmount);

/// <summary>Cumulative committed credit quantity and components (per invoice line or per invoice).</summary>
public sealed record SalesCreditTotals(
    decimal Quantity, decimal NetAmount, decimal TaxAmount, decimal BaseNetAmount, decimal BaseTaxAmount)
{
    public static readonly SalesCreditTotals Zero = new(0m, 0m, 0m, 0m, 0m);

    public SalesCreditTotals Add(FinanceSalesReturnLine line) =>
        new(Quantity + line.Quantity, NetAmount + line.NetAmount, TaxAmount + line.TaxAmount,
            BaseNetAmount + line.BaseNetAmount, BaseTaxAmount + line.BaseTaxAmount);
}

/// <summary>Named rejection with the 1-based return line when one line is at fault.</summary>
public sealed record SalesCreditFailure(int Status, string Code, int? Line = null);

/// <summary>
/// Pure checks for SD return → FI credit posting (WU-FI-002a3a, ADR-113; BR-FI-011/012/014). No I/O. The service
/// (WU-FI-002a3b) reads the originals, prior allocations and open receivables under the FI credit key lock and passes
/// them here, so the cumulative limits are evaluated against committed state only.
/// </summary>
public static class SalesCreditPostingRules
{
    public const decimal MaxQuantity = 9_999_999_999_999.999999m;

    public static bool IsWellFormed(SalesCreditPostingCommand? command) =>
        command is { CompanyId: > 0, ReturnId: > 0, CustomerId: > 0, SourceVersion: > 0 }
        && SalesInvoicePostingRules.IsCanonicalFingerprint(command.Fingerprint);

    /// <summary>
    /// Null when the saved SD return proves the command and its billed amounts reconcile. Order: identity (404) →
    /// state (422) → caller claim (409) → header amounts (422) → each line (422 with line; duplicate line) →
    /// header equals the sum of lines per component (422).
    /// </summary>
    public static SalesCreditFailure? CheckSnapshot(SalesCreditPostingCommand command,
        FinanceSalesReturnSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (snapshot is null || snapshot.CompanyId != command.CompanyId || snapshot.ReturnId != command.ReturnId)
            return new(404, SalesPostingErrors.SourceNotFound);
        if (snapshot.State is not (FinanceSalesSourceState.Issuing or FinanceSalesSourceState.Posted))
            return new(422, SalesPostingErrors.SourceNotPostable);
        if (snapshot.CustomerId != command.CustomerId || snapshot.SourceVersion != command.SourceVersion
            || !string.Equals(snapshot.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return new(409, SalesPostingErrors.SourceMismatch);
        if (!SalesInvoicePostingRules.AmountsReconcile(Header(snapshot)) || snapshot.Lines is not { Count: > 0 })
            return new(422, SalesPostingErrors.InvalidAmounts);

        var sameCurrency = string.Equals(snapshot.Currency, snapshot.BaseCurrency, StringComparison.Ordinal);
        var seen = new HashSet<long>();
        decimal net = 0m, tax = 0m, gross = 0m, baseNet = 0m, baseTax = 0m, baseGross = 0m;
        for (var index = 0; index < snapshot.Lines.Count; index++)
        {
            var line = snapshot.Lines[index];
            if (!LineIsValid(line, sameCurrency))
                return new(422, SalesPostingErrors.InvalidAmounts, index + 1);
            if (!seen.Add(line.InvoiceLineId))
                return new(422, SalesCreditPostingErrors.DuplicateLine, index + 1);
            net += line.NetAmount;
            tax += line.TaxAmount;
            gross += line.GrossAmount;
            baseNet += line.BaseNetAmount;
            baseTax += line.BaseTaxAmount;
            baseGross += line.BaseGrossAmount;
        }

        return net == snapshot.NetAmount && tax == snapshot.TaxAmount && gross == snapshot.GrossAmount
               && baseNet == snapshot.BaseNetAmount && baseTax == snapshot.BaseTaxAmount
               && baseGross == snapshot.BaseGrossAmount
            ? null
            : new(422, SalesPostingErrors.InvalidAmounts);
    }

    /// <summary>
    /// BR-FI-011/012 against committed FI state, line by line in return order; the first line that crosses a limit is
    /// reported. Per line: original FI invoice posted (422) → same customer, currency and base currency (422) →
    /// cumulative line quantity and components ≤ SD-saved invoice line (422) → cumulative per-invoice components ≤
    /// FI-saved invoice totals (422) → this return's base gross per receivable ≤ its current open amount (422).
    /// Exact final remainders pass; nothing is rounded.
    /// </summary>
    public static SalesCreditFailure? CheckAllocations(SalesCreditPostingCommand command,
        FinanceSalesReturnSnapshot snapshot, IReadOnlyDictionary<long, SalesCreditOriginalInvoice> originals,
        IReadOnlyDictionary<long, SalesCreditTotals> priorByLine,
        IReadOnlyDictionary<long, SalesCreditTotals> priorByInvoice)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(priorByLine);
        ArgumentNullException.ThrowIfNull(priorByInvoice);

        var perInvoice = new Dictionary<long, SalesCreditTotals>();
        var perReceivable = new Dictionary<long, decimal>();
        for (var index = 0; index < snapshot.Lines.Count; index++)
        {
            var line = snapshot.Lines[index];
            var lineNo = index + 1;
            if (!originals.TryGetValue(line.InvoiceId, out var original) || original.InvoiceId != line.InvoiceId)
                return new(422, SalesCreditPostingErrors.OriginalNotPosted, lineNo);
            if (original.CustomerId != command.CustomerId
                || !string.Equals(original.Currency, snapshot.Currency, StringComparison.Ordinal)
                || !string.Equals(original.BaseCurrency, snapshot.BaseCurrency, StringComparison.Ordinal))
                return new(422, SalesCreditPostingErrors.OriginalMismatch, lineNo);

            var lineTotal = priorByLine.GetValueOrDefault(line.InvoiceLineId, SalesCreditTotals.Zero).Add(line);
            var cap = line.InvoiceLine;
            if (lineTotal.Quantity > cap.Quantity || lineTotal.NetAmount > cap.NetAmount
                || lineTotal.TaxAmount > cap.TaxAmount || lineTotal.BaseNetAmount > cap.BaseNetAmount
                || lineTotal.BaseTaxAmount > cap.BaseTaxAmount)
                return new(422, SalesCreditPostingErrors.CreditExceedsLine, lineNo);

            var invoiceTotal = (perInvoice.TryGetValue(line.InvoiceId, out var running)
                ? running
                : priorByInvoice.GetValueOrDefault(line.InvoiceId, SalesCreditTotals.Zero)).Add(line);
            perInvoice[line.InvoiceId] = invoiceTotal;
            if (invoiceTotal.NetAmount > original.NetAmount || invoiceTotal.TaxAmount > original.TaxAmount
                || invoiceTotal.BaseNetAmount > original.BaseNetAmount
                || invoiceTotal.BaseTaxAmount > original.BaseTaxAmount)
                return new(422, SalesCreditPostingErrors.CreditExceedsInvoice, lineNo);

            var credited = perReceivable.GetValueOrDefault(original.ReceivableId) + line.BaseGrossAmount;
            perReceivable[original.ReceivableId] = credited;
            if (credited > original.OpenBaseAmount)
                return new(422, SalesCreditPostingErrors.CreditExceedsOpen, lineNo);
        }

        return null;
    }

    /// <summary>
    /// BR-FI-012 base-currency credit journal: Dr revenue (returns contra, ADR-113) base net; Dr output tax base tax;
    /// Cr AR base gross. Zero legs are omitted.
    /// </summary>
    public static IReadOnlyList<(string Code, decimal Debit, decimal Credit)> JournalLegs(SalesInvoiceAccountMap map,
        FinanceSalesReturnSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(snapshot);
        var legs = new List<(string Code, decimal Debit, decimal Credit)>(3);
        if (snapshot.BaseNetAmount > 0m)
            legs.Add((map.RevenueAccountCode, snapshot.BaseNetAmount, 0m));
        if (snapshot.BaseTaxAmount > 0m)
            legs.Add((map.OutputTaxAccountCode, snapshot.BaseTaxAmount, 0m));
        legs.Add((map.ReceivableAccountCode, 0m, snapshot.BaseGrossAmount));
        return legs;
    }

    private static bool LineIsValid(FinanceSalesReturnLine? line, bool sameCurrency) =>
        line is { InvoiceId: > 0, InvoiceLineId: > 0, InvoiceLine: { } cap }
        && IsQuantity(line.Quantity)
        && SalesInvoicePostingRules.Reconciles(line.NetAmount, line.TaxAmount, line.GrossAmount)
        && SalesInvoicePostingRules.Reconciles(line.BaseNetAmount, line.BaseTaxAmount, line.BaseGrossAmount)
        && (!sameCurrency || (line.NetAmount == line.BaseNetAmount && line.TaxAmount == line.BaseTaxAmount))
        && IsQuantity(cap.Quantity)
        && SalesInvoicePostingRules.IsMoney(cap.NetAmount) && SalesInvoicePostingRules.IsMoney(cap.TaxAmount)
        && SalesInvoicePostingRules.IsMoney(cap.BaseNetAmount) && SalesInvoicePostingRules.IsMoney(cap.BaseTaxAmount);

    private static bool IsQuantity(decimal quantity) =>
        quantity > 0m && quantity <= MaxQuantity && decimal.Round(quantity, 6) == quantity;

    private static FinanceSalesInvoiceSnapshot Header(FinanceSalesReturnSnapshot s) =>
        new(s.CompanyId, s.ReturnId, s.SourceVersion, s.Fingerprint, s.CustomerId, s.State, s.BusinessDate, s.Currency,
            s.BaseCurrency, s.ExchangeRate, s.NetAmount, s.TaxAmount, s.GrossAmount, s.BaseNetAmount, s.BaseTaxAmount,
            s.BaseGrossAmount);
}
