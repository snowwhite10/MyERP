using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FX-003／004／007／009／010／026（純規則部分）：單一外幣 AR／AP 敞口逐筆收付款分配的原幣解除、歷史本位幣
/// 按累計比例解除（末筆吸收尾差）、結算本位幣與已實現匯差方向（REQ_F4116.18.2／18.3、BR-FI-114～116、ADR-084）。
/// 不涉及 SQL、鎖、GL 過帳、未實現覆蓋釋放或畫面；已提交的累計解除額由呼叫端在交易內讀取後傳入。
/// </summary>
public sealed class FxRealisedDifferenceRulesTests
{
    private const decimal Max = 999999999999999.9999m;

    // USD 1,000.00 at 30.5 TWD → TWD 30,500 (TWD has 0 decimals).
    private static FxExposureSnapshot Usd(FxExposureSide side = FxExposureSide.Receivable, decimal original = 1000m,
        decimal rate = 30.5m, decimal? historicalBase = null, int docDecimals = 2, int baseDecimals = 0,
        string? doc = "USD", string? @base = "TWD", long id = 7) =>
        new(id, side, doc, @base, docDecimals, baseDecimals, original, rate,
            historicalBase ?? decimal.Round(original * rate, baseDecimals, MidpointRounding.AwayFromZero));

    private static FxAllocationRequest Alloc(long id, decimal original, decimal settlementBase, string? currency = "USD") =>
        new(id, currency, original, settlementBase);

    private static readonly FxDischargedToDate None = new(0m, 0m);

    private static FxRealisedCalculation Calc(FxExposureSnapshot? exposure, params FxAllocationRequest[] allocations) =>
        FxRealisedDifferenceRules.Calculate(exposure, None, allocations);

    private static void AssertOnly(FxRealisedCalculation r, string collection, int? index, string field,
        FxAllocationError error)
    {
        Assert.False(r.IsValid);
        Assert.Null(r.Results);
        var e = Assert.Single(r.Errors);
        Assert.Equal(collection, e.Collection);
        Assert.Equal(index, e.Index);
        Assert.Equal(field, e.Field);
        Assert.Equal(error, e.Error);
    }

    // ---- Normal: AR／AP direction ----

    [Fact]
    public void Receivable_partial_collection_above_historical_base_is_gain()
    {
        var r = Calc(Usd(), Alloc(1, 400m, 12400m));

        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
        var a = Assert.Single(r.Results!);
        Assert.Equal(1, a.AllocationId);
        Assert.Equal(400m, a.OriginalDischarged);
        Assert.Equal(12200m, a.HistoricalBaseDischarged);
        Assert.Equal(12400m, a.SettlementBase);
        Assert.Equal(200m, a.RealisedDifference);
        Assert.Equal(FxRealisedOutcome.Gain, a.Outcome);
        Assert.Equal(200m, a.GainAmount);
        Assert.Equal(0m, a.LossAmount);
        Assert.Equal(600m, a.RemainingOriginal);
        Assert.Equal(18300m, a.RemainingBase);
        Assert.False(a.SettlesExposure);
        Assert.Equal(400m, r.DischargedOriginalAfter);
        Assert.Equal(12200m, r.DischargedBaseAfter);
    }

    [Fact]
    public void Receivable_collection_below_historical_base_is_loss()
    {
        var a = Assert.Single(Calc(Usd(), Alloc(1, 400m, 12000m)).Results!);

        Assert.Equal(-200m, a.RealisedDifference);
        Assert.Equal(FxRealisedOutcome.Loss, a.Outcome);
        Assert.Equal(0m, a.GainAmount);
        Assert.Equal(200m, a.LossAmount);
    }

    [Theory]
    [InlineData(12400, -200, FxRealisedOutcome.Loss)]
    [InlineData(12000, 200, FxRealisedOutcome.Gain)]
    [InlineData(12200, 0, FxRealisedOutcome.None)]
    public void Payable_difference_is_historical_base_minus_base_paid(decimal paid, decimal expected,
        FxRealisedOutcome outcome)
    {
        var a = Assert.Single(Calc(Usd(FxExposureSide.Payable), Alloc(9, 400m, paid)).Results!);

        Assert.Equal(12200m, a.HistoricalBaseDischarged);
        Assert.Equal(expected, a.RealisedDifference);
        Assert.Equal(outcome, a.Outcome);
        Assert.Equal(Math.Max(expected, 0m), a.GainAmount);
        Assert.Equal(Math.Max(-expected, 0m), a.LossAmount);
    }

    [Fact]
    public void Equal_settlement_and_historical_base_has_no_difference()
    {
        var a = Assert.Single(Calc(Usd(), Alloc(1, 400m, 12200m)).Results!);

        Assert.Equal(0m, a.RealisedDifference);
        Assert.Equal(FxRealisedOutcome.None, a.Outcome);
        Assert.Equal(0m, a.GainAmount);
        Assert.Equal(0m, a.LossAmount);
    }

    [Fact]
    public void Empty_allocation_list_is_valid_with_no_results_and_unchanged_balance()
    {
        var r = FxRealisedDifferenceRules.Calculate(Usd(), new FxDischargedToDate(400m, 12200m), []);

        Assert.True(r.IsValid);
        Assert.Empty(r.Results!);
        Assert.Equal(400m, r.DischargedOriginalAfter);
        Assert.Equal(12200m, r.DischargedBaseAfter);
    }

    // ---- Rounding: cumulative proportional release, final takes remainder ----

    [Fact]
    public void Three_partials_release_cumulative_rounded_base_and_final_takes_remainder()
    {
        // USD 100.00 at 33.33333333 → TWD 3,333.
        var exposure = Usd(original: 100m, rate: 33.33333333m);
        Assert.Equal(3333m, exposure.HistoricalBaseGross);

        var r = Calc(exposure, Alloc(1, 33.33m, 1100m), Alloc(2, 33.33m, 1100m), Alloc(3, 33.34m, 1100m));

        Assert.True(r.IsValid);
        Assert.Equal([1111m, 1111m, 1111m], r.Results!.Select(a => a.HistoricalBaseDischarged));
        Assert.Equal([-11m, -11m, -11m], r.Results!.Select(a => a.RealisedDifference));
        Assert.Equal([false, false, true], r.Results!.Select(a => a.SettlesExposure));
        var last = r.Results![^1];
        Assert.Equal(0m, last.RemainingOriginal);
        Assert.Equal(0m, last.RemainingBase);
        Assert.Equal(3333m, r.Results!.Sum(a => a.HistoricalBaseDischarged));
    }

    [Fact]
    public void Midpoint_partials_never_release_negative_base_or_exceed_historical_base()
    {
        // USD 4.00 at 0.005 → 0.02 base (2 decimals). Naive per-allocation rounding of 0.005 away from zero would
        // release 0.01 four times (0.04 > 0.02) and leave the final allocation negative.
        var exposure = Usd(original: 4m, rate: 0.005m, baseDecimals: 2);
        Assert.Equal(0.02m, exposure.HistoricalBaseGross);

        var r = Calc(exposure, Alloc(1, 1m, 0.01m), Alloc(2, 1m, 0.01m), Alloc(3, 1m, 0.01m), Alloc(4, 1m, 0.01m));

        Assert.Equal([0.01m, 0m, 0.01m, 0m], r.Results!.Select(a => a.HistoricalBaseDischarged));
        Assert.All(r.Results!, a => Assert.True(a.HistoricalBaseDischarged >= 0m));
        Assert.Equal(0.02m, r.Results!.Sum(a => a.HistoricalBaseDischarged));
        Assert.Equal(0m, r.Results![^1].RemainingBase);
    }

    [Fact]
    public void One_thousand_partials_release_exact_historical_base_without_drift()
    {
        // USD 1,000.00 at 30.12345678 → TWD 30,123.
        var exposure = Usd(original: 1000m, rate: 30.12345678m);
        var allocations = Enumerable.Range(1, 1000).Select(i => Alloc(i, 1m, 30m)).ToArray();

        var r = Calc(exposure, allocations);

        Assert.True(r.IsValid);
        Assert.Equal(1000, r.Results!.Count);
        Assert.Equal(30123m, r.Results!.Sum(a => a.HistoricalBaseDischarged));
        Assert.All(r.Results!, a => Assert.InRange(a.HistoricalBaseDischarged, 30m, 31m));
        Assert.Equal(1000m, r.DischargedOriginalAfter);
        Assert.Equal(30123m, r.DischargedBaseAfter);
        Assert.True(r.Results![^1].SettlesExposure);
        Assert.Equal(30000m - 30123m, r.Results!.Sum(a => a.RealisedDifference));
    }

    [Fact]
    public void Prior_committed_discharge_continues_and_final_allocation_takes_remaining_base()
    {
        var r = FxRealisedDifferenceRules.Calculate(Usd(), new FxDischargedToDate(400m, 12200m),
            [Alloc(2, 600m, 18000m)]);

        var a = Assert.Single(r.Results!);
        Assert.Equal(18300m, a.HistoricalBaseDischarged);
        Assert.Equal(-300m, a.RealisedDifference);
        Assert.True(a.SettlesExposure);
        Assert.Equal(1000m, r.DischargedOriginalAfter);
        Assert.Equal(30500m, r.DischargedBaseAfter);
    }

    [Fact]
    public void Splitting_allocations_across_transactions_gives_the_same_results_as_one_batch()
    {
        var exposure = Usd(original: 100m, rate: 33.33333333m);
        FxAllocationRequest[] all = [Alloc(1, 12.34m, 400m), Alloc(2, 56.78m, 1900m), Alloc(3, 30.88m, 1000m)];

        var batch = Calc(exposure, all);
        var first = Calc(exposure, all[0]);
        var rest = FxRealisedDifferenceRules.Calculate(exposure,
            new FxDischargedToDate(first.DischargedOriginalAfter, first.DischargedBaseAfter), all[1..]);

        Assert.Equal(batch.Results!, first.Results!.Concat(rest.Results!));
        Assert.Equal(batch.DischargedBaseAfter, rest.DischargedBaseAfter);
        Assert.Equal(3333m, rest.DischargedBaseAfter);
    }

    [Fact]
    public void Partial_release_beyond_decimal_product_range_stays_exact()
    {
        // historical base × cumulative original exceeds decimal range; the ratio path must still round exactly.
        var exposure = Usd(original: Max, rate: 1m, docDecimals: 4, baseDecimals: 4);

        var r = Calc(exposure, Alloc(1, 500000000000000m, 1m), Alloc(2, Max - 500000000000000m, 1m));

        Assert.Equal([500000000000000m, Max - 500000000000000m], r.Results!.Select(a => a.HistoricalBaseDischarged));
        Assert.Equal(0m, r.Results![^1].RemainingBase);
    }

    [Fact]
    public void Prior_discharge_that_is_not_the_cumulative_rounded_base_is_inconsistent()
    {
        var r = FxRealisedDifferenceRules.Calculate(Usd(), new FxDischargedToDate(400m, 12000m),
            [Alloc(2, 100m, 3000m)]);

        AssertOnly(r, "discharged", null, "base", FxAllocationError.Inconsistent);
    }

    // ---- Over-allocation (atomic) ----

    [Fact]
    public void Allocation_exactly_to_remaining_original_is_accepted()
    {
        var r = Calc(Usd(), Alloc(1, 600m, 18300m), Alloc(2, 400m, 12200m));

        Assert.True(r.IsValid);
        Assert.True(r.Results![1].SettlesExposure);
    }

    [Fact]
    public void Over_allocation_rejects_the_whole_batch()
    {
        var r = Calc(Usd(), Alloc(1, 600m, 18300m), Alloc(2, 400.01m, 12200m));

        AssertOnly(r, "allocations", 1, "originalAmount", FxAllocationError.OverAllocated);
    }

    [Fact]
    public void Every_allocation_beyond_the_remaining_original_is_reported()
    {
        var r = Calc(Usd(), Alloc(1, 900m, 27000m), Alloc(2, 200m, 6000m), Alloc(3, 1m, 30m));

        Assert.False(r.IsValid);
        Assert.Equal([1, 2], r.Errors.Select(e => e.Index));
        Assert.All(r.Errors, e => Assert.Equal(FxAllocationError.OverAllocated, e.Error));
    }

    [Fact]
    public void Already_settled_exposure_rejects_any_further_allocation()
    {
        var r = FxRealisedDifferenceRules.Calculate(Usd(), new FxDischargedToDate(1000m, 30500m),
            [Alloc(5, 0.01m, 1m)]);

        AssertOnly(r, "allocations", 0, "originalAmount", FxAllocationError.OverAllocated);
    }

    [Fact]
    public void Prior_discharge_beyond_exposure_is_out_of_range()
    {
        var r = FxRealisedDifferenceRules.Calculate(Usd(), new FxDischargedToDate(1000.01m, 30500m), []);

        AssertOnly(r, "discharged", null, "original", FxAllocationError.OutOfRange);
    }

    [Fact]
    public void Prior_base_beyond_historical_base_is_out_of_range()
    {
        var r = FxRealisedDifferenceRules.Calculate(Usd(), new FxDischargedToDate(1000m, 30501m), []);

        AssertOnly(r, "discharged", null, "base", FxAllocationError.OutOfRange);
    }

    // ---- Currency ----

    [Fact]
    public void Allocation_in_another_currency_is_rejected()
    {
        AssertOnly(Calc(Usd(), Alloc(1, 400m, 12400m, "EUR")), "allocations", 0, "currency",
            FxAllocationError.CurrencyMismatch);
    }

    [Theory]
    [InlineData(null, FxAllocationError.Required)]
    [InlineData(" ", FxAllocationError.Required)]
    [InlineData("usd", FxAllocationError.InvalidFormat)]
    [InlineData("US", FxAllocationError.InvalidFormat)]
    public void Allocation_currency_must_be_three_upper_letters(string? currency, FxAllocationError error)
    {
        AssertOnly(Calc(Usd(), Alloc(1, 400m, 12400m, currency)), "allocations", 0, "currency", error);
    }

    [Fact]
    public void Base_currency_exposure_has_no_exchange_difference_and_is_rejected()
    {
        var r = Calc(Usd(doc: "TWD", rate: 1m), Alloc(1, 400m, 400m, "TWD"));

        AssertOnly(r, "exposure", null, "baseCurrency", FxAllocationError.SameCurrency);
    }

    // ---- Exposure snapshot integrity (BR-FI-114) ----

    [Fact]
    public void Historical_base_that_is_not_the_rounded_original_times_rate_is_a_mismatch()
    {
        AssertOnly(Calc(Usd(historicalBase: 30501m), Alloc(1, 400m, 12400m)), "exposure", null,
            "historicalBaseGross", FxAllocationError.BaseMismatch);
    }

    [Fact]
    public void Exposure_field_errors_are_reported_once_each_in_field_order()
    {
        var bad = new FxExposureSnapshot(0, (FxExposureSide)9, "usd", null, 5, -1, 0m, 0m, -1m);

        var r = FxRealisedDifferenceRules.Calculate(bad, None, []);

        Assert.False(r.IsValid);
        Assert.All(r.Errors, e => Assert.Equal("exposure", e.Collection));
        Assert.Equal(
            ["exposureId", "side", "documentCurrency", "baseCurrency", "documentDecimals", "baseDecimals",
                "originalGross", "transactionRate", "historicalBaseGross"],
            r.Errors.Select(e => e.Field));
        Assert.Equal(
            [FxAllocationError.Required, FxAllocationError.OutOfRange, FxAllocationError.InvalidFormat,
                FxAllocationError.Required, FxAllocationError.OutOfRange, FxAllocationError.OutOfRange,
                FxAllocationError.OutOfRange, FxAllocationError.OutOfRange, FxAllocationError.OutOfRange],
            r.Errors.Select(e => e.Error));
    }

    [Fact]
    public void Invalid_exposure_still_reports_allocation_item_errors_but_skips_dependent_checks()
    {
        var bad = Usd(id: 0);

        var r = FxRealisedDifferenceRules.Calculate(bad, None, [Alloc(0, 400m, 12400m), Alloc(2, 5000m, 1m)]);

        Assert.Equal(
            [("exposure", (int?)null, "exposureId"), ("allocations", 0, "allocationId")],
            r.Errors.Select(e => (e.Collection, e.Index, e.Field)));
    }

    // ---- Boundaries: scale and SQL decimal(19,4)／(19,8) ----

    [Fact]
    public void Amounts_beyond_currency_scale_are_rejected()
    {
        Assert.Equal(FxAllocationError.InvalidScale,
            Assert.Single(Calc(Usd(original: 1000.001m, historicalBase: 30500m)).Errors).Error);
        AssertOnly(Calc(Usd(), Alloc(1, 400.001m, 12400m)), "allocations", 0, "originalAmount",
            FxAllocationError.InvalidScale);
        AssertOnly(Calc(Usd(), Alloc(1, 400m, 12400.5m)), "allocations", 0, "settlementBaseAmount",
            FxAllocationError.InvalidScale);
        AssertOnly(Calc(Usd(rate: 30.123456789m, historicalBase: 30123m)), "exposure", null, "transactionRate",
            FxAllocationError.InvalidScale);
        AssertOnly(Calc(Usd(historicalBase: 30500.5m)), "exposure", null, "historicalBaseGross",
            FxAllocationError.InvalidScale);
        AssertOnly(FxRealisedDifferenceRules.Calculate(Usd(), new FxDischargedToDate(400.001m, 12200m), []),
            "discharged", null, "original", FxAllocationError.InvalidScale);
    }

    [Fact]
    public void Sql_maximum_amounts_are_accepted_and_one_step_above_is_rejected()
    {
        var max = Usd(original: Max, rate: 1m, docDecimals: 4, baseDecimals: 4);
        var a = Assert.Single(Calc(max, Alloc(1, Max, Max)).Results!);
        Assert.Equal(Max, a.HistoricalBaseDischarged);
        Assert.Equal(0m, a.RealisedDifference);
        Assert.True(a.SettlesExposure);

        // Smallest historical base against the largest settlement still fits: both operands are positive.
        var tiny = Usd(original: Max, rate: 0.00000001m, docDecimals: 4, baseDecimals: 4);
        var gain = Assert.Single(Calc(tiny, Alloc(1, Max, Max)).Results!);
        Assert.Equal(Max - tiny.HistoricalBaseGross, gain.RealisedDifference);

        AssertOnly(Calc(Usd(original: Max + 0.0001m, rate: 1m, historicalBase: 1m, docDecimals: 4)), "exposure",
            null, "originalGross", FxAllocationError.OutOfRange);
        AssertOnly(Calc(Usd(), Alloc(1, Max + 0.0001m, 1m)), "allocations", 0, "originalAmount",
            FxAllocationError.OutOfRange);
        AssertOnly(Calc(Usd(), Alloc(1, 1m, Max + 0.0001m)), "allocations", 0, "settlementBaseAmount",
            FxAllocationError.OutOfRange);
    }

    [Fact]
    public void Sql_maximum_rate_is_accepted_and_one_step_above_is_rejected()
    {
        const decimal maxRate = 99999999999.99999999m;
        var ok = Usd(original: 0.0001m, rate: maxRate, docDecimals: 4, baseDecimals: 4);
        Assert.Equal(10000000m, ok.HistoricalBaseGross);
        Assert.True(Calc(ok, Alloc(1, 0.0001m, 10000000m)).IsValid);

        AssertOnly(Calc(Usd(original: 0.0001m, rate: maxRate + 0.00000001m, historicalBase: 10000000m,
                docDecimals: 4, baseDecimals: 4)),
            "exposure", null, "transactionRate", FxAllocationError.OutOfRange);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Zero_or_negative_allocation_amounts_are_out_of_range(decimal value)
    {
        AssertOnly(Calc(Usd(), Alloc(1, value, 12400m)), "allocations", 0, "originalAmount",
            FxAllocationError.OutOfRange);
        AssertOnly(Calc(Usd(), Alloc(1, 400m, value)), "allocations", 0, "settlementBaseAmount",
            FxAllocationError.OutOfRange);
    }

    // ---- Error handling ----

    [Fact]
    public void Null_inputs_are_required()
    {
        var r = FxRealisedDifferenceRules.Calculate(null, null, null);

        Assert.Equal(
            [("exposure", "exposure"), ("discharged", "discharged"), ("allocations", "allocations")],
            r.Errors.Select(e => (e.Collection, e.Field)));
        Assert.All(r.Errors, e => Assert.Equal(FxAllocationError.Required, e.Error));
    }

    [Fact]
    public void Null_allocation_and_duplicate_id_are_reported_by_position()
    {
        var r = FxRealisedDifferenceRules.Calculate(Usd(), None,
            [Alloc(1, 100m, 3000m), null!, Alloc(1, 100m, 3000m)]);

        Assert.Equal(
            [(1, "allocation", FxAllocationError.Required), (2, "allocationId", FxAllocationError.Duplicate)],
            r.Errors.Select(e => (e.Index!.Value, e.Field, e.Error)));
    }

    [Fact]
    public void Allocation_field_errors_are_reported_once_each_in_field_order()
    {
        var r = Calc(Usd(), new FxAllocationRequest(-1, "EUR", 0.001m, -5m));

        Assert.Equal(
            [("allocationId", FxAllocationError.Required), ("currency", FxAllocationError.CurrencyMismatch),
                ("originalAmount", FxAllocationError.InvalidScale),
                ("settlementBaseAmount", FxAllocationError.OutOfRange)],
            r.Errors.Select(e => (e.Field, e.Error)));
    }
}
