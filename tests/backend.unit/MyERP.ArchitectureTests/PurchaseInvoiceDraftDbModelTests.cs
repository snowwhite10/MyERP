using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MyERP.Migrations.Migrations;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftDbModelTests
{
    [Fact]
    public void HeaderAndLinesKeepCompanyScopedSourceAndAmountShape()
    {
        var builder = new ModelBuilder(new ConventionSet());
        new PurchaseInvoiceDraftDbModel().Configure(builder);
        var model = builder.FinalizeModel();
        var header = model.FindEntityType(typeof(PurchaseInvoice))!;
        var line = model.FindEntityType(typeof(PurchaseInvoiceLine))!;

        header.GetTableName().Should().Be("pur_purchase_invoice");
        line.GetTableName().Should().Be("pur_purchase_invoice_line");
        header.FindProperty(nameof(PurchaseInvoice.SupplierInvoiceNumber))!.GetMaxLength().Should().Be(80);
        header.FindProperty(nameof(PurchaseInvoice.NumberKey))!.GetCollation().Should().Be("Latin1_General_100_BIN2");
        header.FindProperty(nameof(PurchaseInvoice.TotalGross))!.GetPrecision().Should().Be(19);
        header.FindProperty(nameof(PurchaseInvoice.TotalGross))!.GetScale().Should().Be(4);
        line.FindProperty(nameof(PurchaseInvoiceLine.Quantity))!.GetScale().Should().Be(6);
        line.FindProperty(nameof(PurchaseInvoiceLine.UnitPrice))!.GetScale().Should().Be(6);
        line.FindProperty(nameof(PurchaseInvoiceLine.Tax))!.GetScale().Should().Be(4);
        line.GetForeignKeys().Should().ContainSingle(x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { nameof(PurchaseInvoiceLine.CompanyId), nameof(PurchaseInvoiceLine.InvoiceId) }));
        line.GetIndexes().Should().Contain(x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { nameof(PurchaseInvoiceLine.CompanyId), nameof(PurchaseInvoiceLine.InvoiceId), nameof(PurchaseInvoiceLine.LineNo) }));
        line.GetIndexes().Should().Contain(x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { nameof(PurchaseInvoiceLine.CompanyId), nameof(PurchaseInvoiceLine.ReceiptLineId) }));
    }

    [Fact]
    public void DraftMigrationCreatesAndDropsBothTablesInDependencyOrder()
    {
        var migration = new Purchasing_InvoiceDraft();
        migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name)
            .Should().ContainInOrder("pur_purchase_invoice", "pur_purchase_invoice_line");
        migration.DownOperations.OfType<DropTableOperation>().Select(x => x.Name)
            .Should().ContainInOrder("pur_purchase_invoice_line", "pur_purchase_invoice");
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        tables["pur_purchase_invoice"].Columns.Single(x => x.Name == "number_key")
            .Collation.Should().Be("Latin1_General_100_BIN2");
        tables["pur_purchase_invoice_line"].ForeignKeys.Should().ContainSingle(x =>
            x.PrincipalTable == "pur_purchase_invoice" &&
            x.Columns.SequenceEqual(new[] { "company_id", "invoice_id" }) &&
            x.PrincipalColumns.SequenceEqual(new[] { "company_id", "id" }));
        migration.UpOperations.OfType<CreateIndexOperation>().Should().Contain(x => x.IsUnique &&
            x.Table == "pur_purchase_invoice_line" &&
            x.Columns.SequenceEqual(new[] { "company_id", "invoice_id", "line_no" }));
    }
}
