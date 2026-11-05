namespace MyERP.Modules.Finance.Domain;

/// <summary>Saved fixed asset card lifecycle relevant to depreciation (BR-FI-096/100).</summary>
public enum FixedAssetCardState
{
    Draft,
    Active,
    Disposed
}

/// <summary>
/// One requested asset for a company/period depreciation batch. <c>PeriodAlreadyPosted</c> is the
/// caller's committed-source lookup for <c>(company, asset, period)</c>; this rule does not query it.
/// </summary>
public sealed record FixedAssetDepreciationCandidate(
    long AssetId, FixedAssetCardState State, FixedAssetDepreciationInput? Card, bool PeriodAlreadyPosted);

public enum FixedAssetDepreciationPlanStatus
{
    Ok,
    InvalidInput,
    InvalidPeriod,
    InvalidAssetId,
    DuplicateAsset
}

public enum FixedAssetBatchDecision
{
    Post,
    Skip,
    Fail
}

/// <summary>Specific per-asset cause shown for skipped and failed assets (BR-FI-102).</summary>
public enum FixedAssetBatchReason
{
    None,
    PeriodNotOpen,
    CardNotActive,
    CardDisposed,
    AlreadyPosted,
    BeforeInService,
    LifeExhausted,
    FullyDepreciated,
    ZeroAmount,
    InvalidInput,
    InvalidCurrencyDecimals,
    InvalidCost,
    InvalidResidual,
    InvalidUsefulLife,
    InvalidPeriod,
    /// <summary>The per-asset posting transaction rolled back; <c>FailureCode</c> carries the errorCode.</summary>
    PostingFailed
}

public sealed record FixedAssetDepreciationPlanItem(
    long AssetId, FixedAssetBatchDecision Decision, FixedAssetBatchReason Reason, decimal Amount = 0, int? PeriodNumber = null);

/// <summary>
/// Pre-posting decision for every requested asset, ordered by asset ID ascending (the BR-FI-098 lock
/// order). A rejected plan has no items and <see cref="ErrorIndex"/> names the offending request position.
/// </summary>
public sealed record FixedAssetDepreciationPlan(
    FixedAssetDepreciationPlanStatus Status,
    int Year,
    int Month,
    IReadOnlyList<FixedAssetDepreciationPlanItem> Items,
    int? ErrorIndex = null)
{
    public int PostCount => Items.Count(x => x.Decision == FixedAssetBatchDecision.Post);
    public decimal PostTotal => Items.Where(x => x.Decision == FixedAssetBatchDecision.Post).Sum(x => x.Amount);
}

/// <summary>Result of one per-asset posting transaction: committed, or rolled back with an errorCode.</summary>
public sealed record FixedAssetDepreciationAttempt(long AssetId, bool Committed, string? FailureCode = null);

public enum FixedAssetBatchSummaryStatus
{
    Ok,
    InvalidInput,
    InvalidPlan,
    InvalidAttempt,
    UnexpectedAttempt,
    DuplicateAttempt,
    MissingAttempt
}

public enum FixedAssetBatchItemResult
{
    Posted,
    Skipped,
    Failed
}

/// <summary>
/// Overall batch outcome. Only <see cref="Success"/> may be presented as success; every other value is a
/// warning (BR-FI-102: zero committed changes or partial outcome is never success).
/// </summary>
public enum FixedAssetBatchOutcome
{
    Success,
    Partial,
    NoChange,
    AllFailed,
    NoEligibleAssets
}

public sealed record FixedAssetBatchItem(
    long AssetId, FixedAssetBatchItemResult Result, FixedAssetBatchReason Reason, decimal Amount, string? FailureCode);

public sealed record FixedAssetBatchSummary(
    FixedAssetBatchSummaryStatus Status,
    FixedAssetBatchOutcome Outcome,
    IReadOnlyList<FixedAssetBatchItem> Items,
    int Requested,
    int Posted,
    int Skipped,
    int Failed,
    decimal PostedTotal,
    long? ErrorAssetId = null)
{
    public bool Changed => Posted > 0;

    internal static FixedAssetBatchSummary Rejected(FixedAssetBatchSummaryStatus status, long? assetId = null) =>
        new(status, FixedAssetBatchOutcome.NoChange, Array.Empty<FixedAssetBatchItem>(), 0, 0, 0, 0, 0m, assetId);
}

/// <summary>
/// Pure depreciation batch rules (WU-FI-012g): decide post/skip/fail per asset from saved card state,
/// committed-source lookup, period openness and <see cref="FixedAssetDepreciationRules"/>, then combine
/// per-asset transaction outcomes into exact requested/posted/skipped/failed counts. No SQL, lock,
/// source key or GL authority is implied; the caller supplies all facts and performs each transaction.
/// </summary>
public static class FixedAssetDepreciationBatchRules
{
    public static FixedAssetDepreciationPlan Plan(
        int year, int month, bool periodOpen, IReadOnlyList<FixedAssetDepreciationCandidate?>? candidates)
    {
        if (candidates is null)
            return Rejected(FixedAssetDepreciationPlanStatus.InvalidInput, year, month);
        if (year is < 1 or > 9999 || month is < 1 or > 12)
            return Rejected(FixedAssetDepreciationPlanStatus.InvalidPeriod, year, month);

        var seen = new HashSet<long>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate is null || !Enum.IsDefined(candidate.State))
                return Rejected(FixedAssetDepreciationPlanStatus.InvalidInput, year, month, i);
            if (candidate.AssetId <= 0)
                return Rejected(FixedAssetDepreciationPlanStatus.InvalidAssetId, year, month, i);
            if (!seen.Add(candidate.AssetId))
                return Rejected(FixedAssetDepreciationPlanStatus.DuplicateAsset, year, month, i);
        }

        var items = candidates
            .Select(x => Decide(x!, year, month, periodOpen))
            .OrderBy(x => x.AssetId)
            .ToArray();
        return new(FixedAssetDepreciationPlanStatus.Ok, year, month, items);
    }

    public static FixedAssetBatchSummary Summarize(
        FixedAssetDepreciationPlan? plan, IReadOnlyList<FixedAssetDepreciationAttempt?>? attempts)
    {
        if (plan is null || attempts is null)
            return FixedAssetBatchSummary.Rejected(FixedAssetBatchSummaryStatus.InvalidInput);
        if (plan.Status != FixedAssetDepreciationPlanStatus.Ok)
            return FixedAssetBatchSummary.Rejected(FixedAssetBatchSummaryStatus.InvalidPlan);

        var planned = plan.Items.ToDictionary(x => x.AssetId);
        var outcomes = new Dictionary<long, FixedAssetDepreciationAttempt>();
        foreach (var attempt in attempts)
        {
            if (attempt is null || !HasConsistentShape(attempt))
                return FixedAssetBatchSummary.Rejected(FixedAssetBatchSummaryStatus.InvalidAttempt, attempt?.AssetId);
            if (!planned.TryGetValue(attempt.AssetId, out var item) || item.Decision != FixedAssetBatchDecision.Post)
                return FixedAssetBatchSummary.Rejected(FixedAssetBatchSummaryStatus.UnexpectedAttempt, attempt.AssetId);
            if (!outcomes.TryAdd(attempt.AssetId, attempt))
                return FixedAssetBatchSummary.Rejected(FixedAssetBatchSummaryStatus.DuplicateAttempt, attempt.AssetId);
        }

        var missing = plan.Items.FirstOrDefault(x => x.Decision == FixedAssetBatchDecision.Post && !outcomes.ContainsKey(x.AssetId));
        if (missing is not null)
            return FixedAssetBatchSummary.Rejected(FixedAssetBatchSummaryStatus.MissingAttempt, missing.AssetId);

        var items = plan.Items.Select(x => ToResult(x, outcomes)).ToArray();
        var posted = items.Count(x => x.Result == FixedAssetBatchItemResult.Posted);
        var skipped = items.Count(x => x.Result == FixedAssetBatchItemResult.Skipped);
        var failed = items.Count(x => x.Result == FixedAssetBatchItemResult.Failed);
        var outcome = items.Length == 0 ? FixedAssetBatchOutcome.NoEligibleAssets
            : posted == items.Length ? FixedAssetBatchOutcome.Success
            : posted > 0 ? FixedAssetBatchOutcome.Partial
            : failed == items.Length ? FixedAssetBatchOutcome.AllFailed
            : FixedAssetBatchOutcome.NoChange;
        var total = items.Where(x => x.Result == FixedAssetBatchItemResult.Posted).Sum(x => x.Amount);
        return new(FixedAssetBatchSummaryStatus.Ok, outcome, items, items.Length, posted, skipped, failed, total);
    }

    private static FixedAssetDepreciationPlanItem Decide(
        FixedAssetDepreciationCandidate candidate, int year, int month, bool periodOpen)
    {
        var id = candidate.AssetId;
        if (!periodOpen)
            return Skip(id, FixedAssetBatchReason.PeriodNotOpen);
        switch (candidate.State)
        {
            case FixedAssetCardState.Draft:
                return Skip(id, FixedAssetBatchReason.CardNotActive);
            case FixedAssetCardState.Disposed:
                return Skip(id, FixedAssetBatchReason.CardDisposed);
        }
        if (candidate.PeriodAlreadyPosted)
            return Skip(id, FixedAssetBatchReason.AlreadyPosted);

        var month0 = FixedAssetDepreciationRules.CalculateMonth(candidate.Card, year, month);
        return month0.Status switch
        {
            FixedAssetDepreciationStatus.Allowed =>
                new(id, FixedAssetBatchDecision.Post, FixedAssetBatchReason.None, month0.Amount, month0.PeriodNumber),
            FixedAssetDepreciationStatus.BeforeInService => Skip(id, FixedAssetBatchReason.BeforeInService),
            FixedAssetDepreciationStatus.LifeExhausted => Skip(id, FixedAssetBatchReason.LifeExhausted),
            FixedAssetDepreciationStatus.FullyDepreciated => Skip(id, FixedAssetBatchReason.FullyDepreciated),
            FixedAssetDepreciationStatus.ZeroAmount => Skip(id, FixedAssetBatchReason.ZeroAmount),
            FixedAssetDepreciationStatus.InvalidCurrencyDecimals => Fail(id, FixedAssetBatchReason.InvalidCurrencyDecimals),
            FixedAssetDepreciationStatus.InvalidCost => Fail(id, FixedAssetBatchReason.InvalidCost),
            FixedAssetDepreciationStatus.InvalidResidual => Fail(id, FixedAssetBatchReason.InvalidResidual),
            FixedAssetDepreciationStatus.InvalidUsefulLife => Fail(id, FixedAssetBatchReason.InvalidUsefulLife),
            FixedAssetDepreciationStatus.InvalidPeriod => Fail(id, FixedAssetBatchReason.InvalidPeriod),
            _ => Fail(id, FixedAssetBatchReason.InvalidInput),
        };
    }

    private static FixedAssetBatchItem ToResult(
        FixedAssetDepreciationPlanItem item, Dictionary<long, FixedAssetDepreciationAttempt> outcomes) =>
        item.Decision switch
        {
            FixedAssetBatchDecision.Post when outcomes[item.AssetId].Committed =>
                new(item.AssetId, FixedAssetBatchItemResult.Posted, FixedAssetBatchReason.None, item.Amount, null),
            FixedAssetBatchDecision.Post =>
                new(item.AssetId, FixedAssetBatchItemResult.Failed, FixedAssetBatchReason.PostingFailed, 0m,
                    outcomes[item.AssetId].FailureCode),
            FixedAssetBatchDecision.Skip =>
                new(item.AssetId, FixedAssetBatchItemResult.Skipped, item.Reason, 0m, null),
            _ => new(item.AssetId, FixedAssetBatchItemResult.Failed, item.Reason, 0m, null),
        };

    // Committed attempts carry no error; rolled-back attempts must name a non-blank errorCode.
    private static bool HasConsistentShape(FixedAssetDepreciationAttempt attempt) =>
        attempt.Committed ? attempt.FailureCode is null : !string.IsNullOrWhiteSpace(attempt.FailureCode);

    private static FixedAssetDepreciationPlanItem Skip(long id, FixedAssetBatchReason reason) =>
        new(id, FixedAssetBatchDecision.Skip, reason);

    private static FixedAssetDepreciationPlanItem Fail(long id, FixedAssetBatchReason reason) =>
        new(id, FixedAssetBatchDecision.Fail, reason);

    private static FixedAssetDepreciationPlan Rejected(
        FixedAssetDepreciationPlanStatus status, int year, int month, int? index = null) =>
        new(status, year, month, Array.Empty<FixedAssetDepreciationPlanItem>(), index);
}
