namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// One company/regime tax return period (BR-FI-107). Both dates are inclusive; the period is linked to one declared
/// policy version that defines the tax point. A period without a policy is never eligible.
/// </summary>
public sealed record TaxReturnPeriod(long PeriodId, DateOnly StartDate, DateOnly EndDate, string? PolicyVersion);

public enum TaxPeriodError
{
    Required,
    OutOfRange,
    Duplicate,
    Overlap,
    PolicyRequired
}

/// <summary>One calendar error; <see cref="Index"/> is the period's position in the request (null for the list).</summary>
public sealed record TaxPeriodCalendarError(int? Index, string Field, TaxPeriodError Error);

public enum TaxPointAssignmentStatus
{
    Assigned,
    TaxPointMissing,
    NoPeriod,
    Ambiguous,
    PolicyMissing
}

/// <summary>
/// Where a source's policy tax point falls. Only <see cref="TaxPointAssignmentStatus.Assigned"/> has a period;
/// <see cref="CandidatePeriodIds"/> names every covering period (ascending) so an ambiguity can be explained.
/// </summary>
public sealed record TaxPointAssignment(
    TaxPointAssignmentStatus Status,
    long? PeriodId,
    string? PolicyVersion,
    IReadOnlyList<long> CandidatePeriodIds);

/// <summary>
/// One committed FI tax detail in the frozen source set (values from <see cref="TaxDetailLine"/>). Output and input
/// are non-negative, output credit non-positive; only input carries a claimable amount.
/// </summary>
public sealed record TaxReturnSource(
    long TaxDetailId,
    TaxRole Role,
    DateOnly TaxPointDate,
    decimal SignedBaseTax,
    decimal ClaimableBaseTax);

/// <summary>An explicit return adjustment in base currency; only an approved adjustment enters the net.</summary>
public sealed record TaxReturnAdjustment(long AdjustmentId, decimal SignedBaseAmount, bool Approved);

public enum TaxReturnPosition
{
    Zero,
    Payable,
    Credit
}

/// <summary>
/// Period totals in base currency. <see cref="Net"/> = output − claimable input + approved adjustments. A negative net
/// is a <see cref="TaxReturnPosition.Credit"/> carried as <see cref="CreditAmount"/>, never a cash receipt.
/// Unapproved adjustments are listed as pending (an incompleteness reason), not added.
/// </summary>
public sealed record TaxReturnTotals(
    decimal OutputBaseTax,
    decimal InputClaimableBaseTax,
    decimal ApprovedAdjustment,
    decimal Net,
    TaxReturnPosition Position,
    decimal PayableAmount,
    decimal CreditAmount,
    IReadOnlyList<long> IncludedSourceIds,
    IReadOnlyList<long> AppliedAdjustmentIds,
    IReadOnlyList<long> PendingAdjustmentIds);

public enum TaxReturnError
{
    Required,
    OutOfRange,
    InvalidScale,
    Duplicate,
    OutsidePeriod,
    SignMismatch,
    ExceedsTax,
    NotApplicable,
    PolicyRequired
}

/// <summary>
/// One calculation error. <see cref="Collection"/> is <c>period</c>, <c>request</c>, <c>sources</c>,
/// <c>adjustments</c> or <c>totals</c>; <see cref="Index"/> is the item position for the two lists.
/// </summary>
public sealed record TaxReturnFieldError(string Collection, int? Index, string Field, TaxReturnError Error);

public sealed record TaxReturnCalculation(TaxReturnTotals? Totals, IReadOnlyList<TaxReturnFieldError> Errors)
{
    public bool IsValid => Totals is not null;
}

/// <summary>
/// Pure tax return period rules (BR-FI-107／108, ADR-082): inclusive non-overlapping policy periods, unique tax point
/// assignment and period payable/credit. Queries nothing: the durable cutoff, frozen source set, revisions and SQL
/// stay with WU-FI-013b. Every invalid field is reported once, in request then field order.
/// </summary>
public static class TaxReturnPeriodRules
{
    public const int MaxCurrencyDecimals = 4;
    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static IReadOnlyList<TaxPeriodCalendarError> ValidateCalendar(IReadOnlyList<TaxReturnPeriod>? periods)
    {
        if (periods is null)
            return [new(null, "periods", TaxPeriodError.Required)];

        var errors = new List<TaxPeriodCalendarError>();
        var seenIds = new HashSet<long>();
        var validRanges = new List<TaxReturnPeriod>();
        for (var i = 0; i < periods.Count; i++)
        {
            var p = periods[i];
            if (p is null)
            {
                errors.Add(new(i, "period", TaxPeriodError.Required));
                continue;
            }

            if (p.PeriodId <= 0) errors.Add(new(i, "periodId", TaxPeriodError.Required));
            else if (!seenIds.Add(p.PeriodId)) errors.Add(new(i, "periodId", TaxPeriodError.Duplicate));

            if (p.StartDate > p.EndDate)
            {
                errors.Add(new(i, "endDate", TaxPeriodError.OutOfRange));
            }
            else
            {
                // Inclusive dates: sharing even one boundary day assigns that day twice.
                if (validRanges.Any(v => p.StartDate <= v.EndDate && v.StartDate <= p.EndDate))
                    errors.Add(new(i, "startDate", TaxPeriodError.Overlap));
                validRanges.Add(p);
            }

            if (string.IsNullOrWhiteSpace(p.PolicyVersion))
                errors.Add(new(i, "policyVersion", TaxPeriodError.PolicyRequired));
        }

        return errors.OrderBy(e => e.Index).ThenBy(e => FieldRank(e.Field)).ToList();
    }

    /// <summary>
    /// Assigns an explicit policy tax point to exactly one period. A missing tax point is never defaulted from an
    /// invoice or posting date; no covering period, several covering periods or a covering period without policy
    /// blocks. Reversed or null periods cover nothing (the calendar check reports them).
    /// </summary>
    public static TaxPointAssignment AssignTaxPoint(DateOnly? taxPointDate, IReadOnlyList<TaxReturnPeriod>? periods)
    {
        if (taxPointDate is not { } date)
            return new(TaxPointAssignmentStatus.TaxPointMissing, null, null, []);

        var covering = (periods ?? [])
            .Where(p => p is not null && p.StartDate <= date && date <= p.EndDate)
            .ToList();
        var ids = covering.Select(p => p.PeriodId).Distinct().Order().ToList();

        if (covering.Count == 0) return new(TaxPointAssignmentStatus.NoPeriod, null, null, []);
        if (covering.Count > 1) return new(TaxPointAssignmentStatus.Ambiguous, null, null, ids);

        var only = covering[0];
        return string.IsNullOrWhiteSpace(only.PolicyVersion)
            ? new(TaxPointAssignmentStatus.PolicyMissing, null, null, ids)
            : new(TaxPointAssignmentStatus.Assigned, only.PeriodId, only.PolicyVersion.Trim(), ids);
    }

    public static TaxReturnCalculation Calculate(
        TaxReturnPeriod? period,
        int baseCurrencyDecimals,
        IReadOnlyList<TaxReturnSource>? sources,
        IReadOnlyList<TaxReturnAdjustment>? adjustments)
    {
        var errors = new List<TaxReturnFieldError>();

        var periodRangeValid = false;
        if (period is null)
        {
            errors.Add(new("period", null, "period", TaxReturnError.Required));
        }
        else
        {
            if (period.PeriodId <= 0) errors.Add(new("period", null, "periodId", TaxReturnError.Required));
            if (period.StartDate > period.EndDate) errors.Add(new("period", null, "endDate", TaxReturnError.OutOfRange));
            else periodRangeValid = true;
            if (string.IsNullOrWhiteSpace(period.PolicyVersion))
                errors.Add(new("period", null, "policyVersion", TaxReturnError.PolicyRequired));
        }

        int? scale = baseCurrencyDecimals is >= 0 and <= MaxCurrencyDecimals ? baseCurrencyDecimals : null;
        if (scale is null) errors.Add(new("request", null, "baseCurrencyDecimals", TaxReturnError.OutOfRange));
        if (sources is null) errors.Add(new("request", null, "sources", TaxReturnError.Required));
        if (adjustments is null) errors.Add(new("request", null, "adjustments", TaxReturnError.Required));

        // Returns the amount error, or null when the value fits SQL decimal(19,4) at the base scale.
        TaxReturnError? AmountError(decimal value) =>
            Math.Abs(value) > SqlAmountMaximum ? TaxReturnError.OutOfRange
            : scale is { } s && decimal.Round(value, s) != value ? TaxReturnError.InvalidScale
            : null;

        var seenSources = new HashSet<long>();
        for (var i = 0; i < (sources?.Count ?? 0); i++)
        {
            var s = sources![i];
            void Fail(string field, TaxReturnError e) => errors.Add(new("sources", i, field, e));
            if (s is null)
            {
                Fail("source", TaxReturnError.Required);
                continue;
            }

            if (s.TaxDetailId <= 0) Fail("taxDetailId", TaxReturnError.Required);
            else if (!seenSources.Add(s.TaxDetailId)) Fail("taxDetailId", TaxReturnError.Duplicate);

            var roleValid = Enum.IsDefined(s.Role);
            if (!roleValid) Fail("role", TaxReturnError.OutOfRange);

            if (periodRangeValid && (s.TaxPointDate < period!.StartDate || s.TaxPointDate > period.EndDate))
                Fail("taxPointDate", TaxReturnError.OutsidePeriod);

            var taxError = AmountError(s.SignedBaseTax);
            if (taxError is { } te) Fail("signedBaseTax", te);
            else if (roleValid && (s.Role == TaxRole.OutputCredit ? s.SignedBaseTax > 0 : s.SignedBaseTax < 0))
                Fail("signedBaseTax", TaxReturnError.SignMismatch);

            if (!roleValid) continue;
            if (s.Role != TaxRole.Input)
            {
                if (s.ClaimableBaseTax != 0) Fail("claimableBaseTax", TaxReturnError.NotApplicable);
            }
            else if (s.ClaimableBaseTax < 0) Fail("claimableBaseTax", TaxReturnError.OutOfRange);
            else if (AmountError(s.ClaimableBaseTax) is { } ce) Fail("claimableBaseTax", ce);
            else if (taxError is null && s.SignedBaseTax >= 0 && s.ClaimableBaseTax > s.SignedBaseTax)
                Fail("claimableBaseTax", TaxReturnError.ExceedsTax);
        }

        var seenAdjustments = new HashSet<long>();
        for (var i = 0; i < (adjustments?.Count ?? 0); i++)
        {
            var a = adjustments![i];
            void Fail(string field, TaxReturnError e) => errors.Add(new("adjustments", i, field, e));
            if (a is null)
            {
                Fail("adjustment", TaxReturnError.Required);
                continue;
            }

            if (a.AdjustmentId <= 0) Fail("adjustmentId", TaxReturnError.Required);
            else if (!seenAdjustments.Add(a.AdjustmentId)) Fail("adjustmentId", TaxReturnError.Duplicate);

            if (a.SignedBaseAmount == 0) Fail("signedBaseAmount", TaxReturnError.OutOfRange);
            else if (AmountError(a.SignedBaseAmount) is { } ae) Fail("signedBaseAmount", ae);
        }

        if (errors.Count > 0) return new(null, errors);

        var output = sources!.Where(s => s.Role != TaxRole.Input).Sum(s => s.SignedBaseTax);
        var input = sources!.Where(s => s.Role == TaxRole.Input).Sum(s => s.ClaimableBaseTax);
        var applied = adjustments!.Where(a => a.Approved).ToList();
        var adjustment = applied.Sum(a => a.SignedBaseAmount);
        var net = output - input + adjustment;

        foreach (var (field, value) in new[]
                 {
                     ("outputBaseTax", output), ("inputClaimableBaseTax", input), ("approvedAdjustment", adjustment)
                 })
        {
            if (Math.Abs(value) > SqlAmountMaximum) errors.Add(new("totals", null, field, TaxReturnError.OutOfRange));
        }

        // The net is derived; only blame it when every component it is built from fits.
        if (errors.Count == 0 && Math.Abs(net) > SqlAmountMaximum)
            errors.Add(new("totals", null, "net", TaxReturnError.OutOfRange));

        if (errors.Count > 0) return new(null, errors);

        var position = net > 0 ? TaxReturnPosition.Payable : net < 0 ? TaxReturnPosition.Credit : TaxReturnPosition.Zero;
        return new(new TaxReturnTotals(
            output, input, adjustment, net, position,
            net > 0 ? net : 0m,
            net < 0 ? -net : 0m,
            sources!.Select(s => s.TaxDetailId).ToList(),
            applied.Select(a => a.AdjustmentId).ToList(),
            adjustments!.Where(a => !a.Approved).Select(a => a.AdjustmentId).ToList()), []);
    }

    private static int FieldRank(string field) => field switch
    {
        "period" => 0,
        "periodId" => 1,
        "startDate" => 2,
        "endDate" => 3,
        _ => 4
    };
}
