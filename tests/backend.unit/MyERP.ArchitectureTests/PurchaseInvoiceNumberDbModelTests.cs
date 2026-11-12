using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MyERP.Migrations.Migrations;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceNumberDbModelTests
{
    [Fact]
    public void NumberClaimsKeepEveryInvoiceKeyAndReserveItAcrossSoftDeletion()
    {
        var builder = new ModelBuilder(new ConventionSet());
        new PurchaseInvoiceNumberDbModel().Configure(builder);
        var claim = builder.FinalizeModel().FindEntityType(typeof(PurchaseInvoiceNumberClaim))!;

        claim.GetTableName().Should().Be("pur_purchase_invoice_number_claim");
        claim.FindProperty(nameof(PurchaseInvoiceNumberClaim.NumberKey))!.GetMaxLength().Should().Be(80);
        claim.GetIndexes().Should().Contain(x => x.IsUnique && x.GetFilter() == null &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] {
                nameof(PurchaseInvoiceNumberClaim.CompanyId), nameof(PurchaseInvoiceNumberClaim.SupplierId),
                nameof(PurchaseInvoiceNumberClaim.NumberKey) }));
        claim.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] {
                nameof(PurchaseInvoiceNumberClaim.CompanyId), nameof(PurchaseInvoiceNumberClaim.InvoiceId),
                nameof(PurchaseInvoiceNumberClaim.NumberKey) }));
        claim.GetIndexes().Should().Contain(x => x.IsUnique && x.GetFilter() == "[is_active] = 1" &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] {
                nameof(PurchaseInvoiceNumberClaim.CompanyId), nameof(PurchaseInvoiceNumberClaim.InvoiceId),
                nameof(PurchaseInvoiceNumberClaim.IsActive) }));
        claim.GetForeignKeys().Should().ContainSingle().Which.Properties.Select(p => p.Name)
            .Should().Equal(nameof(PurchaseInvoiceNumberClaim.CompanyId),
                nameof(PurchaseInvoiceNumberClaim.InvoiceId));
        claim.FindProperty(nameof(PurchaseInvoiceNumberClaim.NumberKey))!.GetCollation()
            .Should().Be("Latin1_General_100_BIN2");
    }

    [Fact]
    public void NumberClaimMigrationHasPermanentUniqueIndexAndReversibleTable()
    {
        var migration = new Purchasing_InvoiceNumberClaims();
        migration.UpOperations.OfType<CreateTableOperation>().Should().ContainSingle()
            .Which.Name.Should().Be("pur_purchase_invoice_number_claim");
        migration.UpOperations.OfType<CreateIndexOperation>().Should().Contain(x =>
            x.IsUnique && x.Filter == null &&
            x.Columns.SequenceEqual(new[] { "company_id", "supplier_id", "number_key" }));
        migration.UpOperations.OfType<CreateIndexOperation>().Should().Contain(x =>
            x.IsUnique && x.Filter == "[is_active] = 1" &&
            x.Columns.SequenceEqual(new[] { "company_id", "invoice_id", "is_active" }));
        migration.DownOperations.OfType<DropTableOperation>().Should().ContainSingle()
            .Which.Name.Should().Be("pur_purchase_invoice_number_claim");
    }

    [Fact]
    public void IdentityLinkMigrationAddsAndRemovesScopedClaimForeignKey()
    {
        var migration = new Purchasing_InvoiceClaimIdentityLink();
        migration.UpOperations.OfType<AddForeignKeyOperation>().Should().ContainSingle(x =>
            x.Table == "pur_purchase_invoice_number_claim" &&
            x.PrincipalTable == "pur_purchase_invoice" &&
            x.Columns.SequenceEqual(new[] { "company_id", "invoice_id" }) &&
            x.PrincipalColumns.SequenceEqual(new[] { "company_id", "id" }));
        migration.DownOperations.OfType<DropForeignKeyOperation>().Should().ContainSingle();
    }
}
