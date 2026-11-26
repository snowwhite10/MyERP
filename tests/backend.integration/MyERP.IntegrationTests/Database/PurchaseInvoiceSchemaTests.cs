using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-PUR-008a50: real SQL contract for invoice sources, Draft and permanent number claims.</summary>
public sealed class PurchaseInvoiceSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task InvoiceSourceTablesHaveCompanyScopedLinksAndLookupIndexes()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        string[] tables = ["pur_purchase_order", "pur_purchase_order_line", "pur_purchase_receipt",
            "pur_purchase_receipt_line", "pur_purchase_invoice", "pur_purchase_invoice_line",
            "pur_purchase_invoice_number_claim"];
        foreach (var table in tables)
            Assert.Equal(1, await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE object_id = OBJECT_ID({"dbo." + table})").SingleAsync(token));

        var fks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN " +
            "(OBJECT_ID('dbo.pur_purchase_order_line'),OBJECT_ID('dbo.pur_purchase_receipt')," +
            "OBJECT_ID('dbo.pur_purchase_receipt_line'),OBJECT_ID('dbo.pur_purchase_invoice')," +
            "OBJECT_ID('dbo.pur_purchase_invoice_line'),OBJECT_ID('dbo.pur_purchase_invoice_number_claim'))")
            .ToListAsync(token);
        foreach (var name in new[] { "fk_pur_order_line_order", "fk_pur_receipt_order",
                     "fk_pur_receipt_line_receipt", "fk_pur_receipt_line_order_line",
                     "fk_pur_purchase_invoice_order", "fk_pur_purchase_invoice_line_receipt_line",
                     "fk_pur_purchase_invoice_line_pur_purchase_invoice_company_id_invoice_id",
                     "fk_pur_purchase_invoice_number_claim_purchase_invoice_company_id_invoice_id" })
            Assert.Contains(name, fks);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN " +
            "(OBJECT_ID('dbo.pur_purchase_order'),OBJECT_ID('dbo.pur_purchase_order_line')," +
            "OBJECT_ID('dbo.pur_purchase_receipt'),OBJECT_ID('dbo.pur_purchase_receipt_line')," +
            "OBJECT_ID('dbo.pur_purchase_invoice'),OBJECT_ID('dbo.pur_purchase_invoice_line')," +
            "OBJECT_ID('dbo.pur_purchase_invoice_number_claim'))")
            .ToListAsync(token);
        foreach (var name in new[] { "ux_pur_purchase_order_line_company_id_purchase_order_id_line_no",
                     "ux_pur_purchase_receipt_line_company_id_purchase_receipt_id_line_no",
                     "ux_pur_purchase_invoice_line_company_id_invoice_id_line_no",
                     "ux_pur_purchase_invoice_number_claim_company_id_supplier_id_number_key",
                     "ix_pur_purchase_invoice_line_company_id_receipt_line_id",
                     "ix_pur_purchase_invoice_company_id_purchase_order_id_status_id",
                     // WU-PUR-008a55i: default list sort -businessDate,-id.
                     "ix_pur_purchase_invoice_company_id_business_date_id" })
            Assert.Contains(name, indexes);
    }

    [Fact]
    public async Task SoftDeletedInvoiceKeepsItsSupplierNumberClaim()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var company = Random.Shared.NextInt64(92000000, 93000000);
        await db.Database.ExecuteSqlAsync(
            $"INSERT dbo.pur_purchase_order(company_id,supplier_id,currency,status,is_deleted,created_at,created_by,version) VALUES ({company},7,'TWD','Approved',0,'2026-01-01',1,1)", token);
        var order = await db.Database.SqlQuery<long>(
            $"SELECT id AS [Value] FROM dbo.pur_purchase_order WHERE company_id={company}")
            .SingleAsync(token);
        await db.Database.ExecuteSqlAsync(
            $"INSERT dbo.pur_purchase_invoice(company_id,supplier_id,purchase_order_id,currency,business_date,supplier_invoice_number,number_key,status,total_net,total_tax,total_gross,is_deleted,created_at,created_by,version) VALUES ({company},7,{order},'TWD','2026-02-28','INV-1','INV-1','Draft',1,0,1,0,'2026-01-01',1,1)", token);
        var first = await db.Database.SqlQuery<long>(
            $"SELECT id AS [Value] FROM dbo.pur_purchase_invoice WHERE company_id={company}").SingleAsync(token);
        await db.Database.ExecuteSqlAsync(
            $"INSERT dbo.pur_purchase_invoice_number_claim(company_id,supplier_id,invoice_id,number_key,is_active,created_at,created_by,version) VALUES ({company},7,{first},'INV-1',1,'2026-01-01',1,1)", token);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.pur_purchase_invoice SET is_deleted=1,deleted_at='2026-03-01' WHERE id={first}", token);
        await db.Database.ExecuteSqlAsync(
            $"INSERT dbo.pur_purchase_invoice(company_id,supplier_id,purchase_order_id,currency,business_date,supplier_invoice_number,number_key,status,total_net,total_tax,total_gross,is_deleted,created_at,created_by,version) VALUES ({company},7,{order},'TWD','2026-02-28','INV-1','INV-1','Draft',1,0,1,0,'2026-01-01',1,1)", token);
        var second = await db.Database.SqlQuery<long>(
            $"SELECT MAX(id) AS [Value] FROM dbo.pur_purchase_invoice WHERE company_id={company}").SingleAsync(token);
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT dbo.pur_purchase_invoice_number_claim(company_id,supplier_id,invoice_id,number_key,is_active,created_at,created_by,version) VALUES ({company},7,{second},'INV-1',1,'2026-01-01',1,1)", token));
        Assert.True(error.Number is 2601 or 2627, $"Unexpected SQL error {error.Number}");
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.pur_purchase_invoice_number_claim WHERE company_id={company} AND supplier_id=7 AND number_key='INV-1'").SingleAsync(token));
        await transaction.RollbackAsync(token);
    }
}
