using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

public sealed class PurchaseReturnSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task ReturnTablesAndCompanyScopedConstraintsExist()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = database.CreateRawContext();
        var tables = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.tables
            WHERE name IN ('pur_purchase_return','pur_purchase_return_line','pur_purchase_return_line_tracking')
            """).SingleAsync(ct);
        Assert.Equal(3, tables);
        var keys = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
            WHERE name IN ('fk_pur_purchase_return_order','fk_pur_purchase_return_line_return',
              'fk_pur_purchase_return_line_receipt_line','fk_pur_purchase_return_tracking_line')
            """).SingleAsync(ct);
        Assert.Equal(4, keys);
        var checks = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.check_constraints
            WHERE name IN ('ck_pur_return_status','ck_pur_return_line_quantity','ck_pur_return_tracking_target')
            """).SingleAsync(ct);
        Assert.Equal(3, checks);
        var indexes = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.indexes
            WHERE name IN ('ix_pur_purchase_return_company_id_document_no',
              'ix_pur_purchase_return_company_id_purchase_order_id_status_id',
              'ix_pur_purchase_return_line_company_id_purchase_return_id_line_no',
              'ix_pur_purchase_return_line_company_id_receipt_line_id',
              'ix_pur_purchase_return_line_tracking_company_id_line_id_seq_no')
            """).SingleAsync(ct);
        Assert.Equal(5, indexes);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("line")]
    [InlineData("tracking")]
    public async Task ReturnSqlRejectsInvalidRowsAndProtectsPostedSnapshot(string postedChange)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = database.CreateRawContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var company = TestCompanies.Next();
        var other = TestCompanies.Next();
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
        var orderLine = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order_line WHERE company_id={0}", company).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_receipt(company_id,purchase_order_id,supplier_id,currency,status,is_deleted,
                document_no,business_date,created_at,created_by,version)
            VALUES ({company},{order},71,'TWD','Posted',0,'RET-SCH-R','2026-11-28','2026-01-01',1,1)
            """, ct);
        var receipt = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_receipt WHERE company_id={0}", company).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_receipt_line(company_id,purchase_receipt_id,purchase_order_line_id,line_no,material_id,
                quantity,warehouse_id,posted_received,posted_returned,pending_return_reserved,posted_invoiced,matched_reserved,
                created_at,created_by,version)
            VALUES ({company},{receipt},{orderLine},1,81,5,91,5,0,0,0,0,'2026-01-01',1,1)
            """, ct);
        var receiptLine = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_receipt_line WHERE company_id={0}", company).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return(company_id,document_no,purchase_order_id,business_date,status,is_deleted,created_at,created_by,version)
            VALUES ({company},'RET-SCH-1',{order},'2026-11-28','Draft',0,'2026-01-01',1,1)
            """, ct);
        var returnId = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_return WHERE company_id={0}", company).SingleAsync(ct);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return(company_id,document_no,purchase_order_id,business_date,status,is_deleted,created_at,created_by,version)
            VALUES ({company},'RET-SCH-1',{order},'2026-11-28','Draft',1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return(company_id,document_no,purchase_order_id,business_date,status,is_deleted,created_at,created_by,version)
            VALUES ({other},'RET-SCH-1',{order},'2026-11-28','Draft',0,'2026-01-01',1,1)
            """, ct));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order(company_id,supplier_id,currency,status,is_deleted,created_at,created_by,version)
            VALUES ({other},71,'TWD','Approved',0,'2026-01-01',1,1)
            """, ct);
        var otherOrder = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order WHERE company_id={0}", other).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return(company_id,document_no,purchase_order_id,business_date,status,is_deleted,created_at,created_by,version)
            VALUES ({other},'RET-SCH-1',{otherOrder},'2026-11-28','Draft',0,'2026-01-01',1,1)
            """, ct);
        var otherReturn = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_return WHERE company_id={0}", other).SingleAsync(ct);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return(company_id,document_no,purchase_order_id,business_date,status,is_deleted,created_at,created_by,version)
            VALUES ({company},'RET-SCH-2',{order},'2026-11-28','Invalid',0,'2026-01-01',1,1)
            """, ct));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line(company_id,purchase_return_id,receipt_line_id,original_movement_id,line_no,
                material_id,warehouse_id,quantity,created_at,created_by,version)
            VALUES ({company},{returnId},{receiptLine},101,1,81,91,0.000001,'2026-01-01',1,1)
            """, ct);
        var returnLine = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_return_line WHERE company_id={0}", company).SingleAsync(ct);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line(company_id,purchase_return_id,receipt_line_id,original_movement_id,line_no,
                material_id,warehouse_id,quantity,created_at,created_by,version)
            VALUES ({company},{returnId},{receiptLine},101,2,81,91,0,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line(company_id,purchase_return_id,receipt_line_id,original_movement_id,line_no,
                material_id,warehouse_id,quantity,created_at,created_by,version)
            VALUES ({company},{returnId},{receiptLine},0,2,81,91,1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line(company_id,purchase_return_id,receipt_line_id,original_movement_id,line_no,
                material_id,warehouse_id,quantity,created_at,created_by,version)
            VALUES ({company},{returnId},{receiptLine},101,0,81,91,1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line(company_id,purchase_return_id,receipt_line_id,original_movement_id,line_no,
                material_id,warehouse_id,quantity,created_at,created_by,version)
            VALUES ({company},{returnId},{receiptLine},101,1,81,91,1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line(company_id,purchase_return_id,receipt_line_id,original_movement_id,line_no,
                material_id,warehouse_id,quantity,original_unit_cost,created_at,created_by,version)
            VALUES ({company},{returnId},{receiptLine},101,2,81,91,1,-1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line(company_id,purchase_return_id,receipt_line_id,original_movement_id,line_no,
                material_id,warehouse_id,quantity,created_at,created_by,version)
            VALUES ({other},{returnId},{receiptLine},101,2,81,91,1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line(company_id,purchase_return_id,receipt_line_id,original_movement_id,line_no,
                material_id,warehouse_id,quantity,created_at,created_by,version)
            VALUES ({other},{otherReturn},{receiptLine},101,2,81,91,1,'2026-01-01',1,1)
            """, ct));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line_tracking(company_id,line_id,seq_no,lot_no,quantity,created_at,created_by,version)
            VALUES ({company},{returnLine},1,'LOT-1',0.000001,'2026-01-01',1,1)
            """, ct);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line_tracking(company_id,line_id,seq_no,serial_no,quantity,created_at,created_by,version)
            VALUES ({company},{returnLine},2,'SER-1',2,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line_tracking(company_id,line_id,seq_no,lot_no,quantity,created_at,created_by,version)
            VALUES ({company},{returnLine},2,'',1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line_tracking(company_id,line_id,seq_no,lot_no,quantity,created_at,created_by,version)
            VALUES ({company},{returnLine},1,'LOT-DUP',1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_return_line_tracking(company_id,line_id,seq_no,lot_no,quantity,created_at,created_by,version)
            VALUES ({other},{returnLine},2,'LOT-OTHER',1,'2026-01-01',1,1)
            """, ct));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.pur_purchase_receipt_line SET pending_return_reserved=6 WHERE id={receiptLine}
            """, ct));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.pur_purchase_return SET status='Posted' WHERE id={returnId}
            """, ct);
        switch (postedChange)
        {
            case "header":
                await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.pur_purchase_return SET document_no='RET-SCH-CHANGED' WHERE id={returnId}
                    """, ct));
                break;
            case "line":
                await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.pur_purchase_return_line SET quantity=2 WHERE id={returnLine}
                    """, ct));
                break;
            default:
                await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE dbo.pur_purchase_return_line_tracking WHERE company_id={company} AND line_id={returnLine}
                    """, ct));
                break;
        }
        await tx.RollbackAsync(ct);
    }
}
