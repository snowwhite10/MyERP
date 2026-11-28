using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;
using E = MyERP.Modules.Purchasing.Application.PurchaseReceiptTrackingInput;

namespace MyERP.ArchitectureTests;

/// <summary>WU-PUR-007b2（ADR-221，PUR-RECV-TRK-008）：收貨行批號／序號格式（400＋欄位路徑），不需資料庫。</summary>
public sealed class PurchaseReceiptTrackingRulesTests
{
    private static readonly DateOnly Made = new(2028, 2, 29);

    private static PurchaseReceiptSaveLine Line(decimal quantity, params PurchaseReceiptTrackingInput[] tracking) =>
        new(1, 11, quantity, 91, tracking);

    private static IReadOnlyDictionary<string, string[]> Errors(params PurchaseReceiptSaveLine[] lines) =>
        Assert.Throws<RequestValidationException>(() => PurchaseReceiptTrackingRules.Validate(lines)).Errors;

    [Fact]
    public void ValidLotsSerialsAndAbsentTrackingPass()
    {
        PurchaseReceiptTrackingRules.Validate([
            Line(5m, new E(" L-1 ", null, 2m, Made, Made), new E("L-2", null, 3m)),
            new(2, 12, 2m, 91, [new E(null, "S-1", 1m), new E(null, "S-2", 1m)]),
            new(3, 13, 1m, 91),
            Line(4m, new E("L-3", null, 1m)) with { LineNumber = 4 } // incomplete draft capture is allowed
        ]);
        PurchaseReceiptTrackingRules.Validate(null);
    }

    [Theory]
    [InlineData(null, null, "lines[0].tracking[0]")]
    [InlineData("L", "S", "lines[0].tracking[0]")]
    [InlineData("   ", null, "lines[0].tracking[0].lotNo")]
    [InlineData(null, "", "lines[0].tracking[0].serialNo")]
    public void EntryNeedsExactlyOneNonBlankNumber(string? lot, string? serial, string field) =>
        Assert.Contains(field, Errors(Line(5m, new E(lot, serial, 1m))).Keys);

    [Fact]
    public void LengthLimitsFollowInventory()
    {
        PurchaseReceiptTrackingRules.Validate([Line(2m, new E(new string('L', 50), null, 1m)),
            new(2, 12, 1m, 91, [new E(null, new string('S', 100), 1m)])]);
        Assert.Contains("lines[0].tracking[0].lotNo", Errors(Line(5m, new E(new string('L', 51), null, 1m))).Keys);
        Assert.Contains("lines[0].tracking[0].serialNo", Errors(Line(5m, new E(null, new string('S', 101), 1m))).Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0000001)]
    public void LotQuantityMustBePositiveWithSixDecimals(decimal quantity) =>
        Assert.Contains("lines[0].tracking[0].quantity", Errors(Line(5m, new E("L", null, quantity))).Keys);

    [Fact]
    public void SerialIsExactlyOneUnitWithoutDates()
    {
        Assert.Contains("lines[0].tracking[0].quantity", Errors(Line(5m, new E(null, "S", 2m))).Keys);
        Assert.Contains("lines[0].tracking[0]", Errors(Line(5m, new E(null, "S", 1m, Made))).Keys);
    }

    [Fact]
    public void ExpiryCannotPrecedeManufacture() =>
        Assert.Contains("lines[0].tracking[0].expiryDate",
            Errors(Line(5m, new E("L", null, 1m, Made, Made.AddDays(-1)))).Keys);

    [Fact]
    public void LineCannotMixDuplicateOrExceedQuantity()
    {
        Assert.Contains("lines[0].tracking", Errors(Line(5m, new E("L", null, 1m), new E(null, "S", 1m))).Keys);
        Assert.Contains("lines[0].tracking", Errors(Line(5m, new E("lot-a", null, 1m), new E(" LOT-A", null, 1m))).Keys);
        Assert.Contains("lines[0].tracking", Errors(Line(2m, new E("L1", null, 1.5m), new E("L2", null, 0.500001m))).Keys);
    }

    [Fact]
    public void AtMostOneThousandEntriesAndNullEntryReported()
    {
        var many = Enumerable.Range(1, 1001).Select(i => new PurchaseReceiptTrackingInput(null, $"S{i}", 1m)).ToArray();
        Assert.Contains("lines[0].tracking", Errors(Line(2000m, many)).Keys);
        Assert.Contains("lines[0].tracking[0]", Errors(Line(5m, new PurchaseReceiptTrackingInput[] { null! })).Keys);
    }
}
