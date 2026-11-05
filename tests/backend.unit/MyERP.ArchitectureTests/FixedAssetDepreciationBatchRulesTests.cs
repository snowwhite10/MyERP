using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FA-013／014（純規則部分）：折舊批次逐項決定 post／skip／fail 與原因，並把逐資產交易結果彙總成
/// requested／posted／skipped／failed 計數。不涉及 SQL、來源鍵、期間鎖或 GL 過帳。
/// </summary>
public sealed class FixedAssetDepreciationBatchRulesTests
{
    private static readonly FixedAssetDepreciationInput Even = new(1200m, 0m, 12, new DateOnly(2026, 1, 10), 2);

    private static FixedAssetDepreciationCandidate Active(long id, FixedAssetDepreciationInput? card = null, bool posted = false) =>
        new(id, FixedAssetCardState.Active, card ?? Even, posted);

    private static FixedAssetDepreciationPlan Plan(bool periodOpen, params FixedAssetDepreciationCandidate?[] items) =>
        FixedAssetDepreciationBatchRules.Plan(2026, 3, periodOpen, items);

    [Fact]
    public void MixedBatchIsOrderedByAssetIdWithSpecificReasons()
    {
        var plan = Plan(true,
            Active(9),
            new FixedAssetDepreciationCandidate(3, FixedAssetCardState.Draft, Even, false),
            new FixedAssetDepreciationCandidate(5, FixedAssetCardState.Disposed, Even, false),
            Active(7, posted: true),
            Active(4, Even with { InServiceDate = new DateOnly(2026, 4, 1) }),
            Active(2, Even with { UsefulLifeMonths = 2 }),
            Active(8, Even with { Residual = 1200m }),
            Active(1));

        Assert.Equal(FixedAssetDepreciationPlanStatus.Ok, plan.Status);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 7, 8, 9 }, plan.Items.Select(x => x.AssetId));
        var byId = plan.Items.ToDictionary(x => x.AssetId);
        AssertItem(byId[1], FixedAssetBatchDecision.Post, FixedAssetBatchReason.None, 100m, 3);
        AssertItem(byId[9], FixedAssetBatchDecision.Post, FixedAssetBatchReason.None, 100m, 3);
        AssertItem(byId[2], FixedAssetBatchDecision.Skip, FixedAssetBatchReason.LifeExhausted, 0m, null);
        AssertItem(byId[3], FixedAssetBatchDecision.Skip, FixedAssetBatchReason.CardNotActive, 0m, null);
        AssertItem(byId[4], FixedAssetBatchDecision.Skip, FixedAssetBatchReason.BeforeInService, 0m, null);
        AssertItem(byId[5], FixedAssetBatchDecision.Skip, FixedAssetBatchReason.CardDisposed, 0m, null);
        AssertItem(byId[7], FixedAssetBatchDecision.Skip, FixedAssetBatchReason.AlreadyPosted, 0m, null);
        AssertItem(byId[8], FixedAssetBatchDecision.Fail, FixedAssetBatchReason.InvalidResidual, 0m, null);
        Assert.Equal(2, plan.PostCount);
        Assert.Equal(200m, plan.PostTotal);
    }

    [Fact]
    public void ClosedPeriodSkipsEveryAssetWithoutCalculating()
    {
        var plan = Plan(false, Active(2), Active(1, Even with { Cost = 0m }));
        Assert.Equal(FixedAssetDepreciationPlanStatus.Ok, plan.Status);
        Assert.All(plan.Items, x =>
        {
            Assert.Equal(FixedAssetBatchDecision.Skip, x.Decision);
            Assert.Equal(FixedAssetBatchReason.PeriodNotOpen, x.Reason);
            Assert.Equal(0m, x.Amount);
        });
        Assert.Equal(0, plan.PostCount);
    }

    [Fact]
    public void ScheduleStatusesMapToSkipOrFailReasons()
    {
        var zeroBase = new FixedAssetDepreciationInput(1m, 0m, 3, new DateOnly(2026, 1, 1), 0);
        var capped = new FixedAssetDepreciationInput(5m, 0m, 8, new DateOnly(2025, 12, 1), 0);
        var plan = FixedAssetDepreciationBatchRules.Plan(2026, 1, true, new FixedAssetDepreciationCandidate?[]
        {
            Active(1, zeroBase),
            Active(2, Even with { CurrencyDecimals = 5 }),
            Active(3, Even with { Cost = -1m }),
            Active(4, Even with { UsefulLifeMonths = 0 }),
        });
        var byId = plan.Items.ToDictionary(x => x.AssetId);
        Assert.Equal(FixedAssetBatchReason.ZeroAmount, byId[1].Reason);
        Assert.Equal(FixedAssetBatchDecision.Skip, byId[1].Decision);
        Assert.Equal(FixedAssetBatchReason.InvalidCurrencyDecimals, byId[2].Reason);
        Assert.Equal(FixedAssetBatchReason.InvalidCost, byId[3].Reason);
        Assert.Equal(FixedAssetBatchReason.InvalidUsefulLife, byId[4].Reason);
        Assert.All(new[] { byId[2], byId[3], byId[4] }, x => Assert.Equal(FixedAssetBatchDecision.Fail, x.Decision));

        // cost 5, life 8, 0 decimals: base 1 → months 6–8 are FullyDepreciated (ADR-078).
        var full = FixedAssetDepreciationBatchRules.Plan(2026, 6, true, new FixedAssetDepreciationCandidate?[] { Active(1, capped) });
        Assert.Equal(FixedAssetBatchReason.FullyDepreciated, full.Items.Single().Reason);
        Assert.Equal(FixedAssetBatchDecision.Skip, full.Items.Single().Decision);
    }

    [Fact]
    public void InvalidBatchShapeIsRejectedWithoutItems()
    {
        Assert.Equal(FixedAssetDepreciationPlanStatus.InvalidInput,
            FixedAssetDepreciationBatchRules.Plan(2026, 3, true, null).Status);
        var nullItem = Plan(true, Active(1), null);
        Assert.Equal(FixedAssetDepreciationPlanStatus.InvalidInput, nullItem.Status);
        Assert.Empty(nullItem.Items);
        Assert.Equal(1, nullItem.ErrorIndex);
        var badId = Plan(true, Active(1), Active(0));
        Assert.Equal(FixedAssetDepreciationPlanStatus.InvalidAssetId, badId.Status);
        Assert.Equal(1, badId.ErrorIndex);
        var dup = Plan(true, Active(5), Active(6), Active(5));
        Assert.Equal(FixedAssetDepreciationPlanStatus.DuplicateAsset, dup.Status);
        Assert.Equal(2, dup.ErrorIndex);
        Assert.Empty(dup.Items);
        var undefinedState = Plan(true, new FixedAssetDepreciationCandidate(1, (FixedAssetCardState)99, Even, false));
        Assert.Equal(FixedAssetDepreciationPlanStatus.InvalidInput, undefinedState.Status);
        Assert.Equal(0, undefinedState.ErrorIndex);
        Assert.Equal(FixedAssetDepreciationPlanStatus.InvalidPeriod,
            FixedAssetDepreciationBatchRules.Plan(2026, 13, true, new FixedAssetDepreciationCandidate?[] { Active(1) }).Status);
        Assert.Equal(FixedAssetDepreciationPlanStatus.InvalidPeriod,
            FixedAssetDepreciationBatchRules.Plan(0, 1, true, new FixedAssetDepreciationCandidate?[] { Active(1) }).Status);
    }

    [Fact]
    public void NullCardOnActiveAssetFailsThatItemOnly()
    {
        var plan = Plan(true, new FixedAssetDepreciationCandidate(1, FixedAssetCardState.Active, null, false), Active(2));
        Assert.Equal(FixedAssetDepreciationPlanStatus.Ok, plan.Status);
        Assert.Equal(FixedAssetBatchReason.InvalidInput, plan.Items[0].Reason);
        Assert.Equal(FixedAssetBatchDecision.Fail, plan.Items[0].Decision);
        Assert.Equal(FixedAssetBatchDecision.Post, plan.Items[1].Decision);
    }

    [Fact]
    public void AllPostedIsSuccessAndChanged()
    {
        var plan = Plan(true, Active(1), Active(2));
        var summary = FixedAssetDepreciationBatchRules.Summarize(plan, new[]
        {
            new FixedAssetDepreciationAttempt(2, true), new FixedAssetDepreciationAttempt(1, true),
        });
        Assert.Equal(FixedAssetBatchSummaryStatus.Ok, summary.Status);
        Assert.Equal(FixedAssetBatchOutcome.Success, summary.Outcome);
        Assert.True(summary.Changed);
        Assert.Equal((2, 2, 0, 0), (summary.Requested, summary.Posted, summary.Skipped, summary.Failed));
        Assert.Equal(200m, summary.PostedTotal);
        Assert.All(summary.Items, x => Assert.Equal(FixedAssetBatchItemResult.Posted, x.Result));
    }

    [Fact]
    public void MixedCommittedAndFailedAttemptsArePartialWithExactCounts()
    {
        var plan = Plan(true, Active(1), Active(2), Active(3, posted: true), Active(4, Even with { Cost = 0m }));
        var summary = FixedAssetDepreciationBatchRules.Summarize(plan, new[]
        {
            new FixedAssetDepreciationAttempt(1, true),
            new FixedAssetDepreciationAttempt(2, false, "FI.GL.MAPPING_MISSING"),
        });
        Assert.Equal(FixedAssetBatchSummaryStatus.Ok, summary.Status);
        Assert.Equal(FixedAssetBatchOutcome.Partial, summary.Outcome);
        Assert.True(summary.Changed);
        Assert.Equal((4, 1, 1, 2), (summary.Requested, summary.Posted, summary.Skipped, summary.Failed));
        Assert.Equal(100m, summary.PostedTotal);
        var two = summary.Items.Single(x => x.AssetId == 2);
        Assert.Equal(FixedAssetBatchItemResult.Failed, two.Result);
        Assert.Equal(FixedAssetBatchReason.PostingFailed, two.Reason);
        Assert.Equal("FI.GL.MAPPING_MISSING", two.FailureCode);
        Assert.Equal(0m, two.Amount);
        var four = summary.Items.Single(x => x.AssetId == 4);
        Assert.Equal(FixedAssetBatchItemResult.Failed, four.Result);
        Assert.Equal(FixedAssetBatchReason.InvalidCost, four.Reason);
        Assert.Null(four.FailureCode);
        Assert.Equal(FixedAssetBatchReason.AlreadyPosted, summary.Items.Single(x => x.AssetId == 3).Reason);
    }

    [Fact]
    public void ZeroCommittedOutcomesAreWarningsNotSuccess()
    {
        var empty = FixedAssetDepreciationBatchRules.Summarize(Plan(true), Array.Empty<FixedAssetDepreciationAttempt>());
        Assert.Equal(FixedAssetBatchOutcome.NoEligibleAssets, empty.Outcome);
        Assert.False(empty.Changed);
        Assert.Equal(0, empty.Requested);

        var skipped = FixedAssetDepreciationBatchRules.Summarize(Plan(true, Active(1, posted: true)), Array.Empty<FixedAssetDepreciationAttempt>());
        Assert.Equal(FixedAssetBatchOutcome.NoChange, skipped.Outcome);
        Assert.False(skipped.Changed);
        Assert.Equal((1, 0, 1, 0), (skipped.Requested, skipped.Posted, skipped.Skipped, skipped.Failed));

        var failed = FixedAssetDepreciationBatchRules.Summarize(Plan(true, Active(1), Active(2, Even with { Cost = 0m })),
            new[] { new FixedAssetDepreciationAttempt(1, false, "FI.GL.PERIOD_CLOSED") });
        Assert.Equal(FixedAssetBatchOutcome.AllFailed, failed.Outcome);
        Assert.False(failed.Changed);
        Assert.Equal((2, 0, 0, 2), (failed.Requested, failed.Posted, failed.Skipped, failed.Failed));

        var skipAndFail = FixedAssetDepreciationBatchRules.Summarize(Plan(true, Active(1, posted: true), Active(2, Even with { Cost = 0m })),
            Array.Empty<FixedAssetDepreciationAttempt>());
        Assert.Equal(FixedAssetBatchOutcome.NoChange, skipAndFail.Outcome);
        Assert.False(skipAndFail.Changed);
    }

    [Fact]
    public void AttemptsMustMatchPostItemsExactly()
    {
        var plan = Plan(true, Active(1), Active(2), Active(3, posted: true));
        AssertSummaryStatus(plan, FixedAssetBatchSummaryStatus.MissingAttempt, new FixedAssetDepreciationAttempt(1, true));
        AssertSummaryStatus(plan, FixedAssetBatchSummaryStatus.DuplicateAttempt,
            new FixedAssetDepreciationAttempt(1, true), new FixedAssetDepreciationAttempt(2, true), new FixedAssetDepreciationAttempt(1, true));
        AssertSummaryStatus(plan, FixedAssetBatchSummaryStatus.UnexpectedAttempt,
            new FixedAssetDepreciationAttempt(1, true), new FixedAssetDepreciationAttempt(2, true), new FixedAssetDepreciationAttempt(3, true));
        AssertSummaryStatus(plan, FixedAssetBatchSummaryStatus.UnexpectedAttempt,
            new FixedAssetDepreciationAttempt(1, true), new FixedAssetDepreciationAttempt(2, true), new FixedAssetDepreciationAttempt(99, false, "X"));
        AssertSummaryStatus(plan, FixedAssetBatchSummaryStatus.InvalidAttempt,
            new FixedAssetDepreciationAttempt(1, true), new FixedAssetDepreciationAttempt(2, false));
        AssertSummaryStatus(plan, FixedAssetBatchSummaryStatus.InvalidAttempt,
            new FixedAssetDepreciationAttempt(1, true), new FixedAssetDepreciationAttempt(2, false, "  "));
        AssertSummaryStatus(plan, FixedAssetBatchSummaryStatus.InvalidAttempt,
            new FixedAssetDepreciationAttempt(1, true, "FI.X"), new FixedAssetDepreciationAttempt(2, true));
        AssertSummaryStatus(plan, FixedAssetBatchSummaryStatus.InvalidInput, null!);
        Assert.Equal(FixedAssetBatchSummaryStatus.InvalidInput,
            FixedAssetDepreciationBatchRules.Summarize(plan, null).Status);
        Assert.Equal(FixedAssetBatchSummaryStatus.InvalidInput,
            FixedAssetDepreciationBatchRules.Summarize(null, Array.Empty<FixedAssetDepreciationAttempt>()).Status);
        var rejected = Plan(true, Active(1), Active(1));
        Assert.Equal(FixedAssetBatchSummaryStatus.InvalidPlan,
            FixedAssetDepreciationBatchRules.Summarize(rejected, Array.Empty<FixedAssetDepreciationAttempt>()).Status);
    }

    [Fact]
    public void RejectedSummaryHasNoCountsOrItems()
    {
        var plan = Plan(true, Active(1));
        var summary = FixedAssetDepreciationBatchRules.Summarize(plan, Array.Empty<FixedAssetDepreciationAttempt>());
        Assert.Equal(FixedAssetBatchSummaryStatus.MissingAttempt, summary.Status);
        Assert.Equal(1L, summary.ErrorAssetId);
        Assert.Empty(summary.Items);
        Assert.False(summary.Changed);
        Assert.Equal(0, summary.Requested);
    }

    [Fact]
    public void ClosedPeriodAcceptsNoPostingAttemptAndSummaryKeepsAssetOrder()
    {
        var closed = Plan(false, Active(3), Active(1));
        var rejected = FixedAssetDepreciationBatchRules.Summarize(closed, new[] { new FixedAssetDepreciationAttempt(1, true) });
        Assert.Equal(FixedAssetBatchSummaryStatus.UnexpectedAttempt, rejected.Status);
        Assert.Equal(1L, rejected.ErrorAssetId);

        var summary = FixedAssetDepreciationBatchRules.Summarize(closed, Array.Empty<FixedAssetDepreciationAttempt>());
        Assert.Equal(FixedAssetBatchOutcome.NoChange, summary.Outcome);
        Assert.Equal(new long[] { 1, 3 }, summary.Items.Select(x => x.AssetId));
        Assert.All(summary.Items, x => Assert.Equal(FixedAssetBatchReason.PeriodNotOpen, x.Reason));
        Assert.Equal(0m, summary.PostedTotal);
    }

    private static void AssertSummaryStatus(
        FixedAssetDepreciationPlan plan, FixedAssetBatchSummaryStatus expected, params FixedAssetDepreciationAttempt?[] attempts)
    {
        var summary = FixedAssetDepreciationBatchRules.Summarize(plan, attempts);
        Assert.Equal(expected, summary.Status);
        Assert.Empty(summary.Items);
    }

    private static void AssertItem(
        FixedAssetDepreciationPlanItem item, FixedAssetBatchDecision decision, FixedAssetBatchReason reason, decimal amount, int? periodNumber)
    {
        Assert.Equal(decision, item.Decision);
        Assert.Equal(reason, item.Reason);
        Assert.Equal(amount, item.Amount);
        Assert.Equal(periodNumber, item.PeriodNumber);
    }
}
