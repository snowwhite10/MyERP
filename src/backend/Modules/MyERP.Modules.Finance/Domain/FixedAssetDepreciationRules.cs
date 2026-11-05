namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// Saved card values needed for straight-line monthly depreciation (BR-FI-095/096).
/// <c>CurrencyDecimals</c> is the company base-currency scale from the MD public contract (0–4, GL scale).
/// </summary>
public sealed record FixedAssetDepreciationInput(
    decimal Cost, decimal Residual, int UsefulLifeMonths, DateOnly InServiceDate, int CurrencyDecimals);

public enum FixedAssetDepreciationStatus
{
    Allowed,
    InvalidInput,
    InvalidCurrencyDecimals,
    InvalidCost,
    InvalidResidual,
    InvalidUsefulLife,
    InvalidPeriod,
    BeforeInService,
    LifeExhausted,
    FullyDepreciated,
    /// <summary>Rounded base is below the smallest currency unit; the month posts nothing and the last month catches up.</summary>
    ZeroAmount
}

/// <summary>
/// Scheduled (unposted) amount for one calendar month. <see cref="AccumulatedAfter"/> and
/// <see cref="RemainingAfter"/> describe the deterministic schedule, never committed FI postings.
/// </summary>
public sealed record FixedAssetDepreciationResult(
    FixedAssetDepreciationStatus Status,
    int? PeriodNumber = null,
    decimal Amount = 0,
    decimal AccumulatedAfter = 0,
    decimal RemainingAfter = 0);

/// <summary>
/// Pure straight-line schedule: full-month policy from the in-service calendar month, the first
/// <c>life-1</c> months use <c>(cost-residual)/life</c> rounded away from zero to currency decimals
/// (ADR-037), capped so the cumulative amount never exceeds cost less residual, and the last month
/// takes the exact remainder (ADR-078). No source key, period state, GL or SQL authority is implied.
/// </summary>
public static class FixedAssetDepreciationRules
{
    public const int MaxUsefulLifeMonths = 600;
    public const int MaxCurrencyDecimals = 4;
    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static FixedAssetDepreciationResult CalculateMonth(FixedAssetDepreciationInput? card, int year, int month)
    {
        if (card is null)
            return new(FixedAssetDepreciationStatus.InvalidInput);
        if (card.CurrencyDecimals is < 0 or > MaxCurrencyDecimals)
            return new(FixedAssetDepreciationStatus.InvalidCurrencyDecimals);
        if (card.Cost <= 0 || card.Cost > SqlAmountMaximum || !HasScale(card.Cost, card.CurrencyDecimals))
            return new(FixedAssetDepreciationStatus.InvalidCost);
        if (card.Residual < 0 || card.Residual >= card.Cost || !HasScale(card.Residual, card.CurrencyDecimals))
            return new(FixedAssetDepreciationStatus.InvalidResidual);
        if (card.UsefulLifeMonths is < 1 or > MaxUsefulLifeMonths)
            return new(FixedAssetDepreciationStatus.InvalidUsefulLife);
        if (year is < 1 or > 9999 || month is < 1 or > 12)
            return new(FixedAssetDepreciationStatus.InvalidPeriod);

        var index = (year * 12 + month) - (card.InServiceDate.Year * 12 + card.InServiceDate.Month);
        if (index < 0)
            return new(FixedAssetDepreciationStatus.BeforeInService);
        var life = card.UsefulLifeMonths;
        var depreciable = card.Cost - card.Residual;
        if (index >= life)
            return new(FixedAssetDepreciationStatus.LifeExhausted, AccumulatedAfter: depreciable);

        var periodBase = decimal.Round(depreciable / life, card.CurrencyDecimals, MidpointRounding.AwayFromZero);
        var accumulatedBefore = ScheduledThrough(index, periodBase, depreciable);
        var accumulatedAfter = index == life - 1 ? depreciable : ScheduledThrough(index + 1, periodBase, depreciable);
        var amount = accumulatedAfter - accumulatedBefore;
        var status = amount != 0
            ? FixedAssetDepreciationStatus.Allowed
            : accumulatedBefore == depreciable
                ? FixedAssetDepreciationStatus.FullyDepreciated
                : FixedAssetDepreciationStatus.ZeroAmount;
        return new(status, index + 1, amount, accumulatedAfter, depreciable - accumulatedAfter);
    }

    // months × base fits easily in decimal (≤ 600 × 1e15); checked keeps the arithmetic explicit.
    private static decimal ScheduledThrough(int months, decimal periodBase, decimal depreciable) =>
        Math.Min(checked(periodBase * months), depreciable);

    private static bool HasScale(decimal value, int decimals) => decimal.Round(value, decimals) == value;
}
