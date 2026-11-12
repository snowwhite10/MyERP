using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceSourceStorageTests
{
    [Fact]
    public void SourceModelHasCompanyScopedRelationsAndSavedPrice()
    {
        var builder = new ModelBuilder();
        new PurchaseInvoiceSourceDbModel().Configure(builder);
        var model = builder.Model;
        var order = model.FindEntityType(typeof(PurchaseOrderSource))!;
        var line = model.FindEntityType(typeof(PurchaseOrderSourceLine))!;
        var receipt = model.FindEntityType(typeof(PurchaseReceiptSource))!;
        var receiptLine = model.FindEntityType(typeof(PurchaseReceiptSourceLine))!;
        order.GetTableName().Should().Be("pur_purchase_order");
        line.GetTableName().Should().Be("pur_purchase_order_line");
        receipt.GetTableName().Should().Be("pur_purchase_receipt");
        receiptLine.GetTableName().Should().Be("pur_purchase_receipt_line");
        line.FindProperty(nameof(PurchaseOrderSourceLine.UnitPrice)).Should().NotBeNull();
        line.FindProperty(nameof(PurchaseOrderSourceLine.TaxRatePercent)).Should().NotBeNull();
        line.GetForeignKeys().Should().ContainSingle();
        receiptLine.GetForeignKeys().Should().HaveCount(2);
    }
}
