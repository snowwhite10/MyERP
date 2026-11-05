using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FX-VAL（純規則部分）：期末估值以保存期末匯率對未結原幣計目標本位，目標減目前帳面（未解除歷史本位＋仍附著的
/// 未實現覆蓋）得估值差額；結算按累計比例釋放既有覆蓋，全額結清覆蓋歸零，已實現匯差仍以歷史本位計算
/// （REQ_F4116.18.4／18.5、BR-FI-117／118、ADR-085）。不涉及 cutoff、manifest、政策、SQL、鎖、GL 或畫面。
/// </summary>
public sealed class FxUnrealisedOverlayRulesTests
{
    private const decimal Max = 999999999999999.9999m;

    // USD 1,000.00 at 30.5 TWD → TWD 30,500 (TWD has 0 decimals).
    private static FxExposureSnapshot Usd(FxExposureSide side = FxExposureSide.Receivable, decimal original = 1000m,
        decimal rate = 30.5m, int docDecimals = 2, int baseDecimals = 0, string? doc = "USD", string? @base = "TWD") =>
        new(7, side, doc, @base, docDecimals, baseDecimals, original, rate,
            decimal.Round(original * rate, baseDecimals, MidpointRounding.AwayFromZero));

    private static FxClosingRate Closing(decimal rate, string? from = "USD", string? to = "TWD") => new(from, to, rate);

    private static FxAllocationRequest Alloc(long id, decimal original, decimal settlementBase, string? currency = "USD") =>
        new(id, currency, original, settlementBase);

    private static readonly FxDischargedToDate NoDischarge = new(0m, 0m);

    private static FxValuationResult Valid(FxValuationCalculation c)
    {
        Assert.Empty(c.Errors);
        Assert.True(c.IsValid);
        return c.Result!;
    }

    private static void AssertOnly(IReadOnlyList<FxAllocationFieldError> errors, bool isValid, string collection,
        int? index, string field, FxAllocationError error)
    {
        Assert.False(isValid);
        var e = Assert.Single(errors);
        Assert.Equal(collection, e.Collection);
        Assert.Equal(index, e.Index);
        Assert.Equal(field, e.Field);
        Assert.Equal(error, e.Error);
    }

    // ---- Normal: first valuation ----

    [Fact]
    public void Receivable_first_valuation_at_higher_rate_posts_gain_delta()
    {
        var r = Valid(FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, FxOverlayState.None, Closing(31m)));

        Assert.Equal(1000m, r.OpenOriginal);
        Assert.Equal(30500m, r.OpenHistoricalBase);
        Assert.Equal(0m, r.CurrentOverlay);
        Assert.Equal(30500m, r.CarryingBase);
        Assert.Equal(31000m, r.TargetBase);
        Assert.Equal(500m, r.ValuationDelta);
        Assert.Equal(500m, r.UnrealisedDifference);
        Assert.Equal(FxRealisedOutcome.Gain, r.Outcome);
        Assert.Equal(500m, r.GainAmount);
        Assert.Equal(0m, r.LossAmount);
        Assert.True(r.HasOpenExposure);
        Assert.Equal(new FxOverlayState(1000m, 500m, 0m), r.OverlayAfter);
    }

    [Theory]
    [InlineData(31, 500, -500, FxRealisedOutcome.Loss)]
    [InlineData(30, -500, 500, FxRealisedOutcome.Gain)]
    [InlineData(30.5, 0, 0, FxRealisedOutcome.None)]
    public void Payable_valuation_sign_is_reversed(decimal rate, decimal delta, decimal difference,
        FxRealisedOutcome outcome)
    {
        var r = Valid(FxUnrealisedOverlayRules.Value(Usd(FxExposureSide.Payable), NoDischarge, FxOverlayState.None,
            Closing(rate)));

        Assert.Equal(delta, r.ValuationDelta);
        Assert.Equal(difference, r.UnrealisedDifference);
        Assert.Equal(outcome, r.Outcome);
        Assert.Equal(difference > 0 ? difference : 0m, r.GainAmount);
        Assert.Equal(difference < 0 ? -difference : 0m, r.LossAmount);
        Assert.Equal(new FxOverlayState(1000m, delta, 0m), r.OverlayAfter);
    }

    [Fact]
    public void Zero_delta_is_none_and_keeps_existing_overlay_as_anchor()
    {
        var r = Valid(FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, new(1000m, 500m, 0m), Closing(31m)));

        Assert.Equal(31000m, r.CarryingBase);
        Assert.Equal(0m, r.ValuationDelta);
        Assert.Equal(FxRealisedOutcome.None, r.Outcome);
        Assert.Equal(0m, r.GainAmount);
        Assert.Equal(0m, r.LossAmount);
        Assert.Equal(new FxOverlayState(1000m, 500m, 0m), r.OverlayAfter);
    }

    // ---- Normal: revaluation only posts the delta from the current carrying amount ----

    [Fact]
    public void Second_valuation_posts_only_delta_from_prior_overlay()
    {
        var r = Valid(FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, new(1000m, 500m, 0m), Closing(30.8m)));

        Assert.Equal(500m, r.CurrentOverlay);
        Assert.Equal(31000m, r.CarryingBase);
        Assert.Equal(30800m, r.TargetBase);
        Assert.Equal(-200m, r.ValuationDelta);
        Assert.Equal(FxRealisedOutcome.Loss, r.Outcome);
        Assert.Equal(200m, r.LossAmount);
        Assert.Equal(new FxOverlayState(1000m, 300m, 0m), r.OverlayAfter);
    }

    [Fact]
    public void Valuation_after_partial_settlement_values_only_remaining_open_exposure()
    {
        // 400 of 1,000 settled after a 500 overlay: 200 released, 300 still attached to the open 600.
        var r = Valid(FxUnrealisedOverlayRules.Value(Usd(), new(400m, 12200m), new(1000m, 500m, 200m),
            Closing(31.2m)));

        Assert.Equal(600m, r.OpenOriginal);
        Assert.Equal(18300m, r.OpenHistoricalBase);
        Assert.Equal(300m, r.CurrentOverlay);
        Assert.Equal(18600m, r.CarryingBase);
        Assert.Equal(18720m, r.TargetBase);
        Assert.Equal(120m, r.ValuationDelta);
        Assert.Equal(new FxOverlayState(600m, 420m, 0m), r.OverlayAfter);
    }

    [Fact]
    public void Fully_settled_exposure_has_nothing_to_value()
    {
        var r = Valid(FxUnrealisedOverlayRules.Value(Usd(), new(1000m, 30500m), new(1000m, 500m, 500m),
            Closing(32m)));

        Assert.False(r.HasOpenExposure);
        Assert.Equal(0m, r.OpenOriginal);
        Assert.Equal(0m, r.OpenHistoricalBase);
        Assert.Equal(0m, r.CurrentOverlay);
        Assert.Equal(0m, r.TargetBase);
        Assert.Equal(0m, r.ValuationDelta);
        Assert.Equal(FxRealisedOutcome.None, r.Outcome);
        Assert.Equal(FxOverlayState.None, r.OverlayAfter);
    }

    [Fact]
    public void Target_rounds_once_away_from_zero_at_base_precision()
    {
        // USD 0.01 × 30.5 = 0.305 → 0 TWD; × 50 = 0.5 → 1 TWD (away from zero).
        var e = Usd(original: 0.01m, rate: 50m);
        Assert.Equal(1m, e.HistoricalBaseGross);

        var low = Valid(FxUnrealisedOverlayRules.Value(e, NoDischarge, FxOverlayState.None, Closing(30.5m)));
        Assert.Equal(0m, low.TargetBase);
        Assert.Equal(-1m, low.ValuationDelta);

        var mid = Valid(FxUnrealisedOverlayRules.Value(e, NoDischarge, FxOverlayState.None, Closing(150m)));
        Assert.Equal(2m, mid.TargetBase);
    }

    // ---- Settlement release ----

    [Fact]
    public void Settlement_releases_proportional_overlay_and_realised_stays_against_historical_base()
    {
        var c = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, new(1000m, 500m, 0m),
            [Alloc(1, 400m, 12400m), Alloc(2, 600m, 18700m)]);

        Assert.True(c.IsValid);
        Assert.Empty(c.Errors);
        Assert.Collection(c.Results!,
            a =>
            {
                Assert.Equal(12200m, a.Realised.HistoricalBaseDischarged);
                Assert.Equal(200m, a.Realised.RealisedDifference);
                Assert.Equal(200m, a.OverlayReleased);
                Assert.Equal(300m, a.RemainingOverlay);
            },
            a =>
            {
                Assert.Equal(18300m, a.Realised.HistoricalBaseDischarged);
                Assert.Equal(400m, a.Realised.RealisedDifference);
                Assert.Equal(300m, a.OverlayReleased);
                Assert.Equal(0m, a.RemainingOverlay);
                Assert.True(a.Realised.SettlesExposure);
            });
        Assert.Equal(new FxOverlayState(1000m, 500m, 500m), c.OverlayAfter);
    }

    [Fact]
    public void Realised_results_equal_the_realised_rule_so_overlay_is_not_counted_twice()
    {
        var allocations = new[] { Alloc(1, 333.33m, 10100m), Alloc(2, 0.01m, 1m) };
        var plain = FxRealisedDifferenceRules.Calculate(Usd(), NoDischarge, allocations);
        var withOverlay = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, new(1000m, -700m, 0m), allocations);

        Assert.Equal(plain.Results!, withOverlay.Results!.Select(x => x.Realised));
    }

    [Fact]
    public void Release_without_overlay_releases_zero()
    {
        var c = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, FxOverlayState.None, [Alloc(1, 1000m, 31000m)]);

        var a = Assert.Single(c.Results!);
        Assert.Equal(0m, a.OverlayReleased);
        Assert.Equal(0m, a.RemainingOverlay);
        Assert.Equal(FxOverlayState.None, c.OverlayAfter);
    }

    [Fact]
    public void Thirds_release_cumulatively_and_total_exactly()
    {
        var c = FxUnrealisedOverlayRules.Release(Usd(original: 3m, rate: 30m), NoDischarge, new(3m, 100m, 0m),
            [Alloc(1, 1m, 30m), Alloc(2, 1m, 30m), Alloc(3, 1m, 30m)]);

        Assert.Equal([33m, 34m, 33m], c.Results!.Select(x => x.OverlayReleased));
        Assert.Equal([67m, 33m, 0m], c.Results!.Select(x => x.RemainingOverlay));
        Assert.Equal(100m, c.OverlayAfter!.ReleasedSinceAnchor);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(-1, 0, -1)]
    public void Midpoint_release_never_changes_sign(decimal overlay, decimal first, decimal second)
    {
        var c = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, new(1000m, overlay, 0m),
            [Alloc(1, 500m, 15250m), Alloc(2, 500m, 15250m)]);

        Assert.Equal([first, second], c.Results!.Select(x => x.OverlayReleased));
    }

    [Fact]
    public void Many_partial_releases_total_the_overlay_without_drift()
    {
        var allocations = Enumerable.Range(1, 1000).Select(i => Alloc(i, 1m, 31m)).ToArray();
        var c = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, new(1000m, 777m, 0m), allocations);

        Assert.All(c.Results!, x => Assert.InRange(x.OverlayReleased, 0m, 1m));
        Assert.Equal(777m, c.Results!.Sum(x => x.OverlayReleased));
        Assert.Equal(0m, c.Results![^1].RemainingOverlay);
    }

    [Fact]
    public void Split_release_across_transactions_equals_one_batch()
    {
        var anchor = new FxOverlayState(1000m, 500m, 0m);
        var batch = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, anchor,
            [Alloc(1, 123.45m, 3800m), Alloc(2, 456.78m, 14000m), Alloc(3, 419.77m, 12900m)]);

        var first = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, anchor, [Alloc(1, 123.45m, 3800m)]);
        var r1 = first.Results![0].Realised;
        var rest = FxUnrealisedOverlayRules.Release(Usd(),
            new(r1.OriginalDischarged, r1.HistoricalBaseDischarged), first.OverlayAfter,
            [Alloc(2, 456.78m, 14000m), Alloc(3, 419.77m, 12900m)]);

        Assert.Equal(batch.Results!, first.Results!.Concat(rest.Results!));
        Assert.Equal(batch.OverlayAfter, rest.OverlayAfter);
    }

    [Fact]
    public void Release_after_revaluation_uses_new_anchor()
    {
        // Revalued at 600 open: overlay 420 anchored at 600; settling 300 releases half.
        var c = FxUnrealisedOverlayRules.Release(Usd(), new(400m, 12200m), new(600m, 420m, 0m),
            [Alloc(1, 300m, 9300m)]);

        var a = Assert.Single(c.Results!);
        Assert.Equal(210m, a.OverlayReleased);
        Assert.Equal(210m, a.RemainingOverlay);
        Assert.Equal(new FxOverlayState(600m, 420m, 210m), c.OverlayAfter);
    }

    [Fact]
    public void Empty_allocation_list_keeps_overlay_state()
    {
        var state = new FxOverlayState(1000m, 500m, 0m);
        var c = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, state, []);

        Assert.True(c.IsValid);
        Assert.Empty(c.Results!);
        Assert.Equal(state, c.OverlayAfter);
    }

    [Fact]
    public void Valuation_then_settlements_round_trip_to_zero_overlay_and_carrying_equals_target()
    {
        var discharged = new FxDischargedToDate(123.45m, 3765m);
        var v = Valid(FxUnrealisedOverlayRules.Value(Usd(FxExposureSide.Payable), discharged, FxOverlayState.None,
            Closing(29.87654321m)));
        Assert.Equal(v.TargetBase, v.OpenHistoricalBase + v.OverlayAfter.AnchorOverlay);

        // Re-valuing at the same rate posts nothing.
        var again = Valid(FxUnrealisedOverlayRules.Value(Usd(FxExposureSide.Payable), discharged, v.OverlayAfter,
            Closing(29.87654321m)));
        Assert.Equal(0m, again.ValuationDelta);

        var c = FxUnrealisedOverlayRules.Release(Usd(FxExposureSide.Payable), discharged, v.OverlayAfter,
            [Alloc(1, 500m, 15000m), Alloc(2, 376.55m, 11300m)]);
        Assert.True(c.IsValid);
        Assert.Equal(v.OverlayAfter.AnchorOverlay, c.Results!.Sum(x => x.OverlayReleased));
        Assert.All(c.Results!, x => Assert.True(x.OverlayReleased <= 0m));
        Assert.Equal(0m, c.Results![^1].RemainingOverlay);
        Assert.True(c.Results![^1].Realised.SettlesExposure);
    }

    // ---- State / consistency ----

    [Fact]
    public void No_valuation_state_with_released_amount_is_inconsistent()
    {
        var c = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, new(0m, 0m, 10m), [Alloc(1, 100m, 3050m)]);

        AssertOnly(c.Errors, c.IsValid, "overlay", null, "releasedSinceAnchor", FxAllocationError.Inconsistent);
    }

    [Fact]
    public void Residual_overlay_on_fully_settled_exposure_is_inconsistent()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(), new(1000m, 30500m), new(1000m, 500m, 400m), Closing(31m));

        AssertOnly(c.Errors, c.IsValid, "overlay", null, "releasedSinceAnchor", FxAllocationError.Inconsistent);
        Assert.Null(c.Result);
    }

    [Fact]
    public void Released_not_matching_cumulative_formula_is_inconsistent()
    {
        var c = FxUnrealisedOverlayRules.Release(Usd(), new(400m, 12200m), new(1000m, 500m, 199m),
            [Alloc(1, 100m, 3100m)]);

        AssertOnly(c.Errors, c.IsValid, "overlay", null, "releasedSinceAnchor", FxAllocationError.Inconsistent);
        Assert.Null(c.Results);
        Assert.Null(c.OverlayAfter);
    }

    [Fact]
    public void Anchor_below_current_open_original_is_inconsistent()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(), new(400m, 12200m), new(500m, 100m, 0m), Closing(31m));

        AssertOnly(c.Errors, c.IsValid, "overlay", null, "anchorOpenOriginal", FxAllocationError.Inconsistent);
    }

    [Fact]
    public void Overlay_without_anchor_open_original_is_inconsistent()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(), new(1000m, 30500m), new(0m, 5m, 0m), Closing(31m));

        AssertOnly(c.Errors, c.IsValid, "overlay", null, "anchorOverlay", FxAllocationError.Inconsistent);
    }

    [Fact]
    public void Anchor_beyond_original_gross_is_out_of_range()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, new(1000.01m, 0m, 0m), Closing(31m));

        AssertOnly(c.Errors, c.IsValid, "overlay", null, "anchorOpenOriginal", FxAllocationError.OutOfRange);
    }

    // ---- Boundary ----

    [Fact]
    public void Overlay_amounts_at_sql_bounds_and_scale()
    {
        var max = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, new(1000m, -999999999999999m, 0m),
            [Alloc(1, 500m, 15250m)]);
        Assert.True(max.IsValid);
        // −499,999,999,999,999.5 remaining rounds away from zero; the release takes the rest.
        Assert.Equal(-500000000000000m, max.Results![0].RemainingOverlay);
        Assert.Equal(-499999999999999m, max.Results![0].OverlayReleased);

        var over = FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, new(1000m, -Max - 0.0001m, 0m), Closing(31m));
        AssertOnly(over.Errors, over.IsValid, "overlay", null, "anchorOverlay", FxAllocationError.OutOfRange);

        var scale = FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, new(999.999m, 0.5m, 0.5m), Closing(31m));
        Assert.Equal(
            [
                ("anchorOpenOriginal", FxAllocationError.InvalidScale),
                ("anchorOverlay", FxAllocationError.InvalidScale),
                ("releasedSinceAnchor", FxAllocationError.InvalidScale)
            ],
            scale.Errors.Select(x => (x.Field, x.Error)));
    }

    [Fact]
    public void Negative_anchor_open_original_is_out_of_range()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, new(-1m, 0m, 0m), Closing(31m));

        AssertOnly(c.Errors, c.IsValid, "overlay", null, "anchorOpenOriginal", FxAllocationError.OutOfRange);
    }

    [Fact]
    public void Closing_rate_bounds_and_scale()
    {
        Assert.True(FxUnrealisedOverlayRules.Value(Usd(original: 0.01m, rate: 100m), NoDischarge, FxOverlayState.None,
            Closing(99999999999.99999999m)).IsValid);

        foreach (var (rate, error) in new[]
                 {
                     (0m, FxAllocationError.OutOfRange),
                     (-1m, FxAllocationError.OutOfRange),
                     (100000000000m, FxAllocationError.OutOfRange),
                     (31.000000001m, FxAllocationError.InvalidScale)
                 })
        {
            var c = FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, FxOverlayState.None, Closing(rate));
            AssertOnly(c.Errors, c.IsValid, "closingRate", null, "rate", error);
        }
    }

    [Fact]
    public void Target_beyond_sql_amount_is_overflow()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(original: 1000000000000m), NoDischarge, FxOverlayState.None,
            Closing(10000m));

        AssertOnly(c.Errors, c.IsValid, "closingRate", null, "rate", FxAllocationError.Overflow);
    }

    [Fact]
    public void Delta_beyond_sql_amount_is_overflow()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, new(1000m, -999999999999999m, 0m), Closing(32m));

        AssertOnly(c.Errors, c.IsValid, "closingRate", null, "rate", FxAllocationError.Overflow);
    }

    [Fact]
    public void Large_overlay_release_beyond_decimal_product_is_exact()
    {
        // 999,999,999,999,999.98 × 80,000,000,000,000 exceeds the decimal range; the ratio fallback stays exact.
        var e = Usd(original: 100000000000000m, rate: 1m, baseDecimals: 2);
        var c = FxUnrealisedOverlayRules.Release(e, NoDischarge, new(100000000000000m, 999999999999999.98m, 0m),
            [Alloc(1, 20000000000000m, 20000000000000m)]);

        Assert.True(c.IsValid);
        Assert.Equal(799999999999999.98m, c.Results![0].RemainingOverlay);
        Assert.Equal(200000000000000m, c.Results![0].OverlayReleased);
    }

    // ---- Relation / error ----

    [Fact]
    public void Closing_rate_for_other_currency_pair_is_mismatch()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, FxOverlayState.None, Closing(31m, "EUR", "USD"));

        Assert.Equal(
            [("fromCurrency", FxAllocationError.CurrencyMismatch), ("toCurrency", FxAllocationError.CurrencyMismatch)],
            c.Errors.Select(x => (x.Field, x.Error)));
    }

    [Fact]
    public void Malformed_closing_rate_currency_codes()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(), NoDischarge, FxOverlayState.None, Closing(31m, " ", "twd"));

        Assert.Equal(
            [("fromCurrency", FxAllocationError.Required), ("toCurrency", FxAllocationError.InvalidFormat)],
            c.Errors.Select(x => (x.Field, x.Error)));
    }

    [Fact]
    public void Null_inputs_are_required_in_collection_order()
    {
        var v = FxUnrealisedOverlayRules.Value(null, null, null, null);
        Assert.Equal(
            [("exposure", "exposure"), ("discharged", "discharged"), ("overlay", "overlay"),
                ("closingRate", "closingRate")],
            v.Errors.Select(x => (x.Collection, x.Field)));
        Assert.All(v.Errors, x => Assert.Equal(FxAllocationError.Required, x.Error));

        var r = FxUnrealisedOverlayRules.Release(null, null, null, null);
        Assert.Equal(
            [("exposure", "exposure"), ("discharged", "discharged"), ("overlay", "overlay"),
                ("allocations", "allocations")],
            r.Errors.Select(x => (x.Collection, x.Field)));
        Assert.Null(r.Results);
    }

    [Fact]
    public void Invalid_exposure_still_reports_overlay_and_rate_field_errors_but_skips_dependent_checks()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(doc: "usd"), NoDischarge, new(5000m, 0.5m, 0m),
            Closing(0m, "EUR", "TWD"));

        Assert.Equal(
            [
                ("exposure", "documentCurrency", FxAllocationError.InvalidFormat),
                ("overlay", "anchorOverlay", FxAllocationError.InvalidScale),
                ("closingRate", "rate", FxAllocationError.OutOfRange)
            ],
            c.Errors.Select(x => (x.Collection, x.Field, x.Error)));
    }

    [Fact]
    public void Release_reports_overlay_before_allocation_errors_and_rejects_whole_batch()
    {
        var c = FxUnrealisedOverlayRules.Release(Usd(), NoDischarge, new(1000m, 500m, 1m),
            [Alloc(1, 600m, 18000m), Alloc(2, 600m, 18000m, "EUR")]);

        Assert.Equal(
            [
                ("overlay", (int?)null, "releasedSinceAnchor", FxAllocationError.Inconsistent),
                ("allocations", 1, "currency", FxAllocationError.CurrencyMismatch),
                ("allocations", 1, "originalAmount", FxAllocationError.OverAllocated)
            ],
            c.Errors.Select(x => (x.Collection, x.Index, x.Field, x.Error)));
        Assert.Null(c.Results);
        Assert.Null(c.OverlayAfter);
    }

    [Fact]
    public void Base_currency_exposure_is_rejected_for_valuation()
    {
        var c = FxUnrealisedOverlayRules.Value(Usd(doc: "TWD"), NoDischarge, FxOverlayState.None,
            Closing(1m, "TWD", "TWD"));

        AssertOnly(c.Errors, c.IsValid, "exposure", null, "baseCurrency", FxAllocationError.SameCurrency);
    }
}
