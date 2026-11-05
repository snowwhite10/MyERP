using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FX-BAT（純規則部分）：外幣期末估值批次結果彙總。按敞口 ID 升序合併適格性（014g）、估值（014f）、分錄組成
/// （014h）與逐來源過帳交易結果，得出 posted／previewed／zero／excluded／failed 與 Success／Partial／NoChange／
/// AllFailed／NoEligible；預覽不得出現 posted，重複／缺漏／未預期結果整批拒絕（REQ_F4116.18.4／18.6、
/// BR-FI-117／122、ADR-088）。不涉及 manifest 保存、SQL、鎖或畫面。
/// </summary>
public sealed class FxValuationBatchRulesTests
{
    private const long Control = 1100, UGain = 7300, ULoss = 7400;
    private static readonly IReadOnlyList<FxAllocationFieldError> NoErrors = Array.Empty<FxAllocationFieldError>();

    private static FxExposureEligibility Eligible(long id) =>
        new(id, true, new FxClosingRate("USD", "TWD", 31.25m), 900 + id, Array.Empty<FxValuationExclusion>());

    private static FxExposureEligibility Excluded(long id, params FxValuationExclusion[] reasons) =>
        new(id, false, null, null, reasons);

    private static FxValuationEligibilityResult Eligibility(FxValuationMode mode, params FxExposureEligibility?[] items) =>
        new(mode, FxPolicyResolution.Resolved, null, NoErrors, items!,
            items.Count(x => x is { IsEligible: true }), items.Count(x => x is { IsEligible: false }),
            mode == FxValuationMode.Post && items.Any(x => x is { IsEligible: true }));

    private static FxValuationResult ValuationResult(decimal delta) =>
        new(1000m, 30500m, 0m, 30500m, 30500m + delta, delta, delta,
            delta > 0 ? FxRealisedOutcome.Gain : delta < 0 ? FxRealisedOutcome.Loss : FxRealisedOutcome.None,
            delta > 0 ? delta : 0m, delta < 0 ? -delta : 0m, true,
            new FxOverlayState(1000m, delta, 0m));

    /// <summary>A receivable valuation composed by the real 014h rule, so the journal matches the delta.</summary>
    private static FxValuationComputation Computed(long id, decimal delta)
    {
        var result = ValuationResult(delta);
        var journal = FxJournalCompositionRules.ComposeValuation(
            FxExposureSide.Receivable, new FxValuationJournalAccounts(Control, new FxDifferenceAccounts(UGain, ULoss)), result);
        Assert.True(journal.IsValid);
        return new(id, new FxValuationCalculation(result, NoErrors), journal);
    }

    private static FxValuationComputation InvalidValuation(long id) =>
        new(id, new FxValuationCalculation(null, [new("closingRate", null, "rate", FxAllocationError.CurrencyMismatch)]), null);

    private static FxValuationComputation InvalidJournal(long id, decimal delta) =>
        new(id, new FxValuationCalculation(ValuationResult(delta), NoErrors),
            new FxJournalComposition(null, 0m, 0m, [new("accounts", null, "controlAccountId", FxAllocationError.Duplicate)]));

    private static FxValuationPostAttempt Ok(long id) => new(id, true);
    private static FxValuationPostAttempt Rolled(long id, string code = "FI.FX.CONFLICT") => new(id, false, code);

    private static FxValuationBatchSummary Sum(FxValuationEligibilityResult? e,
        IReadOnlyList<FxValuationComputation?>? c, IReadOnlyList<FxValuationPostAttempt?>? a) =>
        FxValuationBatchRules.Summarize(e, c, a);

    private static void AssertRejected(FxValuationBatchSummary s, FxValuationBatchStatus status, long? exposureId)
    {
        Assert.Equal(status, s.Status);
        Assert.Equal(exposureId, s.ErrorExposureId);
        Assert.Empty(s.Items);
        Assert.Equal((0, 0, 0, 0, 0, 0), (s.Requested, s.Posted, s.Previewed, s.Zero, s.Excluded, s.Failed));
        Assert.Equal(FxValuationBatchOutcome.NoChange, s.Outcome);
        Assert.False(s.Changed);
    }

    private static void AssertCounts(FxValuationBatchSummary s, int requested, int posted, int previewed, int zero,
        int excluded, int failed)
    {
        Assert.Equal(FxValuationBatchStatus.Ok, s.Status);
        Assert.Null(s.ErrorExposureId);
        Assert.Equal((requested, posted, previewed, zero, excluded, failed),
            (s.Requested, s.Posted, s.Previewed, s.Zero, s.Excluded, s.Failed));
        Assert.Equal(requested, s.Items.Count);
        Assert.Equal(requested, posted + previewed + zero + excluded + failed);
    }

    // ---------- FI-FX-BAT-001 Normal ----------

    [Fact]
    public void Post_all_committed_is_success_with_posted_net_and_journal_amount()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(2), Eligible(1)),
            [Computed(2, -120.5m), Computed(1, 750m)], [Ok(1), Ok(2)]);

        AssertCounts(s, 2, 2, 0, 0, 0, 0);
        Assert.Equal(FxValuationBatchOutcome.Success, s.Outcome);
        Assert.True(s.Changed);
        Assert.Equal(FxValuationMode.Post, s.Mode);
        Assert.Equal([1L, 2L], s.Items.Select(x => x.ExposureId));
        Assert.Equal((FxValuationItemResult.Posted, 750m, 750m, 750m), (s.Items[0].Result, s.Items[0].ValuationDelta,
            s.Items[0].UnrealisedDifference, s.Items[0].JournalAmount));
        Assert.Equal((FxValuationItemResult.Posted, -120.5m, 120.5m), (s.Items[1].Result, s.Items[1].ValuationDelta,
            s.Items[1].JournalAmount));
        Assert.Equal(629.5m, s.PostedUnrealisedNet);
        Assert.Equal(0m, s.PreviewUnrealisedNet);
        Assert.All(s.Items, x => Assert.Null(x.FailureCode));
    }

    [Fact]
    public void Real_eligibility_valuation_and_composition_rules_feed_the_summary()
    {
        var exposure = new FxExposureSnapshot(5, FxExposureSide.Receivable, "USD", "TWD", 2, 0, 1000m, 30.5m, 30500m);
        var calc = FxUnrealisedOverlayRules.Value(exposure, new FxDischargedToDate(0m, 0m), FxOverlayState.None,
            new FxClosingRate("USD", "TWD", 31.25m));
        Assert.True(calc.IsValid);
        var journal = FxJournalCompositionRules.ComposeValuation(FxExposureSide.Receivable,
            new FxValuationJournalAccounts(Control, new FxDifferenceAccounts(UGain, ULoss)), calc.Result);

        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(5)), [new(5, calc, journal)], [Ok(5)]);

        AssertCounts(s, 1, 1, 0, 0, 0, 0);
        Assert.Equal((750m, 750m), (s.Items[0].ValuationDelta, s.PostedUnrealisedNet));
    }

    // ---------- FI-FX-BAT-002 Boundary ----------

    [Fact]
    public void Empty_request_is_no_eligible()
    {
        var s = Sum(Eligibility(FxValuationMode.Post), [], []);

        AssertCounts(s, 0, 0, 0, 0, 0, 0);
        Assert.Equal(FxValuationBatchOutcome.NoEligible, s.Outcome);
        Assert.False(s.Changed);
    }

    [Fact]
    public void All_zero_delta_is_no_change_without_attempts()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(1), Eligible(2)), [Computed(1, 0m), Computed(2, 0m)], []);

        AssertCounts(s, 2, 0, 0, 2, 0, 0);
        Assert.Equal(FxValuationBatchOutcome.NoChange, s.Outcome);
        Assert.All(s.Items, x => Assert.Equal((FxValuationItemResult.Zero, 0m, 0m),
            (x.Result, x.ValuationDelta, x.JournalAmount)));
    }

    [Fact]
    public void Smallest_delta_posts_and_large_batch_counts_exactly()
    {
        var ids = Enumerable.Range(1, 500).Select(i => (long)i).ToArray();
        var eligibility = Eligibility(FxValuationMode.Post, ids.Reverse().Select(Eligible).ToArray());
        var computations = ids.Select(i => Computed(i, i % 3 == 0 ? 0m : 0.0001m)).ToArray();
        var attempts = ids.Where(i => i % 3 != 0).Select(i => i % 5 == 0 ? Rolled(i) : Ok(i)).ToArray();

        var s = Sum(eligibility, computations, attempts);

        var zero = ids.Count(i => i % 3 == 0);
        var failed = ids.Count(i => i % 3 != 0 && i % 5 == 0);
        AssertCounts(s, 500, 500 - zero - failed, 0, zero, 0, failed);
        Assert.Equal(FxValuationBatchOutcome.Partial, s.Outcome);
        Assert.Equal(ids, s.Items.Select(x => x.ExposureId));
        Assert.Equal((500 - zero - failed) * 0.0001m, s.PostedUnrealisedNet);
    }

    // ---------- FI-FX-BAT-003 Duplicate / conflict ----------

    [Fact]
    public void Duplicate_exposure_in_eligibility_rejects_batch()
    {
        AssertRejected(Sum(Eligibility(FxValuationMode.Post, Eligible(1), Eligible(1)), [Computed(1, 1m)], [Ok(1)]),
            FxValuationBatchStatus.InvalidEligibility, 1);
    }

    [Fact]
    public void Duplicate_computation_rejects_batch()
    {
        AssertRejected(Sum(Eligibility(FxValuationMode.Post, Eligible(1)), [Computed(1, 1m), Computed(1, 1m)], [Ok(1)]),
            FxValuationBatchStatus.DuplicateComputation, 1);
    }

    [Fact]
    public void Duplicate_attempt_rejects_batch()
    {
        AssertRejected(Sum(Eligibility(FxValuationMode.Post, Eligible(1)), [Computed(1, 1m)], [Ok(1), Rolled(1)]),
            FxValuationBatchStatus.DuplicateAttempt, 1);
    }

    [Fact]
    public void Rolled_back_attempt_is_failed_with_its_error_code()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(1), Eligible(2)), [Computed(1, 5m), Computed(2, 6m)],
            [Ok(1), Rolled(2, "COMMON.CONCURRENCY.CONFLICT")]);

        AssertCounts(s, 2, 1, 0, 0, 0, 1);
        Assert.Equal(FxValuationBatchOutcome.Partial, s.Outcome);
        Assert.Equal((FxValuationItemResult.Failed, "COMMON.CONCURRENCY.CONFLICT", 0m, 0m),
            (s.Items[1].Result, s.Items[1].FailureCode, s.Items[1].ValuationDelta, s.Items[1].JournalAmount));
        Assert.Equal(5m, s.PostedUnrealisedNet);
    }

    // ---------- FI-FX-BAT-004 Partial result ----------

    [Fact]
    public void Mixed_post_zero_excluded_and_failed_is_partial_with_reasons()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(4), Excluded(3, FxValuationExclusion.StaleRate,
                FxValuationExclusion.InactiveCurrency), Eligible(2), Eligible(1), Eligible(5)),
            [Computed(1, 10m), Computed(2, 0m), InvalidValuation(4), InvalidJournal(5, 3m)], [Ok(1)]);

        AssertCounts(s, 5, 1, 0, 1, 1, 2);
        Assert.Equal(FxValuationBatchOutcome.Partial, s.Outcome);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], s.Items.Select(x => x.ExposureId));
        Assert.Equal([FxValuationExclusion.StaleRate, FxValuationExclusion.InactiveCurrency], s.Items[2].Reasons);
        Assert.Equal(FxValuationItemResult.Excluded, s.Items[2].Result);
        Assert.Null(s.Items[2].FailureCode);
        Assert.Equal((FxValuationItemResult.Failed, FxValuationBatchRules.ValuationInvalidCode),
            (s.Items[3].Result, s.Items[3].FailureCode));
        Assert.Equal("rate", Assert.Single(s.Items[3].Errors).Field);
        Assert.Equal((FxValuationItemResult.Failed, FxValuationBatchRules.JournalInvalidCode),
            (s.Items[4].Result, s.Items[4].FailureCode));
        Assert.Equal(FxAllocationError.Duplicate, Assert.Single(s.Items[4].Errors).Error);
        Assert.All(s.Items.Where(x => x.Result != FxValuationItemResult.Excluded), x => Assert.Empty(x.Reasons));
    }

    [Fact]
    public void All_excluded_is_no_eligible()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Excluded(1, FxValuationExclusion.PolicyMissing),
            Excluded(2, FxValuationExclusion.PolicyMissing)), [], []);

        AssertCounts(s, 2, 0, 0, 0, 2, 0);
        Assert.Equal(FxValuationBatchOutcome.NoEligible, s.Outcome);
    }

    [Fact]
    public void All_failed_is_all_failed()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(1), Eligible(2), Eligible(3)),
            [Computed(1, 1m), InvalidValuation(2), InvalidJournal(3, 1m)], [Rolled(1)]);

        AssertCounts(s, 3, 0, 0, 0, 0, 3);
        Assert.Equal(FxValuationBatchOutcome.AllFailed, s.Outcome);
    }

    [Fact]
    public void Zero_and_excluded_without_post_is_no_change_not_success()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(1), Excluded(2, FxValuationExclusion.MissingRate)),
            [Computed(1, 0m)], []);

        AssertCounts(s, 2, 0, 0, 1, 1, 0);
        Assert.Equal(FxValuationBatchOutcome.NoChange, s.Outcome);
    }

    [Fact]
    public void Posted_with_zero_is_partial_not_success()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(1), Eligible(2)), [Computed(1, 2m), Computed(2, 0m)], [Ok(1)]);

        AssertCounts(s, 2, 1, 0, 1, 0, 0);
        Assert.Equal(FxValuationBatchOutcome.Partial, s.Outcome);
    }

    [Fact]
    public void Excluded_and_failed_only_is_no_change()
    {
        var s = Sum(Eligibility(FxValuationMode.Post, Eligible(1), Excluded(2, FxValuationExclusion.MissingRate)),
            [InvalidValuation(1)], []);

        AssertCounts(s, 2, 0, 0, 0, 1, 1);
        Assert.Equal(FxValuationBatchOutcome.NoChange, s.Outcome);
    }

    // ---------- FI-FX-BAT-005 State (Preview / Post) ----------

    [Fact]
    public void Preview_never_posts_and_is_no_change()
    {
        var s = Sum(Eligibility(FxValuationMode.Preview, Eligible(1), Eligible(2), Eligible(3)),
            [Computed(1, 10m), Computed(2, -4m), Computed(3, 0m)], []);

        AssertCounts(s, 3, 0, 2, 1, 0, 0);
        Assert.Equal(FxValuationMode.Preview, s.Mode);
        Assert.Equal(FxValuationBatchOutcome.NoChange, s.Outcome);
        Assert.False(s.Changed);
        Assert.DoesNotContain(s.Items, x => x.Result == FxValuationItemResult.Posted);
        Assert.Equal((FxValuationItemResult.Previewed, 10m, 10m), (s.Items[0].Result, s.Items[0].ValuationDelta,
            s.Items[0].JournalAmount));
        Assert.Equal(6m, s.PreviewUnrealisedNet);
        Assert.Equal(0m, s.PostedUnrealisedNet);
    }

    [Fact]
    public void Preview_with_attempt_rejects_batch()
    {
        AssertRejected(Sum(Eligibility(FxValuationMode.Preview, Eligible(1)), [Computed(1, 1m)], [Ok(1)]),
            FxValuationBatchStatus.UnexpectedAttempt, 1);
    }

    [Fact]
    public void Preview_all_failed_is_all_failed_and_empty_is_no_eligible()
    {
        Assert.Equal(FxValuationBatchOutcome.AllFailed,
            Sum(Eligibility(FxValuationMode.Preview, Eligible(1)), [InvalidValuation(1)], []).Outcome);
        Assert.Equal(FxValuationBatchOutcome.NoEligible,
            Sum(Eligibility(FxValuationMode.Preview, Excluded(1, FxValuationExclusion.StaleRate)), [], []).Outcome);
    }

    // ---------- FI-FX-BAT-006 Relation / error: whole-batch rejection ----------

    [Fact]
    public void Null_inputs_are_invalid_input()
    {
        var e = Eligibility(FxValuationMode.Post, Eligible(1));
        AssertRejected(Sum(null, [], []), FxValuationBatchStatus.InvalidInput, null);
        AssertRejected(Sum(e, null, []), FxValuationBatchStatus.InvalidInput, null);
        AssertRejected(Sum(e, [Computed(1, 1m)], null), FxValuationBatchStatus.InvalidInput, null);
    }

    [Fact]
    public void Malformed_eligibility_rejects_batch()
    {
        AssertRejected(Sum(Eligibility((FxValuationMode)9, Eligible(1)), [Computed(1, 1m)], []),
            FxValuationBatchStatus.InvalidEligibility, null);
        AssertRejected(Sum(Eligibility(FxValuationMode.Post, Eligible(1), null), [Computed(1, 1m)], [Ok(1)]),
            FxValuationBatchStatus.InvalidEligibility, null);
        AssertRejected(Sum(Eligibility(FxValuationMode.Post, Eligible(0)), [Computed(0, 1m)], [Ok(0)]),
            FxValuationBatchStatus.InvalidEligibility, 0);
        AssertRejected(Sum(Eligibility(FxValuationMode.Post, Excluded(1)), [], []),
            FxValuationBatchStatus.InvalidEligibility, 1);
        AssertRejected(Sum(Eligibility(FxValuationMode.Post,
                new FxExposureEligibility(1, true, null, 901, Array.Empty<FxValuationExclusion>())), [Computed(1, 1m)], [Ok(1)]),
            FxValuationBatchStatus.InvalidEligibility, 1);
        AssertRejected(Sum(Eligibility(FxValuationMode.Post,
                new FxExposureEligibility(1, true, new FxClosingRate("USD", "TWD", 1m), 901,
                    [FxValuationExclusion.StaleRate])), [Computed(1, 1m)], [Ok(1)]),
            FxValuationBatchStatus.InvalidEligibility, 1);
        var miscounted = Eligibility(FxValuationMode.Post, Eligible(1)) with { EligibleCount = 2 };
        AssertRejected(Sum(miscounted, [Computed(1, 1m)], [Ok(1)]), FxValuationBatchStatus.InvalidEligibility, null);
        var previewClaimsPosting = Eligibility(FxValuationMode.Preview, Eligible(1)) with { PostingAllowed = true };
        AssertRejected(Sum(previewClaimsPosting, [Computed(1, 1m)], []), FxValuationBatchStatus.InvalidEligibility, null);
        var postDeniesPosting = Eligibility(FxValuationMode.Post, Eligible(1)) with { PostingAllowed = false };
        AssertRejected(Sum(postDeniesPosting, [Computed(1, 1m)], [Ok(1)]), FxValuationBatchStatus.InvalidEligibility, null);
        var nullList = Eligibility(FxValuationMode.Post) with { Exposures = null! };
        AssertRejected(Sum(nullList, [], []), FxValuationBatchStatus.InvalidEligibility, null);
    }

    [Fact]
    public void Computation_for_excluded_or_unknown_exposure_is_unexpected()
    {
        var e = Eligibility(FxValuationMode.Post, Eligible(1), Excluded(2, FxValuationExclusion.MissingRate));
        AssertRejected(Sum(e, [Computed(1, 1m), Computed(2, 1m)], [Ok(1)]), FxValuationBatchStatus.UnexpectedComputation, 2);
        AssertRejected(Sum(e, [Computed(1, 1m), Computed(9, 1m)], [Ok(1)]), FxValuationBatchStatus.UnexpectedComputation, 9);
    }

    [Fact]
    public void Missing_computation_rejects_batch()
    {
        AssertRejected(Sum(Eligibility(FxValuationMode.Post, Eligible(1), Eligible(2)), [Computed(2, 1m)], [Ok(2)]),
            FxValuationBatchStatus.MissingComputation, 1);
    }

    [Fact]
    public void Malformed_computation_rejects_batch()
    {
        var e = Eligibility(FxValuationMode.Post, Eligible(1));
        AssertRejected(Sum(e, [null], []), FxValuationBatchStatus.InvalidComputation, null);
        AssertRejected(Sum(e, [new FxValuationComputation(1, null, null)], []), FxValuationBatchStatus.InvalidComputation, 1);
        AssertRejected(Sum(e, [new FxValuationComputation(1, new FxValuationCalculation(ValuationResult(1m), NoErrors), null)],
            []), FxValuationBatchStatus.InvalidComputation, 1);
    }

    [Fact]
    public void Inconsistent_computation_rejects_batch()
    {
        var e = Eligibility(FxValuationMode.Post, Eligible(1));
        var invalidWithJournal = InvalidValuation(1) with { Journal = Computed(1, 1m).Journal };
        AssertRejected(Sum(e, [invalidWithJournal], []), FxValuationBatchStatus.InconsistentComputation, 1);

        var entryForZero = Computed(1, 0m) with { Journal = Computed(1, 5m).Journal };
        AssertRejected(Sum(e, [entryForZero], [Ok(1)]), FxValuationBatchStatus.InconsistentComputation, 1);

        var noEntryForDelta = Computed(1, 5m) with { Journal = Computed(1, 0m).Journal };
        AssertRejected(Sum(e, [noEntryForDelta], []), FxValuationBatchStatus.InconsistentComputation, 1);

        var wrongAmount = Computed(1, 5m) with { Journal = Computed(1, 6m).Journal };
        AssertRejected(Sum(e, [wrongAmount], [Ok(1)]), FxValuationBatchStatus.InconsistentComputation, 1);
    }

    [Fact]
    public void Attempt_shape_and_relation_errors_reject_batch()
    {
        var e = Eligibility(FxValuationMode.Post, Eligible(1), Eligible(2), Excluded(3, FxValuationExclusion.MissingRate));
        IReadOnlyList<FxValuationComputation?> c = [Computed(1, 1m), Computed(2, 0m)];

        AssertRejected(Sum(e, c, [null]), FxValuationBatchStatus.InvalidAttempt, null);
        AssertRejected(Sum(e, c, [new FxValuationPostAttempt(1, true, "X")]), FxValuationBatchStatus.InvalidAttempt, 1);
        AssertRejected(Sum(e, c, [new FxValuationPostAttempt(1, false, " ")]), FxValuationBatchStatus.InvalidAttempt, 1);
        AssertRejected(Sum(e, c, [Ok(1), Ok(2)]), FxValuationBatchStatus.UnexpectedAttempt, 2);
        AssertRejected(Sum(e, c, [Ok(1), Ok(3)]), FxValuationBatchStatus.UnexpectedAttempt, 3);
        AssertRejected(Sum(e, c, [Ok(1), Ok(8)]), FxValuationBatchStatus.UnexpectedAttempt, 8);
        AssertRejected(Sum(e, c, []), FxValuationBatchStatus.MissingAttempt, 1);
    }

    [Fact]
    public void Attempt_for_failed_computation_is_unexpected()
    {
        AssertRejected(Sum(Eligibility(FxValuationMode.Post, Eligible(1)), [InvalidValuation(1)], [Rolled(1)]),
            FxValuationBatchStatus.UnexpectedAttempt, 1);
    }
}
