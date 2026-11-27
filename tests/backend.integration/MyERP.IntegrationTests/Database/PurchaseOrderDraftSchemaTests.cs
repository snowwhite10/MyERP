using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

public sealed class PurchaseOrderDraftSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task DraftSchemaKeepsExistingRowsAndEnforcesCompanyScopedRequisitionSource()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var companyId = TestCompanies.Next();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order(company_id,supplier_id,currency,status,is_deleted,created_at,created_by,version)
            VALUES ({companyId},101,'TWD','Draft',0,'2026-01-01',7,1)
            """, token);
        var orderId = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order WHERE company_id = {0}", companyId)
            .SingleAsync(token);
        var defaults = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(CAST(requisition_consumption_active AS varchar(1)),'|',COALESCE(remark,'NULL')) AS [Value] " +
            "FROM dbo.pur_purchase_order WHERE id = {0}", orderId).SingleAsync(token);
        Assert.Equal("0|NULL", defaults);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.pur_purchase_order SET requisition_consumption_active = 1
            WHERE id = {orderId} AND company_id = {companyId}
            """, token));

        var sourceFk = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
            WHERE parent_object_id = OBJECT_ID('dbo.pur_purchase_order_line')
              AND referenced_object_id = OBJECT_ID('dbo.pur_requisition_line')
            """).SingleAsync(token);
        Assert.Equal(1, sourceFk);
        var amountColumns = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.pur_purchase_order_line')
              AND name IN ('original_net','original_tax','original_gross')
            """).SingleAsync(token);
        Assert.Equal(3, amountColumns);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order_line
              (company_id,purchase_order_id,line_no,material_id,requisition_line_id,quantity,
               unit_price,tax_rate_percent,created_at,created_by,version)
            VALUES ({companyId},{orderId},1,102,{long.MaxValue},2,10,5,'2026-01-01',7,1)
            """, token));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order_line
              (company_id,purchase_order_id,line_no,material_id,quantity,unit_price,tax_rate_percent,
               original_net,original_tax,original_gross,created_at,created_by,version)
            VALUES ({companyId},{orderId},1,102,2,10,5,20,1,20,'2026-01-01',7,1)
            """, token));
        var savedLines = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.pur_purchase_order_line WHERE purchase_order_id = {0}", orderId)
            .SingleAsync(token);
        Assert.Equal(0, savedLines);
        await transaction.RollbackAsync(token);
    }
}
