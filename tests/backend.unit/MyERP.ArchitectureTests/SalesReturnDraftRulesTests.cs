using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>
/// SD-RETURN-U01～U06 (WU-SD-008a2a, ADR-185, BR-SD-044/046/047/048): remaining-proportional allocation with exact final
/// remainder, separate original/base rounding, rejection of over-consumption, request validation and the canonical
/// payload fingerprint.
/// </summary>
public sealed class SalesReturnDraftRulesTests
{
    // USD invoice line: 3 units, net 100.00 + tax 5.00; base TWD (0 decimals) 3,100 + 155.
    private static readonly SalesReturnRemaining Line = new(3m, 100m, 5m, 3100m, 155m);

    [Fact]
    public void ThreeReturnsOfOneUnitReconcileExactlyToTheSource()
    {
        // SD-RETURN-U01 / SD-RETURN-008: each later portion is a share of what is left; the last takes the remainder.
        var (first, afterFirst) = SalesReturnAmounts.Take(Line, 1m, "USD", "TWD", 1);
        first.Should().Be(new SalesReturnPortionAmounts(33.33m, 1.67m, 35.00m, 1033m, 52m, 1085m));
        var (second, afterSecond) = SalesReturnAmounts.Take(afterFirst, 1m, "USD", "TWD", 1);
        second.Should().Be(new SalesReturnPortionAmounts(33.34m, 1.67m, 35.01m, 1034m, 52m, 1086m));
        var (third, rest) = SalesReturnAmounts.Take(afterSecond, 1m, "USD", "TWD", 1);
        third.Should().Be(new SalesReturnPortionAmounts(33.33m, 1.66m, 34.99m, 1033m, 51m, 1084m));

        rest.Should().Be(new SalesReturnRemaining(0m, 0m, 0m, 0m, 0m));
        (first.OriginalNet + second.OriginalNet + third.OriginalNet).Should().Be(100m);
        (first.BaseTax + second.BaseTax + third.BaseTax).Should().Be(155m);
    }

    [Fact]
    public void WholeRemainderAndSameCurrencyKeepOriginalEqualToBase()
    {
        // SD-RETURN-U02: completing portion takes the exact saved components; TWD→TWD stays equal (ck_sal_return_rate).
        var (all, rest) = SalesReturnAmounts.Take(Line, 3m, "USD", "TWD", 4);
        all.Should().Be(new SalesReturnPortionAmounts(100m, 5m, 105m, 3100m, 155m, 3255m));
        rest.Quantity.Should().Be(0m);

        var (same, _) = SalesReturnAmounts.Take(new SalesReturnRemaining(7m, 250m, 12.5m, 250m, 12.5m), 0.333333m,
            "TWD", "TWD", 1);
        (same.OriginalNet, same.OriginalTax).Should().Be((same.BaseNet, same.BaseTax));
    }

    [Theory]
    [InlineData(3.000001)]
    [InlineData(0)]
    [InlineData(-1)]
    public void OverConsumptionOrNonPositiveQuantityIsRejectedWithTheLineNumber(decimal quantity)
    {
        // SD-RETURN-U03: never patched into a negative remainder.
        var error = Assert.Throws<BusinessRuleException>(() => SalesReturnAmounts.Take(Line, quantity, "USD", "TWD", 7));
        (error.ErrorCode, error.Extensions!["lineNo"]).Should().Be(("SAL.RETURN.SOURCE_INVALID", 7));
    }

    [Fact]
    public void InconsistentRemainderIsRejected()
    {
        // SD-RETURN-U04: a source already over-consumed (negative amount or zero quantity) is a damaged snapshot.
        foreach (var bad in new[]
                 {
                     Line with { OriginalTax = -0.01m }, Line with { BaseNet = -1m }, Line with { Quantity = 0m }
                 })
            Assert.Throws<BusinessRuleException>(() => SalesReturnAmounts.Take(bad, 0.5m, "USD", "TWD", 1))
                .ErrorCode.Should().Be("SAL.RETURN.SOURCE_INVALID");
    }

    [Fact]
    public void ValidationNamesEveryInvalidFieldAndDerivesLineQuantity()
    {
        // SD-RETURN-U05 / SD-RETURN-014/015/016: boundaries, duplicates, zero total, six decimals.
        var ok = Input("K1", new SalesReturnLineInput(1, 10, 51, null, 0.5m, [new(7, 1.25m), new(8, 0.25m)]));
        ok.Validate();
        ok.Lines![0].Quantity.Should().Be(2m);

        var error = Assert.Throws<RequestValidationException>(() => new SalesReturnDraftInput("bad key!", default,
            new string('x', 501),
            [
                new SalesReturnLineInput(0, 0, 0, 0, -1m, null),
                new SalesReturnLineInput(2, 10, 51, null, 0m, null),
                new SalesReturnLineInput(2, 10, 51, null, 0.0000001m, [new(7, 0m), new(7, 1m)]),
                new SalesReturnLineInput(3, 11, 51, null, 0m, [new(0, 1m)])
            ]).Validate());
        error.Errors.Keys.Should().BeEquivalentTo(
            "requestKey", "businessDate", "remark", "lines[0].lineNo", "lines[0].deliveryLineId",
            "lines[0].warehouseId", "lines[0].locationId", "lines[0].unbilledQuantity", "lines[1].quantity",
            "lines[2].lineNo", "lines[2].deliveryLineId", "lines[2].unbilledQuantity", "lines[2].billed[0].quantity",
            "lines[2].billed[1].invoiceLineId", "lines[3].billed[0].invoiceLineId");

        Assert.Throws<RequestValidationException>(() => Input("K", null!).Validate())
            .Errors.Keys.Should().Contain("lines[0]");
        Assert.Throws<RequestValidationException>(() =>
                new SalesReturnDraftInput("K", new DateOnly(2028, 2, 29), null, []).Validate())
            .Errors.Keys.Should().Contain("lines");
    }

    [Fact]
    public void FingerprintIsCanonicalOverOrderAndScaleButCoversEveryField()
    {
        // SD-RETURN-U06 / SD-RETURN-020: same content in another order or scale is the same payload.
        var a = new SalesReturnDraftInput("K", new DateOnly(2028, 2, 29), "  note ",
        [
            new SalesReturnLineInput(2, 11, 52, 9, 1m, null),
            new SalesReturnLineInput(1, 10, 51, null, 0.5m, [new(8, 0.25m), new(7, 1.25m)])
        ]);
        var b = new SalesReturnDraftInput("other-key", new DateOnly(2028, 2, 29), "note",
        [
            new SalesReturnLineInput(1, 10, 51, null, 0.500000m, [new(7, 1.250m), new(8, 0.25m)]),
            new SalesReturnLineInput(2, 11, 52, 9, 1.0m, [])
        ]);
        a.Fingerprint().Should().Be(b.Fingerprint()).And.MatchRegex("^[0-9a-f]{64}$");

        var changes = new[]
        {
            a with { BusinessDate = new DateOnly(2028, 3, 1) },
            a with { Remark = "note2" },
            a with { Remark = null },
            a with { Lines = [a.Lines![0] with { WarehouseId = 53 }, a.Lines[1]] },
            a with { Lines = [a.Lines![0] with { LocationId = null }, a.Lines[1]] },
            a with { Lines = [a.Lines![0] with { UnbilledQuantity = 1.000001m }, a.Lines[1]] },
            a with { Lines = [a.Lines![0], a.Lines[1] with { Billed = [new(8, 0.25m)] }] },
            a with { Lines = [a.Lines![0], a.Lines[1] with { Billed = [new(8, 0.25m), new(7, 1.2m)] }] },
            a with { Lines = [a.Lines![0] with { LineNo = 3 }, a.Lines[1]] }
        };
        changes.Select(x => x.Fingerprint()).Should().NotContain(a.Fingerprint()).And.OnlyHaveUniqueItems();
    }

    private static SalesReturnDraftInput Input(string key, SalesReturnLineInput line) =>
        new(key, new DateOnly(2028, 2, 29), null, [line]);
}
