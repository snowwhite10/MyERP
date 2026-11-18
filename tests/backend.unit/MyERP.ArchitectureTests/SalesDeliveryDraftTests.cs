using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesDeliveryDraftTests
{
    private static SalesDeliveryDraftInput Valid() => new(7, new DateOnly(2024, 2, 29),
        [new SalesDeliveryLineInput(1, 11, 21, null, 1.000001m)]);

    [Fact]
    public void AcceptsLeapDayMaximumLineCountAndRepeatedOrderLine()
    {
        (Valid() with { Lines = Enumerable.Range(1, 1000)
            .Select(n => Valid().Lines![0] with { LineNo = n }).ToArray() }).Validate();
    }

    [Theory]
    [InlineData("orderId")]
    [InlineData("businessDate")]
    [InlineData("lines")]
    [InlineData("lines[0]")]
    [InlineData("lines[0].lineNo")]
    [InlineData("lines[0].orderLineId")]
    [InlineData("lines[0].warehouseId")]
    [InlineData("lines[0].locationId")]
    [InlineData("lines[0].quantity")]
    public void InvalidDraftNamesItsField(string field)
    {
        var input = field switch
        {
            "orderId" => Valid() with { OrderId = 0 },
            "businessDate" => Valid() with { BusinessDate = default },
            "lines" => Valid() with { Lines = [] },
            "lines[0]" => Valid() with { Lines = [null!] },
            "lines[0].lineNo" => WithLine(line => line with { LineNo = 0 }),
            "lines[0].orderLineId" => WithLine(line => line with { OrderLineId = 0 }),
            "lines[0].warehouseId" => WithLine(line => line with { WarehouseId = 0 }),
            "lines[0].locationId" => WithLine(line => line with { LocationId = 0 }),
            _ => WithLine(line => line with { Quantity = 0.0000001m })
        };
        input.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public void RejectsDuplicateNumbersAndExcessLineCount()
    {
        (Valid() with { Lines = [Valid().Lines![0], Valid().Lines![0]] })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines");
        (Valid() with { Lines = Enumerable.Range(1, 1001)
            .Select(n => Valid().Lines![0] with { LineNo = n }).ToArray() })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000_000_000_000)]
    public void RejectsQuantityOutsideDecimalNineteenSix(decimal quantity)
    {
        WithLine(line => line with { Quantity = quantity })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines[0].quantity");
    }

    [Fact]
    public void AcceptsTrimmedLotsAndSerialsUpToTheLineQuantity()
    {
        WithLine(line => line with { Quantity = 3m, Tracking =
            [new(" " + new string('L', 50) + " ", null, 1.5m), new("L2", null, 0.000001m)] }).Validate();
        WithLine(line => line with { Quantity = 2m, Tracking =
            [new(null, new string('S', 100), 1m), new(null, "S2", 1m)] }).Validate();
        WithLine(line => line with { Quantity = 1000m, Tracking = Enumerable.Range(1, 1000)
            .Select(n => new SalesDeliveryTrackingInput(null, "S" + n, 1m)).ToArray() }).Validate();
    }

    [Theory]
    [InlineData("both", "lines[0].tracking[0]")]
    [InlineData("neither", "lines[0].tracking[0]")]
    [InlineData("null", "lines[0].tracking[0]")]
    [InlineData("blankLot", "lines[0].tracking[0].lotNo")]
    [InlineData("longLot", "lines[0].tracking[0].lotNo")]
    [InlineData("longSerial", "lines[0].tracking[0].serialNo")]
    [InlineData("zero", "lines[0].tracking[0].quantity")]
    [InlineData("precision", "lines[0].tracking[0].quantity")]
    [InlineData("serialTwo", "lines[0].tracking[0].quantity")]
    [InlineData("mixed", "lines[0].tracking")]
    [InlineData("duplicate", "lines[0].tracking")]
    [InlineData("exceeds", "lines[0].tracking")]
    [InlineData("tooMany", "lines[0].tracking")]
    public void InvalidTrackingNamesItsField(string kind, string field)
    {
        SalesDeliveryTrackingInput[] tracking = kind switch
        {
            "both" => [new("L", "S", 1m)],
            "neither" => [new(null, null, 1m)],
            "null" => [null!],
            "blankLot" => [new("  ", null, 1m)],
            "longLot" => [new(new string('L', 51), null, 1m)],
            "longSerial" => [new(null, new string('S', 101), 1m)],
            "zero" => [new("L", null, 0m)],
            "precision" => [new("L", null, 0.0000001m)],
            "serialTwo" => [new(null, "S", 2m)],
            "mixed" => [new("L", null, 1m), new(null, "S", 1m)],
            "duplicate" => [new("lot-a", null, 1m), new(" LOT-A ", null, 1m)],
            "exceeds" => [new("L1", null, 1m), new("L2", null, 0.000002m)],
            _ => Enumerable.Range(1, 1001).Select(n => new SalesDeliveryTrackingInput("L" + n, null, 0.000001m)).ToArray()
        };
        WithLine(line => line with { Quantity = kind == "tooMany" ? 5m : 1.000001m, Tracking = tracking })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey(field);
    }

    private static SalesDeliveryDraftInput WithLine(Func<SalesDeliveryLineInput, SalesDeliveryLineInput> edit) =>
        Valid() with { Lines = [edit(Valid().Lines![0])] };
}
