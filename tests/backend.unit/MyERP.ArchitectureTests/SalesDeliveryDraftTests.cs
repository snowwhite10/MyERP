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

    private static SalesDeliveryDraftInput WithLine(Func<SalesDeliveryLineInput, SalesDeliveryLineInput> edit) =>
        Valid() with { Lines = [edit(Valid().Lines![0])] };
}
