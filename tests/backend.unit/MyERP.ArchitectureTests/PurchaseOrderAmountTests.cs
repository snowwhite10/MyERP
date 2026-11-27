using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseOrderAmountTests
{
    [Fact]
    public void RoundsEachLineBeforeAddingHeaderAmounts()
    {
        var lines = new[]
        {
            new PurchaseOrderAmountInput(1, 1m, 0.005m, "USD", 0m, 1m),
            new PurchaseOrderAmountInput(2, 1m, 0.005m, "USD", 0m, 1m)
        };

        var result = PurchaseOrderAmounts.Calculate(lines, "USD");

        result.Lines.Select(x => x.BaseNet).Should().Equal(0.01m, 0.01m);
        result.BaseNet.Should().Be(0.02m);
        result.BaseGross.Should().Be(0.02m);
    }

    [Fact]
    public void AppliesExchangeRateAndInputTaxPerLine()
    {
        var result = PurchaseOrderAmounts.Calculate(
            [new(1, 2m, 10.125m, "USD", 5m, 2m)], "TWD");

        result.Lines[0].BaseNet.Should().Be(41m);
        result.Lines[0].BaseTax.Should().Be(2m);
        result.Lines[0].BaseGross.Should().Be(43m);
    }

    [Fact]
    public void RejectsOverflowInsteadOfReturningTruncatedMoney()
    {
        var line = new PurchaseOrderAmountInput(1, 1000m, 1000000000000m,
            "USD", 0m, 1m);

        Action action = () => PurchaseOrderAmounts.Calculate([line], "USD");

        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.ORDER.AMOUNT_OVERFLOW");
    }

    [Fact]
    public void RejectsHeaderOverflowEvenWhenEachLineFits()
    {
        var lines = Enumerable.Range(1, 2)
            .Select(n => new PurchaseOrderAmountInput(n, 1m, 600000000000000m,
                "USD", 0m, 1m)).ToArray();

        Action action = () => PurchaseOrderAmounts.Calculate(lines, "USD");

        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.ORDER.AMOUNT_OVERFLOW");
    }

    [Fact]
    public void SumStorableAddsSavedAndRecalculatedLinesAndRejectsTotalsAtTheLimit()
    {
        PurchaseOrderAmounts.SumStorable([(50m, 3m, 53m), (20m, 2m, 22m)])
            .Should().Be((70m, 5m, 75m));
        PurchaseOrderAmounts.SumStorable([(999_999_999_999_999.9999m, 0m, 999_999_999_999_999.9999m)])
            .Net.Should().Be(999_999_999_999_999.9999m);

        Action atLimit = () => PurchaseOrderAmounts.SumStorable(
            [(900_000_000_000_000m, 0m, 900_000_000_000_000m), (100_000_000_000_000m, 0m, 100_000_000_000_000m)]);
        Action decimalOverflow = () => PurchaseOrderAmounts.SumStorable(
            [(decimal.MaxValue, 0m, 0m), (decimal.MaxValue, 0m, 0m)]);

        atLimit.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.ORDER.AMOUNT_OVERFLOW");
        decimalOverflow.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.ORDER.AMOUNT_OVERFLOW");
    }
}
