using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-TAX-021（純規則部分）：稅明細 signed base tax 對已過帳 GL 稅額分錄對帳（REQ_F4116.17.4、BR-FI-109、ADR-083）。
/// 不涉及 SQL、cutoff 令牌或畫面；稅明細與 GL 分錄由呼叫端在同一 cutoff 讀到的快照傳入。
/// </summary>
public sealed class TaxGlReconciliationRulesTests
{
    private const long Company = 7;
    private const long OutputTax = 2201;
    private const long InputTax = 1301;
    private const long CostAccount = 5101;
    private static readonly DateOnly Jan1 = new(2028, 1, 1);
    private static readonly DateOnly Feb29 = new(2028, 2, 29);
    private static readonly DateOnly Jan15 = new(2028, 1, 15);

    private static TaxReconciliationScope Scope(int decimals = 0, long company = Company) =>
        new(company, Jan1, Feb29, decimals);

    private static TaxReconciliationDetail Detail(long id, TaxRole role, decimal signedBaseTax, long? account,
        long sourceId = 0, DateOnly? taxPoint = null, long company = Company) =>
        new(id, company, sourceId == 0 ? 900 + id : sourceId, role, account, taxPoint ?? Jan15, signedBaseTax);

    private static TaxReconciliationGlLeg Leg(long lineId, long? detailId, long account, decimal debit, decimal credit,
        DateOnly? posting = null, long journalId = 0, long company = Company) =>
        new(lineId, journalId == 0 ? 500 + lineId : journalId, company, detailId, account, posting ?? Jan15, debit,
            credit);

    private static TaxReconciliationResult Run(IReadOnlyList<TaxReconciliationDetail>? details,
        IReadOnlyList<TaxReconciliationGlLeg>? legs, TaxReconciliationScope? scope = null, bool nullScope = false) =>
        TaxGlReconciliationRules.Reconcile(nullScope ? null : scope ?? Scope(), details, legs);

    private static void AssertOnlyError(TaxReconciliationResult r, string collection, int? index, string field,
        TaxReconciliationError error)
    {
        Assert.False(r.IsValid);
        Assert.Null(r.Report);
        var e = Assert.Single(r.Errors);
        Assert.Equal(collection, e.Collection);
        Assert.Equal(index, e.Index);
        Assert.Equal(field, e.Field);
        Assert.Equal(error, e.Error);
    }

    private static TaxReconciliationDifference OnlyDifference(TaxReconciliationResult r)
    {
        Assert.True(r.IsValid);
        Assert.True(r.Report!.BlocksApproval);
        return Assert.Single(r.Report.Differences);
    }

    // ---- Normal ----

    [Fact]
    public void MatchingLegsForEveryRoleReconcileAndAllowApproval()
    {
        var r = Run(
            [
                Detail(1, TaxRole.Output, 500m, OutputTax),
                Detail(2, TaxRole.OutputCredit, -50m, OutputTax),
                Detail(3, TaxRole.Input, 300m, InputTax),
                Detail(4, TaxRole.Input, 80m, CostAccount)
            ],
            [
                Leg(11, 1, OutputTax, 0m, 500m),
                Leg(12, 2, OutputTax, 50m, 0m),
                Leg(13, 3, InputTax, 300m, 0m),
                Leg(14, 4, CostAccount, 80m, 0m)
            ]);

        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
        var report = r.Report!;
        Assert.False(report.BlocksApproval);
        Assert.Empty(report.Differences);
        Assert.Equal([1L, 2L, 3L, 4L], report.MatchedTaxDetailIds);
        Assert.Empty(report.ExcludedTaxDetailIds);
        Assert.Equal(830m, report.DetailBaseTaxTotal);
        Assert.Equal(830m, report.GlBaseTaxTotal);
    }

    [Fact]
    public void NoTaxSourceIsExcludedNotMissing()
    {
        var r = Run([Detail(1, TaxRole.Output, 0m, null), Detail(2, TaxRole.Input, 0m, null)], []);

        Assert.True(r.IsValid);
        Assert.False(r.Report!.BlocksApproval);
        Assert.Equal([1L, 2L], r.Report.ExcludedTaxDetailIds);
        Assert.Empty(r.Report.MatchedTaxDetailIds);
        Assert.Empty(r.Report.Differences);
    }

    [Fact]
    public void EmptyPeriodReconcilesToZeroWithoutDifferences()
    {
        var r = Run([], []);

        Assert.True(r.IsValid);
        Assert.False(r.Report!.BlocksApproval);
        Assert.Equal(0m, r.Report.DetailBaseTaxTotal);
        Assert.Equal(0m, r.Report.GlBaseTaxTotal);
    }

    // ---- Differences ----

    [Fact]
    public void DetailWithoutGlLegIsMissingAndNamesSource()
    {
        var d = OnlyDifference(Run([Detail(1, TaxRole.Output, 500m, OutputTax, sourceId: 4711)], []));

        Assert.Equal(TaxReconciliationDifferenceKind.Missing, d.Kind);
        Assert.Equal(1L, d.TaxDetailId);
        Assert.Equal(4711L, d.SourceId);
        Assert.Equal(TaxRole.Output, d.Role);
        Assert.Empty(d.GlLineIds);
        Assert.Equal(OutputTax, d.ExpectedAccountId);
        Assert.Equal(500m, d.ExpectedBaseTax);
        Assert.Equal(0m, d.ActualBaseTax);
        Assert.Equal(-500m, d.Difference);
    }

    [Fact]
    public void TwoLegsForOneDetailAreDuplicateAndNameEveryGlLine()
    {
        var r = Run([Detail(1, TaxRole.Output, 500m, OutputTax)],
            [Leg(12, 1, OutputTax, 0m, 500m, journalId: 81), Leg(11, 1, OutputTax, 0m, 500m, journalId: 80)]);

        var d = OnlyDifference(r);
        Assert.Equal(TaxReconciliationDifferenceKind.Duplicate, d.Kind);
        Assert.Equal([11L, 12L], d.GlLineIds);
        Assert.Equal([80L, 81L], d.JournalIds);
        Assert.Equal(500m, d.ExpectedBaseTax);
        Assert.Equal(1000m, d.ActualBaseTax);
        Assert.Equal(500m, d.Difference);
        Assert.Empty(r.Report!.MatchedTaxDetailIds);
    }

    [Fact]
    public void LegOnAnotherAccountIsWrongAccount()
    {
        var d = OnlyDifference(Run([Detail(3, TaxRole.Input, 300m, InputTax)], [Leg(13, 3, CostAccount, 300m, 0m)]));

        Assert.Equal(TaxReconciliationDifferenceKind.WrongAccount, d.Kind);
        Assert.Equal(InputTax, d.ExpectedAccountId);
        Assert.Equal(CostAccount, d.ActualAccountId);
        Assert.Equal([13L], d.GlLineIds);
        Assert.Equal(0m, d.Difference);
    }

    [Theory]
    [InlineData(2027, 12, 31)]
    [InlineData(2028, 3, 1)]
    public void LegPostedOutsideReturnPeriodIsWrongPeriod(int y, int m, int day)
    {
        var d = OnlyDifference(Run([Detail(1, TaxRole.Output, 500m, OutputTax)],
            [Leg(11, 1, OutputTax, 0m, 500m, posting: new DateOnly(y, m, day))]));

        Assert.Equal(TaxReconciliationDifferenceKind.WrongPeriod, d.Kind);
        Assert.Equal(new DateOnly(y, m, day), d.GlPostingDate);
    }

    [Fact]
    public void LegPostedOnPeriodBoundaryDaysAndLeapDayMatches()
    {
        var r = Run(
            [
                Detail(1, TaxRole.Output, 1m, OutputTax, taxPoint: Jan1),
                Detail(2, TaxRole.Output, 2m, OutputTax, taxPoint: Feb29)
            ],
            [Leg(11, 1, OutputTax, 0m, 1m, posting: Jan1), Leg(12, 2, OutputTax, 0m, 2m, posting: Feb29)]);

        Assert.False(r.Report!.BlocksApproval);
        Assert.Equal([1L, 2L], r.Report.MatchedTaxDetailIds);
    }

    [Fact]
    public void RoundingDifferenceIsReportedWithSignedDelta()
    {
        var d = OnlyDifference(Run([Detail(1, TaxRole.Output, 500.25m, OutputTax)],
            [Leg(11, 1, OutputTax, 0m, 500.24m)], Scope(decimals: 2)));

        Assert.Equal(TaxReconciliationDifferenceKind.AmountDifference, d.Kind);
        Assert.Equal(500.25m, d.ExpectedBaseTax);
        Assert.Equal(500.24m, d.ActualBaseTax);
        Assert.Equal(-0.01m, d.Difference);
    }

    [Fact]
    public void LegOnWrongSideIsAmountDifferenceForCredit()
    {
        // A sales return credit must debit output tax; crediting it doubles the liability instead of reducing it.
        var d = OnlyDifference(Run([Detail(2, TaxRole.OutputCredit, -50m, OutputTax)],
            [Leg(12, 2, OutputTax, 0m, 50m)]));

        Assert.Equal(TaxReconciliationDifferenceKind.AmountDifference, d.Kind);
        Assert.Equal(-50m, d.ExpectedBaseTax);
        Assert.Equal(50m, d.ActualBaseTax);
        Assert.Equal(100m, d.Difference);
    }

    [Fact]
    public void OneLegWithSeveralFaultsReportsEachKindInFixedOrder()
    {
        var r = Run([Detail(1, TaxRole.Output, 500m, OutputTax)],
            [Leg(11, 1, CostAccount, 0m, 400m, posting: new DateOnly(2028, 3, 1))]);

        Assert.True(r.Report!.BlocksApproval);
        Assert.Equal(
            [
                TaxReconciliationDifferenceKind.WrongAccount, TaxReconciliationDifferenceKind.WrongPeriod,
                TaxReconciliationDifferenceKind.AmountDifference
            ],
            r.Report.Differences.Select(d => d.Kind));
        Assert.All(r.Report.Differences, d => Assert.Equal([11L], d.GlLineIds));
    }

    [Fact]
    public void UntaggedLegOrLegForUnknownDetailIsUnmatchedAndNamesGlLine()
    {
        var r = Run([Detail(1, TaxRole.Output, 500m, OutputTax)],
        [
            Leg(11, 1, OutputTax, 0m, 500m),
            Leg(20, null, OutputTax, 0m, 30m, journalId: 70),
            Leg(21, 99, InputTax, 10m, 0m, journalId: 71)
        ]);

        var diffs = r.Report!.Differences;
        Assert.True(r.Report.BlocksApproval);
        Assert.Equal(2, diffs.Count);
        Assert.All(diffs, d => Assert.Equal(TaxReconciliationDifferenceKind.Unmatched, d.Kind));
        Assert.Null(diffs[0].TaxDetailId);
        Assert.Equal([20L], diffs[0].GlLineIds);
        Assert.Equal([70L], diffs[0].JournalIds);
        Assert.Equal(OutputTax, diffs[0].ActualAccountId);
        Assert.Equal(-30m, diffs[0].ActualBaseTax); // unknown role: debit − credit
        Assert.Equal(99L, diffs[1].TaxDetailId);
        Assert.Equal([21L], diffs[1].GlLineIds);
        Assert.Equal(10m, diffs[1].ActualBaseTax);
        Assert.Equal([1L], r.Report.MatchedTaxDetailIds);
    }

    [Fact]
    public void LegTaggedToNoTaxSourceIsUnexpected()
    {
        var d = OnlyDifference(Run([Detail(1, TaxRole.Output, 0m, null)], [Leg(11, 1, OutputTax, 0m, 1m)]));

        Assert.Equal(TaxReconciliationDifferenceKind.UnexpectedLeg, d.Kind);
        Assert.Equal(1L, d.TaxDetailId);
        Assert.Equal([11L], d.GlLineIds);
        Assert.Null(d.ExpectedAccountId);
    }

    [Fact]
    public void OffsettingDifferencesStillBlockApproval()
    {
        // Totals agree (500 + 300 on both sides) but each source is wrong; no aggregate netting may hide it.
        var r = Run([Detail(1, TaxRole.Output, 500m, OutputTax), Detail(2, TaxRole.Output, 300m, OutputTax)],
            [Leg(11, 1, OutputTax, 0m, 300m), Leg(12, 2, OutputTax, 0m, 500m)]);

        Assert.Equal(r.Report!.DetailBaseTaxTotal, r.Report.GlBaseTaxTotal);
        Assert.True(r.Report.BlocksApproval);
        Assert.Equal([1L, 2L], r.Report.Differences.Select(d => d.TaxDetailId!.Value));
    }

    [Fact]
    public void DifferencesFollowDetailOrderThenLegOrder()
    {
        var r = Run([Detail(5, TaxRole.Output, 1m, OutputTax), Detail(3, TaxRole.Output, 1m, OutputTax)],
            [Leg(40, null, OutputTax, 0m, 1m), Leg(30, null, OutputTax, 0m, 1m)]);

        Assert.Equal([5L, 3L, null, null], r.Report!.Differences.Select(d => d.TaxDetailId));
        Assert.Equal([40L, 30L], r.Report.Differences.Skip(2).Select(d => d.GlLineIds.Single()));
    }

    [Fact]
    public void NonRecoverableLineFromLineRulesReconcilesOnCostAccount()
    {
        var line = TaxDetailLineRules.Validate(new TaxDetailLineInput(TaxRole.Input, 1, "TX5", 0, 1000m, 50m, 1050m,
            0, 1000m, 50m, 1050m, TaxTreatment.NonRecoverable, null, CostAccount, null)).Detail!;

        var r = Run([Detail(1, line.Role, line.SignedBaseTax, line.GlAccountId)],
            [Leg(11, 1, CostAccount, 50m, 0m)]);

        Assert.False(r.Report!.BlocksApproval);
    }

    // ---- Boundary ----

    [Fact]
    public void SqlMaximumIsAcceptedAndOneStepAboveRejected()
    {
        const decimal max = 999999999999999.9999m;
        var ok = Run([Detail(1, TaxRole.Output, max, OutputTax)], [Leg(11, 1, OutputTax, 0m, max)], Scope(4));
        Assert.False(ok.Report!.BlocksApproval);

        AssertOnlyError(Run([Detail(1, TaxRole.Output, max + 0.0001m, OutputTax)], [], Scope(4)),
            "details", 0, "signedBaseTax", TaxReconciliationError.OutOfRange);
        AssertOnlyError(Run([], [Leg(11, null, OutputTax, 0m, max + 0.0001m)], Scope(4)),
            "glLegs", 0, "credit", TaxReconciliationError.OutOfRange);
    }

    [Fact]
    public void TotalsAboveSqlMaximumAreRejected()
    {
        const decimal max = 999999999999999.9999m;
        var r = Run([Detail(1, TaxRole.Output, max, OutputTax), Detail(2, TaxRole.Output, 1m, OutputTax)],
            [Leg(11, 1, OutputTax, 0m, max), Leg(12, 2, OutputTax, 0m, 1m)], Scope(4));

        Assert.False(r.IsValid);
        Assert.Equal(
            [("totals", "detailBaseTaxTotal"), ("totals", "glBaseTaxTotal")],
            r.Errors.Select(e => (e.Collection, e.Field)));
        Assert.All(r.Errors, e => Assert.Equal(TaxReconciliationError.OutOfRange, e.Error));
    }

    [Fact]
    public void AmountsBeyondBaseScaleAreRejected()
    {
        var r = Run([Detail(1, TaxRole.Output, 1.5m, OutputTax)], [Leg(11, 1, OutputTax, 0m, 1.5m)], Scope(0));

        Assert.Equal(
            [("details", 0, "signedBaseTax"), ("glLegs", 0, "credit")],
            r.Errors.Select(e => (e.Collection, e.Index ?? -1, e.Field)));
        Assert.All(r.Errors, e => Assert.Equal(TaxReconciliationError.InvalidScale, e.Error));
    }

    // ---- Errors ----

    [Fact]
    public void NullScopeAndListsAreRequired()
    {
        var r = Run(null, null, nullScope: true);

        Assert.Equal(
            [("scope", "scope"), ("request", "details"), ("request", "glLegs")],
            r.Errors.Select(e => (e.Collection, e.Field)));
        Assert.All(r.Errors, e => Assert.Equal(TaxReconciliationError.Required, e.Error));
    }

    [Theory]
    [InlineData(0L, 1, 1, 0, "companyId", TaxReconciliationError.Required)]
    [InlineData(7L, 3, 1, 0, "endDate", TaxReconciliationError.OutOfRange)]
    [InlineData(7L, 1, 1, -1, "baseCurrencyDecimals", TaxReconciliationError.OutOfRange)]
    [InlineData(7L, 1, 1, 5, "baseCurrencyDecimals", TaxReconciliationError.OutOfRange)]
    public void InvalidScopeIsRejected(long company, int startMonth, int endMonth, int decimals, string field,
        TaxReconciliationError error)
    {
        var scope = new TaxReconciliationScope(company, new DateOnly(2028, startMonth, 1),
            new DateOnly(2028, endMonth, 1), decimals);

        AssertOnlyError(Run([], [], scope), "scope", null, field, error);
    }

    [Fact]
    public void NullEntriesAreRequired()
    {
        var r = Run([null!], [null!]);

        Assert.Equal([("details", "detail"), ("glLegs", "glLeg")], r.Errors.Select(e => (e.Collection, e.Field)));
    }

    [Fact]
    public void DetailFieldErrorsAreEachReportedOnceInFieldOrder()
    {
        var bad = new TaxReconciliationDetail(0, 8, 0, (TaxRole)9, null, new DateOnly(2028, 3, 1), 1.5m);

        var r = Run([bad], []);

        Assert.Equal(
            [
                ("taxDetailId", TaxReconciliationError.Required),
                ("companyId", TaxReconciliationError.CrossCompany),
                ("sourceId", TaxReconciliationError.Required),
                ("role", TaxReconciliationError.OutOfRange),
                ("glAccountId", TaxReconciliationError.Required),
                ("taxPointDate", TaxReconciliationError.OutsidePeriod),
                ("signedBaseTax", TaxReconciliationError.InvalidScale)
            ],
            r.Errors.Select(e => (e.Field, e.Error)));
        Assert.All(r.Errors, e => Assert.Equal(("details", 0), (e.Collection, e.Index ?? -1)));
    }

    [Theory]
    [InlineData(TaxRole.Output, -1)]
    [InlineData(TaxRole.Input, -1)]
    [InlineData(TaxRole.OutputCredit, 1)]
    public void SignContraryToRoleIsRejected(TaxRole role, int tax)
    {
        AssertOnlyError(Run([Detail(1, role, tax, OutputTax)], []), "details", 0, "signedBaseTax",
            TaxReconciliationError.SignMismatch);
    }

    [Fact]
    public void TaxWithoutAccountOrAccountWithoutTaxIsRejected()
    {
        AssertOnlyError(Run([Detail(1, TaxRole.Output, 5m, null)], []), "details", 0, "glAccountId",
            TaxReconciliationError.Required);
        AssertOnlyError(Run([Detail(1, TaxRole.Output, 0m, OutputTax)], []), "details", 0, "glAccountId",
            TaxReconciliationError.NotApplicable);
        AssertOnlyError(Run([Detail(1, TaxRole.Output, 5m, 0)], []), "details", 0, "glAccountId",
            TaxReconciliationError.Required);
    }

    [Fact]
    public void DuplicateDetailAndGlLineIdsAreRejected()
    {
        var r = Run([Detail(1, TaxRole.Output, 1m, OutputTax), Detail(1, TaxRole.Output, 1m, OutputTax)],
            [Leg(11, 1, OutputTax, 0m, 1m), Leg(11, 1, OutputTax, 0m, 1m)]);

        Assert.Equal(
            [("details", 1, "taxDetailId"), ("glLegs", 1, "glLineId")],
            r.Errors.Select(e => (e.Collection, e.Index ?? -1, e.Field)));
        Assert.All(r.Errors, e => Assert.Equal(TaxReconciliationError.Duplicate, e.Error));
    }

    [Fact]
    public void GlLegFieldErrorsAreEachReportedOnceInFieldOrder()
    {
        var bad = new TaxReconciliationGlLeg(0, 0, 8, -1, 0, Jan15, -1m, 0m);

        var r = Run([], [bad]);

        Assert.Equal(
            [
                ("glLineId", TaxReconciliationError.Required),
                ("journalId", TaxReconciliationError.Required),
                ("companyId", TaxReconciliationError.CrossCompany),
                ("taxDetailId", TaxReconciliationError.OutOfRange),
                ("accountId", TaxReconciliationError.Required),
                ("debit", TaxReconciliationError.OutOfRange)
            ],
            r.Errors.Select(e => (e.Field, e.Error)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    public void GlLegMustBeSingleSidedPositive(int debit, int credit)
    {
        AssertOnlyError(Run([], [Leg(11, null, OutputTax, debit, credit)]), "glLegs", 0, "credit",
            TaxReconciliationError.NotSingleSided);
    }

    [Fact]
    public void InvalidScopeStillReportsItemErrorsButSkipsPeriodMembership()
    {
        var scope = new TaxReconciliationScope(Company, Feb29, Jan1, 0);

        var r = Run([Detail(0, TaxRole.Output, 1m, OutputTax, taxPoint: new DateOnly(2030, 1, 1))], [], scope);

        Assert.Equal([("scope", "endDate"), ("details", "taxDetailId")],
            r.Errors.Select(e => (e.Collection, e.Field)));
    }
}
