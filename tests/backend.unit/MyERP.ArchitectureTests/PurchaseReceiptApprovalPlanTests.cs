using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseReceiptApprovalPlanTests
{
    private static readonly PurchaseReceiptOrderSnapshot Order = new(7, 5, "Approved", false);

    [Fact]
    public void FinalApprovalAggregatesInSourceOrderAndMovesOnlyOwnReservation()
    {
        var sources = new[] { Source(12, 4m, 0m, 2m, 1m, 0m), Source(11, 10m, 2m, 5m, 3m, 0m) };
        var changes = PurchaseReceiptApprovalPlan.Create("PendingApproval", 5, Draft(), Order, sources);
        changes.Select(x => x.PurchaseOrderLineId).Should().Equal(11, 12);
        changes.Select(x => (x.CurrentPending, x.TargetPending, x.CurrentPosted, x.TargetPosted))
            .Should().Equal((5m, 2m, 2m, 5m), (2m, 1m, 0m, 1m));
        sources[1].Line.PendingQuantity.Should().Be(5m);
    }

    [Fact]
    public void PostedReplayNeedsMatchingPerSourcePostedEvidenceAndChangesNothing()
    {
        var sources = new[] { Source(12, 4m, 1m, 1m, 0m, 1m), Source(11, 10m, 5m, 2m, 0m, 3m) };
        var changes = PurchaseReceiptApprovalPlan.Create("Posted", 5, Draft(), Order, sources);
        changes.Should().OnlyContain(x => x.PendingChange == 0m && x.PostedChange == 0m);
        Action stale = () => PurchaseReceiptApprovalPlan.Create("Posted", 5, Draft(), Order,
            [sources[0], sources[1] with { CurrentReceiptPosted = 2m }]);
        stale.Should().Throw<BusinessRuleException>();
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Rejected")]
    [InlineData("Unknown")]
    public void OtherStatusesCannotApprove(string status)
    {
        Action act = () => PurchaseReceiptApprovalPlan.Create(status, 5, Draft(), Order,
            [Source(11, 10m, 2m, 3m, 3m, 0m), Source(12, 4m, 0m, 1m, 1m, 0m)]);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void InconsistentOwnEvidenceOrSourceRejectsEntirePlan()
    {
        var valid = new[] { Source(11, 10m, 2m, 3m, 3m, 0m), Source(12, 4m, 0m, 1m, 1m, 0m) };
        var invalid = new[]
        {
            new[] { valid[0], valid[1] with { CurrentReceiptReserved = 0m } },
            new[] { valid[0], valid[1] with { CurrentReceiptPosted = 1m } },
            new[] { valid[0], valid[1] with { Line = valid[1].Line with { CompanyId = 6 } } },
            new[] { valid[0], valid[1] with { Line = valid[1].Line with { PendingQuantity = 0m } } },
            new[] { valid[0], valid[0] }
        };
        foreach (var sources in invalid)
        {
            Action act = () => PurchaseReceiptApprovalPlan.Create("PendingApproval", 5, Draft(), Order, sources);
            act.Should().Throw<BusinessRuleException>();
        }
    }

    [Fact]
    public void FullCapacityAndOtherPendingStillAllowExactOwnMove()
    {
        var changes = PurchaseReceiptApprovalPlan.Create("PendingApproval", 5, Draft(), Order,
            [Source(11, 10m, 2m, 8m, 3m, 0m), Source(12, 4m, 0m, 4m, 1m, 0m)]);
        changes.Select(x => x.TargetPosted).Should().Equal(5m, 1m);
        changes.Select(x => x.TargetPending).Should().Equal(5m, 3m);
    }

    [Fact]
    public void PostedReplayRejectsMissingOrInvalidEvidenceWithoutReturningPartialPlan()
    {
        var valid = new[] { Source(11, 10m, 5m, 2m, 0m, 3m), Source(12, 4m, 1m, 1m, 0m, 1m) };
        var invalid = new[]
        {
            new[] { valid[0] },
            new[] { valid[0], valid[1] with { CurrentReceiptReserved = 1m } },
            new[] { valid[0], valid[1] with { CurrentReceiptPosted = 0.0000001m } },
            new[] { valid[0], valid[1] with { Line = valid[1].Line with { PostedQuantity = 0m } } },
            new[] { valid[0], valid[1] with { Line = valid[1].Line with { IsDeleted = true } } }
        };
        foreach (var sources in invalid)
        {
            Action act = () => PurchaseReceiptApprovalPlan.Create("Posted", 5, Draft(), Order, sources);
            act.Should().Throw<BusinessRuleException>();
        }
    }

    [Fact]
    public void ReturnedChangesAreReadOnly()
    {
        var changes = PurchaseReceiptApprovalPlan.Create("PendingApproval", 5, Draft(), Order,
            [Source(11, 10m, 2m, 3m, 3m, 0m), Source(12, 4m, 0m, 1m, 1m, 0m)]);
        Action mutate = () => ((IList<PurchaseReceiptApprovalChange>)changes)[0] = new(11, 0m, 0m, 0m, 0m);
        mutate.Should().Throw<NotSupportedException>();
    }

    private static PurchaseReceiptValidatedInput Draft() => PurchaseReceiptDraftInput.Validate(
        new(7, new DateOnly(2028, 2, 29), [new(2, 12, 1m), new(1, 11, 2m), new(3, 11, 1m)]));

    private static PurchaseReceiptApprovalSource Source(long id, decimal approved, decimal posted,
        decimal pending, decimal ownReserved, decimal ownPosted) =>
        new(new(id, 7, 5, false, approved, posted, pending), ownReserved, ownPosted);
}
