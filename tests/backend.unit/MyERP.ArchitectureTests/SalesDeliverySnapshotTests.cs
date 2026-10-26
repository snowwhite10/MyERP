using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesDeliverySnapshotTests
{
    [Fact]
    public void CountsPostedAndOtherPendingButNotCurrentPendingOrInactiveDocuments()
    {
        var balances = SalesDeliverySnapshot.Build(
            [new(10, 5m)],
            [new(1, 10, 1m, SalesDeliveryStatus.Posted, false),
             new(2, 10, 1m, SalesDeliveryStatus.Pending, false),
             new(3, 10, 1m, SalesDeliveryStatus.Pending, false),
             new(4, 10, 1m, SalesDeliveryStatus.Draft, false),
             new(5, 10, 1m, SalesDeliveryStatus.Rejected, false),
             new(6, 10, 1m, SalesDeliveryStatus.Posted, true)], 3);

        balances.Should().Equal(new SalesOrderLineBalance(10, 5m, 2m, 1m));
        SalesDeliveryBalance.Check([new(10, 2m)], balances);
    }

    [Fact]
    public void AggregatesSplitLinesAndAllPendingOnSubmit()
    {
        var balances = SalesDeliverySnapshot.Build(
            [new(10, 1.000003m)],
            [new(1, 10, 0.100001m, SalesDeliveryStatus.Posted, false),
             new(1, 10, 0.200001m, SalesDeliveryStatus.Posted, false),
             new(2, 10, 0.300001m, SalesDeliveryStatus.Pending, false)], null);

        balances.Should().Equal(new SalesOrderLineBalance(10, 1.000003m, 0.300002m, 0.300001m));
        SalesDeliveryBalance.Check([new(10, 0.400000m)], balances);
    }

    [Fact]
    public void RejectsMissingOrDuplicateOrderLineAndCorruptSavedQuantity()
    {
        Action[] invalid =
        [
            () => SalesDeliverySnapshot.Build([], [], null),
            () => SalesDeliverySnapshot.Build([new(10, 1m), new(10, 1m)], [], null),
            () => SalesDeliverySnapshot.Build([new(10, 1m)],
                [new(1, 20, 1m, SalesDeliveryStatus.Posted, false)], null),
            () => SalesDeliverySnapshot.Build([new(10, 1m)],
                [new(1, 10, 0.0000001m, SalesDeliveryStatus.Posted, false)], null),
            () => SalesDeliverySnapshot.Build([new(10, 1m)],
                [new(1, 10, 1m, SalesDeliveryStatus.Posted, false)], 1)
        ];
        foreach (var act in invalid)
            act.Should().Throw<RequestValidationException>();
    }

    [Fact]
    public void RejectsPersistedTotalsBeyondOrderQuantity()
    {
        Action act = () => SalesDeliverySnapshot.Build([new(10, 1m)],
            [new(1, 10, 0.75m, SalesDeliveryStatus.Posted, false),
             new(2, 10, 0.5m, SalesDeliveryStatus.Pending, false)], null);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED");
    }
}
