using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-FA-005／006（純規則部分）：固定資產取得草稿欄位驗證與正規化（REQ_F4116.14.1、BR-FI-093／095、ADR-080）。
/// 不涉及 SQL、代碼唯一性、MD 查詢、審批或 GL 過帳；MD 資產類別與公司本位幣以呼叫端讀到的快照傳入。
/// </summary>
public sealed class FixedAssetAcquisitionDraftRulesTests
{
    private static FixedAssetAcquisitionDraftInput Valid(
        string? code = "fa-001", string? description = "  Forklift  ", long classId = 7,
        FixedAssetClassSnapshot? assetClass = null, DateOnly? acquired = null, DateOnly? inService = null,
        decimal cost = 1200m, decimal residual = 0m, int? life = 12,
        FixedAssetCurrencySnapshot? currency = null, string? source = "  PO-2026-001 ",
        bool noClass = false, bool noCurrency = false, bool noAcquired = false, bool noInService = false) =>
        new(code, description, classId,
            noClass ? null : assetClass ?? new FixedAssetClassSnapshot(7, true),
            noAcquired ? null : acquired ?? new DateOnly(2026, 1, 31),
            noInService ? null : inService ?? new DateOnly(2026, 2, 1),
            cost, residual, life,
            noCurrency ? null : currency ?? new FixedAssetCurrencySnapshot("TWD", 2),
            source);

    private static void AssertOnly(FixedAssetAcquisitionDraftResult r, string field, FixedAssetDraftError error)
    {
        Assert.False(r.IsValid);
        Assert.Null(r.Draft);
        var e = Assert.Single(r.Errors);
        Assert.Equal(field, e.Field);
        Assert.Equal(error, e.Error);
    }

    [Fact]
    public void ValidDraftIsNormalised()
    {
        var r = FixedAssetAcquisitionDraftRules.Validate(Valid());

        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
        var d = Assert.IsType<FixedAssetAcquisitionDraft>(r.Draft);
        Assert.Equal("FA-001", d.AssetCode);
        Assert.Equal("Forklift", d.Description);
        Assert.Equal(7, d.AssetClassId);
        Assert.Equal(new DateOnly(2026, 1, 31), d.AcquisitionDate);
        Assert.Equal(new DateOnly(2026, 2, 1), d.InServiceDate);
        Assert.Equal(1200m, d.Cost);
        Assert.Equal(0m, d.Residual);
        Assert.Equal(12, d.UsefulLifeMonths);
        Assert.Equal("TWD", d.CurrencyCode);
        Assert.Equal(2, d.CurrencyDecimals);
        Assert.Equal("PO-2026-001", d.SourceReference);
    }

    [Fact]
    public void ValidDraftFeedsTheDepreciationSchedule()
    {
        var d = FixedAssetAcquisitionDraftRules.Validate(Valid(cost: 1000m, residual: 100m, life: 7)).Draft!;
        var card = d.ToDepreciationInput();

        var first = FixedAssetDepreciationRules.CalculateMonth(card, 2026, 2);
        Assert.Equal(FixedAssetDepreciationStatus.Allowed, first.Status);
        Assert.Equal(1, first.PeriodNumber);
        Assert.Equal(FixedAssetDepreciationStatus.BeforeInService, FixedAssetDepreciationRules.CalculateMonth(card, 2026, 1).Status);
        Assert.Equal(900m, FixedAssetDepreciationRules.CalculateMonth(card, 2026, 8).AccumulatedAfter);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankSourceReferenceIsStoredAsNull(string? source)
    {
        var r = FixedAssetAcquisitionDraftRules.Validate(Valid(source: source));
        Assert.True(r.IsValid);
        Assert.Null(r.Draft!.SourceReference);
    }

    [Fact]
    public void NullInputIsRejected() =>
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(null), "request", FixedAssetDraftError.Required);

    [Theory]
    [InlineData(null, FixedAssetDraftError.Required)]
    [InlineData("  ", FixedAssetDraftError.Required)]
    [InlineData("FA 001", FixedAssetDraftError.InvalidFormat)]
    [InlineData("FA.001", FixedAssetDraftError.InvalidFormat)]
    [InlineData("資產001", FixedAssetDraftError.InvalidFormat)]
    [InlineData("ＦＡ001", FixedAssetDraftError.InvalidFormat)]
    [InlineData("A234567890123456789012345678901", FixedAssetDraftError.TooLong)]
    public void InvalidAssetCodeFailsByField(string? code, FixedAssetDraftError error) =>
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(code: code)), "assetCode", error);

    [Fact]
    public void AssetCodeAtMaximumLengthWithHyphenAndUnderscoreIsAccepted()
    {
        var code = " a_" + new string('9', 27) + "- ";
        var r = FixedAssetAcquisitionDraftRules.Validate(Valid(code: code));
        Assert.True(r.IsValid);
        Assert.Equal(30, r.Draft!.AssetCode.Length);
        Assert.Equal("A_" + new string('9', 27) + "-", r.Draft.AssetCode);
    }

    [Theory]
    [InlineData(null, FixedAssetDraftError.Required)]
    [InlineData(" \t ", FixedAssetDraftError.Required)]
    [InlineData("Line\nbreak", FixedAssetDraftError.InvalidFormat)]
    public void InvalidDescriptionFailsByField(string? description, FixedAssetDraftError error) =>
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(description: description)), "description", error);

    [Fact]
    public void DescriptionLengthBoundaryUsesTrimmedText()
    {
        Assert.True(FixedAssetAcquisitionDraftRules.Validate(Valid(description: "  " + new string('機', 200) + "  ")).IsValid);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(description: new string('機', 201))),
            "description", FixedAssetDraftError.TooLong);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void MissingAssetClassIdIsRequired(long classId) =>
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(classId: classId)), "assetClassId", FixedAssetDraftError.Required);

    [Fact]
    public void AssetClassSnapshotMustExistMatchAndBeActive()
    {
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(noClass: true)), "assetClassId", FixedAssetDraftError.NotFound);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(assetClass: new FixedAssetClassSnapshot(8, true))),
            "assetClassId", FixedAssetDraftError.Mismatch);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(assetClass: new FixedAssetClassSnapshot(7, false))),
            "assetClassId", FixedAssetDraftError.Inactive);
    }

    [Fact]
    public void MissingDatesAreRequired()
    {
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(noAcquired: true)), "acquisitionDate", FixedAssetDraftError.Required);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(noInService: true)), "inServiceDate", FixedAssetDraftError.Required);
    }

    [Fact]
    public void InServiceBeforeAcquisitionIsRejectedAndSameDayIsAccepted()
    {
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(acquired: new DateOnly(2026, 3, 1), inService: new DateOnly(2026, 2, 28))),
            "inServiceDate", FixedAssetDraftError.BeforeAcquisition);
        Assert.True(FixedAssetAcquisitionDraftRules.Validate(Valid(acquired: new DateOnly(2028, 2, 29), inService: new DateOnly(2028, 2, 29))).IsValid);
        Assert.True(FixedAssetAcquisitionDraftRules.Validate(Valid(acquired: new DateOnly(2026, 12, 31), inService: new DateOnly(2027, 1, 1))).IsValid);
    }

    [Theory]
    [InlineData("0", FixedAssetDraftError.OutOfRange)]
    [InlineData("-1", FixedAssetDraftError.OutOfRange)]
    [InlineData("1000000000000000", FixedAssetDraftError.OutOfRange)]
    [InlineData("100.001", FixedAssetDraftError.InvalidScale)]
    public void InvalidCostFailsByField(string cost, FixedAssetDraftError error) =>
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(cost: decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture))),
            "cost", error);

    [Fact]
    public void MaximumCostAtCurrencyScaleIsAccepted()
    {
        var max = 999999999999999.9999m;
        Assert.True(FixedAssetAcquisitionDraftRules.Validate(Valid(cost: max, currency: new FixedAssetCurrencySnapshot("USD", 4))).IsValid);
        Assert.True(FixedAssetAcquisitionDraftRules.Validate(Valid(cost: 5m, currency: new FixedAssetCurrencySnapshot("JPY", 0))).IsValid);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(cost: 5.5m, currency: new FixedAssetCurrencySnapshot("JPY", 0))),
            "cost", FixedAssetDraftError.InvalidScale);
    }

    [Theory]
    [InlineData("-0.01", FixedAssetDraftError.OutOfRange)]
    [InlineData("1200", FixedAssetDraftError.NotLessThanCost)]
    [InlineData("1200.01", FixedAssetDraftError.NotLessThanCost)]
    [InlineData("0.001", FixedAssetDraftError.InvalidScale)]
    public void InvalidResidualFailsByField(string residual, FixedAssetDraftError error) =>
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(residual: decimal.Parse(residual, System.Globalization.CultureInfo.InvariantCulture))),
            "residual", error);

    [Fact]
    public void ResidualJustBelowCostIsAccepted() =>
        Assert.True(FixedAssetAcquisitionDraftRules.Validate(Valid(residual: 1199.99m)).IsValid);

    [Theory]
    [InlineData(null, FixedAssetDraftError.Required)]
    [InlineData(0, FixedAssetDraftError.OutOfRange)]
    [InlineData(601, FixedAssetDraftError.OutOfRange)]
    public void InvalidUsefulLifeFailsByField(int? life, FixedAssetDraftError error) =>
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(life: life)), "usefulLifeMonths", error);

    [Theory]
    [InlineData(1)]
    [InlineData(600)]
    public void UsefulLifeBoundariesAreAccepted(int life) =>
        Assert.True(FixedAssetAcquisitionDraftRules.Validate(Valid(life: life)).IsValid);

    [Theory]
    [InlineData("twd", FixedAssetDraftError.InvalidFormat)]
    [InlineData("TW", FixedAssetDraftError.InvalidFormat)]
    [InlineData("TWDX", FixedAssetDraftError.InvalidFormat)]
    [InlineData("T1D", FixedAssetDraftError.InvalidFormat)]
    [InlineData(null, FixedAssetDraftError.InvalidFormat)]
    public void InvalidCurrencyCodeFailsByField(string? code, FixedAssetDraftError error) =>
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(currency: new FixedAssetCurrencySnapshot(code, 2))), "baseCurrency", error);

    [Fact]
    public void MissingCurrencyOrInvalidDecimalsFailWithoutBlamingAmounts()
    {
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(noCurrency: true, cost: 1.23456m)), "baseCurrency", FixedAssetDraftError.Required);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(currency: new FixedAssetCurrencySnapshot("TWD", 5), residual: 0.001m)),
            "baseCurrency", FixedAssetDraftError.OutOfRange);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(currency: new FixedAssetCurrencySnapshot("TWD", -1))),
            "baseCurrency", FixedAssetDraftError.OutOfRange);
    }

    [Fact]
    public void SourceReferenceLengthBoundary()
    {
        Assert.True(FixedAssetAcquisitionDraftRules.Validate(Valid(source: new string('S', 100))).IsValid);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(source: new string('S', 101))), "sourceReference", FixedAssetDraftError.TooLong);
        AssertOnly(FixedAssetAcquisitionDraftRules.Validate(Valid(source: "PO\u0001")), "sourceReference", FixedAssetDraftError.InvalidFormat);
    }

    [Fact]
    public void EveryFieldErrorIsReportedOnceInFormOrder()
    {
        var input = new FixedAssetAcquisitionDraftInput(
            "bad code", "", 0, null, new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1),
            0m, -1m, 0, new FixedAssetCurrencySnapshot("xx", 9), new string('S', 101));

        var r = FixedAssetAcquisitionDraftRules.Validate(input);

        Assert.False(r.IsValid);
        Assert.Null(r.Draft);
        Assert.Equal(
            ["assetCode", "description", "assetClassId", "inServiceDate", "cost", "residual", "usefulLifeMonths", "baseCurrency", "sourceReference"],
            r.Errors.Select(e => e.Field).ToArray());
        Assert.Equal(r.Errors.Count, r.Errors.Select(e => e.Field).Distinct().Count());
    }

    [Fact]
    public void ResidualIsNotComparedWithAnInvalidCost()
    {
        var r = FixedAssetAcquisitionDraftRules.Validate(Valid(cost: 0m, residual: 5m));
        var e = Assert.Single(r.Errors);
        Assert.Equal("cost", e.Field);
    }

    [Fact]
    public void ResidualAboveSqlMaximumIsOutOfRangeEvenWhenCostIsInvalid()
    {
        var r = FixedAssetAcquisitionDraftRules.Validate(Valid(cost: -1m, residual: decimal.MaxValue));
        Assert.Equal(
            [new FixedAssetDraftFieldError("cost", FixedAssetDraftError.OutOfRange), new FixedAssetDraftFieldError("residual", FixedAssetDraftError.OutOfRange)],
            r.Errors.ToArray());
    }
}
