using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftHttpIntentTests
{
    private static PurchaseInvoiceDraftInput Input() => new(7, 9, "twd", new DateOnly(2028, 2, 29),
        " inv-1 ", [new PurchaseInvoiceDraftLine(1, 31, 1m, 1m, 10m, 1m, .1m, 1.1m)]);

    [Fact]
    public void CreateUsesServerCompanyAndNeverAcceptsClientInvoiceId()
    {
        var intent = PurchaseInvoiceDraftHttpIntent.Create(5, Input());
        intent.CompanyId.Should().Be(5);
        intent.InvoiceId.Should().BeNull();
        intent.ExpectedVersion.Should().BeNull();
        intent.Input.Currency.Should().Be("TWD");
        intent.Input.Number.Display.Should().Be("inv-1");
    }

    [Fact]
    public void EditRequiresPositiveRouteIdAndExpectedVersion()
    {
        var intent = PurchaseInvoiceDraftHttpIntent.Edit(5, 42, 3, Input());
        intent.InvoiceId.Should().Be(42);
        intent.ExpectedVersion.Should().Be(3);

        foreach (var (id, version) in new (long, int)[] { (0, 3), (-1, 3), (42, 0), (42, -1) })
        {
            Action act = () => PurchaseInvoiceDraftHttpIntent.Edit(5, id, version, Input());
            act.Should().Throw<BusinessRuleException>().Which.ErrorCode
                .Should().Be("PUR.INVOICE.INVALID_DRAFT_COMMAND");
        }
    }

    [Fact]
    public void InvalidCompanyOrBodyIsRejectedWithoutReturningIntent()
    {
        Action badCompany = () => PurchaseInvoiceDraftHttpIntent.Create(0, Input());
        badCompany.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_DRAFT_COMMAND");

        Action missingBody = () => PurchaseInvoiceDraftHttpIntent.Create(5, null);
        missingBody.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_HEADER");

        Action badLines = () => PurchaseInvoiceDraftHttpIntent.Edit(5, 42, 1,
            Input() with { Lines = [] });
        badLines.Should().Throw<BusinessRuleException>();
    }
}
