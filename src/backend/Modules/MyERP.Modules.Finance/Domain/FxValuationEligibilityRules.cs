using System.Text.RegularExpressions;

namespace MyERP.Modules.Finance.Domain;

public enum FxPolicyStatus
{
    Draft,
    Approved,
    Retired
}

/// <summary>Draft preview has no accounting effect and can never allow posting (BR-FI-119).</summary>
public enum FxValuationMode
{
    Preview,
    Post
}

/// <summary>A control account whose AR or AP exposures are monetary and may be revalued.</summary>
public sealed record FxEligibleAccount(FxExposureSide Side, long ControlAccountId);

/// <summary>
/// One saved FI valuation policy version (BR-FI-119, ADR-086). It applies to <see cref="CompanyId"/> when
/// <see cref="Status"/> is Approved and <c>EffectiveFrom ≤ valuation date ≤ EffectiveTo</c> (open end when null).
/// The valuation date is the GL period end date of the run. <see cref="RateSource"/> and <see cref="RateType"/> select
/// the MD rate snapshot; <see cref="RateDecimals"/> (0–8) is the maximum rate precision.
/// </summary>
public sealed record FxValuationPolicy(
    long PolicyId,
    int Version,
    long CompanyId,
    FxPolicyStatus Status,
    string? RateSource,
    string? RateType,
    int RateDecimals,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<FxEligibleAccount>? EligibleAccounts,
    long GainAccountId,
    long LossAccountId);

/// <summary>Saved MD currency status snapshot; a currency absent from the list is treated as inactive.</summary>
public sealed record FxCurrencySnapshot(string? Code, bool IsActive);

/// <summary>Saved MD rate snapshot: <c>1 <see cref="FromCurrency"/> = rate <see cref="ToCurrency"/></c> on <see cref="RateDate"/>.</summary>
public sealed record FxRateSnapshot(
    long RateId,
    string? Source,
    string? RateType,
    string? FromCurrency,
    string? ToCurrency,
    DateOnly RateDate,
    decimal Rate,
    bool IsApproved);

/// <summary>One open exposure offered to the run with its company and saved AR/AP control account.</summary>
public sealed record FxValuationCandidate(FxExposureSnapshot? Exposure, long CompanyId, long ControlAccountId);

public sealed record FxValuationEligibilityRequest(
    long CompanyId,
    string? BaseCurrency,
    DateOnly ValuationDate,
    FxValuationMode Mode);

public enum FxPolicyResolution
{
    Resolved,
    Missing,
    Ambiguous,
    Invalid,
    UnsupportedRateType
}

/// <summary>Per-source exclusion reasons, reported in declaration order.</summary>
public enum FxValuationExclusion
{
    PolicyMissing,
    PolicyAmbiguous,
    PolicyInvalid,
    UnsupportedRateType,
    CompanyMismatch,
    BaseCurrencyMismatch,
    BaseCurrencySource,
    InactiveCurrency,
    AccountNotEligible,
    MissingRate,
    StaleRate,
    RateNotApproved,
    AmbiguousRate,
    InvalidRate,
    RatePrecisionExceeded
}

/// <summary>
/// Eligibility of one exposure. When eligible, <see cref="ClosingRate"/> is the input for
/// <see cref="FxUnrealisedOverlayRules.Value"/> and <see cref="RateId"/> is the snapshot to save in the manifest.
/// </summary>
public sealed record FxExposureEligibility(
    long ExposureId,
    bool IsEligible,
    FxClosingRate? ClosingRate,
    long? RateId,
    IReadOnlyList<FxValuationExclusion> Reasons);

/// <summary>
/// <see cref="PostingAllowed"/> is true only in Post mode with a resolved policy and at least one eligible exposure;
/// each exposure stays its own atomic result (BR-FI-117), so excluded ones are reported, not posted.
/// </summary>
public sealed record FxValuationEligibilityResult(
    FxValuationMode Mode,
    FxPolicyResolution PolicyResolution,
    FxValuationPolicy? Policy,
    IReadOnlyList<FxAllocationFieldError> PolicyErrors,
    IReadOnlyList<FxExposureEligibility> Exposures,
    int EligibleCount,
    int ExcludedCount,
    bool PostingAllowed);

public sealed record FxValuationEligibilityCalculation(
    FxValuationEligibilityResult? Result,
    IReadOnlyList<FxAllocationFieldError> Errors)
{
    public bool IsValid => Result is not null;
}

/// <summary>
/// Pure valuation policy and closing-rate eligibility rules (BR-FI-119, ADR-086). Resolves the single Approved policy
/// version of the company covering the valuation date, then for each candidate checks company, base currency, active
/// currencies, monetary account mapping and the one approved rate snapshot of the policy source/type for the pair dated
/// exactly the valuation date. Malformed inputs are errors that reject the call (order request, policies, currencies,
/// rates, candidates); business exclusions are per-exposure reasons. SQL reads, cutoff, manifest, GL period and GL
/// posting stay with WU-FI-014b.
/// </summary>
public static partial class FxValuationEligibilityRules
{
    /// <summary>Rate types usable for a period-end valuation; average or budget rates are not.</summary>
    public static readonly IReadOnlySet<string> SupportedRateTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "CLOSING",
        "SPOT"
    };

    private const decimal SqlRateMaximum = 99999999999.99999999m;

    public static FxValuationEligibilityCalculation Evaluate(
        FxValuationEligibilityRequest? request,
        IReadOnlyList<FxValuationPolicy?>? policies,
        IReadOnlyList<FxCurrencySnapshot?>? currencies,
        IReadOnlyList<FxRateSnapshot?>? rates,
        IReadOnlyList<FxValuationCandidate?>? candidates)
    {
        var errors = new List<FxAllocationFieldError>();
        ValidateRequest(request, errors);
        ValidatePolicies(policies, errors);
        ValidateCurrencies(currencies, errors);
        ValidateRates(rates, errors);
        ValidateCandidates(candidates, errors);
        if (errors.Count > 0) return new(null, errors);

        var req = request!;
        var (resolution, policy, policyErrors) = ResolvePolicy(req, policies!);
        var active = currencies!.Where(c => c!.IsActive).Select(c => c!.Code!).ToHashSet(StringComparer.Ordinal);
        // Index the policy source/type rates by pair once, so a large run is not candidates × rates.
        var ratesByPair = policy is null
            ? null
            : rates!.Where(r => r!.Source == policy.RateSource && r.RateType == policy.RateType)
                .ToLookup(r => (r!.FromCurrency, r.ToCurrency), r => r!);

        var exposures = new List<FxExposureEligibility>(candidates!.Count);
        foreach (var candidate in candidates)
        {
            var id = candidate!.Exposure!.ExposureId;
            if (resolution != FxPolicyResolution.Resolved)
            {
                exposures.Add(new(id, false, null, null, [PolicyReason(resolution)]));
                continue;
            }

            var (reasons, rate) = Reasons(req, policy!, active, ratesByPair!, candidate);
            exposures.Add(rate is null
                ? new(id, false, null, null, reasons)
                : new(id, true, new(rate.FromCurrency, rate.ToCurrency, rate.Rate), rate.RateId, []));
        }

        var eligible = exposures.Count(x => x.IsEligible);
        return new(new(
            req.Mode,
            resolution,
            policy,
            policyErrors,
            exposures,
            eligible,
            exposures.Count - eligible,
            req.Mode == FxValuationMode.Post && resolution == FxPolicyResolution.Resolved && eligible > 0), []);
    }

    private static (FxPolicyResolution, FxValuationPolicy?, IReadOnlyList<FxAllocationFieldError>) ResolvePolicy(
        FxValuationEligibilityRequest request,
        IReadOnlyList<FxValuationPolicy?> policies)
    {
        var applicable = policies
            .Where(p => p!.CompanyId == request.CompanyId
                        && p.Status == FxPolicyStatus.Approved
                        && p.EffectiveFrom <= request.ValuationDate
                        && (p.EffectiveTo is null || request.ValuationDate <= p.EffectiveTo))
            .ToList();
        if (applicable.Count == 0) return (FxPolicyResolution.Missing, null, []);
        if (applicable.Count > 1) return (FxPolicyResolution.Ambiguous, null, []);

        var policy = applicable[0]!;
        var errors = PolicyContentErrors(policy);
        if (errors.Count > 0) return (FxPolicyResolution.Invalid, null, errors);
        return SupportedRateTypes.Contains(policy.RateType!)
            ? (FxPolicyResolution.Resolved, policy, [])
            : (FxPolicyResolution.UnsupportedRateType, null, []);
    }

    private static List<FxAllocationFieldError> PolicyContentErrors(FxValuationPolicy p)
    {
        var errors = new List<FxAllocationFieldError>();
        void Fail(string field, FxAllocationError error) => errors.Add(new("policy", null, field, error));

        if (CodeError(p.RateSource) is { } se) Fail("rateSource", se);
        if (CodeError(p.RateType) is { } te) Fail("rateType", te);
        if (p.RateDecimals is < 0 or > FxRealisedDifferenceRules.RateDecimals) Fail("rateDecimals", FxAllocationError.OutOfRange);
        if (p.EligibleAccounts is not { Count: > 0 }) Fail("eligibleAccounts", FxAllocationError.Required);
        if (p.GainAccountId <= 0) Fail("gainAccountId", FxAllocationError.Required);
        if (p.LossAccountId <= 0) Fail("lossAccountId", FxAllocationError.Required);

        var seen = new HashSet<(FxExposureSide, long)>();
        for (var i = 0; i < (p.EligibleAccounts?.Count ?? 0); i++)
        {
            var a = p.EligibleAccounts![i];
            void FailAccount(string field, FxAllocationError error) => errors.Add(new("policyAccounts", i, field, error));
            if (a is null) FailAccount("account", FxAllocationError.Required);
            else if (!Enum.IsDefined(a.Side)) FailAccount("side", FxAllocationError.OutOfRange);
            else if (a.ControlAccountId <= 0) FailAccount("controlAccountId", FxAllocationError.Required);
            else if (!seen.Add((a.Side, a.ControlAccountId))) FailAccount("controlAccountId", FxAllocationError.Duplicate);
        }

        return errors;
    }

    private static FxValuationExclusion PolicyReason(FxPolicyResolution resolution) => resolution switch
    {
        FxPolicyResolution.Missing => FxValuationExclusion.PolicyMissing,
        FxPolicyResolution.Ambiguous => FxValuationExclusion.PolicyAmbiguous,
        FxPolicyResolution.Invalid => FxValuationExclusion.PolicyInvalid,
        _ => FxValuationExclusion.UnsupportedRateType
    };

    /// <summary>Relation reasons in fixed order; the rate is looked up only when the exposure relation is valid.</summary>
    private static (List<FxValuationExclusion> Reasons, FxRateSnapshot? Rate) Reasons(
        FxValuationEligibilityRequest request,
        FxValuationPolicy policy,
        HashSet<string> active,
        ILookup<(string?, string?), FxRateSnapshot> rates,
        FxValuationCandidate candidate)
    {
        var e = candidate.Exposure!;
        var reasons = new List<FxValuationExclusion>();
        if (candidate.CompanyId != request.CompanyId) reasons.Add(FxValuationExclusion.CompanyMismatch);
        if (e.BaseCurrency != request.BaseCurrency) reasons.Add(FxValuationExclusion.BaseCurrencyMismatch);
        else if (e.DocumentCurrency == e.BaseCurrency) reasons.Add(FxValuationExclusion.BaseCurrencySource);
        if (!active.Contains(e.DocumentCurrency!) || !active.Contains(e.BaseCurrency!))
            reasons.Add(FxValuationExclusion.InactiveCurrency);
        if (!policy.EligibleAccounts!.Contains(new FxEligibleAccount(e.Side, candidate.ControlAccountId)))
            reasons.Add(FxValuationExclusion.AccountNotEligible);
        if (reasons.Count > 0) return (reasons, null);

        var (rate, reason) = SelectRate(request, policy, rates, e);
        if (reason is { } r) reasons.Add(r);
        return (reasons, rate);
    }

    /// <summary>
    /// Only the policy source/type rate for <c>document → base</c> is considered. Exactly one approved snapshot dated
    /// the valuation date is used; an older approved one is <see cref="FxValuationExclusion.StaleRate"/> and is never
    /// carried forward; a later one is ignored.
    /// </summary>
    private static (FxRateSnapshot? Rate, FxValuationExclusion? Reason) SelectRate(
        FxValuationEligibilityRequest request,
        FxValuationPolicy policy,
        ILookup<(string?, string?), FxRateSnapshot> rates,
        FxExposureSnapshot exposure)
    {
        var pair = rates[(exposure.DocumentCurrency, exposure.BaseCurrency)];
        var exact = pair.Where(r => r.RateDate == request.ValuationDate).ToList();
        var approved = exact.Where(r => r.IsApproved).ToList();

        if (approved.Count > 1) return (null, FxValuationExclusion.AmbiguousRate);
        if (approved.Count == 0)
        {
            return (null,
                exact.Count > 0 ? FxValuationExclusion.RateNotApproved
                : pair.Any(r => r.IsApproved && r.RateDate < request.ValuationDate) ? FxValuationExclusion.StaleRate
                : FxValuationExclusion.MissingRate);
        }

        var rate = approved[0];
        if (rate.Rate <= 0 || rate.Rate > SqlRateMaximum) return (null, FxValuationExclusion.InvalidRate);
        if (decimal.Round(rate.Rate, policy.RateDecimals) != rate.Rate)
            return (null, FxValuationExclusion.RatePrecisionExceeded);
        return (rate, null);
    }

    private static void ValidateRequest(FxValuationEligibilityRequest? request, List<FxAllocationFieldError> errors)
    {
        if (request is null)
        {
            errors.Add(new("request", null, "request", FxAllocationError.Required));
            return;
        }

        void Fail(string field, FxAllocationError error) => errors.Add(new("request", null, field, error));
        if (request.CompanyId <= 0) Fail("companyId", FxAllocationError.Required);
        if (CurrencyError(request.BaseCurrency) is { } ce) Fail("baseCurrency", ce);
        if (request.ValuationDate == default) Fail("valuationDate", FxAllocationError.Required);
        if (!Enum.IsDefined(request.Mode)) Fail("mode", FxAllocationError.OutOfRange);
    }

    private static void ValidatePolicies(IReadOnlyList<FxValuationPolicy?>? policies, List<FxAllocationFieldError> errors)
    {
        if (policies is null)
        {
            errors.Add(new("policies", null, "policies", FxAllocationError.Required));
            return;
        }

        var seen = new HashSet<(long, int)>();
        for (var i = 0; i < policies.Count; i++)
        {
            var p = policies[i];
            void Fail(string field, FxAllocationError error) => errors.Add(new("policies", i, field, error));
            if (p is null) Fail("policy", FxAllocationError.Required);
            else if (p.PolicyId <= 0) Fail("policyId", FxAllocationError.Required);
            else if (p.Version <= 0) Fail("version", FxAllocationError.Required);
            else if (!seen.Add((p.PolicyId, p.Version))) Fail("policyId", FxAllocationError.Duplicate);
            else if (!Enum.IsDefined(p.Status)) Fail("status", FxAllocationError.OutOfRange);
            // A reversed range would never cover any date and silently hide the version; reject it instead.
            else if (p.EffectiveTo is { } to && to < p.EffectiveFrom) Fail("effectiveTo", FxAllocationError.OutOfRange);
        }
    }

    private static void ValidateCurrencies(IReadOnlyList<FxCurrencySnapshot?>? currencies, List<FxAllocationFieldError> errors)
    {
        if (currencies is null)
        {
            errors.Add(new("currencies", null, "currencies", FxAllocationError.Required));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < currencies.Count; i++)
        {
            var c = currencies[i];
            void Fail(string field, FxAllocationError error) => errors.Add(new("currencies", i, field, error));
            if (c is null) Fail("currency", FxAllocationError.Required);
            else if (CurrencyError(c.Code) is { } ce) Fail("code", ce);
            else if (!seen.Add(c.Code!)) Fail("code", FxAllocationError.Duplicate);
        }
    }

    private static void ValidateRates(IReadOnlyList<FxRateSnapshot?>? rates, List<FxAllocationFieldError> errors)
    {
        if (rates is null)
        {
            errors.Add(new("rates", null, "rates", FxAllocationError.Required));
            return;
        }

        var seen = new HashSet<long>();
        for (var i = 0; i < rates.Count; i++)
        {
            var r = rates[i];
            void Fail(string field, FxAllocationError error) => errors.Add(new("rates", i, field, error));
            if (r is null) Fail("rate", FxAllocationError.Required);
            else if (r.RateId <= 0) Fail("rateId", FxAllocationError.Required);
            else if (!seen.Add(r.RateId)) Fail("rateId", FxAllocationError.Duplicate);
        }
    }

    private static void ValidateCandidates(IReadOnlyList<FxValuationCandidate?>? candidates, List<FxAllocationFieldError> errors)
    {
        if (candidates is null)
        {
            errors.Add(new("candidates", null, "candidates", FxAllocationError.Required));
            return;
        }

        var seen = new HashSet<long>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            void Fail(string field, FxAllocationError error) => errors.Add(new("candidates", i, field, error));
            if (c is null)
            {
                Fail("candidate", FxAllocationError.Required);
                continue;
            }

            var e = c.Exposure;
            if (e is null)
            {
                Fail("exposure", FxAllocationError.Required);
                continue;
            }

            if (e.ExposureId <= 0) Fail("exposureId", FxAllocationError.Required);
            else if (!seen.Add(e.ExposureId)) Fail("exposureId", FxAllocationError.Duplicate);
            if (!Enum.IsDefined(e.Side)) Fail("side", FxAllocationError.OutOfRange);
            if (CurrencyError(e.DocumentCurrency) is { } de) Fail("documentCurrency", de);
            if (CurrencyError(e.BaseCurrency) is { } be) Fail("baseCurrency", be);
            if (c.CompanyId <= 0) Fail("companyId", FxAllocationError.Required);
            if (c.ControlAccountId <= 0) Fail("controlAccountId", FxAllocationError.Required);
        }
    }

    private static FxAllocationError? CurrencyError(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? FxAllocationError.Required
        : CurrencyCode().IsMatch(currency) ? null
        : FxAllocationError.InvalidFormat;

    private static FxAllocationError? CodeError(string? code) =>
        string.IsNullOrWhiteSpace(code) ? FxAllocationError.Required
        : PolicyCode().IsMatch(code) ? null
        : FxAllocationError.InvalidFormat;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();

    [GeneratedRegex("^[A-Z0-9_]{1,20}$")]
    private static partial Regex PolicyCode();
}
