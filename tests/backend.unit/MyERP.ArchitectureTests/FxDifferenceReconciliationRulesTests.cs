using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FX-REC（純規則部分）：外幣匯差查詢／匯出對帳。以同一耐久截止點把已保存的結算分配／估值來源與每條 Posted GL
/// 分錄行對帳；草稿、rejected、rolled back、已刪除來源歷史標示但不計入 Posted；分頁與匯出使用同一集合；匯出跳脫
/// 公式開頭文字（REQ_F4116.18.6、BR-FI-123、ADR-090）。不涉及 SQL 讀取、API 或畫面。
/// </summary>
public sealed class FxDifferenceReconciliationRulesTests
{
    private const long Company = 7, Cutoff = 500, Journal = 9000;
    private const long Control = 1100, Clearing = 2100, RGain = 7100, RLoss = 7200, UGain = 7300, ULoss = 7400;

    private static readonly FxDifferenceReconciliationScope Scope = new(Company, Cutoff, "TWD", 2);

    private static FxDifferenceSourceRecord Settlement(long id, decimal realised, decimal overlay = 0m,
        long exposure = 1, FxExposureSide side = FxExposureSide.Receivable,
        FxDifferenceSourceState state = FxDifferenceSourceState.Posted, long? journal = null, string reference = "AL-1") =>
        new(id, Company, FxDifferenceSourceKind.Settlement, exposure, side, state, Math.Min(100 + id, Cutoff), reference,
            "USD", 31.25m, 400m, realised, overlay,
            state == FxDifferenceSourceState.Posted ? journal ?? Journal + id : journal);

    private static FxDifferenceSourceRecord Valuation(long id, decimal delta, long exposure = 1,
        FxExposureSide side = FxExposureSide.Receivable,
        FxDifferenceSourceState state = FxDifferenceSourceState.Posted, long? journal = null) =>
        new(id, Company, FxDifferenceSourceKind.Valuation, exposure, side, state, Math.Min(100 + id, Cutoff), "VAL-1",
            "USD", 31.5m, 1000m, delta, 0m,
            state == FxDifferenceSourceState.Posted && delta != 0 ? journal ?? Journal + id : journal);

    private static long _line = 1;

    private static FxDifferenceGlLeg Leg(long? source, FxJournalLineRole role, long account, decimal debit,
        decimal credit, long? journal = null, long company = Company, long sequence = 200) =>
        new(Interlocked.Increment(ref _line), journal ?? Journal + (source ?? 0), company, source, role, account,
            sequence, debit, credit);

    /// <summary>GL legs of a journal composed by the real 014h rule, tagged to <paramref name="source"/>.</summary>
    private static List<FxDifferenceGlLeg> Legs(long source, FxJournalComposition composition) =>
        composition.Lines!.Select(l => Leg(source, l.Role, l.AccountId, l.Debit, l.Credit)).ToList();

    private static FxJournalComposition ComposeSettlement(FxExposureSide side, decimal historical, decimal settlement,
        decimal overlay)
    {
        var realised = side == FxExposureSide.Receivable ? settlement - historical : historical - settlement;
        var release = new FxOverlayReleaseResult(
            new FxAllocationResult(1, 400m, historical, settlement, realised,
                realised > 0 ? FxRealisedOutcome.Gain : realised < 0 ? FxRealisedOutcome.Loss : FxRealisedOutcome.None,
                realised > 0 ? realised : 0m, realised < 0 ? -realised : 0m, 0m, 0m, true),
            overlay, 0m);
        var c = FxJournalCompositionRules.ComposeSettlement(side,
            new FxSettlementJournalAccounts(Control, Clearing, new FxDifferenceAccounts(RGain, RLoss),
                new FxDifferenceAccounts(UGain, ULoss)), release);
        Assert.True(c.IsValid);
        return c;
    }

    private static FxJournalComposition ComposeValuation(FxExposureSide side, decimal delta)
    {
        var unrealised = side == FxExposureSide.Receivable ? delta : -delta;
        var result = new FxValuationResult(1000m, 30500m, 0m, 30500m, 30500m + delta, delta, unrealised,
            unrealised > 0 ? FxRealisedOutcome.Gain : unrealised < 0 ? FxRealisedOutcome.Loss : FxRealisedOutcome.None,
            unrealised > 0 ? unrealised : 0m, unrealised < 0 ? -unrealised : 0m, true,
            new FxOverlayState(1000m, delta, 0m));
        var c = FxJournalCompositionRules.ComposeValuation(side,
            new FxValuationJournalAccounts(Control, new FxDifferenceAccounts(UGain, ULoss)), result);
        Assert.True(c.IsValid);
        return c;
    }

    private static FxDifferenceReconciliationReport Ok(IReadOnlyList<FxDifferenceSourceRecord> sources,
        IReadOnlyList<FxDifferenceGlLeg> legs)
    {
        var r = FxDifferenceReconciliationRules.Reconcile(Scope, sources, legs);
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
        return r.Report!;
    }

    private static FxDifferenceReconciliationResult Run(IReadOnlyList<FxDifferenceSourceRecord?>? sources,
        IReadOnlyList<FxDifferenceGlLeg?>? legs, FxDifferenceReconciliationScope? scope = null) =>
        FxDifferenceReconciliationRules.Reconcile(scope ?? Scope, sources!, legs!);

    // ---- FI-FX-REC-001 Normal ----

    [Fact]
    public void Posted_sources_composed_by_real_rules_reconcile_without_differences()
    {
        // AR settlement: received 12,400 for historical 12,500 → loss −100, plus released prior gain overlay 30.
        var arSettle = ComposeSettlement(FxExposureSide.Receivable, 12500m, 12400m, 30m);
        // AP settlement: paid 9,800 for historical 10,000 → gain 200, released prior AP loss overlay (carrying +50).
        var apSettle = ComposeSettlement(FxExposureSide.Payable, 10000m, 9800m, 50m);
        var arValue = ComposeValuation(FxExposureSide.Receivable, 750m);
        var apValue = ComposeValuation(FxExposureSide.Payable, 120m);

        var sources = new[]
        {
            Settlement(1, -100m, 30m, exposure: 10),
            Settlement(2, 200m, 50m, exposure: 20, side: FxExposureSide.Payable),
            Valuation(3, 750m, exposure: 10),
            Valuation(4, -120m, exposure: 20, side: FxExposureSide.Payable),
        };
        var legs = Legs(1, arSettle).Concat(Legs(2, apSettle)).Concat(Legs(3, arValue)).Concat(Legs(4, apValue))
            .ToList();

        var report = Ok(sources, legs);

        Assert.Empty(report.Differences);
        Assert.False(report.BlocksSignOff);
        Assert.Equal([1L, 3, 2, 4], report.Rows.Select(r => r.SourceId));
        Assert.All(report.Rows, r => Assert.True(r.CountsAsPosted));
        Assert.All(report.Rows, r => Assert.Empty(r.Differences));
        Assert.Equal(100m, report.PostedRealisedNet);
        Assert.Equal(630m, report.PostedUnrealisedNet);
        Assert.Equal(100m, report.GlRealisedNet);
        Assert.Equal(630m, report.GlUnrealisedNet);
        // AR prior gain 30 reversed (−30); AP carrying +50 was a prior loss, reversed as +50.
        Assert.Equal(20m, report.PostedOverlayReversalNet);
        Assert.Equal(20m, report.GlOverlayReversalNet);

        var ar = report.Rows[0];
        Assert.Equal(FxDifferenceCategory.Realised, ar.Category);
        Assert.Equal("USD", ar.DocumentCurrency);
        Assert.Equal("TWD", ar.BaseCurrency);
        Assert.Equal(31.25m, ar.Rate);
        Assert.Equal(-100m, ar.GlDifference);
        Assert.Equal(-30m, ar.ExpectedOverlayReversal);
        Assert.Equal(-30m, ar.GlOverlayReversal);
        Assert.Equal(Journal + 1, ar.JournalId);
        Assert.Equal(5, ar.GlLineIds.Count);
        Assert.Equal(FxDifferenceCategory.Unrealised, report.Rows[1].Category);
        Assert.Equal(Cutoff, report.CutoffSequence);
        Assert.Equal(4, report.PostedCount);
        Assert.Equal(4, report.TotalCount);
    }

    // ---- FI-FX-REC-002 Boundary ----

    [Fact]
    public void Empty_set_is_a_valid_report_with_zero_totals()
    {
        var report = Ok([], []);
        Assert.Empty(report.Rows);
        Assert.Empty(report.Differences);
        Assert.Equal(0, report.TotalCount);
        Assert.Equal(0m, report.PostedRealisedNet + report.GlUnrealisedNet);
    }

    [Fact]
    public void Zero_valuation_has_no_journal_and_reconciles_without_legs()
    {
        var report = Ok([Valuation(1, 0m)], []);
        Assert.Empty(report.Differences);
        Assert.True(report.Rows[0].CountsAsPosted);
        Assert.Null(report.Rows[0].JournalId);
    }

    [Fact]
    public void Zero_valuation_with_a_tagged_leg_is_an_unexpected_leg()
    {
        var report = Ok([Valuation(1, 0m)], [Leg(1, FxJournalLineRole.UnrealisedGain, UGain, 0m, 1m)]);
        Assert.Equal(FxDifferenceKind.UnexpectedLeg, Assert.Single(report.Differences).Kind);
    }

    [Fact]
    public void Cutoff_is_inclusive_and_one_beyond_is_rejected()
    {
        var atCutoff = Settlement(1, 0m) with { CommitSequence = Cutoff };
        var legs = Legs(1, ComposeSettlement(FxExposureSide.Receivable, 100m, 100m, 0m))
            .Select(l => l with { CommitSequence = Cutoff }).ToList();
        Assert.Empty(Ok([atCutoff], legs).Differences);

        var late = Run([atCutoff with { CommitSequence = Cutoff + 1 }], []);
        Assert.Equal(new FxDifferenceFieldError("sources", 0, "commitSequence", FxDifferenceError.AfterCutoff),
            Assert.Single(late.Errors));
        var lateLeg = Run([atCutoff], [legs[0] with { CommitSequence = Cutoff + 1 }, legs[1]]);
        Assert.Equal(new FxDifferenceFieldError("glLegs", 0, "commitSequence", FxDifferenceError.AfterCutoff),
            Assert.Single(lateLeg.Errors));
    }

    [Fact]
    public void Maximum_amounts_are_accepted_and_one_step_beyond_is_rejected()
    {
        const decimal max = 999999999999999.99m; // base scale 2
        var src = Valuation(1, max);
        var report = Ok([src], [Leg(1, FxJournalLineRole.Control, Control, max, 0m),
            Leg(1, FxJournalLineRole.UnrealisedGain, UGain, 0m, max)]);
        Assert.Empty(report.Differences);

        var over = Run([src with { SignedDifference = 1000000000000000m }], []);
        Assert.Contains(over.Errors, e => e.Field == "signedDifference" && e.Error == FxDifferenceError.OutOfRange);
        var scale = Run([src with { SignedDifference = 1.005m }], []);
        Assert.Contains(scale.Errors, e => e.Field == "signedDifference" && e.Error == FxDifferenceError.InvalidScale);
        var rate = Run([src with { Rate = 1.123456789m }], []);
        Assert.Contains(rate.Errors, e => e.Field == "rate" && e.Error == FxDifferenceError.InvalidScale);
    }

    [Fact]
    public void Totals_beyond_decimal_19_4_are_rejected()
    {
        const decimal max = 999999999999999.99m;
        var result = Run([Valuation(1, max, exposure: 1), Valuation(2, max, exposure: 2)], []);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e is { Collection: "totals", Error: FxDifferenceError.OutOfRange });
    }

    // ---- FI-FX-REC-003 Duplicate / conflict ----

    [Fact]
    public void Duplicate_source_and_gl_line_ids_are_input_errors()
    {
        var leg = Leg(1, FxJournalLineRole.Control, Control, 1m, 0m);
        var result = Run([Valuation(1, 1m), Valuation(1, 2m)], [leg, leg]);
        Assert.Contains(new FxDifferenceFieldError("sources", 1, "sourceId", FxDifferenceError.Duplicate), result.Errors);
        Assert.Contains(new FxDifferenceFieldError("glLegs", 1, "glLineId", FxDifferenceError.Duplicate), result.Errors);
    }

    [Fact]
    public void Duplicate_gain_leg_shows_as_amount_difference_and_imbalance_not_netted_to_pass()
    {
        var legs = Legs(1, ComposeValuation(FxExposureSide.Receivable, 750m));
        legs.Add(Leg(1, FxJournalLineRole.UnrealisedGain, UGain, 0m, 750m));
        var report = Ok([Valuation(1, 750m)], legs);
        var kinds = report.Differences.Select(d => d.Kind).ToArray();
        Assert.Equal([FxDifferenceKind.Unbalanced, FxDifferenceKind.AmountDifference], kinds);
        var amount = report.Differences[1];
        Assert.Equal(750m, amount.Expected);
        Assert.Equal(1500m, amount.Actual);
        Assert.Equal(750m, amount.Difference);
        Assert.Equal(1500m, report.GlUnrealisedNet);
        Assert.Equal(750m, report.PostedUnrealisedNet);
        Assert.True(report.Rows[0].CountsAsPosted);
        Assert.Equal([FxDifferenceKind.Unbalanced, FxDifferenceKind.AmountDifference], report.Rows[0].Differences);
        Assert.True(report.BlocksSignOff);
    }

    [Fact]
    public void Leg_in_another_journal_is_a_journal_mismatch()
    {
        var legs = Legs(1, ComposeValuation(FxExposureSide.Receivable, 10m));
        legs[0] = legs[0] with { JournalId = 4242 };
        var d = Assert.Single(Ok([Valuation(1, 10m)], legs).Differences);
        Assert.Equal(FxDifferenceKind.JournalMismatch, d.Kind);
        Assert.Equal([4242L, Journal + 1], d.JournalIds);
        Assert.Equal([legs[0].GlLineId], d.GlLineIds);
    }

    // ---- FI-FX-REC-004 Partial / state: non-Posted history ----

    [Fact]
    public void Non_posted_history_is_labelled_and_never_counted_as_posted()
    {
        var posted = Valuation(5, 10m, exposure: 1);
        var sources = new[]
        {
            Valuation(1, 99m, exposure: 2, state: FxDifferenceSourceState.Draft),
            Settlement(2, 50m, exposure: 2, state: FxDifferenceSourceState.Rejected),
            Settlement(3, 60m, exposure: 2, state: FxDifferenceSourceState.RolledBack),
            Valuation(4, 70m, exposure: 2, state: FxDifferenceSourceState.Deleted),
            posted,
        };
        var report = Ok(sources, Legs(5, ComposeValuation(FxExposureSide.Receivable, 10m)));

        Assert.Empty(report.Differences);
        Assert.Equal([5L, 1, 2, 3, 4], report.Rows.Select(r => r.SourceId));
        Assert.Equal([FxDifferenceSourceState.Posted, FxDifferenceSourceState.Draft, FxDifferenceSourceState.Rejected,
            FxDifferenceSourceState.RolledBack, FxDifferenceSourceState.Deleted], report.Rows.Select(r => r.State));
        Assert.Equal([true, false, false, false, false], report.Rows.Select(r => r.CountsAsPosted));
        Assert.Equal(10m, report.PostedUnrealisedNet);
        Assert.Equal(0m, report.PostedRealisedNet);
        Assert.Equal((1, 1, 1, 1, 1, 5), (report.PostedCount, report.DraftCount, report.RejectedCount,
            report.RolledBackCount, report.DeletedCount, report.TotalCount));
    }

    [Fact]
    public void Gl_leg_tagged_to_a_non_posted_source_is_a_difference()
    {
        var rolled = Settlement(1, 50m, state: FxDifferenceSourceState.RolledBack);
        var leg = Leg(1, FxJournalLineRole.RealisedGain, RGain, 0m, 50m);
        var report = Ok([rolled], [leg]);
        var d = Assert.Single(report.Differences);
        Assert.Equal(FxDifferenceKind.LegOnNonPostedSource, d.Kind);
        Assert.Equal([leg.GlLineId], d.GlLineIds);
        Assert.False(report.Rows[0].CountsAsPosted);
        Assert.Equal(0m, report.PostedRealisedNet);
        Assert.Equal(50m, report.GlRealisedNet);
    }

    [Fact]
    public void Non_posted_source_with_a_journal_id_is_rejected()
    {
        var result = Run([Settlement(1, 5m, state: FxDifferenceSourceState.Draft, journal: 77)], []);
        Assert.Equal(new FxDifferenceFieldError("sources", 0, "journalId", FxDifferenceError.NotApplicable),
            Assert.Single(result.Errors));
    }

    // ---- FI-FX-REC-005 Relation: missing, unmatched, wrong role, overlay ----

    [Fact]
    public void Posted_source_without_gl_legs_is_missing()
    {
        var d = Assert.Single(Ok([Settlement(1, -100m)], []).Differences);
        Assert.Equal(FxDifferenceKind.Missing, d.Kind);
        Assert.Equal(-100m, d.Expected);
        Assert.Equal(0m, d.Actual);
    }

    [Fact]
    public void Untagged_or_unknown_source_legs_are_unmatched()
    {
        var manual = Leg(null, FxJournalLineRole.RealisedLoss, RLoss, 40m, 0m, journal: 1);
        var unknown = Leg(99, FxJournalLineRole.UnrealisedGain, UGain, 0m, 15m);
        var report = Ok([], [manual, unknown]);
        Assert.Equal([FxDifferenceKind.Unmatched, FxDifferenceKind.Unmatched], report.Differences.Select(d => d.Kind));
        Assert.Equal(-40m, report.Differences[0].Actual);
        Assert.Null(report.Differences[0].SourceId);
        Assert.Equal(99L, report.Differences[1].SourceId);
        Assert.Equal(-40m, report.GlRealisedNet);
        Assert.Equal(15m, report.GlUnrealisedNet);
    }

    [Fact]
    public void Role_not_belonging_to_the_source_kind_is_unexpected()
    {
        var legs = Legs(1, ComposeValuation(FxExposureSide.Receivable, 10m));
        legs[1] = legs[1] with { Role = FxJournalLineRole.RealisedGain };
        var kinds = Ok([Valuation(1, 10m)], legs).Differences.Select(d => d.Kind).ToArray();
        Assert.Equal([FxDifferenceKind.UnexpectedRole, FxDifferenceKind.AmountDifference], kinds);
    }

    [Fact]
    public void Wrong_overlay_reversal_is_an_overlay_difference()
    {
        var legs = Legs(1, ComposeSettlement(FxExposureSide.Receivable, 12500m, 12400m, 30m));
        var report = Ok([Settlement(1, -100m, 20m)], legs);
        var d = Assert.Single(report.Differences);
        Assert.Equal(FxDifferenceKind.OverlayDifference, d.Kind);
        Assert.Equal(-20m, d.Expected);
        Assert.Equal(-30m, d.Actual);
        Assert.Equal(-10m, d.Difference);
    }

    // ---- FI-FX-REC-006 Error / permission (cross-company) ----

    [Fact]
    public void Null_inputs_and_scope_are_reported()
    {
        var r = FxDifferenceReconciliationRules.Reconcile(null, null, null);
        Assert.Equal([
            new FxDifferenceFieldError("scope", null, "scope", FxDifferenceError.Required),
            new FxDifferenceFieldError("request", null, "sources", FxDifferenceError.Required),
            new FxDifferenceFieldError("request", null, "glLegs", FxDifferenceError.Required)], r.Errors);
        Assert.Null(r.Report);

        var items = Run([null], [null]);
        Assert.Equal([
            new FxDifferenceFieldError("sources", 0, "source", FxDifferenceError.Required),
            new FxDifferenceFieldError("glLegs", 0, "glLeg", FxDifferenceError.Required)], items.Errors);
    }

    [Fact]
    public void Invalid_scope_fields_are_reported()
    {
        var r = Run([], [], new FxDifferenceReconciliationScope(0, 0, "twd", 5));
        Assert.Equal([
            new FxDifferenceFieldError("scope", null, "companyId", FxDifferenceError.Required),
            new FxDifferenceFieldError("scope", null, "cutoffSequence", FxDifferenceError.Required),
            new FxDifferenceFieldError("scope", null, "baseCurrency", FxDifferenceError.InvalidFormat),
            new FxDifferenceFieldError("scope", null, "baseCurrencyDecimals", FxDifferenceError.OutOfRange)], r.Errors);
    }

    [Fact]
    public void Invalid_source_fields_are_reported_in_field_order()
    {
        var bad = new FxDifferenceSourceRecord(0, 0, (FxDifferenceSourceKind)9, 0, (FxExposureSide)9,
            (FxDifferenceSourceState)9, 0, " ", "usd", 0m, 0m, 0m, 0m, null);
        var r = Run([bad], []);
        Assert.Equal(["sourceId", "companyId", "kind", "exposureId", "side", "state", "commitSequence", "reference",
            "documentCurrency", "rate", "originalAmount"], r.Errors.Select(e => e.Field));
    }

    [Fact]
    public void Source_rules_for_currency_journal_and_overlay()
    {
        Assert.Contains(Run([Valuation(1, 5m) with { DocumentCurrency = "TWD" }], []).Errors,
            e => e is { Field: "documentCurrency", Error: FxDifferenceError.SameCurrency });
        Assert.Contains(Run([Valuation(1, 5m) with { OverlayReleased = 1m }], []).Errors,
            e => e is { Field: "overlayReleased", Error: FxDifferenceError.NotApplicable });
        Assert.Contains(Run([Valuation(1, 5m) with { JournalId = null }], []).Errors,
            e => e is { Field: "journalId", Error: FxDifferenceError.Required });
        Assert.Contains(Run([Valuation(1, 0m) with { JournalId = 5 }], []).Errors,
            e => e is { Field: "journalId", Error: FxDifferenceError.NotApplicable });
        Assert.Contains(Run([Settlement(1, 0m) with { JournalId = null }], []).Errors,
            e => e is { Field: "journalId", Error: FxDifferenceError.Required });
        Assert.Contains(Run([Settlement(1, 0m, reference: new string('A', 65))], []).Errors,
            e => e is { Field: "reference", Error: FxDifferenceError.OutOfRange });
    }

    [Fact]
    public void Invalid_gl_leg_fields_are_reported()
    {
        var r = Run([], [new FxDifferenceGlLeg(0, 0, 0, 0, (FxJournalLineRole)99, 0, 0, -1m, 0m),
            new FxDifferenceGlLeg(2, 1, Company, null, FxJournalLineRole.Control, 1, 1, 5m, 5m)]);
        Assert.Equal(["glLineId", "journalId", "companyId", "sourceId", "role", "accountId", "commitSequence",
            "debit", "credit"], r.Errors.Select(e => e.Field));
        Assert.Equal(FxDifferenceError.NotSingleSided, r.Errors[^1].Error);
    }

    [Fact]
    public void Other_company_rows_are_cross_company_errors()
    {
        var src = Valuation(1, 5m) with { CompanyId = 8 };
        var leg = Leg(1, FxJournalLineRole.Control, Control, 5m, 0m, company: 8);
        var r = Run([src], [leg]);
        Assert.Equal([
            new FxDifferenceFieldError("sources", 0, "companyId", FxDifferenceError.CrossCompany),
            new FxDifferenceFieldError("glLegs", 0, "companyId", FxDifferenceError.CrossCompany)], r.Errors);
    }

    // ---- FI-FX-REC-007 Pagination and export use the same set ----

    private static FxDifferenceReconciliationReport Many(int count) =>
        Ok(Enumerable.Range(1, count).Reverse().Select(i => Valuation(i, 0m, exposure: i)).ToList(), []);

    [Fact]
    public void Pages_partition_the_report_rows_exactly()
    {
        var report = Many(7);
        var pages = Enumerable.Range(1, 3).Select(n => FxDifferenceReconciliationRules.Page(report, n, 3)).ToList();
        Assert.All(pages, p => Assert.True(p.IsValid));
        Assert.Equal(report.Rows.Select(r => r.SourceId), pages.SelectMany(p => p.Page!.Rows).Select(r => r.SourceId));
        Assert.Equal([true, true, false], pages.Select(p => p.Page!.HasNext));
        Assert.All(pages, p => Assert.Equal((7, Cutoff), (p.Page!.TotalRows, p.Page.CutoffSequence)));

        var beyond = FxDifferenceReconciliationRules.Page(report, 4, 3);
        Assert.Empty(beyond.Page!.Rows);
        Assert.False(beyond.Page.HasNext);
    }

    [Fact]
    public void Exact_multiple_has_no_next_page_and_huge_page_number_does_not_overflow()
    {
        var report = Many(6);
        var second = FxDifferenceReconciliationRules.Page(report, 2, 3).Page!;
        Assert.Equal([4L, 5, 6], second.Rows.Select(r => r.SourceId));
        Assert.False(second.HasNext);

        var huge = FxDifferenceReconciliationRules.Page(report, int.MaxValue, FxDifferenceReconciliationRules.MaxPageSize);
        Assert.True(huge.IsValid);
        Assert.Empty(huge.Page!.Rows);
        Assert.False(huge.Page.HasNext);
    }

    [Fact]
    public void Leg_on_deleted_draft_is_flagged_and_its_amount_stays_out_of_posted_totals()
    {
        var deleted = Valuation(1, 40m, state: FxDifferenceSourceState.Deleted);
        var legs = Legs(1, ComposeValuation(FxExposureSide.Receivable, 40m));
        var report = Ok([deleted], legs);
        var d = Assert.Single(report.Differences);
        Assert.Equal(FxDifferenceKind.LegOnNonPostedSource, d.Kind);
        Assert.Equal(legs.Select(l => l.GlLineId).Order(), d.GlLineIds);
        Assert.Equal(0m, report.PostedUnrealisedNet);
        Assert.Equal(40m, report.GlUnrealisedNet);
        Assert.Equal([FxDifferenceKind.LegOnNonPostedSource], report.Rows[0].Differences);
        Assert.Equal(40m, report.Rows[0].GlDifference);
    }

    [Fact]
    public void Page_arguments_are_validated()
    {
        var report = Many(1);
        Assert.Equal(["report", "pageNumber", "pageSize"],
            FxDifferenceReconciliationRules.Page(null, 0, 0).Errors.Select(e => e.Field));
        Assert.Equal("pageSize",
            Assert.Single(FxDifferenceReconciliationRules.Page(report, 1, FxDifferenceReconciliationRules.MaxPageSize + 1)
                .Errors).Field);
        Assert.True(FxDifferenceReconciliationRules.Page(report, 1, FxDifferenceReconciliationRules.MaxPageSize).IsValid);
    }

    private static readonly string[] Headers = Enumerable.Range(1, FxDifferenceReconciliationRules.ExportColumnCount)
        .Select(i => $"H{i}").ToArray();

    [Fact]
    public void Export_contains_cutoff_header_and_every_row_in_report_order()
    {
        var report = Many(3);
        var export = FxDifferenceReconciliationRules.Export(report, Headers);
        Assert.True(export.IsValid);
        var lines = export.Content!.Split("\r\n");
        Assert.Equal($"CompanyId,{Company}", lines[0]);
        Assert.Equal($"CutoffSequence,{Cutoff}", lines[1]);
        Assert.Equal(string.Join(',', Headers), lines[2]);
        Assert.Equal(3, export.RowCount);
        Assert.Equal(6, lines.Length);
        Assert.StartsWith("1,Valuation,Unrealised,Posted,true,1,Receivable,VAL-1,USD,TWD,31.5,1000,0,0,0,0,,,", lines[3]);
        Assert.StartsWith("3,", lines[5]);
    }

    [Fact]
    public void Export_escapes_formula_leading_text_but_not_signed_numbers()
    {
        var src = Settlement(1, -100m, reference: "=HYPERLINK(\"x\")");
        var legs = Legs(1, ComposeSettlement(FxExposureSide.Receivable, 12500m, 12400m, 0m));
        var report = Ok([src], legs);
        var headers = Headers.ToArray();
        headers[0] = "@cmd";
        headers[1] = "a,b";
        var lines = FxDifferenceReconciliationRules.Export(report, headers).Content!.Split("\r\n");
        Assert.StartsWith("'@cmd,\"a,b\",", lines[2]);
        Assert.Contains(",\"'=HYPERLINK(\"\"x\"\")\",", lines[3]);
        Assert.Contains(",-100,-100,", lines[3]);
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1", "'+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("  =1", "'  =1")]
    [InlineData("\t=1", "\"'\t=1\"")]
    [InlineData("\tplain", "\"'\tplain\"")]
    [InlineData("\rplain", "\"'\rplain\"")]
    [InlineData("plain", "plain")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("", "")]
    public void Csv_text_escaping(string input, string expected) =>
        Assert.Equal(expected, FxDifferenceReconciliationRules.EscapeCsvText(input));

    [Fact]
    public void Export_validates_headers_and_row_limit()
    {
        var report = Many(1);
        Assert.Equal(["report", "headers"],
            FxDifferenceReconciliationRules.Export(null, null).Errors.Select(e => e.Field));
        Assert.Equal(FxDifferenceError.OutOfRange,
            Assert.Single(FxDifferenceReconciliationRules.Export(report, Headers[1..]).Errors).Error);
        var nullHeader = Headers.ToArray();
        nullHeader[2] = null!;
        Assert.Equal(new FxDifferenceFieldError("headers", 2, "header", FxDifferenceError.Required),
            Assert.Single(FxDifferenceReconciliationRules.Export(report, nullHeader).Errors));

        var tooMany = Many(FxDifferenceReconciliationRules.MaxExportRows + 1);
        var rejected = FxDifferenceReconciliationRules.Export(tooMany, Headers);
        Assert.Null(rejected.Content);
        Assert.Equal(new FxDifferenceFieldError("report", null, "rows", FxDifferenceError.OutOfRange),
            Assert.Single(rejected.Errors));
        Assert.True(FxDifferenceReconciliationRules.Export(Many(FxDifferenceReconciliationRules.MaxExportRows), Headers)
            .IsValid);
    }

    [Fact]
    public void Reading_and_exporting_do_not_mutate_inputs()
    {
        var sources = new List<FxDifferenceSourceRecord> { Valuation(2, 0m, exposure: 2), Valuation(1, 0m) };
        var snapshot = sources.ToList();
        var report = Ok(sources, []);
        FxDifferenceReconciliationRules.Page(report, 1, 1);
        FxDifferenceReconciliationRules.Export(report, Headers);
        Assert.Equal(snapshot, sources);
        Assert.Equal([1L, 2], report.Rows.Select(r => r.SourceId));
    }
}
