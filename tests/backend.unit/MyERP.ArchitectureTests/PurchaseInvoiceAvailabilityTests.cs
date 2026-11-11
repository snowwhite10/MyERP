using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceAvailabilityTests
{
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
