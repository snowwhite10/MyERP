using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceResolutionTests
{
    private static PurchaseInvoiceAmountDecision Differences() => PurchaseInvoiceAmountMatch.Check([
        new(1, 1m, 1m, 0m, 1.1m, 0m, 1.1m, 0m, 1.1m),
        new(2, 1m, 1m, 0m, 1m, 0m, 1.0001m, 0m, 1.0001m),
        new(3, 1m, 1m, 0m, 1m, 0m, 1m, 0m, 1m)]);

    [Fact]
    public void AcceptingEveryDifferencePreservesEvidenceAndReturnsMatched()
    {
        var result = PurchaseInvoiceResolution.Check("Disputed", Differences(), [
            new(2, "Accept", " tax adjustment "), new(1, "Accept", "price approved")]);
        result.Status.Should().Be("Matched");
        result.Decisions.Select(x => x.LineNo).Should().Equal(1, 2);
        result.Decisions[1].Reason.Should().Be("tax adjustment");
        result.Decisions[0].Differences.Should().ContainSingle(x => x.Kind == "UNIT_PRICE");
    }

    [Fact]
    public void OneRejectionReturnsDraftWhileRetainingDecisionEvidence()
    {
        var result = PurchaseInvoiceResolution.Check("Disputed", Differences(), [
            new(1, "Accept", "approved"), new(2, "Reject", "incorrect tax")]);
        result.Status.Should().Be("Draft");
        result.Decisions.Select(x => x.Action).Should().Equal("Accept", "Reject");
        result.Decisions[1].LineNo.Should().Be(2);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("long")]
    [InlineData("action")]
    [InlineData("status")]
    [InlineData("no-difference")]
    public void InvalidDecisionCannotReturnPartialResult(string variant)
    {
        PurchaseInvoiceLineResolution[] choices = variant switch
        {
            "missing" => [new(1, "Accept", "ok")],
            "duplicate" => [new(1, "Accept", "ok"), new(1, "Reject", "no")],
            "extra" => [new(1, "Accept", "ok"), new(2, "Accept", "ok"), new(3, "Accept", "ok")],
            "empty" => [],
            "blank" => [new(1, "Accept", " "), new(2, "Accept", "ok")],
            "long" => [new(1, "Accept", new string('x', 501)), new(2, "Accept", "ok")],
            "action" => [new(1, "Maybe", "ok"), new(2, "Accept", "ok")],
            _ => [new(1, "Accept", "ok"), new(2, "Accept", "ok")]
        };
        Action action = () => PurchaseInvoiceResolution.Check(
            variant == "status" ? "Draft" : "Disputed",
            variant == "no-difference" ? PurchaseInvoiceAmountMatch.Check([
                new(1, 1m, 1m, 0m, 1m, 0m, 1m, 0m, 1m)]) : Differences(), choices);
        action.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void AcceptsFiveHundredCharacterReason()
    {
        var result = PurchaseInvoiceResolution.Check("Disputed", Differences(), [
            new(1, "Accept", new string('x', 500)), new(2, "Accept", "ok")]);
        result.Decisions[0].Reason.Should().HaveLength(500);
    }

    [Fact]
    public void SingleDisputedLineMayBeAccepted()
    {
        var amount = PurchaseInvoiceAmountMatch.Check([
            new(1, 1m, 1m, 0m, 1.0001m, 0m, 1.0001m, 0m, 1.0001m)]);
        var result = PurchaseInvoiceResolution.Check("Disputed", amount,
            [new(1, "Accept", "approved")]);
        result.Status.Should().Be("Matched");
        result.Decisions.Should().ContainSingle();
    }

    [Fact]
    public void RejectsContradictoryAmountDecision()
    {
        var amount = Differences() with { CanMatch = true };
        Action action = () => PurchaseInvoiceResolution.Check("Disputed", amount,
            [new(1, "Accept", "ok"), new(2, "Accept", "ok")]);
        action.Should().Throw<BusinessRuleException>();
    }
}
