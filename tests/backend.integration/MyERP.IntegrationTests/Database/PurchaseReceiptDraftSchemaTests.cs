using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

public sealed class PurchaseReceiptDraftSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task ReceiptDraftColumnsAndOrderCapacityAreEnforcedBySql()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = database.CreateRawContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var company = TestCompanies.Next();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order(company_id,supplier_id,currency,status,is_deleted,created_at,created_by,version)
            VALUES ({company},71,'TWD','Approved',0,'2026-01-01',1,1)
            """, ct);
        var order = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order WHERE company_id={0}", company).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order_line(company_id,purchase_order_id,line_no,material_id,quantity,unit_price,tax_rate_percent,created_at,created_by,version)
            VALUES ({company},{order},1,81,5,2,0,'2026-01-01',1,1)
            """, ct);
        var line = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order_line WHERE company_id={0}", company).SingleAsync(ct);
        var counters = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(posted_received,'|',pending_receipt_reserved) AS [Value] FROM dbo.pur_purchase_order_line WHERE id={0}", line).SingleAsync(ct);
        Assert.Equal("0.000000|0.000000", counters);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.pur_purchase_order_line SET posted_received=4,pending_receipt_reserved=2 WHERE id={line}
            """, ct));

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_receipt(company_id,purchase_order_id,supplier_id,currency,status,is_deleted,
              document_no,business_date,created_at,created_by,version)
            VALUES ({company},{order},71,'TWD','Draft',0,'PR-1','2028-02-29','2026-01-01',1,1)
            """, ct);
        var receipt = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_receipt WHERE company_id={0}", company).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_receipt_line(company_id,purchase_receipt_id,purchase_order_line_id,line_no,material_id,
              quantity,warehouse_id,posted_received,posted_returned,pending_return_reserved,posted_invoiced,matched_reserved,
              created_at,created_by,version)
            VALUES ({company},{receipt},{line},1,81,2,91,0,0,0,0,0,'2026-01-01',1,1)
            """, ct);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_receipt_line(company_id,purchase_receipt_id,purchase_order_line_id,line_no,material_id,
              quantity,posted_received,posted_returned,pending_return_reserved,posted_invoiced,matched_reserved,
              created_at,created_by,version)
            VALUES ({company},{receipt},{line},2,81,1,0,0,0,0,0,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_receipt_line(company_id,purchase_receipt_id,purchase_order_line_id,line_no,material_id,
              quantity,warehouse_id,posted_received,posted_returned,pending_return_reserved,posted_invoiced,matched_reserved,
              created_at,created_by,version)
            VALUES ({company},{receipt},{line},2,81,0,91,0,0,0,0,0,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_receipt(company_id,purchase_order_id,supplier_id,currency,status,is_deleted,
              document_no,business_date,created_at,created_by,version)
            VALUES ({company},{order},71,'TWD','Draft',0,'PR-1','2028-02-29','2026-01-01',1,1)
            """, ct));
        await tx.RollbackAsync(ct);
    }
}
