using System.Text.RegularExpressions;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// Committed unrealised overlay attached to one exposure (BR-FI-117／118, ADR-085). The anchor is the latest Posted
/// valuation: <see cref="AnchorOpenOriginal"/> is the open original at that valuation and <see cref="AnchorOverlay"/>
/// the signed base overlay then carried (positive raises the carrying base). <see cref="ReleasedSinceAnchor"/> is the
/// signed overlay already released by committed settlements after the anchor. No valuation yet is <c>(0, 0, 0)</c>.
/// </summary>
public sealed record FxOverlayState(decimal AnchorOpenOriginal, decimal AnchorOverlay, decimal ReleasedSinceAnchor)
{
    public static FxOverlayState None { get; } = new(0m, 0m, 0m);
}

/// <summary>Saved approved closing rate snapshot: <c>1 <see cref="FromCurrency"/> = rate <see cref="ToCurrency"/></c>.</summary>
public sealed record FxClosingRate(string? FromCurrency, string? ToCurrency, decimal Rate);

/// <summary>
/// Period-end valuation of one exposure. <see cref="ValuationDelta"/> is the signed change of the carrying base
/// (target − carrying); <see cref="UnrealisedDifference"/> is signed gain (receivable: delta; payable: −delta).
/// <see cref="OverlayAfter"/> is the new anchor saved with the Posted valuation.
/// </summary>
public sealed record FxValuationResult(
    decimal OpenOriginal,
    decimal OpenHistoricalBase,
    decimal CurrentOverlay,
    decimal CarryingBase,
    decimal TargetBase,
    decimal ValuationDelta,
    decimal UnrealisedDifference,
    FxRealisedOutcome Outcome,
    decimal GainAmount,
    decimal LossAmount,
    bool HasOpenExposure,
    FxOverlayState OverlayAfter);

public sealed record FxValuationCalculation(FxValuationResult? Result, IReadOnlyList<FxAllocationFieldError> Errors)
{
    public bool IsValid => Result is not null;
}

/// <summary>
/// One settlement allocation with its realised result (unchanged from <see cref="FxRealisedDifferenceRules"/>, always
/// against historical base) and the signed prior overlay it releases; <see cref="RemainingOverlay"/> is after it.
/// </summary>
public sealed record FxOverlayReleaseResult(FxAllocationResult Realised, decimal OverlayReleased, decimal RemainingOverlay);

public sealed record FxOverlayReleaseCalculation(
    IReadOnlyList<FxOverlayReleaseResult>? Results,
    FxOverlayState? OverlayAfter,
    IReadOnlyList<FxAllocationFieldError> Errors)
{
    public bool IsValid => Results is not null;
}

/// <summary>
/// Pure period-end valuation and unrealised overlay rules for one exposure (BR-FI-117／118, ADR-085). The overlay
/// still attached when <c>open</c> original is open is <c>round(anchor overlay × open ÷ anchor open)</c> (away from
/// zero, base precision): cumulative like ADR-084, so releases never change sign, never exceed the overlay and full
/// settlement leaves exactly zero. Valuation posts only <c>round(open × closing rate) − (open historical base +
/// remaining overlay)</c>; zero is <see cref="FxRealisedOutcome.None"/>. Error collections are reported in order
/// exposure, discharged, overlay, then closingRate or allocations; any error rejects the call. Cutoff, manifest,
/// policy, locks, permanent keys and GL stay with WU-FI-014a／014b.
/// </summary>
public static partial class FxUnrealisedOverlayRules
{
    private const decimal SqlAmountMaximum = 999999999999999.9999m;
    private const decimal SqlRateMaximum = 99999999999.99999999m;

    public static FxValuationCalculation Value(
        FxExposureSnapshot? exposure,
        FxDischargedToDate? discharged,
        FxOverlayState? overlay,
        FxClosingRate? closingRate)
    {
        var errors = new List<FxAllocationFieldError>();
        var stateValid = ValidateState(exposure, discharged, overlay, errors);

        if (closingRate is null)
        {
            errors.Add(new("closingRate", null, "closingRate", FxAllocationError.Required));
            return new(null, errors);
        }

        void Fail(string field, FxAllocationError error) => errors.Add(new("closingRate", null, field, error));
        var e = exposure;
        if (CurrencyError(closingRate.FromCurrency) is { } fe) Fail("fromCurrency", fe);
        else if (e is not null && CurrencyError(e.DocumentCurrency) is null && closingRate.FromCurrency != e.DocumentCurrency)
            Fail("fromCurrency", FxAllocationError.CurrencyMismatch);
        if (CurrencyError(closingRate.ToCurrency) is { } te) Fail("toCurrency", te);
        else if (e is not null && CurrencyError(e.BaseCurrency) is null && closingRate.ToCurrency != e.BaseCurrency)
            Fail("toCurrency", FxAllocationError.CurrencyMismatch);

        var rate = closingRate.Rate;
        var rateError = rate <= 0 || rate > SqlRateMaximum ? FxAllocationError.OutOfRange
            : decimal.Round(rate, FxRealisedDifferenceRules.RateDecimals) != rate ? FxAllocationError.InvalidScale
            : (FxAllocationError?)null;
        if (rateError is { } re) Fail("rate", re);

        if (!stateValid || errors.Count > 0) return new(null, errors);

        var openOriginal = e!.OriginalGross - discharged!.Original;
        var openBase = e.HistoricalBaseGross - discharged.Base;
        var currentOverlay = RemainingOverlay(overlay!, openOriginal, e.BaseDecimals);
        var carrying = openBase + currentOverlay;
        var target = Round(openOriginal * rate, e.BaseDecimals);
        var delta = target - carrying;
        if (target > SqlAmountMaximum || Math.Abs(delta) > SqlAmountMaximum)
        {
            Fail("rate", FxAllocationError.Overflow);
            return new(null, errors);
        }

        var difference = e.Side == FxExposureSide.Receivable ? delta : -delta;
        var outcome = difference > 0 ? FxRealisedOutcome.Gain
            : difference < 0 ? FxRealisedOutcome.Loss
            : FxRealisedOutcome.None;

        return new(new(
            openOriginal, openBase, currentOverlay, carrying, target, delta, difference, outcome,
            difference > 0 ? difference : 0m,
            difference < 0 ? -difference : 0m,
            openOriginal > 0,
            new(openOriginal, openOriginal > 0 ? currentOverlay + delta : 0m, 0m)), []);
    }

    public static FxOverlayReleaseCalculation Release(
        FxExposureSnapshot? exposure,
        FxDischargedToDate? discharged,
        FxOverlayState? overlay,
        IReadOnlyList<FxAllocationRequest>? allocations)
    {
        var errors = new List<FxAllocationFieldError>();
        var stateValid = ValidateState(exposure, discharged, overlay, errors);

        // Realised difference stays against historical base (BR-FI-118): the overlay never changes it.
        var realised = FxRealisedDifferenceRules.Calculate(exposure, discharged, allocations);
        errors.AddRange(realised.Errors.Where(x => x.Collection == "allocations"));
        if (!stateValid || errors.Count > 0) return new(null, null, errors);

        var e = exposure!;
        var o = overlay!;
        var remaining = RemainingOverlay(o, e.OriginalGross - discharged!.Original, e.BaseDecimals);
        var released = o.ReleasedSinceAnchor;
        var results = new List<FxOverlayReleaseResult>(realised.Results!.Count);
        foreach (var r in realised.Results!)
        {
            var next = RemainingOverlay(o, r.RemainingOriginal, e.BaseDecimals);
            var release = remaining - next;
            remaining = next;
            released += release;
            results.Add(new(r, release, next));
        }

        return new(results, o with { ReleasedSinceAnchor = released }, []);
    }

    /// <summary>
    /// Validates exposure and committed discharge through <see cref="FxRealisedDifferenceRules"/>, then the overlay
    /// anchor against them. Returns whether all three are valid.
    /// </summary>
    private static bool ValidateState(
        FxExposureSnapshot? exposure,
        FxDischargedToDate? discharged,
        FxOverlayState? overlay,
        List<FxAllocationFieldError> errors)
    {
        var baseCheck = FxRealisedDifferenceRules.Calculate(exposure, discharged, []);
        errors.AddRange(baseCheck.Errors);
        var e = exposure;
        int? docScale = e is { DocumentDecimals: >= 0 and <= FxRealisedDifferenceRules.MaxCurrencyDecimals }
            ? e.DocumentDecimals : null;
        int? baseScale = e is { BaseDecimals: >= 0 and <= FxRealisedDifferenceRules.MaxCurrencyDecimals }
            ? e.BaseDecimals : null;

        if (overlay is null)
        {
            errors.Add(new("overlay", null, "overlay", FxAllocationError.Required));
            return false;
        }

        void Fail(string field, FxAllocationError error) => errors.Add(new("overlay", null, field, error));
        var stateValid = baseCheck.IsValid;
        var openOriginal = stateValid ? e!.OriginalGross - discharged!.Original : 0m;

        var anchorError = overlay.AnchorOpenOriginal < 0 || overlay.AnchorOpenOriginal > SqlAmountMaximum
                          || stateValid && overlay.AnchorOpenOriginal > e!.OriginalGross
            ? FxAllocationError.OutOfRange
            : docScale is { } ds && decimal.Round(overlay.AnchorOpenOriginal, ds) != overlay.AnchorOpenOriginal
                ? FxAllocationError.InvalidScale
                // Open original only decreases after the anchor valuation; anchor 0 means no valuation yet.
                : stateValid && overlay.AnchorOpenOriginal != 0 && overlay.AnchorOpenOriginal < openOriginal
                    ? FxAllocationError.Inconsistent
                    : (FxAllocationError?)null;
        if (anchorError is { } ae) Fail("anchorOpenOriginal", ae);

        var overlayError = SignedAmountError(overlay.AnchorOverlay, baseScale)
                           ?? (anchorError is null && overlay.AnchorOpenOriginal == 0 && overlay.AnchorOverlay != 0
                               ? FxAllocationError.Inconsistent
                               : null);
        if (overlayError is { } oe) Fail("anchorOverlay", oe);

        var releasedError = SignedAmountError(overlay.ReleasedSinceAnchor, baseScale);
        if (releasedError is { } re) Fail("releasedSinceAnchor", re);
        else if (stateValid && anchorError is null && overlayError is null
                 && overlay.AnchorOverlay - RemainingOverlay(overlay, openOriginal, e!.BaseDecimals)
                 != overlay.ReleasedSinceAnchor)
            Fail("releasedSinceAnchor", FxAllocationError.Inconsistent);

        return stateValid && anchorError is null && overlayError is null && releasedError is null;
    }

    /// <summary>Overlay still attached while <paramref name="openOriginal"/> is open; exact at anchor and zero at full settlement.</summary>
    private static decimal RemainingOverlay(FxOverlayState o, decimal openOriginal, int baseDecimals)
    {
        if (openOriginal == o.AnchorOpenOriginal) return o.AnchorOverlay;
        if (openOriginal == 0 || o.AnchorOverlay == 0) return 0m;

        decimal raw;
        try
        {
            raw = o.AnchorOverlay * openOriginal / o.AnchorOpenOriginal;
        }
        catch (OverflowException)
        {
            raw = o.AnchorOverlay * (openOriginal / o.AnchorOpenOriginal);
        }

        return Round(raw, baseDecimals);
    }

    private static decimal Round(decimal value, int decimals) =>
        decimal.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static FxAllocationError? SignedAmountError(decimal value, int? scale) =>
        Math.Abs(value) > SqlAmountMaximum ? FxAllocationError.OutOfRange
        : scale is { } s && decimal.Round(value, s) != value ? FxAllocationError.InvalidScale
        : null;

    private static FxAllocationError? CurrencyError(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? FxAllocationError.Required
        : CurrencyCode().IsMatch(currency) ? null
        : FxAllocationError.InvalidFormat;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
