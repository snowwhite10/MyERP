using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftInputTests
{
    [Fact]
    public void AcceptsValidHeaderAndLinesAndReturnsNormalizedNumber()
    {
        var result = PurchaseInvoiceDraftInput.Validate(new(7, 9, "twd",
            new DateOnly(2028, 2, 29), "  ＡＢ-1  ", [Line(1, 11, 0.000001m)]));

        result.Number.Display.Should().Be("ＡＢ-1");
        result.Number.Key.Should().Be("AB-1");
    }

    [Theory]
    [InlineData(0, 9, "TWD")]
    [InlineData(7, 0, "TWD")]
    [InlineData(7, 9, "TW")]
    [InlineData(7, 9, "TWD ")]
    [InlineData(7, 9, "T1D")]
    [InlineData(7, 9, "tｗd")]
    public void RejectsInvalidHeader(long supplierId, long orderId, string currency)
        => AssertCode(new(supplierId, orderId, currency, new DateOnly(2028, 2, 29),
            "INV-1", [Line(1, 11, 1m)]), "PUR.INVOICE.INVALID_HEADER");

    [Fact]
    public void RejectsMissingDateOrInput()
    {
        AssertCode(new(7, 9, "TWD", default, "INV-1", [Line(1, 11, 1m)]),
            "PUR.INVOICE.INVALID_HEADER");
        AssertCode(null!, "PUR.INVOICE.INVALID_HEADER");
    }

    [Fact]
    public void ReusesNumberAndWholeLineValidation()
    {
        AssertCode(new(7, 9, "TWD", new DateOnly(2028, 2, 29), " ",
            [Line(1, 11, 1m)]), "PUR.INVOICE.INVALID_NUMBER");
        AssertCode(new(7, 9, "TWD", new DateOnly(2028, 2, 29), "INV-1",
            [Line(1, 11, 1m), Line(1, 12, 1m)]), "PUR.INVOICE.INVALID_LINES");
    }

    [Fact]
    public void CopiesLinesSoLaterCallerChangesCannotAlterValidatedSnapshot()
    {
        var lines = new List<PurchaseInvoiceDraftLine> { Line(1, 11, 1m) };
        var result = PurchaseInvoiceDraftInput.Validate(new(7, 9, "TWD",
            DateOnly.MaxValue, "INV-1", lines));

        lines.Add(Line(2, 12, 2m));
        result.Lines.Should().HaveCount(1);
        result.Lines[0].ReceiptLineId.Should().Be(11);
    }

    private static void AssertCode(PurchaseInvoiceDraftInput input, string code)
    {
        Action action = () => PurchaseInvoiceDraftInput.Validate(input);
        action.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(code);
    }

    private static PurchaseInvoiceDraftLine Line(int number, long receipt, decimal quantity) =>
        new(number, receipt, quantity, 1m, 10m, 1m, 0.1m, 1.1m);
}
