using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseReceiptAvailabilityTests
{
    private static readonly DateOnly Day = new(2028, 2, 29);

    [Fact]
    public void AggregatesRepeatedLinesAndAcceptsExactRemainingQuantity()
    {
        var draft = Draft([new(2, 12, 1m), new(1, 11, 2m), new(3, 11, 1m)]);
        var result = PurchaseReceiptAvailability.Check(5, draft,
            new(7, 5, "Approved", false),
            [new(12, 7, 5, false, 4m, 1m, 2m),
             new(11, 7, 5, false, 10m, 5m, 2m)]);
        result.Select(x => x.PurchaseOrderLineId).Should().Equal(11, 12);
        result.Select(x => x.RequestedQuantity).Should().Equal(3m, 1m);
        result.Select(x => x.AvailableQuantity).Should().Equal(3m, 1m);
    }

    [Theory]
    [InlineData("Draft", false)]
    [InlineData("Rejected", false)]
    [InlineData("Cancelled", false)]
    [InlineData("Approved", true)]
    public void RequiresApprovedOrder(string status, bool valid)
    {
        Action act = () => PurchaseReceiptAvailability.Check(5, Draft([new(1, 11, 1m)]),
            new(7, 5, status, false), [Source(11)]);
        if (valid) act.Should().NotThrow();
        else act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.RECEIPT.INVALID_SOURCE");
    }

    [Fact]
    public void RejectsMissingDuplicateExtraCrossCompanyAndDeletedSources()
    {
        var draft = Draft([new(1, 11, 1m)]);
        AssertInvalid(draft, new(7, 5, "Approved", false), []);
        AssertInvalid(draft, new(7, 5, "Approved", false), [Source(11), Source(11)]);
        AssertInvalid(draft, new(7, 5, "Approved", false), [Source(11), Source(12)]);
        AssertInvalid(draft, new(7, 5, "Approved", false), [Source(11) with { CompanyId = 6 }]);
        AssertInvalid(draft, new(7, 5, "Approved", false), [Source(11) with { IsDeleted = true }]);
        AssertInvalid(draft, new(7, 5, "Approved", false), [Source(11) with { PurchaseOrderId = 8 }]);
        AssertInvalid(draft, new(7, 6, "Approved", false), [Source(11)]);
        AssertInvalid(draft, new(7, 5, "Approved", true), [Source(11)]);
    }

    [Fact]
    public void RejectsOverQuantityAndInconsistentSourceWithoutPartialPlan()
    {
        var draft = Draft([new(1, 11, 1m), new(2, 12, 2m)]);
        Action shortage = () => PurchaseReceiptAvailability.Check(5, draft,
            new(7, 5, "Approved", false), [Source(11), Source(12) with { ApprovedQuantity = 1m }]);
        shortage.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.RECEIPT.QUANTITY_EXCEEDED");
        Action inconsistent = () => PurchaseReceiptAvailability.Check(5, draft,
            new(7, 5, "Approved", false), [Source(11), Source(12) with { PostedQuantity = 11m }]);
        inconsistent.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.RECEIPT.INVALID_SOURCE");
    }

    [Fact]
    public void RejectsAggregateOverflowAndInvalidSnapshotQuantities()
    {
        var draft = Draft([new(1, 11, 6_000_000_000_000m), new(2, 11, 5_000_000_000_000m)]);
        AssertInvalid(draft, new(7, 5, "Approved", false), [Source(11)]);
        AssertInvalid(Draft([new(1, 11, 1m)]), new(7, 5, "Approved", false),
            [Source(11) with { PendingQuantity = -1m }]);
        AssertInvalid(Draft([new(1, 11, 1m)]), new(7, 5, "Approved", false),
            [Source(11) with { ApprovedQuantity = 1.0000001m }]);
    }

    [Fact]
    public void ResultCannotBeMutatedAfterValidation()
    {
        var sources = new List<PurchaseReceiptOrderLineSnapshot> { Source(11) };
        var result = PurchaseReceiptAvailability.Check(5, Draft([new(1, 11, 1m)]),
            new(7, 5, "Approved", false), sources);
        sources[0] = Source(11) with { ApprovedQuantity = 2m };
        result[0].AvailableQuantity.Should().Be(10m);
        Action mutate = () => ((IList<PurchaseReceiptAvailabilityLine>)result)[0] = new(11, 1m, 2m);
        mutate.Should().Throw<NotSupportedException>();
    }

    private static PurchaseReceiptValidatedInput Draft(PurchaseReceiptDraftLine[] lines) =>
        PurchaseReceiptDraftInput.Validate(new(7, Day, lines));

    private static PurchaseReceiptOrderLineSnapshot Source(long id) => new(id, 7, 5, false, 10m, 0m, 0m);

    private static void AssertInvalid(PurchaseReceiptValidatedInput draft,
        PurchaseReceiptOrderSnapshot order, IReadOnlyList<PurchaseReceiptOrderLineSnapshot> sources)
    {
        Action act = () => PurchaseReceiptAvailability.Check(5, draft, order, sources);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.RECEIPT.INVALID_SOURCE");
    }
}
