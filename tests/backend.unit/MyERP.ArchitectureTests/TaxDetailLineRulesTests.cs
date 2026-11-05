using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-TAX-003／006／020／028（純規則部分）：銷項／銷項折讓／進項稅明細行的保存金額驗證、可扣抵分類、
/// 折讓上限與 GL 稅額科目（REQ_F4116.17.1–2、BR-FI-103～106、ADR-081）。
/// 不涉及 SQL、永久來源鍵、MD 稅碼查詢或 GL 過帳；來源金額與映射科目由呼叫端讀到的快照傳入。
/// </summary>
public sealed class TaxDetailLineRulesTests
{
    private static TaxDetailLineInput Line(
        TaxRole role = TaxRole.Output, long lineId = 11, string? taxCode = " vat5 ",
        int decimals = 2, decimal net = 100m, decimal tax = 5m, decimal gross = 105m,
        int baseDecimals = 0, decimal baseNet = 3200m, decimal baseTax = 160m, decimal baseGross = 3360m,
        TaxTreatment? treatment = null, long? taxAccountId = 501, long? costAccountId = null,
        TaxCreditRemaining? remaining = null) =>
        new(role, lineId, taxCode, decimals, net, tax, gross, baseDecimals, baseNet, baseTax, baseGross,
            treatment, taxAccountId, costAccountId, remaining);

    private static TaxCreditRemaining Remaining(
        decimal net = 100m, decimal tax = 5m, decimal baseNet = 3200m, decimal baseTax = 160m) =>
        new(net, tax, baseNet, baseTax);

    private static void AssertOnly(TaxDetailLineResult r, string field, TaxDetailError error)
    {
        Assert.False(r.IsValid);
        Assert.Null(r.Detail);
        var e = Assert.Single(r.Errors);
        Assert.Equal(field, e.Field);
        Assert.Equal(error, e.Error);
    }

    [Fact]
    public void OutputLineIsPositiveAndPostsToOutputTaxAccount()
    {
        var r = TaxDetailLineRules.Validate(Line());

        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
        var d = Assert.IsType<TaxDetailLine>(r.Detail);
        Assert.Equal(TaxRole.Output, d.Role);
        Assert.Equal(11, d.SourceLineId);
        Assert.Equal("VAT5", d.TaxCode);
        Assert.Equal(100m, d.SignedNet);
        Assert.Equal(5m, d.SignedTax);
        Assert.Equal(3200m, d.SignedBaseNet);
        Assert.Equal(160m, d.SignedBaseTax);
        Assert.Equal(0m, d.ClaimableBaseTax);
        Assert.Equal(0m, d.NonRecoverableBaseTax);
        Assert.Equal(501, d.GlAccountId);
        Assert.True(d.RequiresGlLeg);
    }

    [Fact]
    public void OutputCreditIsSignedOppositeWithinRemaining()
    {
        var r = TaxDetailLineRules.Validate(Line(
            role: TaxRole.OutputCredit, net: 40m, tax: 2m, gross: 42m,
            baseNet: 1280m, baseTax: 64m, baseGross: 1344m, remaining: Remaining()));

        var d = Assert.IsType<TaxDetailLine>(r.Detail);
        Assert.Equal(-40m, d.SignedNet);
        Assert.Equal(-2m, d.SignedTax);
        Assert.Equal(-1280m, d.SignedBaseNet);
        Assert.Equal(-64m, d.SignedBaseTax);
        Assert.Equal(501, d.GlAccountId);
    }

    [Fact]
    public void OutputCreditMayUseExactlyTheRemainingAmounts()
    {
        var r = TaxDetailLineRules.Validate(Line(role: TaxRole.OutputCredit, remaining: Remaining()));

        Assert.True(r.IsValid);
        Assert.Equal(-160m, r.Detail!.SignedBaseTax);
    }

    [Theory]
    [InlineData(100.01, 5, 3200, 160, "net")]
    [InlineData(100, 5.01, 3200, 160, "tax")]
    [InlineData(100, 5, 3201, 160, "baseNet")]
    [InlineData(100, 5, 3200, 161, "baseTax")]
    public void OutputCreditAboveRemainingIsRejected(
        double remNet, double remTax, double remBaseNet, double remBaseTax, string field)
    {
        // Remaining is one step below the credited component for exactly one field.
        var credit = Line(role: TaxRole.OutputCredit, net: (decimal)remNet, tax: (decimal)remTax,
            gross: (decimal)remNet + (decimal)remTax, baseNet: (decimal)remBaseNet, baseTax: (decimal)remBaseTax,
            baseGross: (decimal)remBaseNet + (decimal)remBaseTax, remaining: Remaining());

        AssertOnly(TaxDetailLineRules.Validate(credit), field, TaxDetailError.ExceedsRemaining);
    }

    [Fact]
    public void OutputCreditStillReportsOverCreditOnValidComponentsWhenAnotherIsInvalid()
    {
        var r = TaxDetailLineRules.Validate(Line(role: TaxRole.OutputCredit, tax: 5.001m,
            baseNet: 3201m, baseGross: 3361m, remaining: Remaining()));

        Assert.Equal([new("tax", TaxDetailError.InvalidScale), new("baseNet", TaxDetailError.ExceedsRemaining)], r.Errors);
    }

    [Fact]
    public void OutputCreditWithoutRemainingIsRequired() =>
        AssertOnly(TaxDetailLineRules.Validate(Line(role: TaxRole.OutputCredit)), "remaining", TaxDetailError.Required);

    [Fact]
    public void OutputCreditWithNegativeRemainingIsOutOfRange() =>
        AssertOnly(TaxDetailLineRules.Validate(Line(role: TaxRole.OutputCredit, remaining: Remaining(tax: -1m))),
            "remaining", TaxDetailError.OutOfRange);

    [Fact]
    public void RemainingOnNonCreditRoleIsNotApplicable()
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(remaining: Remaining())), "remaining", TaxDetailError.NotApplicable);
        AssertOnly(TaxDetailLineRules.Validate(Line(role: TaxRole.Input, treatment: TaxTreatment.Recoverable,
            remaining: Remaining())), "remaining", TaxDetailError.NotApplicable);
    }

    [Fact]
    public void RecoverableInputIsClaimableAndPostsToInputTaxAccount()
    {
        var r = TaxDetailLineRules.Validate(Line(role: TaxRole.Input, treatment: TaxTreatment.Recoverable, taxAccountId: 601));

        var d = Assert.IsType<TaxDetailLine>(r.Detail);
        Assert.Equal(160m, d.SignedBaseTax);
        Assert.Equal(160m, d.ClaimableBaseTax);
        Assert.Equal(0m, d.NonRecoverableBaseTax);
        Assert.Equal(601, d.GlAccountId);
        Assert.Equal(TaxTreatment.Recoverable, d.Treatment);
    }

    [Fact]
    public void NonRecoverableInputIsNeverClaimedAndPostsToConfiguredCost()
    {
        var r = TaxDetailLineRules.Validate(Line(role: TaxRole.Input, treatment: TaxTreatment.NonRecoverable,
            taxAccountId: null, costAccountId: 702));

        var d = Assert.IsType<TaxDetailLine>(r.Detail);
        Assert.Equal(160m, d.SignedBaseTax);
        Assert.Equal(0m, d.ClaimableBaseTax);
        Assert.Equal(160m, d.NonRecoverableBaseTax);
        Assert.Equal(702, d.GlAccountId);
    }

    [Fact]
    public void InputWithoutTreatmentIsRequired() =>
        AssertOnly(TaxDetailLineRules.Validate(Line(role: TaxRole.Input)), "treatment", TaxDetailError.Required);

    [Theory]
    [InlineData(TaxRole.Output)]
    [InlineData(TaxRole.OutputCredit)]
    public void TreatmentOnOutputRoleIsNotApplicable(TaxRole role) =>
        AssertOnly(TaxDetailLineRules.Validate(Line(role: role, treatment: TaxTreatment.Recoverable,
            remaining: role == TaxRole.OutputCredit ? Remaining() : null)), "treatment", TaxDetailError.NotApplicable);

    [Fact]
    public void UndefinedTreatmentIsOutOfRange() =>
        AssertOnly(TaxDetailLineRules.Validate(Line(role: TaxRole.Input, treatment: (TaxTreatment)9)),
            "treatment", TaxDetailError.OutOfRange);

    [Fact]
    public void MissingMappedAccountIsRequiredForTheRolesLeg()
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(taxAccountId: null)), "taxAccountId", TaxDetailError.Required);
        AssertOnly(TaxDetailLineRules.Validate(Line(taxAccountId: 0)), "taxAccountId", TaxDetailError.Required);
        AssertOnly(TaxDetailLineRules.Validate(Line(role: TaxRole.Input, treatment: TaxTreatment.Recoverable,
            taxAccountId: null)), "taxAccountId", TaxDetailError.Required);
        AssertOnly(TaxDetailLineRules.Validate(Line(role: TaxRole.Input, treatment: TaxTreatment.NonRecoverable,
            taxAccountId: 601, costAccountId: null)), "costAccountId", TaxDetailError.Required);
    }

    [Fact]
    public void CostAccountOnlyAppliesToNonRecoverableInput()
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(costAccountId: 702)), "costAccountId", TaxDetailError.NotApplicable);
        AssertOnly(TaxDetailLineRules.Validate(Line(role: TaxRole.Input, treatment: TaxTreatment.Recoverable,
            costAccountId: 702)), "costAccountId", TaxDetailError.NotApplicable);
    }

    [Fact]
    public void NonRecoverableInputIgnoresAnUnusedInputTaxAccount()
    {
        // The FI mapping may also expose the input-tax control account; only the cost leg is posted.
        var r = TaxDetailLineRules.Validate(Line(role: TaxRole.Input, treatment: TaxTreatment.NonRecoverable,
            taxAccountId: 601, costAccountId: 702));

        Assert.Equal(702, r.Detail!.GlAccountId);
    }

    [Theory]
    [InlineData(TaxRole.Output, null)]
    [InlineData(TaxRole.Input, TaxTreatment.Recoverable)]
    [InlineData(TaxRole.Input, TaxTreatment.NonRecoverable)]
    public void ZeroTaxLineHasNoGlLegAndNeedsNoAccount(TaxRole role, TaxTreatment? treatment)
    {
        var r = TaxDetailLineRules.Validate(Line(role: role, treatment: treatment, tax: 0m, gross: 100m,
            baseTax: 0m, baseGross: 3200m, taxAccountId: null));

        var d = Assert.IsType<TaxDetailLine>(r.Detail);
        Assert.False(d.RequiresGlLeg);
        Assert.Null(d.GlAccountId);
        Assert.Equal(0m, d.SignedBaseTax);
        Assert.Equal(0m, d.ClaimableBaseTax);
        Assert.Equal(0m, d.NonRecoverableBaseTax);
    }

    [Fact]
    public void ZeroOriginalTaxWithNonZeroBaseTaxIsRejectedAsImplicitPlug()
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(tax: 0m, gross: 100m)), "baseTax", TaxDetailError.ZeroMismatch);
        AssertOnly(TaxDetailLineRules.Validate(Line(baseTax: 0m, baseGross: 3200m)), "baseTax", TaxDetailError.ZeroMismatch);
    }

    [Fact]
    public void ComponentsMustBalanceInOriginalAndBaseCurrency()
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(gross: 105.01m)), "gross", TaxDetailError.NotBalanced);
        AssertOnly(TaxDetailLineRules.Validate(Line(baseGross: 3359m)), "baseGross", TaxDetailError.NotBalanced);
    }

    [Fact]
    public void NegativeNetIsOutOfRange() =>
        AssertOnly(TaxDetailLineRules.Validate(Line(net: -1m, gross: 4m)), "net", TaxDetailError.OutOfRange);

    [Fact]
    public void NegativeTaxAndZeroGrossAreOutOfRange()
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(tax: -5m, gross: 95m, baseTax: 160m)), "tax", TaxDetailError.OutOfRange);
        var zero = TaxDetailLineRules.Validate(Line(net: 0m, tax: 0m, gross: 0m, baseNet: 0m, baseTax: 0m, baseGross: 0m));
        Assert.Equal([new("gross", TaxDetailError.OutOfRange), new("baseGross", TaxDetailError.OutOfRange)], zero.Errors);
    }

    [Fact]
    public void SqlMaximumIsAcceptedAndAnythingAboveIsOutOfRange()
    {
        const decimal max = 999999999999999.9999m;
        var ok = TaxDetailLineRules.Validate(Line(decimals: 4, net: max - 1m, tax: 1m, gross: max,
            baseDecimals: 4, baseNet: max - 1m, baseTax: 1m, baseGross: max));
        Assert.True(ok.IsValid);

        var over = TaxDetailLineRules.Validate(Line(decimals: 4, net: max, tax: 1m, gross: max + 1m,
            baseDecimals: 4, baseNet: 3200m, baseTax: 160m, baseGross: 3360m));
        Assert.False(over.IsValid);
        Assert.Equal([new("gross", TaxDetailError.OutOfRange)], over.Errors);
    }

    [Fact]
    public void AmountsMustFitTheirCurrencyScale()
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(tax: 5.001m)), "tax", TaxDetailError.InvalidScale);
        AssertOnly(TaxDetailLineRules.Validate(Line(baseNet: 3200.5m)), "baseNet", TaxDetailError.InvalidScale);
        var both = TaxDetailLineRules.Validate(Line(tax: 5.001m, gross: 105.001m));
        Assert.Equal([new("tax", TaxDetailError.InvalidScale), new("gross", TaxDetailError.InvalidScale)], both.Errors);
        var fifth = TaxDetailLineRules.Validate(Line(decimals: 4, net: 100.00001m, gross: 105.00001m));
        Assert.Equal([new("net", TaxDetailError.InvalidScale), new("gross", TaxDetailError.InvalidScale)], fifth.Errors);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void CurrencyDecimalsOutsideZeroToFourAreRejectedWithoutBlamingAmounts(int decimals)
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(decimals: decimals, net: 100.123m, gross: 105.123m)),
            "currencyDecimals", TaxDetailError.OutOfRange);
        AssertOnly(TaxDetailLineRules.Validate(Line(baseDecimals: decimals)), "baseCurrencyDecimals", TaxDetailError.OutOfRange);
    }

    [Fact]
    public void BalanceIsNotCheckedWhenAComponentIsAlreadyInvalid()
    {
        var r = TaxDetailLineRules.Validate(Line(tax: 5.001m, gross: 999m));

        Assert.Equal([new("tax", TaxDetailError.InvalidScale)], r.Errors);
    }

    [Fact]
    public void IdentityFieldsAreRequired()
    {
        AssertOnly(TaxDetailLineRules.Validate(Line(lineId: 0)), "sourceLineId", TaxDetailError.Required);
        AssertOnly(TaxDetailLineRules.Validate(Line(lineId: -3)), "sourceLineId", TaxDetailError.Required);
        AssertOnly(TaxDetailLineRules.Validate(Line(taxCode: "  ")), "taxCode", TaxDetailError.Required);
        AssertOnly(TaxDetailLineRules.Validate(Line(taxCode: null)), "taxCode", TaxDetailError.Required);
        AssertOnly(TaxDetailLineRules.Validate(Line(role: (TaxRole)7)), "role", TaxDetailError.OutOfRange);
    }

    [Fact]
    public void NullRequestIsRequired() =>
        AssertOnly(TaxDetailLineRules.Validate(null), "request", TaxDetailError.Required);

    [Fact]
    public void SeveralErrorsAreReportedOncePerFieldInOrder()
    {
        var r = TaxDetailLineRules.Validate(Line(role: TaxRole.Input, lineId: 0, taxCode: "",
            gross: 106m, baseTax: 160.5m, taxAccountId: null));

        Assert.Equal(
        [
            new("sourceLineId", TaxDetailError.Required),
            new("taxCode", TaxDetailError.Required),
            new("gross", TaxDetailError.NotBalanced),
            new("baseTax", TaxDetailError.InvalidScale),
            new("treatment", TaxDetailError.Required),
        ], r.Errors);
    }
}
