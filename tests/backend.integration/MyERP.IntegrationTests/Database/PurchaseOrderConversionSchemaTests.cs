using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

public sealed class PurchaseOrderConversionSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task MigrationKeepsLegacyInvoiceSourceRowsAndAddsPermanentAwardLink()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var companyId = TestCompanies.Next();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order(company_id,supplier_id,currency,status,is_deleted,created_at,created_by,version,base_currency_code,exchange_rate)
            VALUES ({companyId},101,'TWD','Approved',0,'2026-01-01',7,1,'TWD',1)
            """, token);
        var orderId = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order WHERE company_id = {0}", companyId)
            .SingleAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order_line(company_id,purchase_order_id,line_no,material_id,quantity,unit_price,tax_rate_percent,created_at,created_by,version)
            VALUES ({companyId},{orderId},1,102,2,10,5,'2026-01-01',7,1)
            """, token);
        var legacy = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(COALESCE(document_no,'NULL'),'|',COALESCE(CONVERT(varchar(30),base_gross),'NULL'),'|',exchange_rate) AS [Value] FROM dbo.pur_purchase_order WHERE id = {0}", orderId)
            .SingleAsync(token);
        Assert.Equal("NULL|NULL|1.00000000", legacy);
        var legacyLine = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(unit_price,'|',COALESCE(CONVERT(varchar(30),base_gross),'NULL')) AS [Value] FROM dbo.pur_purchase_order_line WHERE purchase_order_id = {0}", orderId)
            .SingleAsync(token);
        Assert.Equal("10.000000|NULL", legacyLine);
        var foreignKeys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.pur_order_source')")
            .ToListAsync(token);
        Assert.Equal(2, foreignKeys.Count);
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.pur_order_source')")
            .ToListAsync(token);
        Assert.Contains("ix_pur_order_source_company_id_award_id", indexes);
        Assert.Contains("ix_pur_order_source_company_id_purchase_order_line_id", indexes);
        var trigger = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'tr_pur_order_source_immutable'")
            .SingleAsync(token);
        Assert.Equal(1, trigger);
        await transaction.RollbackAsync(token);
    }
}
