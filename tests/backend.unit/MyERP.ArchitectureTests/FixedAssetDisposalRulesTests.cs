using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FA-004／008／022（純規則部分）：處置前排程缺期判定、處置月補提、成本／累計折舊移除與處置損益（BR-FI-097／099、ADR-079）。
/// 不涉及 SQL、資產鎖、來源鍵、收款來源或 GL 過帳。
/// </summary>
public sealed class FixedAssetDisposalRulesTests
{
    // 1200 / 12 = 100 per month from 2026-01.
    private static readonly FixedAssetDepreciationInput Even = new(1200m, 0m, 12, new DateOnly(2026, 1, 10), 2);

    private static FixedAssetCommittedDepreciation C(int year, int month, decimal amount) => new(year, month, amount);

    private static FixedAssetCommittedDepreciation[] Through(int lastMonth, decimal amount = 100m) =>
        Enumerable.Range(1, lastMonth).Select(m => C(2026, m, amount)).ToArray();

    private static FixedAssetDisposalInput Input(
        DateOnly date, decimal proceeds, IReadOnlyList<FixedAssetCommittedDepreciation?>? committed,
        FixedAssetDepreciationInput? card = null, FixedAssetCardState state = FixedAssetCardState.Active, bool periodOpen = true) =>
        new(state, card ?? Even, date, proceeds, periodOpen, committed);

    [Fact]
    public void DisposalMonthCatchUpIsIncludedAndGainIsExact()
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 30), 1000m, Through(3)));

        Assert.Equal(FixedAssetDisposalStatus.Allowed, r.Status);
        Assert.Equal(100m, r.CatchUpAmount);
        Assert.Equal(4, r.CatchUpPeriodNumber);
        Assert.Equal(1200m, r.CostRemoved);
        Assert.Equal(400m, r.AccumulatedDepreciationRemoved);
        Assert.Equal(800m, r.NetBookValue);
        Assert.Equal(1000m, r.Proceeds);
        Assert.Equal(200m, r.Gain);
        Assert.Equal(0m, r.Loss);
        AssertBalanced(r);
    }

    [Fact]
    public void DisposalMonthAlreadyCommittedNeedsNoCatchUpAndZeroProceedsIsLoss()
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, Through(4)));

        Assert.Equal(FixedAssetDisposalStatus.Allowed, r.Status);
        Assert.Equal(0m, r.CatchUpAmount);
        Assert.Null(r.CatchUpPeriodNumber);
        Assert.Equal(400m, r.AccumulatedDepreciationRemoved);
        Assert.Equal(800m, r.NetBookValue);
        Assert.Equal(0m, r.Gain);
        Assert.Equal(800m, r.Loss);
        AssertBalanced(r);
    }

    [Fact]
    public void ProceedsEqualToBookValueHasNeitherGainNorLoss()
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 15), 800m, Through(3)));
        Assert.Equal(FixedAssetDisposalStatus.Allowed, r.Status);
        Assert.Equal(0m, r.Gain);
        Assert.Equal(0m, r.Loss);
        AssertBalanced(r);
    }

    [Fact]
    public void MissingEarlierPeriodBlocksAndNamesEarliestGap()
    {
        var committed = new[] { C(2026, 1, 100m), C(2026, 3, 100m) };
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 5, 1), 500m, committed));

        Assert.Equal(FixedAssetDisposalStatus.MissingDepreciation, r.Status);
        Assert.Equal(new DateOnly(2026, 2, 1), r.ErrorPeriod);
        AssertNoAmounts(r);
    }

    [Fact]
    public void DisposalInServiceMonthCatchesUpFirstPeriod()
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 1, 10), 1300m, []));
        Assert.Equal(FixedAssetDisposalStatus.Allowed, r.Status);
        Assert.Equal(100m, r.CatchUpAmount);
        Assert.Equal(1, r.CatchUpPeriodNumber);
        Assert.Equal(200m, r.Gain);
        AssertBalanced(r);
    }

    [Fact]
    public void AfterLifeEndNoCatchUpAndFullyDepreciatedResidualRemains()
    {
        var card = Even with { Residual = 120m, UsefulLifeMonths = 3 }; // 360/month, last month 360
        var committed = new[] { C(2026, 1, 360m), C(2026, 2, 360m), C(2026, 3, 360m) };
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2027, 6, 30), 50m, committed, card));

        Assert.Equal(FixedAssetDisposalStatus.Allowed, r.Status);
        Assert.Equal(0m, r.CatchUpAmount);
        Assert.Equal(1080m, r.AccumulatedDepreciationRemoved);
        Assert.Equal(120m, r.NetBookValue);
        Assert.Equal(70m, r.Loss);
        AssertBalanced(r);
    }

    [Fact]
    public void ZeroAmountMonthsAreNotRequiredAndLastMonthRemainderIsUsed()
    {
        // 0.05 / 12 rounds to 0.00 per month; only the last month posts 0.05.
        var card = new FixedAssetDepreciationInput(0.05m, 0m, 12, new DateOnly(2026, 1, 1), 2);
        var early = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 6, 1), 0m, [], card));
        Assert.Equal(FixedAssetDisposalStatus.Allowed, early.Status);
        Assert.Equal(0m, early.CatchUpAmount);
        Assert.Equal(0.05m, early.NetBookValue);
        Assert.Equal(0.05m, early.Loss);

        var last = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 12, 31), 0m, [], card));
        Assert.Equal(FixedAssetDisposalStatus.Allowed, last.Status);
        Assert.Equal(0.05m, last.CatchUpAmount);
        Assert.Equal(12, last.CatchUpPeriodNumber);
        Assert.Equal(0m, last.NetBookValue);
        AssertBalanced(last);
    }

    [Theory]
    [InlineData(FixedAssetCardState.Draft, FixedAssetDisposalStatus.CardNotActive)]
    [InlineData(FixedAssetCardState.Disposed, FixedAssetDisposalStatus.AlreadyDisposed)]
    [InlineData((FixedAssetCardState)99, FixedAssetDisposalStatus.InvalidInput)]
    public void OnlyActiveCardCanBeDisposed(FixedAssetCardState state, FixedAssetDisposalStatus expected)
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, Through(4), state: state));
        Assert.Equal(expected, r.Status);
        AssertNoAmounts(r);
    }

    [Fact]
    public void DisposalBeforeInServiceDateIsRejectedEvenInSameMonth()
    {
        var sameMonth = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 1, 9), 0m, []));
        Assert.Equal(FixedAssetDisposalStatus.BeforeInService, sameMonth.Status);
        var earlierYear = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2025, 12, 31), 0m, []));
        Assert.Equal(FixedAssetDisposalStatus.BeforeInService, earlierYear.Status);
        AssertNoAmounts(sameMonth);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("0.001")]
    [InlineData("1000000000000000")]
    public void InvalidProceedsAreRejected(string proceeds)
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), decimal.Parse(proceeds, System.Globalization.CultureInfo.InvariantCulture), Through(4)));
        Assert.Equal(FixedAssetDisposalStatus.InvalidProceeds, r.Status);
        AssertNoAmounts(r);
    }

    [Fact]
    public void MaximumProceedsAtCurrencyScaleIsAccepted()
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 999999999999999.99m, Through(4)));
        Assert.Equal(FixedAssetDisposalStatus.Allowed, r.Status);
        Assert.Equal(999999999999999.99m - 800m, r.Gain);
        AssertBalanced(r);
    }

    [Fact]
    public void ClosedDisposalPeriodBlocksBeforeLookingAtSchedule()
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, [], periodOpen: false));
        Assert.Equal(FixedAssetDisposalStatus.PeriodNotOpen, r.Status);
        AssertNoAmounts(r);
    }

    [Theory]
    [InlineData(0, FixedAssetDisposalStatus.InvalidCost)]
    [InlineData(1, FixedAssetDisposalStatus.InvalidResidual)]
    [InlineData(2, FixedAssetDisposalStatus.InvalidUsefulLife)]
    [InlineData(3, FixedAssetDisposalStatus.InvalidCurrencyDecimals)]
    public void InvalidCardValuesFailByField(int kind, FixedAssetDisposalStatus expected)
    {
        var card = kind switch
        {
            0 => Even with { Cost = 0m },
            1 => Even with { Residual = 1200m },
            2 => Even with { UsefulLifeMonths = 601 },
            _ => Even with { CurrencyDecimals = 5 }
        };
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, Through(4), card));
        Assert.Equal(expected, r.Status);
        AssertNoAmounts(r);
    }

    [Fact]
    public void NullInputsAreRejected()
    {
        Assert.Equal(FixedAssetDisposalStatus.InvalidInput, FixedAssetDisposalRules.Evaluate(null).Status);
        Assert.Equal(FixedAssetDisposalStatus.InvalidInput,
            FixedAssetDisposalRules.Evaluate(new FixedAssetDisposalInput(FixedAssetCardState.Active, null, new DateOnly(2026, 4, 1), 0m, true, [])).Status);
        Assert.Equal(FixedAssetDisposalStatus.InvalidInput,
            FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, null)).Status);
    }

    [Fact]
    public void NullOrMalformedCommittedEntryNamesIndex()
    {
        var withNull = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, [C(2026, 1, 100m), null]));
        Assert.Equal(FixedAssetDisposalStatus.InvalidCommittedDepreciation, withNull.Status);
        Assert.Equal(1, withNull.ErrorIndex);

        var badMonth = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, [C(2026, 13, 100m)]));
        Assert.Equal(FixedAssetDisposalStatus.InvalidCommittedDepreciation, badMonth.Status);
        Assert.Equal(0, badMonth.ErrorIndex);
        AssertNoAmounts(badMonth);
    }

    [Fact]
    public void DuplicateCommittedPeriodIsRejected()
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, [C(2026, 1, 100m), C(2026, 2, 100m), C(2026, 1, 100m)]));
        Assert.Equal(FixedAssetDisposalStatus.DuplicateCommittedPeriod, r.Status);
        Assert.Equal(2, r.ErrorIndex);
        Assert.Equal(new DateOnly(2026, 1, 1), r.ErrorPeriod);
        AssertNoAmounts(r);
    }

    [Fact]
    public void CommittedDepreciationAfterDisposalMonthIsRejected()
    {
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 3, 31), 0m, Through(4)));
        Assert.Equal(FixedAssetDisposalStatus.DepreciationAfterDisposal, r.Status);
        Assert.Equal(new DateOnly(2026, 4, 1), r.ErrorPeriod);
        AssertNoAmounts(r);
    }

    [Theory]
    [InlineData(2026, 2, 99.99)]   // amount differs from schedule
    [InlineData(2025, 12, 100)]    // before in-service month
    public void CommittedEntryThatDoesNotReconcileWithScheduleIsRejected(int year, int month, decimal amount)
    {
        var committed = new[] { C(2026, 1, 100m), C(year, month, amount), C(2026, 3, 100m) }.ToList();
        if (year == 2026) committed.Add(C(2026, 4, 100m)); else committed.Add(C(2026, 2, 100m));
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, committed));
        Assert.Equal(FixedAssetDisposalStatus.CommittedMismatch, r.Status);
        Assert.Equal(new DateOnly(year, month, 1), r.ErrorPeriod);
        AssertNoAmounts(r);
    }

    [Fact]
    public void CommittedEntryInZeroAmountMonthIsMismatch()
    {
        var card = new FixedAssetDepreciationInput(0.05m, 0m, 12, new DateOnly(2026, 1, 1), 2);
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 6, 1), 0m, [C(2026, 2, 0m)], card));
        Assert.Equal(FixedAssetDisposalStatus.CommittedMismatch, r.Status);
        Assert.Equal(new DateOnly(2026, 2, 1), r.ErrorPeriod);
    }

    [Fact]
    public void GapInsideLifeIsFoundWhenDisposingLongAfterLifeEnd()
    {
        var committed = Through(12).Where(x => x.Month != 12).ToArray();
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(9999, 12, 31), 0m, committed));
        Assert.Equal(FixedAssetDisposalStatus.MissingDepreciation, r.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), r.ErrorPeriod);

        var complete = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(9999, 12, 31), 0m, Through(12)));
        Assert.Equal(FixedAssetDisposalStatus.Allowed, complete.Status);
        Assert.Equal(0m, complete.NetBookValue);
        Assert.Equal(0m, complete.Loss);
    }

    [Fact]
    public void CommittedOrderDoesNotMatter()
    {
        var committed = Through(3).Reverse().ToArray();
        var r = FixedAssetDisposalRules.Evaluate(Input(new DateOnly(2026, 4, 1), 0m, committed));
        Assert.Equal(FixedAssetDisposalStatus.Allowed, r.Status);
        Assert.Equal(400m, r.AccumulatedDepreciationRemoved);
    }

    private static void AssertBalanced(FixedAssetDisposalResult r)
    {
        // Dr accumulated depreciation + proceeds + loss = Cr asset cost + gain.
        Assert.Equal(r.CostRemoved + r.Gain, r.AccumulatedDepreciationRemoved + r.Proceeds + r.Loss);
        Assert.True(r.Gain == 0m || r.Loss == 0m);
        Assert.Equal(r.CostRemoved - r.AccumulatedDepreciationRemoved, r.NetBookValue);
    }

    private static void AssertNoAmounts(FixedAssetDisposalResult r)
    {
        Assert.Equal(0m, r.CatchUpAmount);
        Assert.Null(r.CatchUpPeriodNumber);
        Assert.Equal(0m, r.CostRemoved);
        Assert.Equal(0m, r.AccumulatedDepreciationRemoved);
        Assert.Equal(0m, r.NetBookValue);
        Assert.Equal(0m, r.Proceeds);
        Assert.Equal(0m, r.Gain);
        Assert.Equal(0m, r.Loss);
    }
}
