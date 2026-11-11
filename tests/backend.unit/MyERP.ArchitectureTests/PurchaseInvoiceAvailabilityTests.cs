using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceAvailabilityTests
{
    [Fact]
    public void ChecksWholeInvoiceAfterAggregatingAndAcceptsExactRemainder()
    {
        var decision = PurchaseInvoiceAvailability.CheckRequested(
            [new(8, 1m), new(3, 2m), new(8, 1.5m)],
            [new PurchaseInvoiceReceiptSource(8, new(3, 0, 0, 0, 0.5m, 0)),
                new PurchaseInvoiceReceiptSource(3, new(4, 1, 0, 1, 0, 0))]);

        decision.CanMatch.Should().BeTrue();
        decision.Shortages.Should().BeEmpty();
    }

    [Fact]
    public void ReturnsAllShortagesInSourceOrderWithoutPartialSuccess()
    {
        var decision = PurchaseInvoiceAvailability.CheckRequested(
            [new(9, 3m), new(2, 2m), new(5, 1m)],
            [new PurchaseInvoiceReceiptSource(9, new(2, 0, 0, 0, 0, 0)),
                new PurchaseInvoiceReceiptSource(5, new(1, 0, 0, 0, 0, 0)),
                new PurchaseInvoiceReceiptSource(2, new(1, 0, 0, 0, 0, 0))]);

        decision.CanMatch.Should().BeFalse();
        decision.Shortages.Should().Equal(new PurchaseInvoiceShortage(2, 2m, 1m),
            new PurchaseInvoiceShortage(9, 3m, 2m));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    [InlineData("zero-id")]
    [InlineData("invalid-quantity")]
    public void RejectsUntrustworthySourceSet(string variant)
    {
        PurchaseInvoiceReceiptSource[] sources = variant switch
        {
            "missing" => [],
            "duplicate" => [new(1, new(1, 0, 0, 0, 0, 0)), new(1, new(1, 0, 0, 0, 0, 0))],
            "extra" => [new(1, new(1, 0, 0, 0, 0, 0)), new(2, new(1, 0, 0, 0, 0, 0))],
            "zero-id" => [new(0, new(1, 0, 0, 0, 0, 0))],
            _ => [new(1, new(0, 0, 0, 0, 1, 0))]
        };
        Action action = () => PurchaseInvoiceAvailability.CheckRequested([new(1, 1m)], sources);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_QUANTITY");
    }

    [Fact]
    public void InvalidRequestTakesPriorityOverMissingSources()
    {
        Action action = () => PurchaseInvoiceAvailability.CheckRequested([new(1, 0m)], []);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");
    }

    [Fact]
    public void LateInvalidSourceRejectsWholeDecisionInsteadOfReturningShortages()
    {
        Action action = () => PurchaseInvoiceAvailability.CheckRequested(
            [new(1, 2m), new(2, 1m)],
            [new PurchaseInvoiceReceiptSource(1, new(1, 0, 0, 0, 0, 0)),
                new PurchaseInvoiceReceiptSource(2, new(1, 0, 0, 0, 2, 0))]);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_QUANTITY");
    }

    [Fact]
    public void AggregatesRepeatedReceiptLinesBeforeAvailabilityCheck()
    {
        var requested = PurchaseInvoiceAvailability.AggregateRequested([
            new(8, 1.25m), new(3, 2m), new(8, 1.5m)]);
        requested.Should().Equal(new PurchaseInvoiceReceiptRequest(3, 2m),
            new PurchaseInvoiceReceiptRequest(8, 2.75m));
        PurchaseInvoiceAvailability.CanMatch(new(3, 0, 0, 0, 0, 0), requested[1].Quantity)
            .Should().BeTrue();
        PurchaseInvoiceAvailability.CanMatch(new(2.5m, 0, 0, 0, 0, 0), requested[1].Quantity)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 0.0000001)]
    public void RejectsInvalidReceiptRequest(long receiptLineId, decimal quantity)
    {
        Action action = () => PurchaseInvoiceAvailability.AggregateRequested([
            new(receiptLineId, quantity)]);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");
    }

    [Fact]
    public void RejectsAggregateOverflowAndEmptyRequest()
    {
        Action overflow = () => PurchaseInvoiceAvailability.AggregateRequested([
            new(1, 9_999_999_999_999m), new(1, 1m)]);
        overflow.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");
        Action empty = () => PurchaseInvoiceAvailability.AggregateRequested([]);
        empty.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");
    }

    [Fact]
    public void AllowsLargestRepresentableAggregatedQuantity()
    {
        var requested = PurchaseInvoiceAvailability.AggregateRequested([
            new(7, 9_999_999_999_998m), new(7, 1.999999m)]);
        requested.Should().ContainSingle()
            .Which.Quantity.Should().Be(9_999_999_999_999.999999m);
    }

    [Theory]
    [InlineData(10, 2, 1, 3, 2, 0, 2)]
    [InlineData(10, 2, 1, 3, 2, 1, 3)]
    public void CalculatesNetAvailableAndExcludesOnlyOwnReservation(
        decimal received, decimal returned, decimal pendingReturn, decimal postedInvoice,
        decimal matchedReserve, decimal ownReserve, decimal expected)
    {
        var available = PurchaseInvoiceAvailability.Calculate(new(received, returned,
            pendingReturn, postedInvoice, matchedReserve, ownReserve));
        available.Should().Be(expected);
    }

    [Fact]
    public void AllowsExactRemainderButRejectsOneUnitAbove()
    {
        var source = new PurchaseInvoiceSourceQuantities(10, 2, 1, 3, 2, 0);
        PurchaseInvoiceAvailability.CanMatch(source, 2m).Should().BeTrue();
        PurchaseInvoiceAvailability.CanMatch(source, 2.000001m).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0, 0)]
    [InlineData(1, 0, 0, 0, 0, 1)]
    [InlineData(1, 0, 0, 0, 1, 2)]
    [InlineData(1, 0, 0, 0, 0.0000001, 0)]
    public void RejectsInvalidOrInconsistentSourceSnapshot(decimal received, decimal returned,
        decimal pendingReturn, decimal postedInvoice, decimal matchedReserve, decimal ownReserve)
    {
        Action action = () => PurchaseInvoiceAvailability.Calculate(new(received, returned,
            pendingReturn, postedInvoice, matchedReserve, ownReserve));
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_QUANTITY");
    }

    [Fact]
    public void RejectsOverConsumedSourceAndInvalidRequestedQuantity()
    {
        Action overConsumed = () => PurchaseInvoiceAvailability.Calculate(new(1, 0, 0, 0, 2, 0));
        overConsumed.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_QUANTITY");
        Action zeroRequest = () => PurchaseInvoiceAvailability.CanMatch(new(1, 0, 0, 0, 0, 0), 0);
        zeroRequest.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.0000001)]
    [InlineData(10000000000000)]
    public void RejectsRequestedQuantityOutsideSqlPrecision(decimal requested)
    {
        Action action = () => PurchaseInvoiceAvailability.CanMatch(new(1, 0, 0, 0, 0, 0), requested);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");
    }
}
