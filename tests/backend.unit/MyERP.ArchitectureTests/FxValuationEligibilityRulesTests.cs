using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FX-POL（純規則部分）：期末估值須有同公司唯一已核准、生效涵蓋估值日的政策版本（匯率來源、匯率類型、匯率精度、
/// 適格貨幣性科目映射、損益科目），期末匯率須同來源／類型／幣別對、日期等於估值日且為核准快照；逐敞口回報原因，
/// 草稿預覽不得允許過帳（REQ_F4116.18.4、BR-FI-119、ADR-086）。不涉及 SQL、MD 讀取、cutoff、GL 或畫面。
/// </summary>
public sealed class FxValuationEligibilityRulesTests
{
    private const long Company = 1;
    private const long ArControl = 1130;
    private const long ApControl = 2170;
    private static readonly DateOnly PeriodEnd = new(2026, 10, 31);

    private static FxValuationPolicy Policy(long id = 10, int version = 1, long company = Company,
        FxPolicyStatus status = FxPolicyStatus.Approved, string? source = "BOT", string? rateType = "CLOSING",
        int rateDecimals = 4, DateOnly? from = null, DateOnly? to = null,
        IReadOnlyList<FxEligibleAccount>? accounts = null, long gain = 7110, long loss = 7510) =>
        new(id, version, company, status, source, rateType, rateDecimals, from ?? new DateOnly(2026, 1, 1), to,
            accounts ?? [new(FxExposureSide.Receivable, ArControl), new(FxExposureSide.Payable, ApControl)],
            gain, loss);

    private static FxRateSnapshot Rate(long id = 501, decimal rate = 31.25m, DateOnly? date = null,
        string? from = "USD", string? to = "TWD", string? source = "BOT", string? type = "CLOSING",
        bool approved = true) =>
        new(id, source, type, from, to, date ?? PeriodEnd, rate, approved);

    private static FxValuationCandidate Candidate(long id = 7, FxExposureSide side = FxExposureSide.Receivable,
        string? doc = "USD", string? @base = "TWD", long company = Company, long? account = null) =>
        new(new FxExposureSnapshot(id, side, doc, @base, 2, 0, 1000m, 30.5m, 30500m), company,
            account ?? (side == FxExposureSide.Receivable ? ArControl : ApControl));

    private static readonly IReadOnlyList<FxCurrencySnapshot> Currencies =
        [new("TWD", true), new("USD", true), new("EUR", true), new("JPY", false)];

    private static FxValuationEligibilityRequest Request(FxValuationMode mode = FxValuationMode.Post,
        long company = Company, string? baseCurrency = "TWD", DateOnly? date = null) =>
        new(company, baseCurrency, date ?? PeriodEnd, mode);

    private static FxValuationEligibilityResult Evaluate(
        IReadOnlyList<FxValuationPolicy>? policies = null,
        IReadOnlyList<FxRateSnapshot>? rates = null,
        IReadOnlyList<FxValuationCandidate>? candidates = null,
        FxValuationEligibilityRequest? request = null,
        IReadOnlyList<FxCurrencySnapshot>? currencies = null)
    {
        var c = FxValuationEligibilityRules.Evaluate(request ?? Request(), policies ?? [Policy()],
            currencies ?? Currencies, rates ?? [Rate()], candidates ?? [Candidate()]);
        Assert.Empty(c.Errors);
        Assert.True(c.IsValid);
        return c.Result!;
    }

    private static FxExposureEligibility Single(FxValuationEligibilityResult r) => Assert.Single(r.Exposures);

    private static void AssertExcluded(FxExposureEligibility e, params FxValuationExclusion[] reasons)
    {
        Assert.False(e.IsEligible);
        Assert.Null(e.ClosingRate);
        Assert.Null(e.RateId);
        Assert.Equal(reasons, e.Reasons);
    }

    // ---- Normal ----

    [Fact]
    public void Single_approved_policy_and_exact_date_rate_make_exposure_eligible_with_closing_rate()
    {
        var r = Evaluate();

        Assert.Equal(FxPolicyResolution.Resolved, r.PolicyResolution);
        Assert.Equal(10, r.Policy!.PolicyId);
        Assert.Empty(r.PolicyErrors);
        var e = Single(r);
        Assert.Equal(7, e.ExposureId);
        Assert.True(e.IsEligible);
        Assert.Empty(e.Reasons);
        Assert.Equal(new FxClosingRate("USD", "TWD", 31.25m), e.ClosingRate);
        Assert.Equal(501, e.RateId);
        Assert.Equal(1, r.EligibleCount);
        Assert.Equal(0, r.ExcludedCount);
        Assert.True(r.PostingAllowed);
        Assert.Equal(7110, r.Policy.GainAccountId);
        Assert.Equal(7510, r.Policy.LossAccountId);
    }

    [Fact]
    public void Eligible_closing_rate_feeds_unrealised_overlay_valuation()
    {
        var e = Single(Evaluate());
        var exposure = Candidate().Exposure!;

        var v = FxUnrealisedOverlayRules.Value(exposure, new FxDischargedToDate(0m, 0m), FxOverlayState.None,
            e.ClosingRate);

        Assert.True(v.IsValid);
        Assert.Equal(31250m, v.Result!.TargetBase);
        Assert.Equal(750m, v.Result.UnrealisedDifference);
    }

    [Fact]
    public void Payable_uses_payable_mapping_and_each_currency_pair_gets_its_own_rate()
    {
        var r = Evaluate(
            rates: [Rate(), Rate(id: 502, rate: 34.1234m, from: "EUR")],
            candidates: [Candidate(), Candidate(id: 8, side: FxExposureSide.Payable, doc: "EUR")]);

        Assert.All(r.Exposures, x => Assert.True(x.IsEligible));
        Assert.Equal(502, r.Exposures[1].RateId);
        Assert.Equal(new FxClosingRate("EUR", "TWD", 34.1234m), r.Exposures[1].ClosingRate);
        Assert.Equal(2, r.EligibleCount);
    }

    [Fact]
    public void Other_company_draft_retired_and_out_of_range_versions_are_ignored()
    {
        var r = Evaluate(policies:
        [
            Policy(id: 11, company: 2),
            Policy(id: 12, status: FxPolicyStatus.Draft),
            Policy(id: 13, status: FxPolicyStatus.Retired),
            Policy(id: 14, from: new DateOnly(2026, 11, 1)),
            Policy(id: 15, from: new DateOnly(2025, 1, 1), to: new DateOnly(2026, 10, 30)),
            Policy(id: 10, version: 3, to: new DateOnly(2026, 10, 31))
        ]);

        Assert.Equal(FxPolicyResolution.Resolved, r.PolicyResolution);
        Assert.Equal(3, r.Policy!.Version);
        Assert.True(Single(r).IsEligible);
    }

    [Fact]
    public void Rates_with_other_source_type_pair_or_future_date_are_not_used()
    {
        var r = Evaluate(rates:
        [
            Rate(id: 1, source: "ECB"),
            Rate(id: 2, type: "SPOT"),
            Rate(id: 3, from: "EUR"),
            Rate(id: 4, to: "USD", from: "TWD"),
            Rate(id: 5, date: PeriodEnd.AddDays(1)),
            Rate(id: 6, rate: 31.5m)
        ]);

        var e = Single(r);
        Assert.True(e.IsEligible);
        Assert.Equal(6, e.RateId);
    }

    // ---- Policy resolution ----

    [Fact]
    public void No_policy_excludes_every_exposure_with_policy_missing()
    {
        var r = Evaluate(policies: [Policy(status: FxPolicyStatus.Draft)],
            candidates: [Candidate(), Candidate(id: 8)]);

        Assert.Equal(FxPolicyResolution.Missing, r.PolicyResolution);
        Assert.Null(r.Policy);
        Assert.All(r.Exposures, x => AssertExcluded(x, FxValuationExclusion.PolicyMissing));
        Assert.Equal(0, r.EligibleCount);
        Assert.Equal(2, r.ExcludedCount);
        Assert.False(r.PostingAllowed);
    }

    [Theory]
    [InlineData(10, 1, 10, 2)]
    [InlineData(10, 1, 20, 1)]
    public void Two_approved_versions_covering_the_date_are_ambiguous(long id1, int v1, long id2, int v2)
    {
        var r = Evaluate(policies: [Policy(id: id1, version: v1), Policy(id: id2, version: v2)]);

        Assert.Equal(FxPolicyResolution.Ambiguous, r.PolicyResolution);
        Assert.Null(r.Policy);
        AssertExcluded(Single(r), FxValuationExclusion.PolicyAmbiguous);
        Assert.False(r.PostingAllowed);
    }

    [Theory]
    [InlineData("AVERAGE")]
    [InlineData("BUDGET")]
    public void Policy_rate_type_not_supported_for_valuation_excludes_with_unsupported_rate_type(string type)
    {
        var r = Evaluate(policies: [Policy(rateType: type)], rates: [Rate(type: type)]);

        Assert.Equal(FxPolicyResolution.UnsupportedRateType, r.PolicyResolution);
        Assert.Null(r.Policy);
        AssertExcluded(Single(r), FxValuationExclusion.UnsupportedRateType);
    }

    [Fact]
    public void Spot_rate_type_is_supported()
    {
        var r = Evaluate(policies: [Policy(rateType: "SPOT")], rates: [Rate(type: "SPOT")]);

        Assert.True(Single(r).IsEligible);
    }

    [Fact]
    public void Invalid_policy_content_reports_every_field_and_excludes_with_policy_invalid()
    {
        var r = Evaluate(policies:
        [
            Policy(source: " ", rateDecimals: 9, accounts: [], gain: 0, loss: -1)
        ]);

        Assert.Equal(FxPolicyResolution.Invalid, r.PolicyResolution);
        Assert.Null(r.Policy);
        Assert.Equal(
            [
                new("policy", null, "rateSource", FxAllocationError.Required),
                new("policy", null, "rateDecimals", FxAllocationError.OutOfRange),
                new("policy", null, "eligibleAccounts", FxAllocationError.Required),
                new("policy", null, "gainAccountId", FxAllocationError.Required),
                new("policy", null, "lossAccountId", FxAllocationError.Required)
            ],
            r.PolicyErrors);
        AssertExcluded(Single(r), FxValuationExclusion.PolicyInvalid);
    }

    [Fact]
    public void Policy_with_duplicate_or_invalid_account_mapping_is_invalid()
    {
        var r = Evaluate(policies:
        [
            Policy(rateType: "closing", accounts:
            [
                new(FxExposureSide.Receivable, ArControl),
                new(FxExposureSide.Receivable, ArControl),
                new(FxExposureSide.Payable, 0)
            ])
        ]);

        Assert.Equal(FxPolicyResolution.Invalid, r.PolicyResolution);
        Assert.Equal(
            [
                new("policy", null, "rateType", FxAllocationError.InvalidFormat),
                new("policyAccounts", 1, "controlAccountId", FxAllocationError.Duplicate),
                new("policyAccounts", 2, "controlAccountId", FxAllocationError.Required)
            ],
            r.PolicyErrors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void Rate_decimals_zero_to_eight_are_valid(int decimals)
    {
        var r = Evaluate(policies: [Policy(rateDecimals: decimals)], rates: [Rate(rate: 31m)]);

        Assert.True(Single(r).IsEligible);
    }

    // ---- Rate selection ----

    [Fact]
    public void No_rate_for_pair_is_missing_rate()
    {
        var r = Evaluate(rates: [Rate(from: "EUR")]);

        AssertExcluded(Single(r), FxValuationExclusion.MissingRate);
        Assert.Equal(1, r.ExcludedCount);
        Assert.False(r.PostingAllowed);
    }

    [Fact]
    public void Only_earlier_dated_approved_rate_is_stale_not_reused()
    {
        var r = Evaluate(rates: [Rate(date: PeriodEnd.AddDays(-1))]);

        AssertExcluded(Single(r), FxValuationExclusion.StaleRate);
    }

    [Fact]
    public void Exact_date_rate_that_is_not_approved_is_reported_as_not_approved()
    {
        var r = Evaluate(rates: [Rate(approved: false), Rate(id: 2, date: PeriodEnd.AddDays(-1))]);

        AssertExcluded(Single(r), FxValuationExclusion.RateNotApproved);
    }

    [Fact]
    public void Two_approved_exact_date_rates_are_ambiguous_even_with_same_value()
    {
        var r = Evaluate(rates: [Rate(), Rate(id: 502)]);

        AssertExcluded(Single(r), FxValuationExclusion.AmbiguousRate);
    }

    [Fact]
    public void Approved_rate_wins_over_unapproved_duplicate_on_same_date()
    {
        var e = Single(Evaluate(rates: [Rate(id: 1, rate: 99m, approved: false), Rate(id: 2)]));

        Assert.True(e.IsEligible);
        Assert.Equal(2, e.RateId);
    }

    [Theory]
    [InlineData(4, 31.12345)]
    [InlineData(0, 31.5)]
    public void Rate_beyond_policy_precision_is_excluded(int decimals, decimal rate)
    {
        var r = Evaluate(policies: [Policy(rateDecimals: decimals)], rates: [Rate(rate: rate)]);

        AssertExcluded(Single(r), FxValuationExclusion.RatePrecisionExceeded);
    }

    [Fact]
    public void Rate_at_policy_precision_is_accepted()
    {
        var e = Single(Evaluate(policies: [Policy(rateDecimals: 4)], rates: [Rate(rate: 31.1234m)]));

        Assert.True(e.IsEligible);
        Assert.Equal(31.1234m, e.ClosingRate!.Rate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_rate_is_invalid_rate(decimal rate)
    {
        AssertExcluded(Single(Evaluate(rates: [Rate(rate: rate)])), FxValuationExclusion.InvalidRate);
    }

    [Fact]
    public void Rate_above_decimal_19_8_is_invalid_rate()
    {
        var r = Evaluate(policies: [Policy(rateDecimals: 0)], rates: [Rate(rate: 100000000000m)]);

        AssertExcluded(Single(r), FxValuationExclusion.InvalidRate);
    }

    [Fact]
    public void Leap_day_period_end_requires_leap_day_rate()
    {
        var leap = new DateOnly(2028, 2, 29);
        var r = Evaluate(request: Request(date: leap),
            rates: [Rate(id: 1, date: new DateOnly(2028, 2, 28)), Rate(id: 2, date: leap)]);

        Assert.Equal(2, Single(r).RateId);
    }

    // ---- Exposure relation reasons ----

    [Fact]
    public void Base_currency_source_is_excluded_as_base_currency_source()
    {
        AssertExcluded(Single(Evaluate(candidates: [Candidate(doc: "TWD")])),
            FxValuationExclusion.BaseCurrencySource);
    }

    [Fact]
    public void Inactive_or_unknown_currency_is_excluded()
    {
        var r = Evaluate(
            rates: [Rate(from: "JPY"), Rate(id: 2, from: "GBP")],
            candidates: [Candidate(doc: "JPY"), Candidate(id: 8, doc: "GBP")]);

        AssertExcluded(r.Exposures[0], FxValuationExclusion.InactiveCurrency);
        AssertExcluded(r.Exposures[1], FxValuationExclusion.InactiveCurrency);
    }

    [Fact]
    public void Inactive_base_currency_is_excluded()
    {
        var r = Evaluate(currencies: [new("TWD", false), new("USD", true)]);

        AssertExcluded(Single(r), FxValuationExclusion.InactiveCurrency);
    }

    [Fact]
    public void Account_not_in_mapping_or_mapped_for_other_side_is_not_eligible()
    {
        var r = Evaluate(candidates:
        [
            Candidate(account: 1190),
            Candidate(id: 8, account: ApControl)
        ]);

        AssertExcluded(r.Exposures[0], FxValuationExclusion.AccountNotEligible);
        AssertExcluded(r.Exposures[1], FxValuationExclusion.AccountNotEligible);
    }

    [Fact]
    public void Other_company_or_base_currency_exposure_is_source_mismatch()
    {
        var r = Evaluate(candidates: [Candidate(company: 2), Candidate(id: 8, @base: "USD", doc: "EUR")],
            rates: [Rate(), Rate(id: 2, from: "EUR", to: "USD")]);

        AssertExcluded(r.Exposures[0], FxValuationExclusion.CompanyMismatch);
        AssertExcluded(r.Exposures[1], FxValuationExclusion.BaseCurrencyMismatch);
    }

    [Fact]
    public void Every_applicable_reason_is_reported_in_fixed_order_and_rate_is_skipped_after_relation_failure()
    {
        var r = Evaluate(candidates: [Candidate(company: 2, doc: "JPY", account: 1)], rates: []);

        AssertExcluded(Single(r),
            FxValuationExclusion.CompanyMismatch,
            FxValuationExclusion.InactiveCurrency,
            FxValuationExclusion.AccountNotEligible);
    }

    [Fact]
    public void Mixed_candidates_have_exact_counts_and_order_is_kept()
    {
        var r = Evaluate(
            rates: [Rate(), Rate(id: 2, from: "EUR", date: PeriodEnd.AddDays(-3))],
            candidates:
            [
                Candidate(id: 3),
                Candidate(id: 1, doc: "TWD"),
                Candidate(id: 2, doc: "EUR"),
                Candidate(id: 9, side: FxExposureSide.Payable)
            ]);

        Assert.Equal([3L, 1L, 2L, 9L], r.Exposures.Select(x => x.ExposureId));
        Assert.Equal([true, false, false, true], r.Exposures.Select(x => x.IsEligible));
        Assert.Equal(FxValuationExclusion.BaseCurrencySource, Assert.Single(r.Exposures[1].Reasons));
        Assert.Equal(FxValuationExclusion.StaleRate, Assert.Single(r.Exposures[2].Reasons));
        Assert.Equal(2, r.EligibleCount);
        Assert.Equal(2, r.ExcludedCount);
        // BR-FI-117: each exposure is its own atomic result, so eligible ones may post while others are reported.
        Assert.True(r.PostingAllowed);
    }

    [Fact]
    public void Empty_candidate_set_has_zero_counts_and_does_not_allow_posting()
    {
        var r = Evaluate(candidates: []);

        Assert.Equal(FxPolicyResolution.Resolved, r.PolicyResolution);
        Assert.Empty(r.Exposures);
        Assert.Equal(0, r.EligibleCount);
        Assert.Equal(0, r.ExcludedCount);
        Assert.False(r.PostingAllowed);
    }

    // ---- Draft preview ----

    [Fact]
    public void Preview_reports_eligibility_but_never_allows_posting()
    {
        var r = Evaluate(request: Request(FxValuationMode.Preview));

        Assert.True(Single(r).IsEligible);
        Assert.Equal(FxValuationMode.Preview, r.Mode);
        Assert.False(r.PostingAllowed);
    }

    [Fact]
    public void Preview_does_not_accept_a_draft_policy()
    {
        var r = Evaluate(request: Request(FxValuationMode.Preview),
            policies: [Policy(status: FxPolicyStatus.Draft)]);

        Assert.Equal(FxPolicyResolution.Missing, r.PolicyResolution);
        AssertExcluded(Single(r), FxValuationExclusion.PolicyMissing);
    }

    // ---- Input errors ----

    [Fact]
    public void Null_inputs_are_required_in_order()
    {
        var c = FxValuationEligibilityRules.Evaluate(null, null, null, null, null);

        Assert.False(c.IsValid);
        Assert.Null(c.Result);
        Assert.Equal(
            [
                new("request", null, "request", FxAllocationError.Required),
                new("policies", null, "policies", FxAllocationError.Required),
                new("currencies", null, "currencies", FxAllocationError.Required),
                new("rates", null, "rates", FxAllocationError.Required),
                new("candidates", null, "candidates", FxAllocationError.Required)
            ],
            c.Errors);
    }

    [Fact]
    public void Invalid_request_fields_are_rejected()
    {
        var c = FxValuationEligibilityRules.Evaluate(new(0, "twd", default, (FxValuationMode)9),
            [Policy()], Currencies, [Rate()], [Candidate()]);

        Assert.False(c.IsValid);
        Assert.Equal(
            [
                new("request", null, "companyId", FxAllocationError.Required),
                new("request", null, "baseCurrency", FxAllocationError.InvalidFormat),
                new("request", null, "valuationDate", FxAllocationError.Required),
                new("request", null, "mode", FxAllocationError.OutOfRange)
            ],
            c.Errors);
    }

    [Fact]
    public void Null_items_and_duplicate_keys_are_rejected_with_index()
    {
        var c = FxValuationEligibilityRules.Evaluate(Request(),
            [Policy(), null!, Policy()],
            [new("USD", true), new("USD", false), null!],
            [Rate(), Rate(), null!],
            [Candidate(), Candidate(), new(null, Company, ArControl), null!]);

        Assert.False(c.IsValid);
        Assert.Equal(
            [
                new("policies", 1, "policy", FxAllocationError.Required),
                new("policies", 2, "policyId", FxAllocationError.Duplicate),
                new("currencies", 1, "code", FxAllocationError.Duplicate),
                new("currencies", 2, "currency", FxAllocationError.Required),
                new("rates", 1, "rateId", FxAllocationError.Duplicate),
                new("rates", 2, "rate", FxAllocationError.Required),
                new("candidates", 1, "exposureId", FxAllocationError.Duplicate),
                new("candidates", 2, "exposure", FxAllocationError.Required),
                new("candidates", 3, "candidate", FxAllocationError.Required)
            ],
            c.Errors);
    }

    // ---- Supplementary (review) ----

    [Fact]
    public void Reversed_effective_range_is_an_input_error_not_a_silently_ignored_version()
    {
        var c = FxValuationEligibilityRules.Evaluate(Request(),
            [Policy(from: new DateOnly(2026, 11, 1), to: new DateOnly(2026, 10, 1))], Currencies, [Rate()],
            [Candidate()]);

        Assert.False(c.IsValid);
        Assert.Equal([new FxAllocationFieldError("policies", 0, "effectiveTo", FxAllocationError.OutOfRange)],
            c.Errors);
    }

    [Theory]
    [InlineData(2026, 10, 31, null)]
    [InlineData(2026, 10, 1, 2026, 10, 31)]
    public void Effective_range_is_inclusive_on_both_ends(int fy, int fm, int fd, int? ty = null, int? tm = null,
        int? td = null)
    {
        var to = ty is null ? (DateOnly?)null : new DateOnly(ty.Value, tm!.Value, td!.Value);
        var r = Evaluate(policies: [Policy(from: new DateOnly(fy, fm, fd), to: to)]);

        Assert.Equal(FxPolicyResolution.Resolved, r.PolicyResolution);
    }

    [Fact]
    public void Invalid_content_wins_over_unsupported_rate_type()
    {
        var r = Evaluate(policies: [Policy(rateType: "AVERAGE", gain: 0)]);

        Assert.Equal(FxPolicyResolution.Invalid, r.PolicyResolution);
        Assert.Equal([new FxAllocationFieldError("policy", null, "gainAccountId", FxAllocationError.Required)],
            r.PolicyErrors);
    }

    [Fact]
    public void Same_policy_id_and_version_across_companies_is_duplicate_input()
    {
        var c = FxValuationEligibilityRules.Evaluate(Request(), [Policy(), Policy(company: 2)], Currencies,
            [Rate()], [Candidate()]);

        Assert.Equal([new FxAllocationFieldError("policies", 1, "policyId", FxAllocationError.Duplicate)], c.Errors);
    }

    [Fact]
    public void Invalid_candidate_ids_side_company_and_account_are_rejected()
    {
        var bad = new FxValuationCandidate(
            new FxExposureSnapshot(0, (FxExposureSide)5, "USD", "TWD", 2, 0, 1000m, 30.5m, 30500m), 0, 0);

        var c = FxValuationEligibilityRules.Evaluate(Request(), [Policy()], Currencies, [Rate()], [bad]);

        Assert.Equal(
            [
                new("candidates", 0, "exposureId", FxAllocationError.Required),
                new("candidates", 0, "side", FxAllocationError.OutOfRange),
                new("candidates", 0, "companyId", FxAllocationError.Required),
                new("candidates", 0, "controlAccountId", FxAllocationError.Required)
            ],
            c.Errors);
    }

    [Fact]
    public void Many_candidates_and_rates_resolve_each_pair_independently()
    {
        string[] codes = ["AUD", "CAD", "CHF", "EUR", "GBP", "HKD", "SGD", "USD"];
        var currencies = codes.Append("TWD").Select(x => new FxCurrencySnapshot(x, true)).ToList();
        var rates = codes.SelectMany((x, i) => Enumerable.Range(0, 30)
                .Select(d => Rate(id: i * 100 + d + 1, rate: 10m + i, from: x, date: PeriodEnd.AddDays(-d))))
            .ToList();
        var candidates = Enumerable.Range(1, 400)
            .Select(i => Candidate(id: i, doc: codes[i % codes.Length])).ToList();

        var r = Evaluate(rates: rates, candidates: candidates, currencies: currencies);

        Assert.Equal(400, r.EligibleCount);
        Assert.All(r.Exposures, x =>
            Assert.Equal(10m + Array.IndexOf(codes, x.ClosingRate!.FromCurrency), x.ClosingRate.Rate));
    }

    [Fact]
    public void Malformed_candidate_currency_is_rejected_as_input_error()
    {
        var c = FxValuationEligibilityRules.Evaluate(Request(), [Policy()], Currencies, [Rate()],
            [Candidate(doc: "usd"), Candidate(id: 8, @base: null)]);

        Assert.False(c.IsValid);
        Assert.Equal(
            [
                new("candidates", 0, "documentCurrency", FxAllocationError.InvalidFormat),
                new("candidates", 1, "baseCurrency", FxAllocationError.Required)
            ],
            c.Errors);
    }
}
