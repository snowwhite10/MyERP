using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesDeliverySelectionTests
{
    [Theory]
    [InlineData(SalesDeliveryStatus.Draft)]
    [InlineData(SalesDeliveryStatus.Pending)]
    public void LockedDecisionRequiresBothIdentityAndFullBalance(SalesDeliveryStatus status)
    {
        SavedDeliverySelectedLine[] selected = [new(11, 7, 10, 1m, status, false)];
        SavedOrderLineQuantity[] order = [new(10, 2m)];
        SavedDeliveryLineQuantity[] saved =
        [
            new(8, 10, 1m, SalesDeliveryStatus.Posted, false, 90),
            new(7, 10, 1m, status, false, 11)
        ];

        Action valid = () => SalesDeliverySelection.CheckLockedDecision(
            selected, order, saved, 7, status);
        valid.Should().NotThrow();

        Action changedIdentity = () => SalesDeliverySelection.CheckLockedDecision(
            selected, order, [saved[0], saved[1] with { LineId = 12 }], 7, status);
        changedIdentity.Should().Throw<RequestValidationException>();

        Action exceeded = () => SalesDeliverySelection.CheckLockedDecision(
            selected, [new(10, 1.999999m)], saved, 7, status);
        exceeded.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED");
    }

    [Fact]
    public void LockedDecisionRejectsUnsupportedState()
    {
        Action act = () => SalesDeliverySelection.CheckLockedDecision(
            [new(11, 7, 10, 1m, SalesDeliveryStatus.Posted, false)],
            [new(10, 1m)],
            [new(7, 10, 1m, SalesDeliveryStatus.Posted, false, 11)],
            7, SalesDeliveryStatus.Posted);
        act.Should().Throw<RequestValidationException>();
    }

    [Theory]
    [InlineData(SalesDeliveryStatus.Draft)]
    [InlineData(SalesDeliveryStatus.Pending)]
    public void MatchingSnapshotAcceptsSamePersistedRowsInAnyOrder(SalesDeliveryStatus status)
    {
        SavedDeliverySelectedLine[] selected =
        [
            new(11, 7, 20, 0.000001m, status, false),
            new(12, 7, 10, 1m, status, false)
        ];
        SavedDeliveryLineQuantity[] snapshot =
        [
            new(7, 10, 1m, status, false, 12),
            new(8, 10, 1m, SalesDeliveryStatus.Posted, false, 90),
            new(7, 20, 0.000001m, status, false, 11)
        ];

        Action act = () => SalesDeliverySelection.CheckSnapshotMatchesSelection(
            selected, snapshot, 7, status);
        act.Should().NotThrow();
    }

    [Fact]
    public void MatchingSnapshotRejectsMissingExtraDuplicateOrChangedCurrentRows()
    {
        SavedDeliverySelectedLine first = new(11, 7, 10, 1m,
            SalesDeliveryStatus.Pending, false);
        SavedDeliverySelectedLine second = new(12, 7, 20, 0.000001m,
            SalesDeliveryStatus.Pending, false);
        SavedDeliveryLineQuantity a = new(7, 10, 1m,
            SalesDeliveryStatus.Pending, false, 11);
        SavedDeliveryLineQuantity b = new(7, 20, 0.000001m,
            SalesDeliveryStatus.Pending, false, 12);
        SavedDeliveryLineQuantity unrelated = new(8, 10, 1m,
            SalesDeliveryStatus.Posted, false, 90);

        SavedDeliveryLineQuantity[][] invalid =
        [
            [], [a], [a, b, b], [a, a],
            [a, b with { LineId = 0 }],
            [a, b with { OrderLineId = 10 }],
            [a, b with { Quantity = 0.000002m }],
            [a, b with { Status = SalesDeliveryStatus.Draft }],
            [a, b with { IsDeleted = true }],
            [a, b with { DeliveryId = 8 }, unrelated]
        ];
        foreach (var rows in invalid)
        {
            Action act = () => SalesDeliverySelection.CheckSnapshotMatchesSelection(
                [first, second], rows, 7, SalesDeliveryStatus.Pending);
            act.Should().Throw<RequestValidationException>();
        }
    }

    [Fact]
    public void MatchingSnapshotAcceptsExactlyOneThousandCurrentRows()
    {
        var selected = Enumerable.Range(1, 1000)
            .Select(id => new SavedDeliverySelectedLine(id, 7, 10, 0.000001m,
                SalesDeliveryStatus.Draft, false)).ToArray();
        var snapshot = selected.Select(row => new SavedDeliveryLineQuantity(
            row.DeliveryId, row.OrderLineId, row.Quantity, row.Status,
            row.IsDeleted, row.LineId)).ToArray();

        Action act = () => SalesDeliverySelection.CheckSnapshotMatchesSelection(
            selected, snapshot, 7, SalesDeliveryStatus.Draft);
        act.Should().NotThrow();
    }
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
