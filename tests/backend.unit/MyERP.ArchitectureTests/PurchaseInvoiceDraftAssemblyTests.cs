using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftAssemblyTests
{
    [Fact]
    public void BuildsFromValidatedInputAndAuthoritativeSources()
    {
        var lines = new List<PurchaseInvoiceDraftLine> { Line(1, 31), Line(2, 31) };
        var result = PurchaseInvoiceDraftAssembly.Create(5, Input(lines), Order(), [Receipt()]);

        result.Identity.Should().Be(new PurchaseInvoiceIdentity(5, 7, 9, "TWD"));
        result.Totals.Should().Be(new PurchaseInvoiceDraftTotals(2m, .2m, 2.2m));
        lines[0] = Line(1, 99);
        result.Lines[0].ReceiptLineId.Should().Be(31);
    }

    [Fact]
    public void RejectsWrongCompanyOrMissingAndDuplicatedSources()
    {
        AssertCode(0, Input(), Order(), [Receipt()], "PUR.INVOICE.INVALID_SOURCE_IDENTITY");
        AssertCode(6, Input(), Order(), [Receipt()], "PUR.INVOICE.INVALID_SOURCE_IDENTITY");
        AssertCode(5, Input(), Order(), [], "PUR.INVOICE.INVALID_SOURCE_IDENTITY");
        AssertCode(5, Input(), Order(), [Receipt(), Receipt()], "PUR.INVOICE.INVALID_SOURCE_IDENTITY");
        AssertCode(5, Input(), Order() with { Status = "Draft" }, [Receipt()], "PUR.INVOICE.INVALID_SOURCE_IDENTITY");
        AssertCode(5, Input(), Order() with { IsDeleted = true }, [Receipt()], "PUR.INVOICE.INVALID_SOURCE_IDENTITY");
        AssertCode(5, Input(), Order(), [Receipt() with { IsDeleted = true }], "PUR.INVOICE.INVALID_SOURCE_IDENTITY");
        AssertCode(5, Input(), Order(), [Receipt() with { MaterialId = 52 }], "PUR.INVOICE.INVALID_SOURCE_IDENTITY");
    }

    [Fact]
    public void RejectsInvalidClientLinesBeforeAuthorityEvaluation()
    {
        AssertCode(5, Input([Line(1, 31), Line(1, 31)]), Order(), [Receipt()],
            "PUR.INVOICE.INVALID_LINES");
    }

    private static void AssertCode(long companyId, PurchaseInvoiceDraftInput input,
        PurchaseInvoiceOrderIdentity order, IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts,
        string code)
    {
        Action act = () => PurchaseInvoiceDraftAssembly.Create(companyId, input, order, receipts);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(code);
    }

    private static PurchaseInvoiceDraftInput Input(IReadOnlyList<PurchaseInvoiceDraftLine>? lines = null) =>
        new(7, 9, "twd", new DateOnly(2028, 2, 29), "INV-1", lines ?? [Line(1, 31)]);

    private static PurchaseInvoiceDraftLine Line(int number, long receipt) =>
        new(number, receipt, 1m, 1m, 10m, 1m, .1m, 1.1m);

    private static PurchaseInvoiceOrderIdentity Order() =>
        new(5, 7, 9, "TWD", "Approved", false);

    private static PurchaseInvoiceReceiptIdentity Receipt() =>
        new(31, 5, 7, 9, 9, 51, 51, "TWD", "Posted", false);
}
