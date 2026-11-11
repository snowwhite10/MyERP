using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseReceiptReservationPlanTests
{
    private static readonly PurchaseReceiptOrderSnapshot Order = new(7, 5, "Approved", false);

    [Fact]
    public void SubmitAggregatesAndSortsWithoutChangingSources()
    {
        var sources = new[] { Source(12, 4m, 1m, 0m, 0m), Source(11, 10m, 2m, 1m, 0m) };
        var result = PurchaseReceiptReservationPlan.ForSubmit("Draft", 5, Draft(), Order, sources);
        result.Select(x => x.PurchaseOrderLineId).Should().Equal(11, 12);
        result.Select(x => (x.CurrentPending, x.TargetPending, x.Change))
            .Should().Equal((1m, 4m, 3m), (0m, 1m, 1m));
        sources[1].Line.PendingQuantity.Should().Be(1m);
    }

    [Fact]
    public void PendingReplayIsZeroChangeAndRejectReleasesExactOwnQuantity()
    {
        var sources = new[] { Source(12, 4m, 1m, 1m, 1m), Source(11, 10m, 2m, 4m, 3m) };
        PurchaseReceiptReservationPlan.ForSubmit("PendingApproval", 5, Draft(), Order, sources)
            .Should().OnlyContain(x => x.Change == 0m);
        var rejected = PurchaseReceiptReservationPlan.ForReject("PendingApproval", 5, Draft(), Order, sources);
        rejected.Select(x => (x.CurrentPending, x.TargetPending, x.Change))
            .Should().Equal((4m, 1m, -3m), (1m, 0m, -1m));
    }

    [Theory]
    [InlineData("Posted")]
    [InlineData("Rejected")]
    [InlineData("Draft")]
    public void RejectRequiresPendingApproval(string status)
    {
        Action act = () => PurchaseReceiptReservationPlan.ForReject(status, 5, Draft(), Order,
            [Source(11, 10m, 2m, 3m, 3m), Source(12, 4m, 1m, 1m, 1m)]);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void ShortageOrInconsistentOwnReservationRejectsEntirePlan()
    {
        Action shortage = () => PurchaseReceiptReservationPlan.ForSubmit("Draft", 5, Draft(), Order,
            [Source(11, 4m, 2m, 0m, 0m), Source(12, 4m, 1m, 0m, 0m)]);
        shortage.Should().Throw<BusinessRuleException>();
        Action stale = () => PurchaseReceiptReservationPlan.ForSubmit("PendingApproval", 5, Draft(), Order,
            [Source(11, 10m, 2m, 4m, 2m), Source(12, 4m, 1m, 1m, 1m)]);
        stale.Should().Throw<BusinessRuleException>();
        Action missing = () => PurchaseReceiptReservationPlan.ForReject("PendingApproval", 5, Draft(), Order,
            [Source(11, 10m, 2m, 3m, 3m)]);
        missing.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void RejectStillWorksWhenOtherPendingUsesRemainingCapacity()
    {
        var result = PurchaseReceiptReservationPlan.ForReject("PendingApproval", 5, Draft(), Order,
            [Source(11, 10m, 2m, 8m, 3m), Source(12, 4m, 1m, 3m, 1m)]);
        result.Select(x => x.TargetPending).Should().Equal(5m, 2m);
    }

    [Fact]
    public void InvalidIdentityPrecisionAndOverCapacityDoNotReturnPartialChanges()
    {
        var valid = new[] { Source(11, 10m, 2m, 0m, 0m), Source(12, 4m, 1m, 0m, 0m) };
        Action crossCompany = () => PurchaseReceiptReservationPlan.ForSubmit("Draft", 5, Draft(), Order,
            [valid[0], valid[1] with { Line = valid[1].Line with { CompanyId = 6 } }]);
        crossCompany.Should().Throw<BusinessRuleException>();
        Action precision = () => PurchaseReceiptReservationPlan.ForSubmit("Draft", 5, Draft(), Order,
            [valid[0], valid[1] with { CurrentReceiptReserved = 0.0000001m }]);
        precision.Should().Throw<BusinessRuleException>();
        Action stale = () => PurchaseReceiptReservationPlan.ForSubmit("Draft", 5, Draft(), Order,
            [valid[0], valid[1] with { CurrentReceiptReserved = 1m }]);
        stale.Should().Throw<BusinessRuleException>();
        Action capacity = () => PurchaseReceiptReservationPlan.ForSubmit("Draft", 5, Draft(), Order,
            [valid[0], valid[1] with { Line = valid[1].Line with { PendingQuantity = 3m } }]);
        capacity.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void ReturnedPlanCannotBeEditedAfterValidation()
    {
        var plan = PurchaseReceiptReservationPlan.ForSubmit("Draft", 5, Draft(), Order,
            [Source(11, 10m, 2m, 0m, 0m), Source(12, 4m, 1m, 0m, 0m)]);
        Action mutate = () => ((IList<PurchaseReceiptReservationChange>)plan)[0] = new(11, 0m, 9m);
        mutate.Should().Throw<NotSupportedException>();
    }

    private static PurchaseReceiptValidatedInput Draft() => PurchaseReceiptDraftInput.Validate(
        new(7, new DateOnly(2028, 2, 29), [new(2, 12, 1m), new(1, 11, 2m), new(3, 11, 1m)]));

    private static PurchaseReceiptReservationSource Source(long id, decimal approved, decimal posted,
        decimal pending, decimal own) => new(new(id, 7, 5, false, approved, posted, pending), own);
}
