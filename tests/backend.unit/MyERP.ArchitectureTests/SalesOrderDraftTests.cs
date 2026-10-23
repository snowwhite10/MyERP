using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesOrderDraftTests
{
    private static SalesOrderDraftInput Valid() => new(1, new DateOnly(2024, 2, 29), "USD", "note",
        [new(1, 2, 1.000001m, 10m, 0m, 3)]);

    [Fact]
    public void AcceptsLeapDayAndMaximumLines()
    {
        (Valid() with { Lines = Enumerable.Range(1, 1000)
            .Select(n => Valid().Lines![0] with { LineNo = n }).ToArray() }).Validate();
    }

    [Theory]
    [InlineData("customerId")]
    [InlineData("businessDate")]
    [InlineData("currencyCode")]
    [InlineData("remark")]
    [InlineData("lines")]
    [InlineData("lines[0].lineNo")]
    [InlineData("lines[0].itemId")]
    [InlineData("lines[0].quantity")]
    [InlineData("lines[0].unitPrice")]
    [InlineData("lines[0].discountRate")]
    [InlineData("lines[0].taxRateId")]
    public void InvalidDraftNamesItsField(string field)
    {
        var input = field switch
        {
            "customerId" => Valid() with { CustomerId = 0 },
            "businessDate" => Valid() with { BusinessDate = default },
            "currencyCode" => Valid() with { CurrencyCode = "usd" },
            "remark" => Valid() with { Remark = new string('x', 501) },
            "lines" => Valid() with { Lines = [] },
            "lines[0].lineNo" => WithLine(l => l with { LineNo = 0 }),
            "lines[0].itemId" => WithLine(l => l with { ItemId = 0 }),
            "lines[0].quantity" => WithLine(l => l with { Quantity = 0.0000001m }),
            "lines[0].unitPrice" => WithLine(l => l with { UnitPrice = -1 }),
            "lines[0].discountRate" => WithLine(l => l with { DiscountRate = 100.00001m }),
            _ => WithLine(l => l with { TaxRateId = 0 })
        };
        input.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public void RejectsDuplicateLinesAndTooManyLines()
    {
        (Valid() with { Lines = [Valid().Lines![0], Valid().Lines![0]] })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines");
        (Valid() with { Lines = Enumerable.Range(1, 1001)
            .Select(n => Valid().Lines![0] with { LineNo = n }).ToArray() })
            .Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("lines");
    }

    [Fact]
    public void CalculatesRoundedLineSnapshotsAndRejectsOverflow()
    {
        var result = SalesOrderAmounts.Calculate(
            [new SalesOrderAmountInput(1, 1m, 0.005m, 0m, "USD", 0m, 2m),
             new(2, 1m, 0.005m, 0m, "USD", 0m, 2m)], "USD");
        result.Lines.Select(x => x.OriginalNet).Should().Equal(0.01m, 0.01m);
        result.BaseNet.Should().Be(0.04m);

        Action overflow = () => SalesOrderAmounts.Calculate(
            [new(1, 1m, 600_000_000_000_000m, 0m, "USD", 0m, 1m),
             new(2, 1m, 600_000_000_000_000m, 0m, "USD", 0m, 1m)], "USD");
        overflow.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.ORDER.AMOUNT_OVERFLOW");
    }

    private static SalesOrderDraftInput WithLine(Func<SalesOrderLineInput, SalesOrderLineInput> edit) =>
        Valid() with { Lines = [edit(Valid().Lines![0])] };
}
