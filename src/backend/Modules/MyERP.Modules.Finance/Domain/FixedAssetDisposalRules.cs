namespace MyERP.Modules.Finance.Domain;

/// <summary>One committed (Posted) depreciation source for the asset, as read by the caller under the asset lock.</summary>
public sealed record FixedAssetCommittedDepreciation(int Year, int Month, decimal Amount);

/// <summary>
/// Saved disposal and card values for the offline disposal check (BR-FI-097/099/100). <c>DisposalPeriodOpen</c> is
/// the caller's lookup of the single Open period containing <c>DisposalDate</c>; <c>Committed</c> lists every
/// committed depreciation source for this asset. This rule queries nothing.
/// </summary>
public sealed record FixedAssetDisposalInput(
    FixedAssetCardState State,
    FixedAssetDepreciationInput? Card,
    DateOnly DisposalDate,
    decimal Proceeds,
    bool DisposalPeriodOpen,
    IReadOnlyList<FixedAssetCommittedDepreciation?>? Committed);

public enum FixedAssetDisposalStatus
{
    Allowed,
    InvalidInput,
    CardNotActive,
    AlreadyDisposed,
    InvalidCurrencyDecimals,
    InvalidCost,
    InvalidResidual,
    InvalidUsefulLife,
    BeforeInService,
    InvalidProceeds,
    PeriodNotOpen,
    InvalidCommittedDepreciation,
    DuplicateCommittedPeriod,
    DepreciationAfterDisposal,
    /// <summary>A committed amount or period does not reconcile with the deterministic schedule (BR-FI-097).</summary>
    CommittedMismatch,
    /// <summary>A scheduled month before the disposal month has no committed depreciation (BR-FI-099).</summary>
    MissingDepreciation
}

/// <summary>
/// Disposal accounting effect. Journal: Dr accumulated depreciation, Dr proceeds clearing, Dr loss; Cr asset cost,
/// Cr gain. <see cref="CatchUpAmount"/> is the disposal-month depreciation to post in the same approval transaction
/// (ADR-079). Every amount is zero unless <see cref="Status"/> is <c>Allowed</c>.
/// </summary>
public sealed record FixedAssetDisposalResult(
    FixedAssetDisposalStatus Status,
    decimal CatchUpAmount = 0,
    int? CatchUpPeriodNumber = null,
    decimal CostRemoved = 0,
    decimal AccumulatedDepreciationRemoved = 0,
    decimal NetBookValue = 0,
    decimal Proceeds = 0,
    decimal Gain = 0,
    decimal Loss = 0,
    int? ErrorIndex = null,
    DateOnly? ErrorPeriod = null);

/// <summary>
/// Pure disposal check (ADR-079): only an Active card in an Open disposal period; every committed depreciation must
/// reconcile with the straight-line schedule and lie on or before the disposal month; any scheduled month before the
/// disposal month without a committed source blocks disposal; the disposal month itself is caught up once. Gain or
/// loss is proceeds minus net book value. No SQL, lock, source key, cash receipt or GL authority is implied.
/// </summary>
public static class FixedAssetDisposalRules
{
    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static FixedAssetDisposalResult Evaluate(FixedAssetDisposalInput? input)
    {
        if (input?.Card is null || input.Committed is null || !Enum.IsDefined(input.State))
            return new(FixedAssetDisposalStatus.InvalidInput);
        if (input.State == FixedAssetCardState.Draft)
            return new(FixedAssetDisposalStatus.CardNotActive);
        if (input.State == FixedAssetCardState.Disposed)
            return new(FixedAssetDisposalStatus.AlreadyDisposed);

        var card = input.Card;
        var cardCheck = FixedAssetDepreciationRules.CalculateMonth(card, card.InServiceDate.Year, card.InServiceDate.Month).Status;
        var cardFailure = cardCheck switch
        {
            FixedAssetDepreciationStatus.InvalidCurrencyDecimals => FixedAssetDisposalStatus.InvalidCurrencyDecimals,
            FixedAssetDepreciationStatus.InvalidCost => FixedAssetDisposalStatus.InvalidCost,
            FixedAssetDepreciationStatus.InvalidResidual => FixedAssetDisposalStatus.InvalidResidual,
            FixedAssetDepreciationStatus.InvalidUsefulLife => FixedAssetDisposalStatus.InvalidUsefulLife,
            FixedAssetDepreciationStatus.InvalidInput or FixedAssetDepreciationStatus.InvalidPeriod => FixedAssetDisposalStatus.InvalidInput,
            _ => (FixedAssetDisposalStatus?)null
        };
        if (cardFailure is { } failure)
            return new(failure);
        if (input.DisposalDate < card.InServiceDate)
            return new(FixedAssetDisposalStatus.BeforeInService);
        if (input.Proceeds < 0 || input.Proceeds > SqlAmountMaximum
            || decimal.Round(input.Proceeds, card.CurrencyDecimals) != input.Proceeds)
            return new(FixedAssetDisposalStatus.InvalidProceeds);
        if (!input.DisposalPeriodOpen)
            return new(FixedAssetDisposalStatus.PeriodNotOpen);

        var disposalKey = MonthKey(input.DisposalDate.Year, input.DisposalDate.Month);
        var committedKeys = new HashSet<int>();
        var accumulated = 0m;
        for (var i = 0; i < input.Committed.Count; i++)
        {
            var entry = input.Committed[i];
            if (entry is null || entry.Year is < 1 or > 9999 || entry.Month is < 1 or > 12)
                return new(FixedAssetDisposalStatus.InvalidCommittedDepreciation, ErrorIndex: i);
            var period = new DateOnly(entry.Year, entry.Month, 1);
            var key = MonthKey(entry.Year, entry.Month);
            if (!committedKeys.Add(key))
                return new(FixedAssetDisposalStatus.DuplicateCommittedPeriod, ErrorIndex: i, ErrorPeriod: period);
            if (key > disposalKey)
                return new(FixedAssetDisposalStatus.DepreciationAfterDisposal, ErrorIndex: i, ErrorPeriod: period);
            var scheduled = FixedAssetDepreciationRules.CalculateMonth(card, entry.Year, entry.Month);
            if (scheduled.Status != FixedAssetDepreciationStatus.Allowed || scheduled.Amount != entry.Amount)
                return new(FixedAssetDisposalStatus.CommittedMismatch, ErrorIndex: i, ErrorPeriod: period);
            accumulated += entry.Amount;
        }

        // Months before the disposal month must all be committed; scheduled months stop at the end of useful life.
        var startKey = MonthKey(card.InServiceDate.Year, card.InServiceDate.Month);
        var lastScheduledKey = startKey + card.UsefulLifeMonths - 1;
        for (var key = startKey; key < disposalKey && key <= lastScheduledKey; key++)
        {
            var (year, month) = FromKey(key);
            if (!committedKeys.Contains(key)
                && FixedAssetDepreciationRules.CalculateMonth(card, year, month).Status == FixedAssetDepreciationStatus.Allowed)
                return new(FixedAssetDisposalStatus.MissingDepreciation, ErrorPeriod: new DateOnly(year, month, 1));
        }

        var catchUp = 0m;
        int? catchUpPeriod = null;
        if (!committedKeys.Contains(disposalKey))
        {
            var disposalMonth = FixedAssetDepreciationRules.CalculateMonth(card, input.DisposalDate.Year, input.DisposalDate.Month);
            if (disposalMonth.Status == FixedAssetDepreciationStatus.Allowed)
            {
                catchUp = disposalMonth.Amount;
                catchUpPeriod = disposalMonth.PeriodNumber;
            }
        }

        var accumulatedRemoved = accumulated + catchUp;
        var netBookValue = card.Cost - accumulatedRemoved;
        var difference = input.Proceeds - netBookValue;
        return new(
            FixedAssetDisposalStatus.Allowed,
            catchUp,
            catchUpPeriod,
            card.Cost,
            accumulatedRemoved,
            netBookValue,
            input.Proceeds,
            Gain: difference > 0 ? difference : 0m,
            Loss: difference < 0 ? -difference : 0m);
    }

    private static int MonthKey(int year, int month) => year * 12 + (month - 1);

    private static (int Year, int Month) FromKey(int key) => (key / 12, key % 12 + 1);
}
