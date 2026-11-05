namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// Offline valuation and journal composition of one eligible exposure: the <see cref="FxUnrealisedOverlayRules.Value"/>
/// calculation and, only when it is valid, the <see cref="FxJournalCompositionRules.ComposeValuation"/> result.
/// </summary>
public sealed record FxValuationComputation(long ExposureId, FxValuationCalculation? Valuation, FxJournalComposition? Journal);

/// <summary>Result of one per-exposure posting transaction: committed, or rolled back with an errorCode.</summary>
public sealed record FxValuationPostAttempt(long ExposureId, bool Committed, string? FailureCode = null);

public enum FxValuationBatchStatus
{
    Ok,
    InvalidInput,
    InvalidEligibility,
    InvalidComputation,
    UnexpectedComputation,
    DuplicateComputation,
    MissingComputation,
    InconsistentComputation,
    InvalidAttempt,
    UnexpectedAttempt,
    DuplicateAttempt,
    MissingAttempt
}

/// <summary>
/// Per-source result. <see cref="Previewed"/> is a non-zero delta in Preview mode (no accounting effect);
/// <see cref="Zero"/> is a valid valuation without a journal (<c>HasEntry = false</c>).
/// </summary>
public enum FxValuationItemResult
{
    Posted,
    Previewed,
    Zero,
    Excluded,
    Failed
}

/// <summary>
/// Overall run outcome. Only <see cref="Success"/> may be presented as success; every other value is a warning
/// (BR-FI-122: zero, all-skipped and mixed outcomes are never an unqualified success).
/// </summary>
public enum FxValuationBatchOutcome
{
    Success,
    Partial,
    NoChange,
    AllFailed,
    NoEligible
}

/// <summary>
/// One source in exposure ID order. Amounts are zero unless the result is Posted or Previewed;
/// <see cref="JournalAmount"/> is the balanced journal debit total (= |delta|).
/// </summary>
public sealed record FxValuationBatchItem(
    long ExposureId,
    FxValuationItemResult Result,
    IReadOnlyList<FxValuationExclusion> Reasons,
    decimal ValuationDelta,
    decimal UnrealisedDifference,
    decimal JournalAmount,
    string? FailureCode,
    IReadOnlyList<FxAllocationFieldError> Errors);

public sealed record FxValuationBatchSummary(
    FxValuationBatchStatus Status,
    FxValuationMode Mode,
    FxValuationBatchOutcome Outcome,
    IReadOnlyList<FxValuationBatchItem> Items,
    int Requested,
    int Posted,
    int Previewed,
    int Zero,
    int Excluded,
    int Failed,
    decimal PostedUnrealisedNet,
    decimal PreviewUnrealisedNet,
    long? ErrorExposureId = null)
{
    public bool Changed => Posted > 0;

    internal static FxValuationBatchSummary Rejected(FxValuationBatchStatus status, FxValuationMode mode, long? id = null) =>
        new(status, mode, FxValuationBatchOutcome.NoChange, Array.Empty<FxValuationBatchItem>(),
            0, 0, 0, 0, 0, 0, 0m, 0m, id);
}

/// <summary>
/// Pure FX valuation run summary (BR-FI-117／122, ADR-088, WU-FI-014i). Merges, by exposure ID ascending, the
/// eligibility result (<see cref="FxValuationEligibilityRules"/>), each eligible exposure's valuation and journal
/// composition and the per-exposure posting transaction outcomes into posted／previewed／zero／excluded／failed
/// items with exact counts. Preview never yields Posted and accepts no attempts. Duplicate, missing, unexpected or
/// inconsistent inputs reject the whole summary. Manifest, SQL, locks and GL posting stay with WU-FI-014b.
/// </summary>
public static class FxValuationBatchRules
{
    /// <summary>errorCode of a source whose valuation calculation was rejected (field errors in the item).</summary>
    public const string ValuationInvalidCode = "FI.FX.VALUATION_INVALID";

    /// <summary>errorCode of a source whose journal composition was rejected (field errors in the item).</summary>
    public const string JournalInvalidCode = "FI.FX.JOURNAL_INVALID";

    private static readonly IReadOnlyList<FxValuationExclusion> NoReasons = Array.Empty<FxValuationExclusion>();
    private static readonly IReadOnlyList<FxAllocationFieldError> NoErrors = Array.Empty<FxAllocationFieldError>();

    public static FxValuationBatchSummary Summarize(
        FxValuationEligibilityResult? eligibility,
        IReadOnlyList<FxValuationComputation?>? computations,
        IReadOnlyList<FxValuationPostAttempt?>? attempts)
    {
        var mode = eligibility?.Mode ?? FxValuationMode.Preview;
        if (eligibility is null || computations is null || attempts is null)
            return FxValuationBatchSummary.Rejected(FxValuationBatchStatus.InvalidInput, mode);

        FxValuationBatchSummary Reject(FxValuationBatchStatus status, long? id = null) =>
            FxValuationBatchSummary.Rejected(status, mode, id);

        // 1. Eligibility (014g) must be well formed and self-consistent.
        if (!Enum.IsDefined(mode) || eligibility.Exposures is null)
            return Reject(FxValuationBatchStatus.InvalidEligibility);
        var sources = new Dictionary<long, FxExposureEligibility>();
        foreach (var exposure in eligibility.Exposures)
        {
            if (exposure is null) return Reject(FxValuationBatchStatus.InvalidEligibility);
            if (exposure.ExposureId <= 0 || !HasConsistentShape(exposure) || !sources.TryAdd(exposure.ExposureId, exposure))
                return Reject(FxValuationBatchStatus.InvalidEligibility, exposure.ExposureId);
        }
        var eligibleCount = sources.Values.Count(x => x.IsEligible);
        if (eligibility.EligibleCount != eligibleCount || eligibility.ExcludedCount != sources.Count - eligibleCount
            || eligibility.PostingAllowed != (mode == FxValuationMode.Post && eligibleCount > 0))
            return Reject(FxValuationBatchStatus.InvalidEligibility);

        // 2. Exactly one consistent computation (014f + 014h) per eligible exposure.
        var computed = new Dictionary<long, FxValuationComputation>();
        foreach (var computation in computations)
        {
            if (computation is null) return Reject(FxValuationBatchStatus.InvalidComputation);
            var id = computation.ExposureId;
            if (!sources.TryGetValue(id, out var source) || !source.IsEligible)
                return Reject(FxValuationBatchStatus.UnexpectedComputation, id);
            if (!computed.TryAdd(id, computation)) return Reject(FxValuationBatchStatus.DuplicateComputation, id);
            if (CheckComputation(computation) is { } status) return Reject(status, id);
        }
        var missing = sources.Keys.Where(id => sources[id].IsEligible && !computed.ContainsKey(id)).Order().ToArray();
        if (missing.Length > 0) return Reject(FxValuationBatchStatus.MissingComputation, missing[0]);

        // 3. Post mode: exactly one transaction outcome per source with a journal; Preview accepts none.
        var outcomes = new Dictionary<long, FxValuationPostAttempt>();
        foreach (var attempt in attempts)
        {
            if (attempt is null) return Reject(FxValuationBatchStatus.InvalidAttempt);
            var id = attempt.ExposureId;
            if (!HasConsistentShape(attempt)) return Reject(FxValuationBatchStatus.InvalidAttempt, id);
            if (mode != FxValuationMode.Post || !computed.TryGetValue(id, out var c) || !HasEntry(c))
                return Reject(FxValuationBatchStatus.UnexpectedAttempt, id);
            if (!outcomes.TryAdd(id, attempt)) return Reject(FxValuationBatchStatus.DuplicateAttempt, id);
        }
        if (mode == FxValuationMode.Post)
        {
            var unattempted = computed.Values.Where(x => HasEntry(x) && !outcomes.ContainsKey(x.ExposureId))
                .Select(x => x.ExposureId).Order().ToArray();
            if (unattempted.Length > 0) return Reject(FxValuationBatchStatus.MissingAttempt, unattempted[0]);
        }

        var items = sources.Values
            .OrderBy(x => x.ExposureId)
            .Select(x => ToItem(x, mode, computed, outcomes))
            .ToArray();
        int Count(FxValuationItemResult r) => items.Count(x => x.Result == r);
        decimal Net(FxValuationItemResult r) => items.Where(x => x.Result == r).Sum(x => x.UnrealisedDifference);

        var posted = Count(FxValuationItemResult.Posted);
        var failed = Count(FxValuationItemResult.Failed);
        var outcome = eligibleCount == 0 ? FxValuationBatchOutcome.NoEligible
            : posted == items.Length ? FxValuationBatchOutcome.Success
            : posted > 0 ? FxValuationBatchOutcome.Partial
            : failed == items.Length ? FxValuationBatchOutcome.AllFailed
            : FxValuationBatchOutcome.NoChange;

        return new(FxValuationBatchStatus.Ok, mode, outcome, items, items.Length, posted,
            Count(FxValuationItemResult.Previewed), Count(FxValuationItemResult.Zero),
            Count(FxValuationItemResult.Excluded), failed,
            Net(FxValuationItemResult.Posted), Net(FxValuationItemResult.Previewed));
    }

    private static FxValuationBatchItem ToItem(
        FxExposureEligibility source,
        FxValuationMode mode,
        Dictionary<long, FxValuationComputation> computed,
        Dictionary<long, FxValuationPostAttempt> outcomes)
    {
        var id = source.ExposureId;
        if (!source.IsEligible)
            return new(id, FxValuationItemResult.Excluded, source.Reasons, 0m, 0m, 0m, null, NoErrors);

        var c = computed[id];
        if (!c.Valuation!.IsValid) return Failed(id, ValuationInvalidCode, c.Valuation.Errors);
        if (!c.Journal!.IsValid) return Failed(id, JournalInvalidCode, c.Journal.Errors);
        if (!c.Journal.HasEntry)
            return new(id, FxValuationItemResult.Zero, NoReasons, 0m, 0m, 0m, null, NoErrors);

        var v = c.Valuation.Result!;
        if (mode == FxValuationMode.Post && !outcomes[id].Committed)
            return Failed(id, outcomes[id].FailureCode, NoErrors);
        var result = mode == FxValuationMode.Post ? FxValuationItemResult.Posted : FxValuationItemResult.Previewed;
        return new(id, result, NoReasons, v.ValuationDelta, v.UnrealisedDifference, c.Journal.TotalDebit, null, NoErrors);
    }

    private static FxValuationBatchItem Failed(long id, string? code, IReadOnlyList<FxAllocationFieldError> errors) =>
        new(id, FxValuationItemResult.Failed, NoReasons, 0m, 0m, 0m, code, errors);

    private static bool HasEntry(FxValuationComputation c) => c.Journal is { HasEntry: true };

    /// <summary>
    /// A rejected valuation cannot be composed (no journal); a valid one needs a journal result, and a valid journal
    /// must have an entry exactly when the delta is non-zero, for |delta| (ADR-087).
    /// </summary>
    private static FxValuationBatchStatus? CheckComputation(FxValuationComputation c)
    {
        if (c.Valuation is null) return FxValuationBatchStatus.InvalidComputation;
        if (!c.Valuation.IsValid)
            return c.Journal is null ? null : FxValuationBatchStatus.InconsistentComputation;
        if (c.Journal is null) return FxValuationBatchStatus.InvalidComputation;
        if (!c.Journal.IsValid) return null;

        var delta = c.Valuation.Result!.ValuationDelta;
        return c.Journal.HasEntry == (delta != 0) && c.Journal.TotalDebit == Math.Abs(delta)
            ? null
            : FxValuationBatchStatus.InconsistentComputation;
    }

    // Eligible: closing rate and rate ID, no reasons. Excluded: at least one reason.
    private static bool HasConsistentShape(FxExposureEligibility e) =>
        e.Reasons is not null && (e.IsEligible
            ? e.ClosingRate is not null && e.RateId is not null && e.Reasons.Count == 0
            : e.Reasons.Count > 0);

    // Committed attempts carry no error; rolled-back attempts must name a non-blank errorCode.
    private static bool HasConsistentShape(FxValuationPostAttempt attempt) =>
        attempt.Committed ? attempt.FailureCode is null : !string.IsNullOrWhiteSpace(attempt.FailureCode);
}
