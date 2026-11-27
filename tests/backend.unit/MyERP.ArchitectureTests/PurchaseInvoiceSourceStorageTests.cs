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
        line.GetForeignKeys().Should().HaveCount(2);
        line.GetForeignKeys().Should().ContainSingle(fk =>
            fk.PrincipalEntityType.ClrType == typeof(PurchaseOrderSource) &&
            fk.Properties.Count == 2 && fk.Properties[0].Name == "CompanyId" &&
            fk.Properties[1].Name == "PurchaseOrderId");
        line.GetForeignKeys().Should().ContainSingle(fk =>
            fk.PrincipalEntityType.ClrType == typeof(RequisitionLine) &&
            fk.Properties.Count == 2 && fk.Properties[0].Name == "CompanyId" &&
            fk.Properties[1].Name == "RequisitionLineId");
        receiptLine.GetForeignKeys().Should().HaveCount(2);
    }
}
