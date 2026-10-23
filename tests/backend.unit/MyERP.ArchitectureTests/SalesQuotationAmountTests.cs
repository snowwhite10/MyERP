using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesQuotationAmountTests
{
    [Fact]
    public void RoundsOriginalAmountsBeforeBaseConversionAndSumsLines()
    {
        var lines = new[] { new SalesQuotationAmountInput(1, 1m, 0.005m, 0m, "USD", 0m, 2m),
            new SalesQuotationAmountInput(2, 1m, 0.005m, 0m, "USD", 0m, 2m) };
        var result = SalesQuotationAmounts.Calculate(lines, "USD");
        result.Lines.Select(x => x.OriginalNet).Should().Equal(0.01m, 0.01m);
        result.Lines.Select(x => x.BaseNet).Should().Equal(0.02m, 0.02m);
        result.BaseNet.Should().Be(0.04m);
    }

    [Fact]
    public void AppliesDiscountAndTaxPerLine()
    {
        var result = SalesQuotationAmounts.Calculate(
            [new(1, 2m, 10m, 10m, "USD", 5m, 2m)], "TWD");
        result.Lines[0].OriginalNet.Should().Be(18m);
        result.Lines[0].OriginalTax.Should().Be(0.90m);
        result.Lines[0].BaseGross.Should().Be(38m);
    }

    [Fact]
    public void RejectsLineAndHeaderOverflow()
    {
        Action line = () => SalesQuotationAmounts.Calculate(
            [new(1, 1000m, 1_000_000_000_000m, 0m, "USD", 0m, 1m)], "USD");
        line.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.QUOTATION.AMOUNT_OVERFLOW");
        Action header = () => SalesQuotationAmounts.Calculate(
            [new(1, 1m, 600_000_000_000_000m, 0m, "USD", 0m, 1m),
             new(2, 1m, 600_000_000_000_000m, 0m, "USD", 0m, 1m)], "USD");
        header.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.QUOTATION.AMOUNT_OVERFLOW");
    }
}
