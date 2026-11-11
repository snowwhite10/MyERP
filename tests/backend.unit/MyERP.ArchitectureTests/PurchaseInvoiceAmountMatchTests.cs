using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceAmountMatchTests
{
    [Fact]
    public void MatchesSavedOrderBasisAtFourPlaces()
    {
        var result = PurchaseInvoiceAmountMatch.Check([
            new(1, 2m, 3.125m, 5m, 3.125m, 5m, 6.25m, 0.3125m, 6.5625m)]);
        result.CanMatch.Should().BeTrue();
        result.Lines.Should().ContainSingle().Which.Differences.Should().BeEmpty();
    }

    [Fact]
    public void ReportsPriceTaxAndSavedAmountDifferencesSeparately()
    {
        var result = PurchaseInvoiceAmountMatch.Check([
            new(1, 2m, 3m, 5m, 3.1m, 6m, 6.1m, 0.366m, 6.466m)]);
        result.CanMatch.Should().BeFalse();
        result.Lines[0].Differences.Select(x => x.Kind).Should().Equal(
            "UNIT_PRICE", "TAX_RATE", "NET", "TAX", "GROSS");
        result.Lines[0].Differences[2].Difference.Should().Be(0.1m);
    }

    [Fact]
    public void RoundsEachLineBeforeSummingAndDisputesMinimumDifference()
    {
        var result = PurchaseInvoiceAmountMatch.Check([
            new(2, 1m, 0.00005m, 0m, 0.00005m, 0m, 0.0001m, 0m, 0.0001m),
            new(1, 1m, 1m, 0m, 1m, 0m, 1.0001m, 0m, 1.0001m)]);
        result.CanMatch.Should().BeFalse();
        result.BaselineNet.Should().Be(1.0001m);
        result.InvoiceNet.Should().Be(1.0002m);
        result.Lines[0].LineNo.Should().Be(1);
        result.Lines[0].Differences.Select(x => x.Kind).Should().Equal("NET", "GROSS");
        result.Lines[0].Differences[0].Difference.Should().Be(0.0001m);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("tax")]
    [InlineData("amount")]
    [InlineData("duplicate")]
    public void RejectsInvalidWholeInvoice(string variant)
    {
        PurchaseInvoiceAmountLine line = variant switch
        {
            "quantity" => new(1, 0m, 1m, 0m, 1m, 0m, 0m, 0m, 0m),
            "price" => new(1, 1m, -1m, 0m, 1m, 0m, 1m, 0m, 1m),
            "tax" => new(1, 1m, 1m, 100.0001m, 1m, 0m, 1m, 0m, 1m),
            "amount" => new(1, 1m, 1m, 0m, 1m, 0m, 1.00001m, 0m, 1m),
            _ => new(1, 1m, 1m, 0m, 1m, 0m, 1m, 0m, 1m)
        };
        Action action = () => PurchaseInvoiceAmountMatch.Check(variant == "duplicate" ? [line, line] : [line]);
        action.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void RejectsAggregateAmountOverflowWithoutReturningPartialDecision()
    {
        Action action = () => PurchaseInvoiceAmountMatch.Check([
            new(1, 60m, 9_000_000_000_000m, 0m, 0m, 0m,
                540_000_000_000_000m, 0m, 540_000_000_000_000m),
            new(2, 60m, 9_000_000_000_000m, 0m, 0m, 0m,
                540_000_000_000_000m, 0m, 540_000_000_000_000m)]);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_AMOUNT_SOURCE");
    }

    [Fact]
    public void OneDisputedLineDisputesWholeInvoice()
    {
        var result = PurchaseInvoiceAmountMatch.Check([
            new(1, 1m, 1m, 0m, 1m, 0m, 1m, 0m, 1m),
            new(2, 1m, 1m, 0m, 1m, 0m, 1.0001m, 0m, 1.0001m)]);
        result.CanMatch.Should().BeFalse();
        result.Lines[0].Differences.Should().BeEmpty();
        result.Lines[1].Differences.Should().HaveCount(2);
    }
}
