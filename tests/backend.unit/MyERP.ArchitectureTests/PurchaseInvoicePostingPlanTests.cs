using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoicePostingPlanTests
{
    private static PurchaseInvoiceReceiptRequest Request(long id, decimal quantity) => new(id, quantity);
    private static PurchaseInvoicePostingSource Source(long id, decimal received, decimal invoiced,
        decimal reserved, decimal currentReserved, decimal currentPosted = 0,
        decimal returned = 0) => new(id,
            new(received, returned, 0, invoiced, reserved, currentReserved), currentPosted);

    [Fact]
    public void MatchedPostingTransfersAggregatedReservationExactlyOnce()
    {
        var plan = PurchaseInvoicePostingPlan.Create("Matched",
            [Request(20, 1), Request(10, 0.5m), Request(10, 1)],
            [Source(20, 3, 0, 1, 1), Source(10, 3, 0, 1.5m, 1.5m)]);
        plan.Select(x => x.ReceiptLineId).Should().Equal(10, 20);
        plan.Select(x => x.ReservedChange).Should().Equal(-1.5m, -1m);
        plan.Select(x => x.InvoicedChange).Should().Equal(1.5m, 1m);
        plan.Should().OnlyContain(x => x.TargetReserved == 0 &&
            x.TargetInvoiced == x.CurrentInvoiced + x.InvoicedChange);
    }

    [Fact]
    public void PostedReplayWithOwnPostedQuantityHasZeroChanges()
    {
        var plan = PurchaseInvoicePostingPlan.Create("Posted", [Request(10, 1.5m)],
            [Source(10, 2, 1.5m, 0, 0, 1.5m)]);
        plan.Should().ContainSingle().Which.Should().Match<PurchaseInvoicePostingChange>(x =>
            x.ReservedChange == 0 && x.InvoicedChange == 0);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Disputed")]
    [InlineData("Rejected")]
    public void NonPostableStatusRejects(string status)
    {
        Action act = () => PurchaseInvoicePostingPlan.Create(status, [Request(10, 1)],
            [Source(10, 2, 0, 1, 1)]);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void ShortageAndStaleReservationRejectWholePlan()
    {
        Action shortage = () => PurchaseInvoicePostingPlan.Create("Matched",
            [Request(10, 1), Request(20, 1)],
            [Source(10, 2, 0, 1, 1), Source(20, 1, 0, 1, 1, returned: 0.5m)]);
        shortage.Should().Throw<BusinessRuleException>();
        Action stale = () => PurchaseInvoicePostingPlan.Create("Matched", [Request(10, 1)],
            [Source(10, 2, 0, 0.5m, 0.5m)]);
        stale.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void ReplayRequiresOwnPostedProofAndNoReservation()
    {
        Action missingProof = () => PurchaseInvoicePostingPlan.Create("Posted", [Request(10, 1)],
            [Source(10, 2, 1, 0, 0)]);
        missingProof.Should().Throw<BusinessRuleException>();
        Action doubleCount = () => PurchaseInvoicePostingPlan.Create("Posted", [Request(10, 1)],
            [Source(10, 2, 1, 1, 1, 1)]);
        doubleCount.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void MissingDuplicateOrExtraAuthorityRejectsWholePlan()
    {
        var requests = new[] { Request(10, 1), Request(20, 1) };
        Action missing = () => PurchaseInvoicePostingPlan.Create("Matched", requests,
            [Source(10, 2, 0, 1, 1)]);
        Action duplicate = () => PurchaseInvoicePostingPlan.Create("Matched", requests,
            [Source(10, 2, 0, 1, 1), Source(10, 2, 0, 1, 1)]);
        Action extra = () => PurchaseInvoicePostingPlan.Create("Matched", [Request(10, 1)],
            [Source(10, 2, 0, 1, 1), Source(20, 2, 0, 1, 1)]);
        missing.Should().Throw<BusinessRuleException>();
        duplicate.Should().Throw<BusinessRuleException>();
        extra.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void InvalidPostedProofAndRequestPrecisionReject()
    {
        Action impossibleProof = () => PurchaseInvoicePostingPlan.Create("Posted", [Request(10, 1)],
            [Source(10, 2, 0.5m, 0, 0, 1)]);
        Action invalidPrecision = () => PurchaseInvoicePostingPlan.Create("Matched",
            [Request(10, 1.0000001m)], [Source(10, 2, 0, 1, 1)]);
        impossibleProof.Should().Throw<BusinessRuleException>();
        invalidPrecision.Should().Throw<BusinessRuleException>();
    }
}
