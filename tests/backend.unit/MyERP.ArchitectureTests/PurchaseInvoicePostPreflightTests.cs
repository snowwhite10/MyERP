using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoicePostPreflightTests
{
    private static readonly PurchaseInvoiceIdentity Invoice = new(1, 2, 3, "TWD");
    private static readonly PurchaseInvoiceOrderIdentity Order = new(1, 2, 3, "TWD", "Approved", false);
    private static PurchaseInvoiceMatchLine Line(int no = 1, long receiptId = 10, decimal invoiceNet = 2) =>
        new(receiptId, new(no, 1, 2, 0, 2, 0, invoiceNet, 0, invoiceNet));
    private static PurchaseInvoiceReceiptIdentity Receipt(long id = 10, string status = "Posted") =>
        new(id, 1, 2, 3, 3, 4, 4, "TWD", status, false);
    private static PurchaseInvoicePostingSource Source(long id = 10, decimal reserved = 1,
        decimal posted = 0, decimal ownPosted = 0) =>
        new(id, new(3, 0, 0, posted, reserved, reserved), ownPosted);

    [Fact]
    public void MatchedZeroDifferenceProducesWholePostingPlan()
    {
        var result = PurchaseInvoicePostPreflight.Check("Matched", [Line()], Invoice, Order,
            [Receipt()], [Source()], []);
        result.IsReplay.Should().BeFalse();
        result.Changes.Should().ContainSingle().Which.InvoicedChange.Should().Be(1);
    }

    [Fact]
    public void AcceptedSavedDifferenceAllowsPostingWithoutChangingInvoiceAmount()
    {
        var difference = new PurchaseInvoiceAmountDifference("NET", 2, 2.0001m, 0.0001m);
        var result = PurchaseInvoicePostPreflight.Check("Matched", [Line(invoiceNet: 2.0001m)],
            Invoice, Order, [Receipt()], [Source()],
            [new(1, "Accept", "approved", [difference,
                new("GROSS", 2, 2.0001m, 0.0001m)])]);
        result.Changes.Should().ContainSingle();
    }

    [Fact]
    public void PostedReplayRequiresProofAndDoesNotReapplyQuantity()
    {
        var source = new PurchaseInvoicePostingSource(10, new(3, 0, 0, 1, 0, 0), 1);
        var result = PurchaseInvoicePostPreflight.Check("Posted", [Line()], Invoice, Order,
            [Receipt()], [source], []);
        result.IsReplay.Should().BeTrue();
        result.Changes.Should().OnlyContain(x => x.InvoicedChange == 0 && x.ReservedChange == 0);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Disputed")]
    [InlineData("Rejected")]
    public void UnpostableStatusRejects(string status)
    {
        Action act = () => PurchaseInvoicePostPreflight.Check(status, [Line()], Invoice, Order,
            [Receipt()], [Source()], []);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void ChangedSourceIdentityRejectsBeforePostingPlan()
    {
        Action act = () => PurchaseInvoicePostPreflight.Check("Matched", [Line()], Invoice, Order,
            [Receipt(status: "Draft")], [Source()], []);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_SOURCE_IDENTITY");
    }

    [Fact]
    public void NewDifferenceWithoutSavedAcceptanceRejects()
    {
        Action act = () => PurchaseInvoicePostPreflight.Check("Matched", [Line(invoiceNet: 2.0001m)],
            Invoice, Order, [Receipt()], [Source()], []);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void StaleOrRejectedDifferenceEvidenceRejects()
    {
        var stale = new PurchaseInvoiceAmountDifference("NET", 2, 2.0002m, 0.0002m);
        Action staleAct = () => PurchaseInvoicePostPreflight.Check("Matched", [Line(invoiceNet: 2.0001m)],
            Invoice, Order, [Receipt()], [Source()], [new(1, "Accept", "ok", [stale])]);
        Action rejectedAct = () => PurchaseInvoicePostPreflight.Check("Matched", [Line(invoiceNet: 2.0001m)],
            Invoice, Order, [Receipt()], [Source()], [new(1, "Reject", "no", [stale])]);
        staleAct.Should().Throw<BusinessRuleException>();
        rejectedAct.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void AllSourcesMustBeValidBeforeAnyPlanIsReturned()
    {
        Action act = () => PurchaseInvoicePostPreflight.Check("Matched", [Line(), Line(2, 20)],
            Invoice, Order, [Receipt(), Receipt(20)], [Source(), Source(20, reserved: 0)], []);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void DuplicateDifferenceDecisionAndUnexpectedDecisionReject()
    {
        var diff = new PurchaseInvoiceAmountDifference("NET", 2, 2.0001m, 0.0001m);
        var gross = new PurchaseInvoiceAmountDifference("GROSS", 2, 2.0001m, 0.0001m);
        var accepted = new PurchaseInvoiceResolvedLine(1, "Accept", "ok", [diff, gross]);
        Action duplicate = () => PurchaseInvoicePostPreflight.Check("Matched",
            [Line(invoiceNet: 2.0001m)], Invoice, Order, [Receipt()], [Source()], [accepted, accepted]);
        Action extra = () => PurchaseInvoicePostPreflight.Check("Matched", [Line()],
            Invoice, Order, [Receipt()], [Source()], [accepted]);
        duplicate.Should().Throw<BusinessRuleException>();
        extra.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void PostedReplayWithoutOwnProofAndInvalidSavedLineReject()
    {
        Action noProof = () => PurchaseInvoicePostPreflight.Check("Posted", [Line()],
            Invoice, Order, [Receipt()], [Source(reserved: 0, posted: 1)], []);
        Action invalidLine = () => PurchaseInvoicePostPreflight.Check("Matched", [Line(invoiceNet: -1)],
            Invoice, Order, [Receipt()], [Source()], []);
        noProof.Should().Throw<BusinessRuleException>();
        invalidLine.Should().Throw<BusinessRuleException>();
    }
}
