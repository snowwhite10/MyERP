using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FX-JNL（純規則部分）：把期末估值結果與結算釋放結果組成本位幣借貸分錄行。估值以政策損益科目對控制科目成對
/// 平衡，零差額不產生行；結算含清算、控制、已實現匯差及獨立的覆蓋迴轉行（零行省略），AR／AP 借貸方向正確，
/// 總額必平衡且不產生 GL plug（REQ_F4116.18.2～18.5、BR-FI-116／118、ADR-087）。不涉及 IGlPostingService、SQL、
/// 永久鍵或畫面。
/// </summary>
public sealed class FxJournalCompositionRulesTests
{
    private const decimal Max = 999999999999999.9999m;
    private const long Control = 1100, Clearing = 2300, RGain = 7100, RLoss = 7200, UGain = 7300, ULoss = 7400;

    private static readonly FxDifferenceAccounts Realised = new(RGain, RLoss);
    private static readonly FxDifferenceAccounts Unrealised = new(UGain, ULoss);

    private static FxValuationJournalAccounts ValAccounts(long control = Control, FxDifferenceAccounts? u = null) =>
        new(control, u ?? Unrealised);

    private static FxSettlementJournalAccounts SetAccounts(long control = Control, long clearing = Clearing,
        FxDifferenceAccounts? r = null, FxDifferenceAccounts? u = null) =>
        new(control, clearing, r ?? Realised, u ?? Unrealised);

    private static FxValuationResult Valuation(FxExposureSide side, decimal delta) =>
        new(1000m, 30500m, 0m, 30500m, 30500m + delta, delta, side == FxExposureSide.Receivable ? delta : -delta,
            FxRealisedOutcome.None, 0m, 0m, true, FxOverlayState.None);

    private static FxOverlayReleaseResult Release(FxExposureSide side, decimal historical, decimal settlement,
        decimal overlay, decimal? difference = null)
    {
        var diff = difference ?? (side == FxExposureSide.Receivable ? settlement - historical : historical - settlement);
        var realised = new FxAllocationResult(11, 400m, historical, settlement, diff,
            diff > 0 ? FxRealisedOutcome.Gain : diff < 0 ? FxRealisedOutcome.Loss : FxRealisedOutcome.None,
            diff > 0 ? diff : 0m, diff < 0 ? -diff : 0m, 600m, 30500m - historical, false);
        return new(realised, overlay, 0m);
    }

    private static IReadOnlyList<FxJournalLine> Valid(FxJournalComposition c)
    {
        Assert.Empty(c.Errors);
        Assert.True(c.IsValid);
        var lines = c.Lines!;
        Assert.Equal(lines.Sum(x => x.Debit), c.TotalDebit);
        Assert.Equal(lines.Sum(x => x.Credit), c.TotalCredit);
        Assert.Equal(c.TotalDebit, c.TotalCredit);
        Assert.All(lines, l => Assert.True((l.Debit > 0) ^ (l.Credit > 0)));
        return lines;
    }

    private static void AssertOnly(FxJournalComposition c, string collection, string field, FxAllocationError error)
    {
        Assert.False(c.IsValid);
        Assert.Null(c.Lines);
        var e = Assert.Single(c.Errors);
        Assert.Equal((collection, (int?)null, field, error), (e.Collection, e.Index, e.Field, e.Error));
    }

    private static FxJournalLine L(FxJournalLineRole role, long account, decimal debit, decimal credit) =>
        new(role, account, debit, credit);

    // ---------- Normal: valuation ----------

    [Fact]
    public void Valuation_receivable_gain_debits_control_and_credits_unrealised_gain()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeValuation(
            FxExposureSide.Receivable, ValAccounts(), Valuation(FxExposureSide.Receivable, 750m)));

        Assert.Equal(
            [L(FxJournalLineRole.Control, Control, 750m, 0m), L(FxJournalLineRole.UnrealisedGain, UGain, 0m, 750m)],
            lines);
    }

    [Fact]
    public void Valuation_receivable_loss_debits_unrealised_loss_and_credits_control()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeValuation(
            FxExposureSide.Receivable, ValAccounts(), Valuation(FxExposureSide.Receivable, -120.5m)));

        Assert.Equal(
            [L(FxJournalLineRole.Control, Control, 0m, 120.5m), L(FxJournalLineRole.UnrealisedLoss, ULoss, 120.5m, 0m)],
            lines);
    }

    [Fact]
    public void Valuation_payable_increase_is_loss_crediting_control()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeValuation(
            FxExposureSide.Payable, ValAccounts(), Valuation(FxExposureSide.Payable, 750m)));

        Assert.Equal(
            [L(FxJournalLineRole.Control, Control, 0m, 750m), L(FxJournalLineRole.UnrealisedLoss, ULoss, 750m, 0m)],
            lines);
    }

    [Fact]
    public void Valuation_payable_decrease_is_gain_debiting_control()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeValuation(
            FxExposureSide.Payable, ValAccounts(), Valuation(FxExposureSide.Payable, -300m)));

        Assert.Equal(
            [L(FxJournalLineRole.Control, Control, 300m, 0m), L(FxJournalLineRole.UnrealisedGain, UGain, 0m, 300m)],
            lines);
    }

    [Fact]
    public void Valuation_composes_from_real_overlay_rule_result()
    {
        var exposure = new FxExposureSnapshot(7, FxExposureSide.Receivable, "USD", "TWD", 2, 0, 1000m, 30.5m, 30500m);
        var valuation = FxUnrealisedOverlayRules.Value(
            exposure, new FxDischargedToDate(0m, 0m), FxOverlayState.None, new FxClosingRate("USD", "TWD", 31.25m));

        var lines = Valid(FxJournalCompositionRules.ComposeValuation(
            FxExposureSide.Receivable, ValAccounts(), valuation.Result));

        Assert.Equal(
            [L(FxJournalLineRole.Control, Control, 750m, 0m), L(FxJournalLineRole.UnrealisedGain, UGain, 0m, 750m)],
            lines);
    }

    // ---------- Normal: settlement ----------

    [Fact]
    public void Settlement_receivable_gain_with_positive_overlay_reversal()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeSettlement(
            FxExposureSide.Receivable, SetAccounts(), Release(FxExposureSide.Receivable, 12200m, 12400m, 300m)));

        Assert.Equal(
        [
            L(FxJournalLineRole.Clearing, Clearing, 12400m, 0m),
            L(FxJournalLineRole.Control, Control, 0m, 12200m),
            L(FxJournalLineRole.RealisedGain, RGain, 0m, 200m),
            L(FxJournalLineRole.OverlayReversalControl, Control, 0m, 300m),
            L(FxJournalLineRole.OverlayReversalGain, UGain, 300m, 0m),
        ], lines);
    }

    [Fact]
    public void Settlement_receivable_loss_with_negative_overlay_reversal()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeSettlement(
            FxExposureSide.Receivable, SetAccounts(), Release(FxExposureSide.Receivable, 12200m, 12000m, -50m)));

        Assert.Equal(
        [
            L(FxJournalLineRole.Clearing, Clearing, 12000m, 0m),
            L(FxJournalLineRole.Control, Control, 0m, 12200m),
            L(FxJournalLineRole.RealisedLoss, RLoss, 200m, 0m),
            L(FxJournalLineRole.OverlayReversalControl, Control, 50m, 0m),
            L(FxJournalLineRole.OverlayReversalLoss, ULoss, 0m, 50m),
        ], lines);
    }

    [Fact]
    public void Settlement_payable_loss_with_positive_overlay_reversal()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeSettlement(
            FxExposureSide.Payable, SetAccounts(), Release(FxExposureSide.Payable, 12200m, 12400m, 300m)));

        Assert.Equal(
        [
            L(FxJournalLineRole.Clearing, Clearing, 0m, 12400m),
            L(FxJournalLineRole.Control, Control, 12200m, 0m),
            L(FxJournalLineRole.RealisedLoss, RLoss, 200m, 0m),
            L(FxJournalLineRole.OverlayReversalControl, Control, 300m, 0m),
            L(FxJournalLineRole.OverlayReversalLoss, ULoss, 0m, 300m),
        ], lines);
    }

    [Fact]
    public void Settlement_payable_gain_with_negative_overlay_reversal()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeSettlement(
            FxExposureSide.Payable, SetAccounts(), Release(FxExposureSide.Payable, 12200m, 12000m, -80m)));

        Assert.Equal(
        [
            L(FxJournalLineRole.Clearing, Clearing, 0m, 12000m),
            L(FxJournalLineRole.Control, Control, 12200m, 0m),
            L(FxJournalLineRole.RealisedGain, RGain, 0m, 200m),
            L(FxJournalLineRole.OverlayReversalControl, Control, 0m, 80m),
            L(FxJournalLineRole.OverlayReversalGain, UGain, 80m, 0m),
        ], lines);
    }

    [Fact]
    public void Settlement_composes_from_real_release_rule_and_net_pl_equals_settlement_minus_carrying()
    {
        var exposure = new FxExposureSnapshot(7, FxExposureSide.Receivable, "USD", "TWD", 2, 0, 1000m, 30.5m, 30500m);
        var overlay = new FxOverlayState(1000m, 750m, 0m);
        var release = FxUnrealisedOverlayRules.Release(exposure, new FxDischargedToDate(0m, 0m), overlay,
            [new FxAllocationRequest(11, "USD", 400m, 12400m)]);
        var r = Assert.Single(release.Results!);

        var lines = Valid(FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable, SetAccounts(), r));

        // Carrying base of 400 USD was 12,200 historical + 300 overlay = 12,500; cash 12,400 → net P&L −100.
        var pl = lines.Where(x => x.AccountId is RGain or RLoss or UGain or ULoss).Sum(x => x.Credit - x.Debit);
        Assert.Equal(-100m, pl);
        var control = lines.Where(x => x.AccountId == Control).Sum(x => x.Debit - x.Credit);
        Assert.Equal(-12500m, control);
    }

    // ---------- Boundary: zero lines omitted ----------

    [Fact]
    public void Zero_valuation_delta_produces_no_lines_and_zero_totals()
    {
        var c = FxJournalCompositionRules.ComposeValuation(
            FxExposureSide.Receivable, ValAccounts(), Valuation(FxExposureSide.Receivable, 0m));

        Assert.True(c.IsValid);
        Assert.False(c.HasEntry);
        Assert.Empty(c.Lines!);
        Assert.Equal((0m, 0m), (c.TotalDebit, c.TotalCredit));
    }

    [Fact]
    public void Settlement_without_difference_or_overlay_has_only_clearing_and_control()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeSettlement(
            FxExposureSide.Receivable, SetAccounts(), Release(FxExposureSide.Receivable, 12200m, 12200m, 0m)));

        Assert.Equal(
            [L(FxJournalLineRole.Clearing, Clearing, 12200m, 0m), L(FxJournalLineRole.Control, Control, 0m, 12200m)],
            lines);
    }

    [Fact]
    public void Settlement_with_zero_historical_base_omits_control_line()
    {
        // Tiny allocation whose proportional historical base rounds to zero: all cash is realised gain.
        var lines = Valid(FxJournalCompositionRules.ComposeSettlement(
            FxExposureSide.Receivable, SetAccounts(), Release(FxExposureSide.Receivable, 0m, 1m, 0m)));

        Assert.Equal(
            [L(FxJournalLineRole.Clearing, Clearing, 1m, 0m), L(FxJournalLineRole.RealisedGain, RGain, 0m, 1m)],
            lines);
    }

    [Fact]
    public void Four_decimal_amounts_and_sql_maximum_are_accepted()
    {
        var lines = Valid(FxJournalCompositionRules.ComposeValuation(
            FxExposureSide.Receivable, ValAccounts(), Valuation(FxExposureSide.Receivable, Max)));
        Assert.Equal(Max, lines[0].Debit);

        Valid(FxJournalCompositionRules.ComposeSettlement(
            FxExposureSide.Payable, SetAccounts(), Release(FxExposureSide.Payable, 0.0001m, 0.0002m, -0.0001m)));
    }

    [Fact]
    public void Shared_gain_and_loss_account_and_same_realised_unrealised_accounts_are_allowed()
    {
        var one = new FxDifferenceAccounts(7000, 7000);
        var lines = Valid(FxJournalCompositionRules.ComposeSettlement(
            FxExposureSide.Receivable, SetAccounts(r: one, u: one), Release(FxExposureSide.Receivable, 100m, 90m, 5m)));

        Assert.Equal(5, lines.Count);
        Assert.Equal(-15m, lines.Where(x => x.AccountId == 7000).Sum(x => x.Credit - x.Debit));
    }

    [Fact]
    public void Settlement_grid_always_balances_and_moves_control_by_historical_plus_overlay()
    {
        decimal[] historicals = [0m, 0.0001m, 12200m, 999999.9999m];
        decimal[] settlements = [0.0001m, 12200m, 12400m, 1000000m];
        decimal[] overlays = [-300.5m, 0m, 0.0001m, 300m];
        var count = 0;
        foreach (var side in new[] { FxExposureSide.Receivable, FxExposureSide.Payable })
        foreach (var h in historicals)
        foreach (var s in settlements)
        foreach (var o in overlays)
        {
            var lines = Valid(FxJournalCompositionRules.ComposeSettlement(side, SetAccounts(), Release(side, h, s, o)));
            var sign = side == FxExposureSide.Receivable ? 1m : -1m;
            // Asset carrying decreases by historical + overlay; liability likewise on the opposite side.
            Assert.Equal(-sign * (h + o), lines.Where(x => x.AccountId == Control).Sum(x => x.Debit - x.Credit));
            Assert.Equal(sign * s, lines.Where(x => x.AccountId == Clearing).Sum(x => x.Debit - x.Credit));
            Assert.DoesNotContain(lines, x => x.Debit == 0 && x.Credit == 0);
            count++;
        }

        Assert.Equal(128, count);
    }

    [Fact]
    public void Valuation_moves_control_by_delta_and_profit_by_signed_difference()
    {
        foreach (var side in new[] { FxExposureSide.Receivable, FxExposureSide.Payable })
        foreach (var delta in new[] { -Max, -0.0001m, 0.0001m, 31.25m, Max })
        {
            var v = Valuation(side, delta);
            var lines = Valid(FxJournalCompositionRules.ComposeValuation(side, ValAccounts(), v));
            var sign = side == FxExposureSide.Receivable ? 1m : -1m;
            Assert.Equal(sign * delta, lines.Where(x => x.AccountId == Control).Sum(x => x.Debit - x.Credit));
            Assert.Equal(v.UnrealisedDifference,
                lines.Where(x => x.AccountId is UGain or ULoss).Sum(x => x.Credit - x.Debit));
        }
    }

    // ---------- Duplicate / conflict: no plug, account collisions ----------

    [Fact]
    public void Control_reused_as_difference_or_clearing_account_is_rejected()
    {
        AssertOnly(FxJournalCompositionRules.ComposeValuation(FxExposureSide.Receivable,
                ValAccounts(u: new FxDifferenceAccounts(Control, ULoss)), Valuation(FxExposureSide.Receivable, 1m)),
            "accounts", "unrealisedGainAccountId", FxAllocationError.Duplicate);

        AssertOnly(FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable,
                SetAccounts(clearing: Control), Release(FxExposureSide.Receivable, 1m, 1m, 0m)),
            "accounts", "clearingAccountId", FxAllocationError.Duplicate);

        AssertOnly(FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable,
                SetAccounts(r: new FxDifferenceAccounts(RGain, Clearing)), Release(FxExposureSide.Receivable, 1m, 1m, 0m)),
            "accounts", "realisedLossAccountId", FxAllocationError.Duplicate);
    }

    [Fact]
    public void Difference_inconsistent_with_side_is_rejected_so_no_plug_is_needed()
    {
        var wrong = Valuation(FxExposureSide.Receivable, 10m) with { UnrealisedDifference = -10m };
        AssertOnly(FxJournalCompositionRules.ComposeValuation(FxExposureSide.Receivable, ValAccounts(), wrong),
            "valuation", "unrealisedDifference", FxAllocationError.Inconsistent);

        // A receivable result passed as payable.
        AssertOnly(FxJournalCompositionRules.ComposeValuation(FxExposureSide.Payable, ValAccounts(),
                Valuation(FxExposureSide.Receivable, 10m)),
            "valuation", "unrealisedDifference", FxAllocationError.Inconsistent);

        AssertOnly(FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable, SetAccounts(),
                Release(FxExposureSide.Receivable, 100m, 110m, 0m, difference: 5m)),
            "release", "realisedDifference", FxAllocationError.Inconsistent);

        AssertOnly(FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Payable, SetAccounts(),
                Release(FxExposureSide.Receivable, 100m, 110m, 0m)),
            "release", "realisedDifference", FxAllocationError.Inconsistent);
    }

    // ---------- Partial result ----------

    [Fact]
    public void Each_composition_is_independent_so_a_failed_exposure_does_not_affect_another()
    {
        var bad = FxJournalCompositionRules.ComposeValuation(FxExposureSide.Receivable, ValAccounts(),
            Valuation(FxExposureSide.Receivable, 10m) with { UnrealisedDifference = 1m });
        var good = FxJournalCompositionRules.ComposeValuation(FxExposureSide.Receivable, ValAccounts(),
            Valuation(FxExposureSide.Receivable, 10m));

        Assert.False(bad.IsValid);
        Assert.Equal(2, Valid(good).Count);
    }

    // ---------- Error: input validation ----------

    [Fact]
    public void Null_inputs_are_required_in_order()
    {
        var c = FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable, null, null);
        Assert.Equal(
        [
            new FxAllocationFieldError("accounts", null, "accounts", FxAllocationError.Required),
            new FxAllocationFieldError("release", null, "release", FxAllocationError.Required),
        ], c.Errors);
        Assert.Null(c.Lines);

        var v = FxJournalCompositionRules.ComposeValuation(FxExposureSide.Receivable, null, null);
        Assert.Equal(
        [
            new FxAllocationFieldError("accounts", null, "accounts", FxAllocationError.Required),
            new FxAllocationFieldError("valuation", null, "valuation", FxAllocationError.Required),
        ], v.Errors);
    }

    [Fact]
    public void Undefined_side_is_rejected()
    {
        AssertOnly(FxJournalCompositionRules.ComposeValuation((FxExposureSide)9, ValAccounts(),
                Valuation(FxExposureSide.Receivable, 0m)),
            "request", "side", FxAllocationError.OutOfRange);
    }

    [Fact]
    public void Missing_account_ids_are_reported_in_field_order()
    {
        var c = FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable,
            new FxSettlementJournalAccounts(0, -1, new FxDifferenceAccounts(0, 0), null),
            Release(FxExposureSide.Receivable, 1m, 1m, 0m));

        Assert.Equal(
            ["controlAccountId", "clearingAccountId", "realisedGainAccountId", "realisedLossAccountId", "unrealised"],
            c.Errors.Select(x => x.Field));
        Assert.All(c.Errors, x => Assert.Equal(FxAllocationError.Required, x.Error));
    }

    [Fact]
    public void Release_with_missing_realised_result_is_required()
    {
        AssertOnly(FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable, SetAccounts(),
                new FxOverlayReleaseResult(null!, 0m, 0m)),
            "release", "realised", FxAllocationError.Required);
    }

    [Theory]
    [InlineData(0.00001, FxAllocationError.InvalidScale)]
    [InlineData(1000000000000000, FxAllocationError.OutOfRange)]
    [InlineData(-1000000000000000, FxAllocationError.OutOfRange)]
    public void Valuation_delta_beyond_sql_decimal_is_rejected(decimal delta, FxAllocationError error)
    {
        AssertOnly(FxJournalCompositionRules.ComposeValuation(FxExposureSide.Receivable, ValAccounts(),
                Valuation(FxExposureSide.Receivable, delta)),
            "valuation", "valuationDelta", error);
    }

    [Fact]
    public void Settlement_amount_errors_are_reported_per_field()
    {
        var c = FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable, SetAccounts(),
            Release(FxExposureSide.Receivable, -1m, 0m, 0.00001m, difference: 0m));

        Assert.Equal(
        [
            new FxAllocationFieldError("release", null, "historicalBaseDischarged", FxAllocationError.OutOfRange),
            new FxAllocationFieldError("release", null, "settlementBase", FxAllocationError.OutOfRange),
            new FxAllocationFieldError("release", null, "overlayReleased", FxAllocationError.InvalidScale),
        ], c.Errors);
    }

    [Fact]
    public void Journal_total_above_sql_decimal_is_overflow()
    {
        // Cash at the maximum plus a reversal on the debit side pushes the debit total beyond decimal(19,4).
        AssertOnly(FxJournalCompositionRules.ComposeSettlement(FxExposureSide.Receivable, SetAccounts(),
                Release(FxExposureSide.Receivable, Max, Max, 1m)),
            "lines", "lines", FxAllocationError.Overflow);
    }
}
