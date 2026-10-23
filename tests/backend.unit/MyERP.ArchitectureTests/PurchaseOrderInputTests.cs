using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseOrderInputTests
{
    private static PurchaseOrderInput Valid() => new(19, new DateOnly(2024, 2, 29), " note ",
        [new(1, 7, null, 1.000001m, 0m, "TWD", 3)]);

    [Fact]
    public void LeapDayAndSixDecimalQuantityAndZeroPriceAreAccepted()
    {
        var input = Valid();
        input.Validate();
        input.Lines![0].Quantity.Should().Be(1.000001m);
    }

    [Fact]
    public void ThousandLinesAreAccepted()
    {
        var input = Valid() with
        {
            Lines = Enumerable.Range(1, 1000).Select(n =>
                new PurchaseOrderLineInput(n, 7, null, 1m, 0m, "TWD", 3)).ToArray()
        };
        input.Validate();
    }

    [Theory]
    [InlineData("supplierId")]
    [InlineData("businessDate")]
    [InlineData("lines")]
    [InlineData("lines[0].lineNo")]
    [InlineData("lines[0].itemId")]
    [InlineData("lines[0].requisitionLineId")]
    [InlineData("lines[0].quantity")]
    [InlineData("lines[0].unitPrice")]
    [InlineData("lines[0].currencyCode")]
    [InlineData("lines[0].taxRateId")]
    public void InvalidFieldReturnsFieldSpecificError(string field)
    {
        var input = field switch
        {
            "supplierId" => Valid() with { SupplierId = 0 },
            "businessDate" => Valid() with { BusinessDate = default },
            "lines" => Valid() with { Lines = [] },
            "lines[0].lineNo" => WithLine(Valid(), line => line with { LineNo = 0 }),
            "lines[0].itemId" => WithLine(Valid(), line => line with { ItemId = 0 }),
            "lines[0].requisitionLineId" => WithLine(Valid(), line => line with { RequisitionLineId = 0 }),
            "lines[0].quantity" => WithLine(Valid(), line => line with { Quantity = 0 }),
            "lines[0].unitPrice" => WithLine(Valid(), line => line with { UnitPrice = -1 }),
            "lines[0].currencyCode" => WithLine(Valid(), line => line with { CurrencyCode = "" }),
            _ => WithLine(Valid(), line => line with { TaxRateId = 0 })
        };
        input.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public void MoreThanThousandOrDuplicateLineNumbersAreRejected()
    {
        var tooMany = Valid() with { Lines = Enumerable.Repeat(Valid().Lines![0], 1001).ToArray() };
        tooMany.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines");
        var duplicate = Valid() with { Lines = [Valid().Lines![0], Valid().Lines![0]] };
        duplicate.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines");
    }

    [Theory]
    [InlineData(0.0000001, 0)]
    [InlineData(1, 0.0000001)]
    [InlineData(10000000000000, 0)]
    [InlineData(1, 10000000000000)]
    public void ValuesOutsideDecimalNineteenSixAreRejected(decimal quantity, decimal unitPrice)
    {
        var input = WithLine(Valid(), line => line with { Quantity = quantity, UnitPrice = unitPrice });
        input.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>();
    }

    private static PurchaseOrderInput WithLine(PurchaseOrderInput input,
        Func<PurchaseOrderLineInput, PurchaseOrderLineInput> change) =>
        input with { Lines = [change(input.Lines![0])] };
}
