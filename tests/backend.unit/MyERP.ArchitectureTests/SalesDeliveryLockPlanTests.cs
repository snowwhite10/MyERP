using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesDeliveryLockPlanTests
{
    [Theory]
    [InlineData(SalesDeliveryStatus.Draft)]
    [InlineData(SalesDeliveryStatus.Pending)]
    public void LocksDistinctSavedOrderLinesInAscendingOrder(SalesDeliveryStatus status)
    {
        SavedDeliveryLineQuantity[] saved =
        [
            new(7, 30, 1m, status, false),
            new(7, 10, 1m, status, false),
            new(7, 30, 1m, status, false),
            new(7, 20, 1m, status, false)
        ];

        SalesDeliveryLockPlan.Create(saved, 7, status).Should().Equal(10L, 20L, 30L);
    }

    [Fact]
    public void RejectsMissingForeignDeletedOrMixedSavedRows()
    {
        Action[] invalid =
        [
            () => SalesDeliveryLockPlan.Create([], 7, SalesDeliveryStatus.Draft),
            () => SalesDeliveryLockPlan.Create([new(8, 10, 1m, SalesDeliveryStatus.Draft, false)], 7, SalesDeliveryStatus.Draft),
            () => SalesDeliveryLockPlan.Create([new(7, 10, 1m, SalesDeliveryStatus.Draft, true)], 7, SalesDeliveryStatus.Draft),
            () => SalesDeliveryLockPlan.Create([new(7, 10, 1m, SalesDeliveryStatus.Pending, false)], 7, SalesDeliveryStatus.Draft),
            () => SalesDeliveryLockPlan.Create([new(7, 10, 1m, SalesDeliveryStatus.Draft, false),
                new(7, 20, 1m, SalesDeliveryStatus.Pending, false)], 7, SalesDeliveryStatus.Draft)
        ];
        foreach (var act in invalid)
            act.Should().Throw<RequestValidationException>();
    }

    [Fact]
    public void RejectsInvalidIdentifiersQuantitiesAndLineCount()
    {
        var maximum = Enumerable.Range(0, 1000)
            .Select(_ => new SavedDeliveryLineQuantity(7, 10, 1m, SalesDeliveryStatus.Draft, false)).ToArray();
        SalesDeliveryLockPlan.Create(maximum, 7, SalesDeliveryStatus.Draft).Should().Equal(10L);

        Action[] invalid =
        [
            () => SalesDeliveryLockPlan.Create([new(7, 0, 1m, SalesDeliveryStatus.Draft, false)], 7, SalesDeliveryStatus.Draft),
            () => SalesDeliveryLockPlan.Create([new(7, 10, 0m, SalesDeliveryStatus.Draft, false)], 7, SalesDeliveryStatus.Draft),
            () => SalesDeliveryLockPlan.Create([new(7, 10, 0.0000001m, SalesDeliveryStatus.Draft, false)], 7, SalesDeliveryStatus.Draft),
            () => SalesDeliveryLockPlan.Create([new(7, 10, 1m, SalesDeliveryStatus.Draft, false)], 0, SalesDeliveryStatus.Draft),
            () => SalesDeliveryLockPlan.Create([new(7, 10, 1m, SalesDeliveryStatus.Posted, false)], 7, SalesDeliveryStatus.Posted),
            () => SalesDeliveryLockPlan.Create(Enumerable.Range(0, 1001)
                .Select(_ => new SavedDeliveryLineQuantity(7, 10, 1m, SalesDeliveryStatus.Draft, false)).ToArray(),
                7, SalesDeliveryStatus.Draft)
        ];
        foreach (var act in invalid)
            act.Should().Throw<RequestValidationException>();
    }
}
