using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesDeliveryBalanceTests
{
    [Fact]
    public void AggregatesRepeatedOrderLinesAtExactSixDecimalBalance()
    {
        SalesDeliveryBalance.Check(
            [new(10, 0.300000m), new(10, 0.200001m)],
            [new(10, 1.000001m, 0.25m, 0.25m)]);
    }

    [Fact]
    public void RejectsOneMicroUnitOverBalanceAfterAggregation()
    {
        Action act = () => SalesDeliveryBalance.Check(
            [new(10, 0.300000m), new(10, 0.200002m)],
            [new(10, 1.000001m, 0.25m, 0.25m)]);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED");
    }

    [Fact]
    public void FinalDecisionExcludesCurrentPendingReservation()
    {
        // The current 0.5 Pending quantity is the request, not OtherPending.
        SalesDeliveryBalance.Check([new(10, 0.5m)], [new(10, 1m, 0.25m, 0.25m)]);
    }

    [Fact]
    public void ChecksEachOrderLineSeparatelyAndDoesNotRoundAwayExcess()
    {
        Action act = () => SalesDeliveryBalance.Check(
            [new(10, 0.5m), new(20, 0.000002m)],
            [new(10, 1m, 0m, 0m), new(20, 0.000001m, 0m, 0m)]);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED");
    }

    [Fact]
    public void AcceptsMaximumLineCountAndRejectsOneMore()
    {
        var lines = Enumerable.Repeat(new SalesDeliveryQuantity(10, 0.000001m), 1000).ToArray();
        SalesDeliveryBalance.Check(lines, [new(10, 0.001m, 0m, 0m)]);
        Action tooMany = () => SalesDeliveryBalance.Check([.. lines, new(10, 0.000001m)],
            [new(10, 1m, 0m, 0m)]);
        tooMany.Should().Throw<RequestValidationException>().Which.Errors
            .Should().ContainKey("lines");
    }

    [Fact]
    public void RejectsInvalidInputsBeforeAnyBalanceDecision()
    {
        Action[] invalid =
        [
            () => SalesDeliveryBalance.Check([], [new(10, 1m, 0m, 0m)]),
            () => SalesDeliveryBalance.Check([new(10, 0.0000001m)], [new(10, 1m, 0m, 0m)]),
            () => SalesDeliveryBalance.Check([new(0, 1m)], [new(10, 1m, 0m, 0m)]),
            () => SalesDeliveryBalance.Check([new(10, 1m)], [new(10, 1m, -1m, 0m)]),
            () => SalesDeliveryBalance.Check([new(10, 1m)], [new(10, 1m, 0m, 0m), new(10, 1m, 0m, 0m)]),
            () => SalesDeliveryBalance.Check([new(10, 1m)], []),
            () => SalesDeliveryBalance.Check([new(10, 1m)], [new(10, 10_000_000_000_000m, 0m, 0m)])
        ];
        foreach (var act in invalid)
            act.Should().Throw<RequestValidationException>();
    }
}
