using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftEntityPlanTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData(4, 4)]
    public void MaterializeMapsHeaderLinesAndRevisionVersion(int? expectedVersion, int version)
    {
        var plan = Plan(expectedVersion);

        var invoice = plan.Materialize();

        invoice.Id.Should().Be(30);
        invoice.CompanyId.Should().Be(5);
        invoice.SupplierId.Should().Be(7);
        invoice.PurchaseOrderId.Should().Be(9);
        invoice.Currency.Should().Be("TWD");
        invoice.BusinessDate.Should().Be(new DateOnly(2028, 2, 29));
        invoice.SupplierInvoiceNumber.Should().Be("INV-1");
        invoice.NumberKey.Should().Be("INV-1");
        invoice.Status.Should().Be(PurchaseInvoiceStatus.Draft);
        invoice.Version.Should().Be(version);
        invoice.TotalNet.Should().Be(3m);
        invoice.TotalTax.Should().Be(.3m);
        invoice.TotalGross.Should().Be(3.3m);
        invoice.Lines.Select(x => x.LineNo).Should().Equal(1, 2);
        invoice.Lines.Should().OnlyContain(x => x.CompanyId == 5 && x.InvoiceId == 30 &&
            x.ReceiptLineId == 31 && x.Gross == x.Net + x.Tax);
        invoice.Lines.Select(x => x.Quantity).Should().Equal(2m, 1m);
        invoice.CreatedBy.Should().Be(0);
        invoice.Lines.Should().OnlyContain(x => x.CreatedBy == 0 && x.Version == 0);
    }

    [Fact]
    public void MaterializeReturnsIndependentGraph()
    {
        var plan = Plan(null);
        var first = plan.Materialize();
        first.CompanyId = 99;
        first.Lines[0].CompanyId = 99;

        var second = plan.Materialize();
        second.CompanyId.Should().Be(5);
        second.Lines[0].CompanyId.Should().Be(5);
    }

    [Theory]
    [InlineData(0, 5, 30, 1, "PUR.INVOICE.INVALID_SAVED_IDENTITY")]
    [InlineData(30, 6, 30, 1, "PUR.INVOICE.INVALID_DRAFT_ROWS")]
    [InlineData(30, 5, 31, 1, "PUR.INVOICE.INVALID_DRAFT_ROWS")]
    [InlineData(30, 5, 30, 0, "PUR.INVOICE.INVALID_DRAFT_ROWS")]
    public void InvalidIdentityOrLineCannotProduceEntity(long id, long companyId,
        long lineInvoiceId, int lineNo, string errorCode)
    {
        var original = Plan(null);
        var plan = original with
        {
            InvoiceId = id,
            Lines = [original.Lines[0] with { CompanyId = companyId,
                InvoiceId = lineInvoiceId, LineNo = lineNo }]
        };

        Action act = () => plan.Materialize();
        act.Should().Throw<MyERP.Shared.Errors.BusinessRuleException>()
            .Which.ErrorCode.Should().Be(errorCode);
    }

    [Fact]
    public void InvalidRevisionOrDuplicateLinesCannotProduceEntity()
    {
        var original = Plan(null);
        Action badVersion = () => (original with { ExpectedVersion = 0 }).Materialize();
        badVersion.Should().Throw<MyERP.Shared.Errors.BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SAVED_IDENTITY");

        var duplicate = original with
        {
            Lines = [original.Lines[0], original.Lines[1] with { LineNo = 1 }]
        };
        Action duplicateLine = () => duplicate.Materialize();
        duplicateLine.Should().Throw<MyERP.Shared.Errors.BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_DRAFT_ROWS");

        Action noLines = () => (original with { Lines = [] }).Materialize();
        noLines.Should().Throw<MyERP.Shared.Errors.BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_DRAFT_ROWS");
    }

    private static PurchaseInvoiceDraftRowPlan Plan(int? expectedVersion) => new(30,
        expectedVersion,
        new(5, 7, 9, "TWD", new DateOnly(2028, 2, 29), "INV-1", "INV-1", 3m, .3m, 3.3m),
        [new(5, 30, 1, 31, 2m, 1m, 10m, 2m, .2m, 2.2m),
            new(5, 30, 2, 31, 1m, 1m, 10m, 1m, .1m, 1.1m)]);
}
