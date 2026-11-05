using System.Text.RegularExpressions;

namespace MyERP.Modules.Finance.Domain;

public enum FxExposureSide
{
    Receivable,
    Payable
}

/// <summary>
/// Immutable Posted foreign-currency AR/AP exposure (BR-FI-114). <see cref="TransactionRate"/> is
/// <c>1 document = rate base</c>; <see cref="HistoricalBaseGross"/> must equal original × rate rounded once (away from
/// zero) to the saved base precision. Precisions come from the saved MD currency snapshot, never a current lookup.
/// </summary>
public sealed record FxExposureSnapshot(
    long ExposureId,
    FxExposureSide Side,
    string? DocumentCurrency,
    string? BaseCurrency,
    int DocumentDecimals,
    int BaseDecimals,
    decimal OriginalGross,
    decimal TransactionRate,
    decimal HistoricalBaseGross);

/// <summary>Committed original and historical base already discharged from the exposure, read in the transaction.</summary>
public sealed record FxDischargedToDate(decimal Original, decimal Base);

/// <summary>
/// One approved receipt/payment allocation to the exposure. <see cref="SettlementBaseAmount"/> is the saved base amount
/// of the cash booking for this allocation (BR-FI-115); the rule never fetches or applies a current rate.
/// </summary>
public sealed record FxAllocationRequest(
    long AllocationId,
    string? Currency,
    decimal OriginalAmount,
    decimal SettlementBaseAmount);

public enum FxRealisedOutcome
{
    None,
    Gain,
    Loss
}

/// <summary>
/// Saved operands and signed result of one allocation (BR-FI-116). Receivable: settlement − historical base;
/// payable: historical base − settlement; positive is gain. Remaining amounts are after this allocation.
/// </summary>
public sealed record FxAllocationResult(
    long AllocationId,
    decimal OriginalDischarged,
    decimal HistoricalBaseDischarged,
    decimal SettlementBase,
    decimal RealisedDifference,
    FxRealisedOutcome Outcome,
    decimal GainAmount,
    decimal LossAmount,
    decimal RemainingOriginal,
    decimal RemainingBase,
    bool SettlesExposure);

public enum FxAllocationError
{
    Required,
    OutOfRange,
    InvalidScale,
    InvalidFormat,
    Duplicate,
    SameCurrency,
    BaseMismatch,
    CurrencyMismatch,
    OverAllocated,
    Inconsistent,

    /// <summary>A computed amount (closing target or valuation delta) exceeds SQL <c>decimal(19,4)</c> (BR-FI-119).</summary>
    Overflow
}

/// <summary>
/// One input error. <see cref="Collection"/> is <c>exposure</c>, <c>discharged</c> or <c>allocations</c>;
/// <see cref="Index"/> is the allocation position.
/// </summary>
public sealed record FxAllocationFieldError(string Collection, int? Index, string Field, FxAllocationError Error);

public sealed record FxRealisedCalculation(
    IReadOnlyList<FxAllocationResult>? Results,
    decimal DischargedOriginalAfter,
    decimal DischargedBaseAfter,
    IReadOnlyList<FxAllocationFieldError> Errors)
{
    public bool IsValid => Results is not null;
}

/// <summary>
/// Pure realised exchange-difference rules for one exposure (BR-FI-114～116, ADR-084). Allocations are applied in
/// request order after the committed discharge. Historical base is released by cumulative proportion: after each
/// allocation the cumulative discharged base is <c>round(historical base × cumulative original ÷ original gross)</c>,
/// and the allocation that reaches the original gross takes exactly the remaining base, so no allocation releases a
/// negative amount and the total never drifts. Any error rejects the whole batch (no results). Locks, permanent
/// source keys, unrealised overlay release and GL lines stay with WU-FI-014a.
/// </summary>
public static partial class FxRealisedDifferenceRules
{
    public const int MaxCurrencyDecimals = 4;
    public const int RateDecimals = 8;
    private const decimal SqlAmountMaximum = 999999999999999.9999m;
    private const decimal SqlRateMaximum = 99999999999.99999999m;

    public static FxRealisedCalculation Calculate(
        FxExposureSnapshot? exposure,
        FxDischargedToDate? discharged,
        IReadOnlyList<FxAllocationRequest>? allocations)
    {
        var errors = new List<FxAllocationFieldError>();
        var e = exposure;

        int? docScale = null, baseScale = null;
        string? docCurrency = null;
        if (e is null)
        {
            errors.Add(new("exposure", null, "exposure", FxAllocationError.Required));
        }
        else
        {
            void Fail(string field, FxAllocationError error) => errors.Add(new("exposure", null, field, error));

            if (e.ExposureId <= 0) Fail("exposureId", FxAllocationError.Required);
            if (!Enum.IsDefined(e.Side)) Fail("side", FxAllocationError.OutOfRange);

            var docError = CurrencyError(e.DocumentCurrency);
            if (docError is { } de) Fail("documentCurrency", de);
            else docCurrency = e.DocumentCurrency;
            if (CurrencyError(e.BaseCurrency) is { } be) Fail("baseCurrency", be);
            else if (docError is null && e.BaseCurrency == e.DocumentCurrency)
                Fail("baseCurrency", FxAllocationError.SameCurrency);

            if (e.DocumentDecimals is >= 0 and <= MaxCurrencyDecimals) docScale = e.DocumentDecimals;
            else Fail("documentDecimals", FxAllocationError.OutOfRange);
            if (e.BaseDecimals is >= 0 and <= MaxCurrencyDecimals) baseScale = e.BaseDecimals;
            else Fail("baseDecimals", FxAllocationError.OutOfRange);

            var originalError = PositiveAmountError(e.OriginalGross, docScale);
            if (originalError is { } oe) Fail("originalGross", oe);

            var rateError = e.TransactionRate <= 0 || e.TransactionRate > SqlRateMaximum ? FxAllocationError.OutOfRange
                : decimal.Round(e.TransactionRate, RateDecimals) != e.TransactionRate ? FxAllocationError.InvalidScale
                : (FxAllocationError?)null;
            if (rateError is { } re) Fail("transactionRate", re);

            if (PositiveAmountError(e.HistoricalBaseGross, baseScale) is { } he) Fail("historicalBaseGross", he);
            else if (originalError is null && rateError is null && baseScale is { } bs
                     && RoundBase(e.OriginalGross * e.TransactionRate, bs) != e.HistoricalBaseGross)
                Fail("historicalBaseGross", FxAllocationError.BaseMismatch);
        }

        var exposureValid = e is not null && errors.Count == 0;

        var dischargedValid = false;
        if (discharged is null)
        {
            errors.Add(new("discharged", null, "discharged", FxAllocationError.Required));
        }
        else
        {
            void Fail(string field, FxAllocationError error) => errors.Add(new("discharged", null, field, error));

            var originalError = discharged.Original < 0 || discharged.Original > SqlAmountMaximum
                                || exposureValid && discharged.Original > e!.OriginalGross
                ? FxAllocationError.OutOfRange
                : docScale is { } ds && decimal.Round(discharged.Original, ds) != discharged.Original
                    ? FxAllocationError.InvalidScale
                    : (FxAllocationError?)null;
            if (originalError is { } oe) Fail("original", oe);

            var baseError = discharged.Base < 0 || discharged.Base > SqlAmountMaximum
                            || exposureValid && discharged.Base > e!.HistoricalBaseGross
                ? FxAllocationError.OutOfRange
                : baseScale is { } bs && decimal.Round(discharged.Base, bs) != discharged.Base
                    ? FxAllocationError.InvalidScale
                    : (FxAllocationError?)null;
            if (baseError is { } be) Fail("base", be);
            else if (exposureValid && originalError is null && CumulativeBase(e!, discharged.Original) != discharged.Base)
                Fail("base", FxAllocationError.Inconsistent);

            dischargedValid = exposureValid && originalError is null && baseError is null;
        }

        if (allocations is null)
            errors.Add(new("allocations", null, "allocations", FxAllocationError.Required));

        var seenIds = new HashSet<long>();
        var runningOriginal = dischargedValid ? discharged!.Original : 0m;
        for (var i = 0; i < (allocations?.Count ?? 0); i++)
        {
            var a = allocations![i];
            void Fail(string field, FxAllocationError error) => errors.Add(new("allocations", i, field, error));
            if (a is null)
            {
                Fail("allocation", FxAllocationError.Required);
                continue;
            }

            if (a.AllocationId <= 0) Fail("allocationId", FxAllocationError.Required);
            else if (!seenIds.Add(a.AllocationId)) Fail("allocationId", FxAllocationError.Duplicate);

            // Each allocation is in the document currency only; cross-currency settlement needs another contract.
            if (CurrencyError(a.Currency) is { } ce) Fail("currency", ce);
            else if (docCurrency is not null && a.Currency != docCurrency)
                Fail("currency", FxAllocationError.CurrencyMismatch);

            if (PositiveAmountError(a.OriginalAmount, docScale) is { } oe) Fail("originalAmount", oe);
            else if (dischargedValid)
            {
                runningOriginal += a.OriginalAmount;
                if (runningOriginal > e!.OriginalGross) Fail("originalAmount", FxAllocationError.OverAllocated);
            }

            if (PositiveAmountError(a.SettlementBaseAmount, baseScale) is { } se) Fail("settlementBaseAmount", se);
        }

        if (errors.Count > 0) return new(null, 0m, 0m, errors);

        var results = new List<FxAllocationResult>(allocations!.Count);
        var cumulativeOriginal = discharged!.Original;
        var cumulativeBase = discharged.Base;
        foreach (var a in allocations)
        {
            cumulativeOriginal += a.OriginalAmount;
            var nextBase = CumulativeBase(e!, cumulativeOriginal);
            var historical = nextBase - cumulativeBase;
            cumulativeBase = nextBase;

            var difference = e!.Side == FxExposureSide.Receivable
                ? a.SettlementBaseAmount - historical
                : historical - a.SettlementBaseAmount;
            var outcome = difference > 0 ? FxRealisedOutcome.Gain
                : difference < 0 ? FxRealisedOutcome.Loss
                : FxRealisedOutcome.None;

            results.Add(new(
                a.AllocationId, a.OriginalAmount, historical, a.SettlementBaseAmount, difference, outcome,
                difference > 0 ? difference : 0m,
                difference < 0 ? -difference : 0m,
                e.OriginalGross - cumulativeOriginal,
                e.HistoricalBaseGross - cumulativeBase,
                cumulativeOriginal == e.OriginalGross));
        }

        return new(results, cumulativeOriginal, cumulativeBase, []);
    }

    /// <summary>
    /// Historical base discharged once <paramref name="cumulativeOriginal"/> of the original gross is discharged.
    /// Monotonic in the cumulative original, exact (no rounding) at full settlement.
    /// </summary>
    private static decimal CumulativeBase(FxExposureSnapshot e, decimal cumulativeOriginal)
    {
        if (cumulativeOriginal == e.OriginalGross) return e.HistoricalBaseGross;
        if (cumulativeOriginal == 0) return 0m;

        decimal raw;
        try
        {
            raw = e.HistoricalBaseGross * cumulativeOriginal / e.OriginalGross;
        }
        catch (OverflowException)
        {
            // Product above decimal range: the ratio (< 1) keeps 28 significant digits, far below base precision.
            raw = e.HistoricalBaseGross * (cumulativeOriginal / e.OriginalGross);
        }

        return RoundBase(raw, e.BaseDecimals);
    }

    private static decimal RoundBase(decimal value, int decimals) =>
        decimal.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static FxAllocationError? PositiveAmountError(decimal value, int? scale) =>
        value <= 0 || value > SqlAmountMaximum ? FxAllocationError.OutOfRange
        : scale is { } s && decimal.Round(value, s) != value ? FxAllocationError.InvalidScale
        : null;

    private static FxAllocationError? CurrencyError(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? FxAllocationError.Required
        : CurrencyCode().IsMatch(currency) ? null
        : FxAllocationError.InvalidFormat;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
