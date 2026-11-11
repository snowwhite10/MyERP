using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceSourceIdentityTests
{
    private static readonly PurchaseInvoiceIdentity Invoice = new(1, 2, 3, "TWD");
    private static readonly PurchaseInvoiceOrderIdentity Order =
        new(1, 2, 3, "TWD", "Approved", false);
    private static readonly PurchaseInvoiceReceiptIdentity Receipt =
        new(7, 1, 2, 3, 3, 5, 5, "TWD", "Posted", false);

    [Fact]
    public void AcceptsAuthoritativeSourcesInAnyOrderAfterRequestAggregation()
    {
        var another = Receipt with { ReceiptLineId = 8, MaterialId = 6, OrderMaterialId = 6 };
        PurchaseInvoiceSourceIdentity.Check([new(8, 1m), new(7, 2m), new(7, 1m)],
            Invoice, Order, [Receipt, another]);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("supplier")]
    [InlineData("order")]
    [InlineData("currency")]
    [InlineData("status")]
    [InlineData("deleted")]
    public void RejectsInvalidOrder(string variant)
    {
        var order = variant switch
        {
            "company" => Order with { CompanyId = 9 },
            "supplier" => Order with { SupplierId = 9 },
            "order" => Order with { PurchaseOrderId = 9 },
            "currency" => Order with { Currency = "USD" },
            "status" => Order with { Status = "Draft" },
            _ => Order with { IsDeleted = true }
        };
        Rejects(order, [Receipt]);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("supplier")]
    [InlineData("order")]
    [InlineData("currency")]
    public void RejectsInvalidSavedInvoiceIdentity(string variant)
    {
        var invoice = variant switch
        {
            "company" => Invoice with { CompanyId = 0 },
            "supplier" => Invoice with { SupplierId = 0 },
            "order" => Invoice with { PurchaseOrderId = 0 },
            _ => Invoice with { Currency = " " }
        };
        Action action = () => PurchaseInvoiceSourceIdentity.Check([new(7, 1m)], invoice,
            Order, [Receipt]);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_IDENTITY");
    }

    [Theory]
    [InlineData("company")]
    [InlineData("supplier")]
    [InlineData("order")]
    [InlineData("order-line")]
    [InlineData("material")]
    [InlineData("currency")]
    [InlineData("status")]
    [InlineData("deleted")]
    public void RejectsInvalidReceiptOrOrderLine(string variant)
    {
        var receipt = variant switch
        {
            "company" => Receipt with { CompanyId = 9 },
            "supplier" => Receipt with { SupplierId = 9 },
            "order" => Receipt with { PurchaseOrderId = 9 },
            "order-line" => Receipt with { OrderLinePurchaseOrderId = 9 },
            "material" => Receipt with { MaterialId = 9 },
            "currency" => Receipt with { Currency = "USD" },
            "status" => Receipt with { Status = "Draft" },
            _ => Receipt with { IsDeleted = true }
        };
        Rejects(Order, [receipt]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    [InlineData("invalid-id")]
    public void RejectsUnmatchedSourceSet(string variant)
    {
        PurchaseInvoiceReceiptIdentity[] receipts = variant switch
        {
            "missing" => [],
            "duplicate" => [Receipt, Receipt],
            "extra" => [Receipt, Receipt with { ReceiptLineId = 8 }],
            _ => [Receipt with { ReceiptLineId = 0 }]
        };
        Rejects(Order, receipts);
    }

    [Fact]
    public void InvalidRequestPrecedesMissingSourceAndLateInvalidSourceRejectsWholeInvoice()
    {
        Action invalidRequest = () => PurchaseInvoiceSourceIdentity.Check([new(7, 0m)], Invoice, Order, []);
        invalidRequest.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_REQUEST_QUANTITY");

        Action lateInvalid = () => PurchaseInvoiceSourceIdentity.Check(
            [new(7, 1m), new(8, 1m)], Invoice, Order,
            [Receipt, Receipt with { ReceiptLineId = 8, Status = "Draft" }]);
        lateInvalid.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_IDENTITY");
    }

    private static void Rejects(PurchaseInvoiceOrderIdentity order,
        IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts)
    {
        Action action = () => PurchaseInvoiceSourceIdentity.Check([new(7, 1m)], Invoice, order, receipts);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SOURCE_IDENTITY");
    }
}
