using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>WU-SD-006b0 offline credit decision rules (SD-CREDIT-U01～U14, BR-SD-031～034).</summary>
public sealed class SalesCreditRulesTests
{
    private const decimal Max = 999_999_999_999_999.9999m;
    private static readonly DateOnly Today = new(2028, 2, 29);
    private static readonly DateTime Observed = new(2028, 2, 29, 10, 0, 0);

    private static CreditSnapshot Snapshot(decimal limit = 1000m, decimal ar = 300m, string currency = "TWD",
        long company = 1, long customer = 50, DateOnly? date = null, int version = 3) =>
        CreditSnapshot.Available(company, customer, currency, limit, ar, date ?? Today, version, Observed);

    private static SalesCreditCheckInput Input(decimal candidate = 200m, bool active = true,
        string? baseCurrency = "TWD", long orderId = 9, params SalesCreditCommitment[] commitments) =>
        new(1, 50, Today, active, baseCurrency, orderId, candidate, commitments);

    // SD-CREDIT-U01 (SD-CREDIT-001)
    [Fact]
    public void WithinLimitReturnsComponentsAndHeadroom()
    {
        var result = SalesCreditRules.Evaluate(Input(200m, commitments: [new(7, 100m), new(8, 50m)]), Snapshot());

        result.Status.Should().Be(SalesCreditStatus.WithinLimit);
        result.ErrorCode.Should().BeNull();
        result.BaseCurrency.Should().Be("TWD");
        result.Limit.Should().Be(1000m);
        result.PostedOpenAr.Should().Be(300m);
        result.OtherCommitments.Should().Be(150m);
        result.Candidate.Should().Be(200m);
        result.Headroom.Should().Be(350m);
        result.Excess.Should().Be(0m);
        result.LimitVersion.Should().Be(3);
        result.EvaluationDate.Should().Be(Today);
    }

    // SD-CREDIT-U02 (SD-CREDIT-004)
    [Fact]
    public void ExactZeroHeadroomPassesAndSmallestUnitBeyondRefusesWithExactExcess()
    {
        SalesCreditRules.Evaluate(Input(700m), Snapshot()).Status.Should().Be(SalesCreditStatus.WithinLimit);

        var over = SalesCreditRules.Evaluate(Input(700.0001m), Snapshot());
        over.Status.Should().Be(SalesCreditStatus.OverLimit);
        over.ErrorCode.Should().Be(SalesCreditErrors.LimitExceeded);
        over.Headroom.Should().Be(-0.0001m);
        over.Excess.Should().Be(0.0001m);
    }

    // SD-CREDIT-U03 (SD-CREDIT-004)
    [Fact]
    public void ZeroLimitIsValidButRefusesPositiveExposure()
    {
        SalesCreditRules.Evaluate(Input(0m), Snapshot(limit: 0m, ar: 0m)).Status
            .Should().Be(SalesCreditStatus.WithinLimit);
        var over = SalesCreditRules.Evaluate(Input(0.0001m), Snapshot(limit: 0m, ar: 0m));
        over.Status.Should().Be(SalesCreditStatus.OverLimit);
        over.Excess.Should().Be(0.0001m);
    }

    // SD-CREDIT-U04 (SD-CREDIT-007)
    [Fact]
    public void CandidateListedAmongCommitmentsIsCountedOnce()
    {
        var result = SalesCreditRules.Evaluate(Input(200m, orderId: 9, commitments: [new(9, 200m), new(7, 100m)]),
            Snapshot());
        result.OtherCommitments.Should().Be(100m);
        result.Headroom.Should().Be(400m);
    }

    // SD-CREDIT-U05 (SD-CREDIT-016, SD-CREDIT-020)
    [Fact]
    public void InactiveCustomerIsBlockedBeforeCreditFigures()
    {
        var result = SalesCreditRules.Evaluate(Input(active: false), Snapshot());
        result.Status.Should().Be(SalesCreditStatus.Blocked);
        result.ErrorCode.Should().Be(SalesCreditErrors.CustomerInactive);
        result.Limit.Should().BeNull();
        result.Headroom.Should().BeNull();
    }

    // SD-CREDIT-U06 (SD-CREDIT-016, BR-SD-031)
    [Theory]
    [InlineData(null, SalesCreditErrors.Unavailable)]
    [InlineData(CreditUnavailableReason.MissingLimit, SalesCreditErrors.LimitNotSet)]
    [InlineData(CreditUnavailableReason.AmbiguousLimit, SalesCreditErrors.InvalidSnapshot)]
    [InlineData(CreditUnavailableReason.MissingBaseCurrency, SalesCreditErrors.Unavailable)]
    [InlineData(CreditUnavailableReason.CustomerUnavailable, SalesCreditErrors.Unavailable)]
    [InlineData(CreditUnavailableReason.ExposureUnavailable, SalesCreditErrors.Unavailable)]
    public void MissingOrUnavailableSnapshotFailsClosedWithNamedReason(CreditUnavailableReason? reason, string code)
    {
        var snapshot = reason is null ? null : CreditSnapshot.Unavailable(1, 50, Today, reason.Value);
        var result = SalesCreditRules.Evaluate(Input(), snapshot);
        result.Status.Should().Be(SalesCreditStatus.Blocked);
        result.ErrorCode.Should().Be(code);
        result.Limit.Should().BeNull();
    }

    // SD-CREDIT-U07 (SD-CREDIT-005, SD-CREDIT-009)
    [Fact]
    public void SnapshotOfAnotherCompanyCustomerOrDateIsInvalid()
    {
        foreach (var snapshot in new[]
                 {
                     Snapshot(company: 2), Snapshot(customer: 51), Snapshot(date: Today.AddDays(-1)),
                     CreditSnapshot.Unavailable(2, 50, Today, CreditUnavailableReason.MissingLimit),
                 })
        {
            var result = SalesCreditRules.Evaluate(Input(), snapshot);
            result.Status.Should().Be(SalesCreditStatus.Blocked);
            result.ErrorCode.Should().Be(SalesCreditErrors.InvalidSnapshot);
        }
    }

    // SD-CREDIT-U08 (SD-CREDIT-006)
    [Fact]
    public void CurrencyMismatchIsInvalidAndMissingBaseCurrencyIsUnavailable()
    {
        SalesCreditRules.Evaluate(Input(), Snapshot(currency: "USD")).ErrorCode
            .Should().Be(SalesCreditErrors.InvalidSnapshot);
        SalesCreditRules.Evaluate(Input(baseCurrency: null), Snapshot()).ErrorCode
            .Should().Be(SalesCreditErrors.Unavailable);
        SalesCreditRules.Evaluate(Input(baseCurrency: "twd"), Snapshot()).ErrorCode
            .Should().Be(SalesCreditErrors.Unavailable);
    }

    // SD-CREDIT-U09 (SD-CREDIT-006)
    [Theory]
    [InlineData(-0.0001)]
    [InlineData(0.00001)]
    [InlineData(1_000_000_000_000_000)]
    public void InvalidCandidateAmountIsBlocked(double candidate)
    {
        var result = SalesCreditRules.Evaluate(Input((decimal)candidate), Snapshot());
        result.Status.Should().Be(SalesCreditStatus.Blocked);
        result.ErrorCode.Should().Be(SalesCreditErrors.InvalidAmount);
    }

    // SD-CREDIT-U10 (SD-CREDIT-006, SD-CREDIT-007)
    [Fact]
    public void InvalidOrDuplicateCommitmentsAndOverflowAreBlocked()
    {
        SalesCreditCommitment[][] cases =
        [
            [new(7, -1m)], [new(7, 1.00001m)], [new(0, 1m)], [new(7, 1m), new(7, 2m)], [new(7, Max), new(8, 1m)],
        ];
        foreach (var commitments in cases)
        {
            var result = SalesCreditRules.Evaluate(Input(commitments: commitments), Snapshot());
            result.Status.Should().Be(SalesCreditStatus.Blocked);
            result.ErrorCode.Should().Be(SalesCreditErrors.InvalidAmount);
        }

        SalesCreditRules.Evaluate(Input(Max), Snapshot(limit: Max, ar: 0m)).Status
            .Should().Be(SalesCreditStatus.WithinLimit);
        SalesCreditRules.Evaluate(Input(1m), Snapshot(limit: Max, ar: Max)).ErrorCode
            .Should().Be(SalesCreditErrors.InvalidAmount);
    }

    // SD-CREDIT-U11 (SD-CREDIT-001, SD-CREDIT-019)
    [Fact]
    public void DecideWithinLimitIgnoresOverrideReasonAndNeedsNoGrant()
    {
        var decision = SalesCreditRules.Decide(SalesCreditRules.Evaluate(Input(), Snapshot()), "  please  ", false);
        decision.IsOverride.Should().BeFalse();
        decision.OverrideReason.Should().BeNull();
        decision.Assessment.Status.Should().Be(SalesCreditStatus.WithinLimit);
    }

    // SD-CREDIT-U12 (SD-CREDIT-019, SD-CREDIT-021)
    [Fact]
    public void DecideOverLimitWithoutReasonIs422WithExcess()
    {
        var assessment = SalesCreditRules.Evaluate(Input(800m), Snapshot());
        var act = () => SalesCreditRules.Decide(assessment, null, true);
        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.ErrorCode.Should().Be(SalesCreditErrors.LimitExceeded);
        ex.Extensions!["excess"].Should().Be(100m);
        ex.Extensions!["currency"].Should().Be("TWD");
    }

    // SD-CREDIT-U13 (SD-CREDIT-019, SD-CREDIT-020)
    [Fact]
    public void DecideOverLimitOverrideNeedsGrantThenValidReason()
    {
        var assessment = SalesCreditRules.Evaluate(Input(800m), Snapshot());

        var forbidden = () => SalesCreditRules.Decide(assessment, "VIP customer", false);
        forbidden.Should().Throw<ForbiddenException>().Which.ErrorCode.Should().Be(SalesCreditErrors.OverrideForbidden);

        foreach (var reason in new[] { "", "   ", new string('x', SalesCreditRules.OverrideReasonMaxLength + 1) })
        {
            var invalid = () => SalesCreditRules.Decide(assessment, reason, true);
            invalid.Should().Throw<RequestValidationException>()
                .Which.Errors.Should().ContainKey("creditOverrideReason");
        }

        var decision = SalesCreditRules.Decide(assessment, "  VIP customer  ", true);
        decision.IsOverride.Should().BeTrue();
        decision.OverrideReason.Should().Be("VIP customer");
        decision.Assessment.Excess.Should().Be(100m);
        SalesCreditRules.Decide(assessment, new string('x', SalesCreditRules.OverrideReasonMaxLength), true)
            .IsOverride.Should().BeTrue();
    }

    // SD-CREDIT-U14 (SD-CREDIT-016, SD-CREDIT-020)
    [Fact]
    public void BlockedAssessmentCannotBeOverriddenEvenWithGrantAndReason()
    {
        foreach (var assessment in new[]
                 {
                     SalesCreditRules.Evaluate(Input(active: false), Snapshot()),
                     SalesCreditRules.Evaluate(Input(), null),
                     SalesCreditRules.Evaluate(Input(), Snapshot(currency: "USD")),
                 })
        {
            var act = () => SalesCreditRules.Decide(assessment, "VIP customer", true);
            act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(assessment.ErrorCode);
        }
    }
}
