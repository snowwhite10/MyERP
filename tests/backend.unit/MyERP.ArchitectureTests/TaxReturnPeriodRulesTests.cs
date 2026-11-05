using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-TAX-004／007（純規則部分）：稅務申報期間曆、稅點日期唯一歸屬與期間應納／留抵計算
/// （REQ_F4116.17.3、BR-FI-107／108、ADR-082）。不涉及 SQL、cutoff 令牌、修訂或畫面；
/// 期間、已提交稅明細與調整由呼叫端讀到的快照傳入。
/// </summary>
public sealed class TaxReturnPeriodRulesTests
{
    private static readonly DateOnly Jan1 = new(2028, 1, 1);
    private static readonly DateOnly Feb29 = new(2028, 2, 29);

    private static TaxReturnPeriod Period(long id, DateOnly start, DateOnly end, string? policy = "TW-VAT-2028") =>
        new(id, start, end, policy);

    private static readonly TaxReturnPeriod JanFeb = Period(1, Jan1, Feb29);
    private static readonly TaxReturnPeriod MarApr = Period(2, new(2028, 3, 1), new(2028, 4, 30));

    private static TaxReturnSource Src(long id, TaxRole role, decimal signedBaseTax, decimal claimable = 0m,
        DateOnly? date = null) => new(id, role, date ?? new DateOnly(2028, 1, 15), signedBaseTax, claimable);

    private static TaxReturnCalculation Calc(IReadOnlyList<TaxReturnSource>? sources,
        IReadOnlyList<TaxReturnAdjustment>? adjustments = null, TaxReturnPeriod? period = null, int baseDecimals = 0,
        bool nullPeriod = false) =>
        TaxReturnPeriodRules.Calculate(nullPeriod ? null : period ?? JanFeb, baseDecimals, sources,
            adjustments ?? []);

    private static void AssertOnly(TaxReturnCalculation r, string collection, int? index, string field,
        TaxReturnError error)
    {
        Assert.False(r.IsValid);
        Assert.Null(r.Totals);
        var e = Assert.Single(r.Errors);
        Assert.Equal(collection, e.Collection);
        Assert.Equal(index, e.Index);
        Assert.Equal(field, e.Field);
        Assert.Equal(error, e.Error);
    }

    // ---- Calendar ----

    [Fact]
    public void ContiguousBimonthlyCalendarIsValid()
    {
        Assert.Empty(TaxReturnPeriodRules.ValidateCalendar([JanFeb, MarApr]));
    }

    [Fact]
    public void SingleDayPeriodAndCalendarGapAreAllowed()
    {
        var oneDay = Period(3, new(2028, 6, 1), new(2028, 6, 1));
        Assert.Empty(TaxReturnPeriodRules.ValidateCalendar([JanFeb, oneDay]));
    }

    [Fact]
    public void PeriodsSharingOneBoundaryDayOverlap()
    {
        var sharesFeb29 = Period(2, Feb29, new(2028, 4, 30));

        var e = Assert.Single(TaxReturnPeriodRules.ValidateCalendar([JanFeb, sharesFeb29]));

        Assert.Equal(new TaxPeriodCalendarError(1, "startDate", TaxPeriodError.Overlap), e);
    }

    [Fact]
    public void CalendarReportsEachInvalidFieldOnceInIndexAndFieldOrder()
    {
        var errors = TaxReturnPeriodRules.ValidateCalendar([
            Period(1, Jan1, Feb29),
            Period(1, new(2028, 5, 1), new(2028, 4, 30), " "),
            Period(0, new(2028, 2, 1), new(2028, 3, 31)),
            Period(4, new(2028, 7, 1), new(2028, 8, 31), null)
        ]);

        Assert.Equal(
        [
            new TaxPeriodCalendarError(1, "periodId", TaxPeriodError.Duplicate),
            new TaxPeriodCalendarError(1, "endDate", TaxPeriodError.OutOfRange),
            new TaxPeriodCalendarError(1, "policyVersion", TaxPeriodError.PolicyRequired),
            new TaxPeriodCalendarError(2, "periodId", TaxPeriodError.Required),
            new TaxPeriodCalendarError(2, "startDate", TaxPeriodError.Overlap),
            new TaxPeriodCalendarError(3, "policyVersion", TaxPeriodError.PolicyRequired)
        ], errors);
    }

    [Fact]
    public void NullCalendarOrNullEntryIsRequired()
    {
        Assert.Equal([new TaxPeriodCalendarError(null, "periods", TaxPeriodError.Required)],
            TaxReturnPeriodRules.ValidateCalendar(null));
        Assert.Equal([new TaxPeriodCalendarError(1, "period", TaxPeriodError.Required)],
            TaxReturnPeriodRules.ValidateCalendar([JanFeb, null!]));
    }

    // ---- Tax point assignment ----

    [Theory]
    [InlineData(2028, 1, 1, 1)]
    [InlineData(2028, 2, 29, 1)]
    [InlineData(2028, 3, 1, 2)]
    [InlineData(2028, 4, 30, 2)]
    public void TaxPointOnInclusiveBoundaryIsAssignedOnce(int y, int m, int d, long expectedPeriod)
    {
        var a = TaxReturnPeriodRules.AssignTaxPoint(new DateOnly(y, m, d), [MarApr, JanFeb]);

        Assert.Equal(TaxPointAssignmentStatus.Assigned, a.Status);
        Assert.Equal(expectedPeriod, a.PeriodId);
        Assert.Equal("TW-VAT-2028", a.PolicyVersion);
        Assert.Equal([expectedPeriod], a.CandidatePeriodIds);
    }

    [Fact]
    public void MissingTaxPointIsNeverDefaultedFromAnotherDate()
    {
        var a = TaxReturnPeriodRules.AssignTaxPoint(null, [JanFeb]);

        Assert.Equal(TaxPointAssignmentStatus.TaxPointMissing, a.Status);
        Assert.Null(a.PeriodId);
        Assert.Empty(a.CandidatePeriodIds);
    }

    [Theory]
    [InlineData(2027, 12, 31)]
    [InlineData(2028, 5, 1)]
    public void TaxPointOutsideEveryPeriodHasNoPeriod(int y, int m, int d)
    {
        var a = TaxReturnPeriodRules.AssignTaxPoint(new DateOnly(y, m, d), [JanFeb, MarApr]);

        Assert.Equal(TaxPointAssignmentStatus.NoPeriod, a.Status);
        Assert.Null(a.PeriodId);
        Assert.Empty(a.CandidatePeriodIds);
    }

    [Fact]
    public void TaxPointCoveredByTwoPeriodsIsAmbiguousAndNamesBoth()
    {
        var overlapping = Period(9, new(2028, 2, 1), new(2028, 3, 31));

        var a = TaxReturnPeriodRules.AssignTaxPoint(new DateOnly(2028, 2, 10), [overlapping, JanFeb]);

        Assert.Equal(TaxPointAssignmentStatus.Ambiguous, a.Status);
        Assert.Null(a.PeriodId);
        Assert.Equal([1L, 9L], a.CandidatePeriodIds);
    }

    [Fact]
    public void DuplicatedPeriodRowsAreStillAmbiguousNotCollapsedToOne()
    {
        var a = TaxReturnPeriodRules.AssignTaxPoint(Jan1, [JanFeb, Period(1, Jan1, new(2028, 1, 31), "OTHER")]);

        Assert.Equal(TaxPointAssignmentStatus.Ambiguous, a.Status);
        Assert.Null(a.PeriodId);
        Assert.Null(a.PolicyVersion);
        Assert.Equal([1L], a.CandidatePeriodIds);
    }

    [Fact]
    public void SinglePeriodWithoutPolicyIsNotAssigned()
    {
        var a = TaxReturnPeriodRules.AssignTaxPoint(Jan1, [Period(1, Jan1, Feb29, "  ")]);

        Assert.Equal(TaxPointAssignmentStatus.PolicyMissing, a.Status);
        Assert.Null(a.PeriodId);
        Assert.Equal([1L], a.CandidatePeriodIds);
    }

    [Fact]
    public void ReversedOrNullPeriodsAreIgnoredForAssignmentAndPolicyIsTrimmed()
    {
        var reversed = Period(7, Feb29, Jan1);
        var a = TaxReturnPeriodRules.AssignTaxPoint(new DateOnly(2028, 1, 20),
            [reversed, null!, Period(1, Jan1, Feb29, " tw-vat-2028 ")]);

        Assert.Equal(TaxPointAssignmentStatus.Assigned, a.Status);
        Assert.Equal(1, a.PeriodId);
        Assert.Equal("tw-vat-2028", a.PolicyVersion);
    }

    [Fact]
    public void NullCalendarForAssignmentHasNoPeriod()
    {
        Assert.Equal(TaxPointAssignmentStatus.NoPeriod, TaxReturnPeriodRules.AssignTaxPoint(Jan1, null).Status);
    }

    // ---- Calculation ----

    [Fact]
    public void PayableIsOutputMinusClaimableInputPlusApprovedAdjustments()
    {
        var r = Calc(
        [
            Src(10, TaxRole.Output, 1600m),
            Src(11, TaxRole.OutputCredit, -200m),
            Src(12, TaxRole.Input, 500m, claimable: 500m),
            Src(13, TaxRole.Input, 300m, claimable: 0m)
        ],
        [
            new TaxReturnAdjustment(70, 50m, Approved: true),
            new TaxReturnAdjustment(71, -999m, Approved: false)
        ]);

        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
        var t = Assert.IsType<TaxReturnTotals>(r.Totals);
        Assert.Equal(1400m, t.OutputBaseTax);
        Assert.Equal(500m, t.InputClaimableBaseTax);
        Assert.Equal(50m, t.ApprovedAdjustment);
        Assert.Equal(950m, t.Net);
        Assert.Equal(TaxReturnPosition.Payable, t.Position);
        Assert.Equal(950m, t.PayableAmount);
        Assert.Equal(0m, t.CreditAmount);
        Assert.Equal([10L, 11L, 12L, 13L], t.IncludedSourceIds);
        Assert.Equal([70L], t.AppliedAdjustmentIds);
        Assert.Equal([71L], t.PendingAdjustmentIds);
    }

    [Fact]
    public void NegativeNetIsLabelledCreditNotPayable()
    {
        var r = Calc([Src(10, TaxRole.Output, 100m), Src(12, TaxRole.Input, 350.5m, 350.5m)], baseDecimals: 2);

        var t = Assert.IsType<TaxReturnTotals>(r.Totals);
        Assert.Equal(-250.5m, t.Net);
        Assert.Equal(TaxReturnPosition.Credit, t.Position);
        Assert.Equal(0m, t.PayableAmount);
        Assert.Equal(250.5m, t.CreditAmount);
    }

    [Fact]
    public void NilReturnAndExactlyOffsettingReturnAreZero()
    {
        var nil = Assert.IsType<TaxReturnTotals>(Calc([]).Totals);
        Assert.Equal(TaxReturnPosition.Zero, nil.Position);
        Assert.Equal(0m, nil.Net);
        Assert.Empty(nil.IncludedSourceIds);

        var offset = Assert.IsType<TaxReturnTotals>(
            Calc([Src(1, TaxRole.Output, 80m), Src(2, TaxRole.Input, 80m, 80m)]).Totals);
        Assert.Equal(TaxReturnPosition.Zero, offset.Position);
        Assert.Equal(0m, offset.PayableAmount);
        Assert.Equal(0m, offset.CreditAmount);
    }

    [Fact]
    public void SourcesOnBothInclusivePeriodBoundariesAreIncluded()
    {
        var r = Calc([Src(1, TaxRole.Output, 10m, date: Jan1), Src(2, TaxRole.Output, 20m, date: Feb29)]);

        Assert.Equal(30m, r.Totals!.OutputBaseTax);
    }

    [Theory]
    [InlineData(2027, 12, 31)]
    [InlineData(2028, 3, 1)]
    public void SourceOutsideThePeriodIsRejectedNotSilentlyDropped(int y, int m, int d)
    {
        AssertOnly(Calc([Src(1, TaxRole.Output, 10m, date: new DateOnly(y, m, d))]),
            "sources", 0, "taxPointDate", TaxReturnError.OutsidePeriod);
    }

    [Fact]
    public void PeriodMustBeCompleteBeforeCalculation()
    {
        AssertOnly(Calc([], nullPeriod: true), "period", null, "period", TaxReturnError.Required);
        AssertOnly(Calc([], period: Period(0, Jan1, Feb29)), "period", null, "periodId", TaxReturnError.Required);
        AssertOnly(Calc([], period: Period(1, Feb29, Jan1)), "period", null, "endDate", TaxReturnError.OutOfRange);
        AssertOnly(Calc([], period: Period(1, Jan1, Feb29, "")), "period", null, "policyVersion",
            TaxReturnError.PolicyRequired);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void BaseCurrencyDecimalsOutsideZeroToFourAreRejectedWithoutBlamingAmounts(int decimals)
    {
        AssertOnly(Calc([Src(1, TaxRole.Output, 10.12345m)], baseDecimals: decimals),
            "request", null, "baseCurrencyDecimals", TaxReturnError.OutOfRange);
    }

    [Fact]
    public void NullCollectionsAreRequired()
    {
        AssertOnly(Calc(null), "request", null, "sources", TaxReturnError.Required);
        AssertOnly(TaxReturnPeriodRules.Calculate(JanFeb, 0, [], null), "request", null, "adjustments",
            TaxReturnError.Required);
        AssertOnly(Calc([null!]), "sources", 0, "source", TaxReturnError.Required);
        AssertOnly(Calc([], [null!]), "adjustments", 0, "adjustment", TaxReturnError.Required);
    }

    [Theory]
    [InlineData(TaxRole.Output, -1)]
    [InlineData(TaxRole.OutputCredit, 1)]
    [InlineData(TaxRole.Input, -1)]
    public void SignedBaseTaxMustMatchTheRole(TaxRole role, int amount)
    {
        AssertOnly(Calc([Src(1, role, amount)]), "sources", 0, "signedBaseTax", TaxReturnError.SignMismatch);
    }

    [Fact]
    public void UndefinedRoleIsOutOfRangeWithoutSignOrClaimableNoise()
    {
        AssertOnly(Calc([Src(1, (TaxRole)99, -5m, claimable: 3m)]), "sources", 0, "role", TaxReturnError.OutOfRange);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ClaimableInputMustBeWithinItsTax(int claimable)
    {
        var error = claimable < 0 ? TaxReturnError.OutOfRange : TaxReturnError.ExceedsTax;
        AssertOnly(Calc([Src(1, TaxRole.Input, 100m, claimable)]), "sources", 0, "claimableBaseTax", error);
    }

    [Fact]
    public void ClaimableOnOutputIsNotApplicable()
    {
        AssertOnly(Calc([Src(1, TaxRole.Output, 100m, 1m)]), "sources", 0, "claimableBaseTax",
            TaxReturnError.NotApplicable);
    }

    [Fact]
    public void AmountsMustFitBaseScaleAndSqlMaximum()
    {
        AssertOnly(Calc([Src(1, TaxRole.Output, 10.5m)]), "sources", 0, "signedBaseTax", TaxReturnError.InvalidScale);
        AssertOnly(Calc([Src(1, TaxRole.Input, 10m, 9.5m)]), "sources", 0, "claimableBaseTax",
            TaxReturnError.InvalidScale);
        AssertOnly(Calc([Src(1, TaxRole.Output, 1_000_000_000_000_000m)]), "sources", 0, "signedBaseTax",
            TaxReturnError.OutOfRange);
        AssertOnly(Calc([], [new TaxReturnAdjustment(1, 0.5m, true)]), "adjustments", 0, "signedBaseAmount",
            TaxReturnError.InvalidScale);
        Assert.True(Calc([Src(1, TaxRole.Output, 999_999_999_999_999.9999m)], baseDecimals: 4).IsValid);
    }

    [Fact]
    public void ZeroAdjustmentIsRejectedAsMeaningless()
    {
        AssertOnly(Calc([], [new TaxReturnAdjustment(1, 0m, true)]), "adjustments", 0, "signedBaseAmount",
            TaxReturnError.OutOfRange);
    }

    [Fact]
    public void DuplicateAndMissingIdsAreRejected()
    {
        AssertOnly(Calc([Src(5, TaxRole.Output, 1m), Src(5, TaxRole.Output, 1m)]), "sources", 1, "taxDetailId",
            TaxReturnError.Duplicate);
        AssertOnly(Calc([Src(0, TaxRole.Output, 1m)]), "sources", 0, "taxDetailId", TaxReturnError.Required);
        AssertOnly(Calc([], [new(3, 1m, true), new(3, 2m, false)]), "adjustments", 1, "adjustmentId",
            TaxReturnError.Duplicate);
        AssertOnly(Calc([], [new(-1, 1m, true)]), "adjustments", 0, "adjustmentId", TaxReturnError.Required);
    }

    [Fact]
    public void TotalAboveSqlMaximumIsRejectedEvenWhenEachSourceFits()
    {
        const decimal max = 999_999_999_999_999m;
        AssertOnly(Calc([Src(1, TaxRole.Output, max), Src(2, TaxRole.Output, 1m)]), "totals", null, "outputBaseTax",
            TaxReturnError.OutOfRange);
        AssertOnly(Calc([Src(1, TaxRole.Output, max)], [new TaxReturnAdjustment(1, 1m, true)]), "totals", null,
            "net", TaxReturnError.OutOfRange);
        AssertOnly(Calc([Src(1, TaxRole.Input, max, max), Src(2, TaxRole.Input, 1m, 1m)]), "totals", null,
            "inputClaimableBaseTax", TaxReturnError.OutOfRange);
    }

    [Fact]
    public void SeveralInvalidItemsAreEachReportedOnceInOrder()
    {
        var r = Calc(
        [
            Src(1, TaxRole.Output, -1m, date: new DateOnly(2028, 3, 1)),
            Src(1, TaxRole.Input, 10m, 11m)
        ],
        [new TaxReturnAdjustment(0, 0m, true)], baseDecimals: 0);

        Assert.Null(r.Totals);
        Assert.Equal(
        [
            new TaxReturnFieldError("sources", 0, "taxPointDate", TaxReturnError.OutsidePeriod),
            new TaxReturnFieldError("sources", 0, "signedBaseTax", TaxReturnError.SignMismatch),
            new TaxReturnFieldError("sources", 1, "taxDetailId", TaxReturnError.Duplicate),
            new TaxReturnFieldError("sources", 1, "claimableBaseTax", TaxReturnError.ExceedsTax),
            new TaxReturnFieldError("adjustments", 0, "adjustmentId", TaxReturnError.Required),
            new TaxReturnFieldError("adjustments", 0, "signedBaseAmount", TaxReturnError.OutOfRange)
        ], r.Errors);
    }

    [Fact]
    public void InvalidPeriodStillReportsItemErrorsButSkipsPeriodMembership()
    {
        var r = Calc([Src(1, TaxRole.Output, -1m, date: new DateOnly(2030, 1, 1))], period: Period(1, Feb29, Jan1));

        Assert.Equal(
        [
            new TaxReturnFieldError("period", null, "endDate", TaxReturnError.OutOfRange),
            new TaxReturnFieldError("sources", 0, "signedBaseTax", TaxReturnError.SignMismatch)
        ], r.Errors);
    }

    [Fact]
    public void CalculationAgreesWithLineRuleOutputForCreditAndNonRecoverableInput()
    {
        TaxDetailLine Line(TaxDetailLineInput i) => TaxDetailLineRules.Validate(i).Detail!;
        var credit = Line(new(TaxRole.OutputCredit, 1, "VAT5", 0, 40m, 2m, 42m, 0, 40m, 2m, 42m, null, 501, null,
            new TaxCreditRemaining(100m, 5m, 100m, 5m)));
        var nonRecoverable = Line(new(TaxRole.Input, 2, "VAT5", 0, 100m, 5m, 105m, 0, 100m, 5m, 105m,
            TaxTreatment.NonRecoverable, null, 777, null));

        var r = Calc(
        [
            Src(1, credit.Role, credit.SignedBaseTax, credit.ClaimableBaseTax),
            Src(2, nonRecoverable.Role, nonRecoverable.SignedBaseTax, nonRecoverable.ClaimableBaseTax)
        ]);

        var t = Assert.IsType<TaxReturnTotals>(r.Totals);
        Assert.Equal(-2m, t.OutputBaseTax);
        Assert.Equal(0m, t.InputClaimableBaseTax);
        Assert.Equal(TaxReturnPosition.Credit, t.Position);
        Assert.Equal(2m, t.CreditAmount);
    }
}
