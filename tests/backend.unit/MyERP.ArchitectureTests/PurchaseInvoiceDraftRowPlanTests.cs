using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftRowPlanTests
{
    private static readonly PurchaseInvoiceOrderIdentity Order = new(5, 7, 9, "TWD", "Approved", false);
    private static readonly PurchaseInvoiceReceiptIdentity Receipt =
        new(31, 5, 7, 9, 9, 51, 51, "TWD", "Posted", false) { OrderLineId = 42 };
    private static readonly PurchaseInvoiceOrderLinePrice Price = new(31, 5, 9, 42, 51, 2m, 20m);
    private static readonly PurchaseInvoiceDraftLine First = new(2, 31, 1m, 1m, 10m, 1m, .1m, 1.1m);
    private static readonly PurchaseInvoiceDraftLine Second = new(1, 31, 2m, 1m, 10m, 2m, .2m, 2.2m);

    private static PurchaseInvoiceDraftSnapshot Snapshot() => PurchaseInvoiceDraftAssembly.Create(5,
        new PurchaseInvoiceDraftInput(7, 9, "TWD", new DateOnly(2028, 2, 29), " inv-1 ",
            [First, Second]), Order, [Receipt], [Price]);

    [Fact]
    public void NewDraftMapsOnlyValidatedHeaderAndRowsWithAssignedInvoiceIdentity()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);
        var plan = intent.PlanDraftRows(30);

        plan.InvoiceId.Should().Be(30);
        plan.ExpectedVersion.Should().BeNull();
        plan.Header.Should().Be(new PurchaseInvoiceDraftHeaderRow(5, 7, 9, "TWD",
            new DateOnly(2028, 2, 29), "inv-1", "INV-1", 3m, .3m, 3.3m));
        plan.Lines.Select(x => x.LineNo).Should().Equal(1, 2);
        plan.Lines.Should().OnlyContain(x => x.CompanyId == 5 && x.InvoiceId == 30 &&
            x.ReceiptLineId == 31);
        plan.Lines.Select(x => x.Quantity).Should().Equal(2m, 1m);
        plan.Lines.Sum(x => x.Net).Should().Be(plan.Header.TotalNet);
        plan.Lines.Sum(x => x.Tax).Should().Be(plan.Header.TotalTax);
        plan.Lines.Sum(x => x.Gross).Should().Be(plan.Header.TotalGross);
        plan.Lines.Should().NotBeOfType<PurchaseInvoiceDraftLineRow[]>();
    }

    [Fact]
    public void RevisionKeepsSavedIdentityAndExpectedVersion()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1");
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], [new(5, 7, 20, "INV-1", false)]);
        var plan = intent.PlanDraftRows(20);
        plan.InvoiceId.Should().Be(20);
        plan.ExpectedVersion.Should().Be(4);
        plan.Lines.Should().OnlyContain(x => x.InvoiceId == 20 && x.CompanyId == 5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public void WrongRevisionIdentityCannotProducePartialWritePlan(long invoiceId)
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1");
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], [new(5, 7, 20, "INV-1", false)]);
        Action act = () => intent.PlanDraftRows(invoiceId);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should()
            .Be("PUR.INVOICE.INVALID_SAVED_IDENTITY");
    }

    [Fact]
    public void NewDraftRejectsUnassignedIdentityAndReturnedRowsCannotBeRewritten()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);
        Action missingId = () => intent.PlanDraftRows(0);
        missingId.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should()
            .Be("PUR.INVOICE.INVALID_SAVED_IDENTITY");

        var plan = intent.PlanDraftRows(30);
        Action mutate = () => ((IList<PurchaseInvoiceDraftLineRow>)plan.Lines)[0] =
            plan.Lines[0] with { CompanyId = 6 };
        mutate.Should().Throw<NotSupportedException>();
        intent.Lines[0].LineNumber.Should().Be(2);
        plan.Lines[0].CompanyId.Should().Be(5);
    }
}
