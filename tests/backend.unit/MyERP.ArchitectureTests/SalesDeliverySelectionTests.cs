using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesDeliverySelectionTests
{
    [Theory]
    [InlineData(SalesDeliveryStatus.Draft)]
    [InlineData(SalesDeliveryStatus.Pending)]
    public void CompleteSavedSelectionReturnsAscendingDistinctOrderLineLocks(SalesDeliveryStatus status)
    {
        SavedDeliverySelectedLine[] rows =
        [
            new(30, 7, 20, 1m, status, false),
            new(10, 7, 10, 1m, status, false),
            new(20, 7, 20, 1m, status, false)
        ];

        SalesDeliverySelection.CreateLockPlan(rows, 3, 7, status).Should().Equal(10L, 20L);
    }

    [Fact]
    public void RejectsIncompleteOrSubstitutedSavedSelection()
    {
        SavedDeliverySelectedLine good = new(10, 7, 20, 1m, SalesDeliveryStatus.Draft, false);
        Action[] invalid =
        [
            () => SalesDeliverySelection.CreateLockPlan([], 0, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good], 2, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good], 0, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good, good], 2, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good with { LineId = 0 }], 1, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good with { DeliveryId = 8 }], 1, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good with { IsDeleted = true }], 1, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good with { Status = SalesDeliveryStatus.Pending }], 1, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good with { Quantity = 0.0000001m }], 1, 7, SalesDeliveryStatus.Draft),
            () => SalesDeliverySelection.CreateLockPlan([good], 1, 7, SalesDeliveryStatus.Posted),
            () => SalesDeliverySelection.CreateLockPlan([good], 1001, 7, SalesDeliveryStatus.Draft)
        ];

        foreach (var act in invalid)
            act.Should().Throw<RequestValidationException>();
    }
}
