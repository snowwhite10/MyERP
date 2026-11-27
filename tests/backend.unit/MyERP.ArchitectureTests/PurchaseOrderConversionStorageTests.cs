using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseOrderConversionStorageTests
{
    [Fact]
    public void ConversionExtendsExistingInvoiceOrderAndKeepsAwardLinkCompanyScoped()
    {
        var builder = new ModelBuilder();
        new PurchaseInvoiceSourceDbModel().Configure(builder);
        new RfqAwardDbModel().Configure(builder);
        var order = builder.Model.FindEntityType(typeof(PurchaseOrderSource))!;
        var line = builder.Model.FindEntityType(typeof(PurchaseOrderSourceLine))!;
        var source = builder.Model.FindEntityType(typeof(PurchaseOrderAwardSource))!;
        order.GetTableName().Should().Be("pur_purchase_order");
        line.GetTableName().Should().Be("pur_purchase_order_line");
        source.GetTableName().Should().Be("pur_order_source");
        order.FindProperty(nameof(PurchaseOrderSource.DocumentNo)).Should().NotBeNull();
        line.FindProperty(nameof(PurchaseOrderSourceLine.BaseGross)).Should().NotBeNull();
        source.GetForeignKeys().Should().HaveCount(2);
        source.GetIndexes().Should().Contain(x => x.IsUnique &&
            string.Join(",", x.Properties.Select(p => p.Name)) == "CompanyId,AwardId");
    }
}
