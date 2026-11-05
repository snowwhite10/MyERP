namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// One company's return period being reconciled (BR-FI-109). Dates are inclusive like <see cref="TaxReturnPeriod"/>;
/// amounts are in base currency with <see cref="BaseCurrencyDecimals"/> (0–4).
/// </summary>
public sealed record TaxReconciliationScope(long CompanyId, DateOnly StartDate, DateOnly EndDate,
    int BaseCurrencyDecimals);

/// <summary>
/// One committed tax detail in the frozen period set (values from <see cref="TaxDetailLine"/>). A detail with zero tax
/// has no account and no GL leg and is excluded, not missing.
/// </summary>
public sealed record TaxReconciliationDetail(
    long TaxDetailId,
    long CompanyId,
    long SourceId,
    TaxRole Role,
    long? GlAccountId,
    DateOnly TaxPointDate,
    decimal SignedBaseTax);

/// <summary>
/// One Posted GL line read at the same cutoff: every line on a mapped tax account in the period plus every line tagged
/// with a detail of the set. <see cref="TaxDetailId"/> is the detail stamped by the posting transaction (null for a
/// manual journal). Exactly one of debit/credit is positive.
/// </summary>
public sealed record TaxReconciliationGlLeg(
    long GlLineId,
    long JournalId,
    long CompanyId,
    long? TaxDetailId,
    long AccountId,
    DateOnly PostingDate,
    decimal Debit,
    decimal Credit);

public enum TaxReconciliationDifferenceKind
{
    Missing,
    Duplicate,
    WrongAccount,
    WrongPeriod,
    AmountDifference,
    UnexpectedLeg,
    Unmatched
}

/// <summary>
/// One reconciliation difference naming both sides: the detail and its source (null for an unmatched GL line) and every
/// GL line/journal involved (ascending). Amounts are signed like the detail: output roles credit − debit, input
/// debit − credit; an unmatched line has no role and uses debit − credit. <see cref="Difference"/> = actual − expected.
/// </summary>
public sealed record TaxReconciliationDifference(
    TaxReconciliationDifferenceKind Kind,
    long? TaxDetailId,
    long? SourceId,
    TaxRole? Role,
    IReadOnlyList<long> GlLineIds,
    IReadOnlyList<long> JournalIds,
    long? ExpectedAccountId,
    long? ActualAccountId,
    DateOnly? GlPostingDate,
    decimal ExpectedBaseTax,
    decimal ActualBaseTax,
    decimal Difference);

/// <summary>
/// Outcome of one reconciliation. Every difference is unexplained here and blocks approval; totals are informational
/// only, so offsetting differences never net to a pass.
/// </summary>
public sealed record TaxReconciliationReport(
    IReadOnlyList<TaxReconciliationDifference> Differences,
    IReadOnlyList<long> MatchedTaxDetailIds,
    IReadOnlyList<long> ExcludedTaxDetailIds,
    decimal DetailBaseTaxTotal,
    decimal GlBaseTaxTotal)
{
    public bool BlocksApproval => Differences.Count > 0;
}

public enum TaxReconciliationError
{
    Required,
    OutOfRange,
    InvalidScale,
    Duplicate,
    CrossCompany,
    OutsidePeriod,
    SignMismatch,
    NotApplicable,
    NotSingleSided
}

/// <summary>
/// One input error. <see cref="Collection"/> is <c>scope</c>, <c>request</c>, <c>details</c>, <c>glLegs</c> or
/// <c>totals</c>; <see cref="Index"/> is the item position for the two lists.
/// </summary>
public sealed record TaxReconciliationFieldError(string Collection, int? Index, string Field,
    TaxReconciliationError Error);

public sealed record TaxReconciliationResult(TaxReconciliationReport? Report,
    IReadOnlyList<TaxReconciliationFieldError> Errors)
{
    public bool IsValid => Report is not null;
}

/// <summary>
/// Pure tax-to-GL reconciliation (BR-FI-109, ADR-083). Compares each detail's signed base tax with the Posted GL lines
/// tagged to it and reports missing, duplicate, wrong-account, wrong-period and amount differences, legs tagged to a
/// no-tax detail and untagged/unknown tax-account lines. It never proposes a balancing line or edits either side.
/// Queries nothing: the cutoff, source reads, explanations and approval workflow stay with WU-FI-013b.
/// </summary>
public static class TaxGlReconciliationRules
{
    public const int MaxCurrencyDecimals = 4;
    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static TaxReconciliationResult Reconcile(
        TaxReconciliationScope? scope,
        IReadOnlyList<TaxReconciliationDetail>? details,
        IReadOnlyList<TaxReconciliationGlLeg>? glLegs)
    {
        var errors = new List<TaxReconciliationFieldError>();

        long? company = null;
        var rangeValid = false;
        int? scale = null;
        if (scope is null)
        {
            errors.Add(new("scope", null, "scope", TaxReconciliationError.Required));
        }
        else
        {
            if (scope.CompanyId <= 0) errors.Add(new("scope", null, "companyId", TaxReconciliationError.Required));
            else company = scope.CompanyId;
            if (scope.StartDate > scope.EndDate)
                errors.Add(new("scope", null, "endDate", TaxReconciliationError.OutOfRange));
            else rangeValid = true;
            if (scope.BaseCurrencyDecimals is >= 0 and <= MaxCurrencyDecimals) scale = scope.BaseCurrencyDecimals;
            else errors.Add(new("scope", null, "baseCurrencyDecimals", TaxReconciliationError.OutOfRange));
        }

        if (details is null) errors.Add(new("request", null, "details", TaxReconciliationError.Required));
        if (glLegs is null) errors.Add(new("request", null, "glLegs", TaxReconciliationError.Required));

        TaxReconciliationError? AmountError(decimal value) =>
            Math.Abs(value) > SqlAmountMaximum ? TaxReconciliationError.OutOfRange
            : scale is { } s && decimal.Round(value, s) != value ? TaxReconciliationError.InvalidScale
            : null;

        bool InPeriod(DateOnly date) => date >= scope!.StartDate && date <= scope.EndDate;

        var seenDetails = new HashSet<long>();
        for (var i = 0; i < (details?.Count ?? 0); i++)
        {
            var d = details![i];
            void Fail(string field, TaxReconciliationError e) => errors.Add(new("details", i, field, e));
            if (d is null)
            {
                Fail("detail", TaxReconciliationError.Required);
                continue;
            }

            if (d.TaxDetailId <= 0) Fail("taxDetailId", TaxReconciliationError.Required);
            else if (!seenDetails.Add(d.TaxDetailId)) Fail("taxDetailId", TaxReconciliationError.Duplicate);

            if (d.CompanyId <= 0) Fail("companyId", TaxReconciliationError.Required);
            else if (company is { } c && d.CompanyId != c) Fail("companyId", TaxReconciliationError.CrossCompany);

            if (d.SourceId <= 0) Fail("sourceId", TaxReconciliationError.Required);

            var roleValid = Enum.IsDefined(d.Role);
            if (!roleValid) Fail("role", TaxReconciliationError.OutOfRange);

            if (d.GlAccountId is { } a && a <= 0) Fail("glAccountId", TaxReconciliationError.Required);
            else if (d.SignedBaseTax != 0 && d.GlAccountId is null)
                Fail("glAccountId", TaxReconciliationError.Required);
            else if (d.SignedBaseTax == 0 && d.GlAccountId is not null)
                Fail("glAccountId", TaxReconciliationError.NotApplicable);

            if (rangeValid && !InPeriod(d.TaxPointDate)) Fail("taxPointDate", TaxReconciliationError.OutsidePeriod);

            if (AmountError(d.SignedBaseTax) is { } te) Fail("signedBaseTax", te);
            else if (roleValid && (d.Role == TaxRole.OutputCredit ? d.SignedBaseTax > 0 : d.SignedBaseTax < 0))
                Fail("signedBaseTax", TaxReconciliationError.SignMismatch);
        }

        var seenLines = new HashSet<long>();
        for (var i = 0; i < (glLegs?.Count ?? 0); i++)
        {
            var g = glLegs![i];
            void Fail(string field, TaxReconciliationError e) => errors.Add(new("glLegs", i, field, e));
            if (g is null)
            {
                Fail("glLeg", TaxReconciliationError.Required);
                continue;
            }

            if (g.GlLineId <= 0) Fail("glLineId", TaxReconciliationError.Required);
            else if (!seenLines.Add(g.GlLineId)) Fail("glLineId", TaxReconciliationError.Duplicate);

            if (g.JournalId <= 0) Fail("journalId", TaxReconciliationError.Required);

            if (g.CompanyId <= 0) Fail("companyId", TaxReconciliationError.Required);
            else if (company is { } c && g.CompanyId != c) Fail("companyId", TaxReconciliationError.CrossCompany);

            if (g.TaxDetailId is <= 0) Fail("taxDetailId", TaxReconciliationError.OutOfRange);
            if (g.AccountId <= 0) Fail("accountId", TaxReconciliationError.Required);

            TaxReconciliationError? SideError(decimal v) => v < 0 ? TaxReconciliationError.OutOfRange : AmountError(v);
            var debitError = SideError(g.Debit);
            var creditError = SideError(g.Credit);
            if (debitError is { } de) Fail("debit", de);
            if (creditError is { } ce) Fail("credit", ce);
            if (debitError is null && creditError is null && (g.Debit > 0) == (g.Credit > 0))
                Fail("credit", TaxReconciliationError.NotSingleSided);
        }

        if (errors.Count > 0) return new(null, errors);

        var detailTotal = details!.Sum(d => d.SignedBaseTax);
        var detailById = details!.ToDictionary(d => d.TaxDetailId);
        decimal Signed(TaxReconciliationGlLeg g, TaxRole? role) =>
            role is TaxRole.Output or TaxRole.OutputCredit ? g.Credit - g.Debit : g.Debit - g.Credit;
        var glTotal = glLegs!.Sum(g =>
            Signed(g, g.TaxDetailId is { } id && detailById.TryGetValue(id, out var d) ? d.Role : null));

        if (Math.Abs(detailTotal) > SqlAmountMaximum)
            errors.Add(new("totals", null, "detailBaseTaxTotal", TaxReconciliationError.OutOfRange));
        if (Math.Abs(glTotal) > SqlAmountMaximum)
            errors.Add(new("totals", null, "glBaseTaxTotal", TaxReconciliationError.OutOfRange));
        if (errors.Count > 0) return new(null, errors);

        var legsByDetail = glLegs!
            .Where(g => g.TaxDetailId is { } id && detailById.ContainsKey(id))
            .GroupBy(g => g.TaxDetailId!.Value)
            .ToDictionary(grp => grp.Key, grp => grp.OrderBy(g => g.GlLineId).ToList());

        var differences = new List<TaxReconciliationDifference>();
        var matched = new List<long>();
        var excluded = new List<long>();
        foreach (var d in details!)
        {
            var legs = legsByDetail.GetValueOrDefault(d.TaxDetailId) ?? [];
            var actual = legs.Sum(g => Signed(g, d.Role));
            TaxReconciliationDifference Diff(TaxReconciliationDifferenceKind kind, long? actualAccount = null,
                DateOnly? posting = null) =>
                new(kind, d.TaxDetailId, d.SourceId, d.Role,
                    legs.Select(g => g.GlLineId).ToList(),
                    legs.Select(g => g.JournalId).Distinct().Order().ToList(),
                    d.GlAccountId, actualAccount, posting, d.SignedBaseTax, actual, actual - d.SignedBaseTax);

            if (d.GlAccountId is null)
            {
                // No-tax source: excluded explicitly; any leg tagged to it is itself a difference.
                if (legs.Count == 0) excluded.Add(d.TaxDetailId);
                else differences.Add(Diff(TaxReconciliationDifferenceKind.UnexpectedLeg));
                continue;
            }

            if (legs.Count == 0)
            {
                differences.Add(Diff(TaxReconciliationDifferenceKind.Missing));
                continue;
            }

            if (legs.Count > 1)
            {
                differences.Add(Diff(TaxReconciliationDifferenceKind.Duplicate));
                continue;
            }

            var leg = legs[0];
            var before = differences.Count;
            if (leg.AccountId != d.GlAccountId)
                differences.Add(Diff(TaxReconciliationDifferenceKind.WrongAccount, leg.AccountId, leg.PostingDate));
            if (!InPeriod(leg.PostingDate))
                differences.Add(Diff(TaxReconciliationDifferenceKind.WrongPeriod, leg.AccountId, leg.PostingDate));
            if (actual != d.SignedBaseTax)
                differences.Add(Diff(TaxReconciliationDifferenceKind.AmountDifference, leg.AccountId,
                    leg.PostingDate));
            if (differences.Count == before) matched.Add(d.TaxDetailId);
        }

        foreach (var g in glLegs!)
        {
            if (g.TaxDetailId is { } id && detailById.ContainsKey(id)) continue;
            var amount = Signed(g, null);
            differences.Add(new(TaxReconciliationDifferenceKind.Unmatched, g.TaxDetailId, null, null,
                [g.GlLineId], [g.JournalId], null, g.AccountId, g.PostingDate, 0m, amount, amount));
        }

        return new(new TaxReconciliationReport(differences, matched, excluded, detailTotal, glTotal), []);
    }
}
