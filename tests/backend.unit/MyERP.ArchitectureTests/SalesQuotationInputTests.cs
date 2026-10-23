using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesQuotationInputTests
{
    private static SalesQuotationInput Valid() => new(1, new DateOnly(2024, 2, 29),
        new DateOnly(2024, 3, 1), " note ",
        [new(1, 2, 1.000001m, 0m, 0m, "USD", 3)]);

    [Fact]
    public void LeapDayAndMaximumLineCountAreAccepted()
    {
        (Valid() with { Lines = Enumerable.Range(1, 1000)
            .Select(n => Valid().Lines![0] with { LineNo = n }).ToArray() }).Validate();
    }

    [Theory]
    [InlineData("customerId")]
    [InlineData("businessDate")]
    [InlineData("expiryDate")]
    [InlineData("lines")]
    [InlineData("lines[0].lineNo")]
    [InlineData("lines[0].itemId")]
    [InlineData("lines[0].quantity")]
    [InlineData("lines[0].unitPrice")]
    [InlineData("lines[0].discountRate")]
    [InlineData("lines[0].currencyCode")]
    [InlineData("lines[0].taxRateId")]
    public void InvalidInputNamesItsField(string field)
    {
        var input = field switch
        {
            "customerId" => Valid() with { CustomerId = 0 },
            "businessDate" => Valid() with { BusinessDate = default },
            "expiryDate" => Valid() with { ExpiryDate = new DateOnly(2024, 2, 28) },
            "lines" => Valid() with { Lines = [] },
            "lines[0].lineNo" => WithLine(l => l with { LineNo = 0 }),
            "lines[0].itemId" => WithLine(l => l with { ItemId = 0 }),
            "lines[0].quantity" => WithLine(l => l with { Quantity = 0 }),
            "lines[0].unitPrice" => WithLine(l => l with { UnitPrice = -1 }),
            "lines[0].discountRate" => WithLine(l => l with { DiscountRate = 100.00001m }),
            "lines[0].currencyCode" => WithLine(l => l with { CurrencyCode = "usd" }),
            _ => WithLine(l => l with { TaxRateId = 0 })
        };
        input.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public void RejectsDuplicateLinesAndExcessPrecision()
    {
        (Valid() with { Lines = [Valid().Lines![0], Valid().Lines![0]] })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines");
        WithLine(l => l with { Quantity = 0.0000001m })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines[0].quantity");
        WithLine(l => l with { UnitPrice = 0.0000001m })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines[0].unitPrice");
    }

    private static SalesQuotationInput WithLine(Func<SalesQuotationLineInput, SalesQuotationLineInput> edit) =>
        Valid() with { Lines = [edit(Valid().Lines![0])] };
}
