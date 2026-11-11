using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceMatchTests
{
    private static readonly PurchaseInvoiceIdentity Invoice = new(1, 2, 3, "TWD");
    private static readonly PurchaseInvoiceOrderIdentity Order = new(1, 2, 3, "TWD", "Approved", false);

    private static PurchaseInvoiceMatchLine Line(int no = 1, long receiptId = 10, decimal quantity = 1,
        decimal invoiceNet = 2) => new(receiptId,
            new(no, quantity, 2, 0, 2, 0, invoiceNet, 0, invoiceNet));

    private static PurchaseInvoiceReceiptIdentity Receipt(long id = 10) =>
        new(id, 1, 2, 3, 3, 4, 4, "TWD", "Posted", false);

    private static PurchaseInvoiceReceiptSource Source(long id = 10, decimal received = 1) =>
        new(id, new(received, 0, 0, 0, 0, 0));

    [Fact]
    public void MatchesCompleteAuthoritativeSourcesAndKeepsOrderedEvidence()
    {
        var result = PurchaseInvoiceMatch.Check("Draft", [Line(2, 20), Line()], Invoice, Order,
            [Receipt(20), Receipt()], [Source(20), Source()]);

        result.Status.Should().Be("Matched");
        result.Shortages.Should().BeEmpty();
        result.Amount.Should().NotBeNull();
        result.Amount!.Lines.Select(x => x.LineNo).Should().Equal(1, 2);
    }

    [Fact]
    public void PriceDifferenceDisputesWholeInvoiceWithoutPartialMatch()
    {
        var result = PurchaseInvoiceMatch.Check("Draft", [Line(), Line(2, 20, invoiceNet: 2.0001m)],
            Invoice, Order, [Receipt(), Receipt(20)], [Source(), Source(20)]);

        result.Status.Should().Be("Disputed");
        result.Amount!.Lines[0].Differences.Should().BeEmpty();
        result.Amount.Lines[1].Differences.Should().HaveCount(2);
    }

    [Fact]
    public void RepeatedReceiptUsesAggregateQuantityAndReportsShortageBeforeAmountDifference()
    {
        var result = PurchaseInvoiceMatch.Check("Draft", [Line(), Line(2, invoiceNet: 2.0001m)],
            Invoice, Order, [Receipt()], [Source(received: 1.5m)]);

        result.Status.Should().Be("Shortage");
        result.Shortages.Should().ContainSingle().Which.Requested.Should().Be(2);
        result.Amount.Should().BeNull();
    }

    [Fact]
    public void SourceIdentityFailsBeforeQuantityAndAmount()
    {
        var badReceipt = Receipt() with { SupplierId = 99 };
        Action action = () => PurchaseInvoiceMatch.Check("Draft", [Line(invoiceNet: -1)],
            Invoice, Order, [badReceipt], [Source(received: 0)]);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_IDENTITY");
    }

    [Fact]
    public void InvalidRequestFailsBeforeIdentityAndCannotUseDifferentAmountQuantity()
    {
        Action invalid = () => PurchaseInvoiceMatch.Check("Draft", [Line(quantity: 0)],
            Invoice, Order, [], []);
        invalid.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");
    }

    [Theory]
    [InlineData("Matched")]
    [InlineData("Disputed")]
    [InlineData("Posted")]
    public void RejectsNonEditableStatus(string status)
    {
        Action action = () => PurchaseInvoiceMatch.Check(status, [Line()], Invoice, Order,
            [Receipt()], [Source()]);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_MATCH_STATUS");
    }

    [Fact]
    public void RejectsMissingOrExtraAuthorityAndInvalidAmountAfterQuantity()
    {
        Action missing = () => PurchaseInvoiceMatch.Check("Draft", [Line()], Invoice, Order,
            [Receipt()], []);
        missing.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_QUANTITY");

        Action amount = () => PurchaseInvoiceMatch.Check("Draft", [Line(invoiceNet: -1)],
            Invoice, Order, [Receipt()], [Source()]);
        amount.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_AMOUNT_SOURCE");
    }

    [Fact]
    public void ReturnsAllShortagesInReceiptOrderAndNeverReturnsPartialAmountEvidence()
    {
        var result = PurchaseInvoiceMatch.Check("Rejected", [Line(2, 20), Line()],
            Invoice, Order, [Receipt(20), Receipt()], [Source(20, 0), Source(10, 0)]);

        result.Status.Should().Be("Shortage");
        result.Shortages.Select(x => x.ReceiptLineId).Should().Equal(10, 20);
        result.Amount.Should().BeNull();
    }

    [Fact]
    public void RejectsEmptyInvoiceAndInconsistentQuantityEvenWhenAmountIsDisputed()
    {
        Action empty = () => PurchaseInvoiceMatch.Check("Draft", [], Invoice, Order, [], []);
        empty.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");

        Action inconsistent = () => PurchaseInvoiceMatch.Check("Draft", [Line(invoiceNet: 2.0001m)],
            Invoice, Order, [Receipt()], [Source(received: -1)]);
        inconsistent.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_QUANTITY");
    }
}
