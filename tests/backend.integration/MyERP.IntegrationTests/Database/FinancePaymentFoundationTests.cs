using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-FI-006a: payment and settlement source persistence must be permanent and company scoped.</summary>
public sealed class FinancePaymentFoundationTests(TestDatabase database)
{
    [Fact]
    public async Task PaymentAndSettlementTablesHavePermanentKeysAndCompanyForeignKeys()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.tables WHERE name IN ('ap_payment_source','ap_payment_settlement','ap_payment_allocation')")
            .ToListAsync(token);
        Assert.Equal(3, tables.Count);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN (OBJECT_ID('dbo.ap_payment_source'), OBJECT_ID('dbo.ap_payment_settlement'), OBJECT_ID('dbo.ap_payment_allocation'))")
            .ToListAsync(token);
        Assert.Contains("ux_ap_payment_source_company_id_source_type_source_id", indexes);
        Assert.Contains("ux_ap_payment_source_company_id_bank_account_id_bank_reference", indexes);
        Assert.Contains("ux_ap_payment_settlement_company_id_source_type_source_id", indexes);
        Assert.Contains("ux_ap_payment_allocation_company_id_settlement_id_payable_id", indexes);
        Assert.DoesNotContain("ux_ap_payment_allocation_company_id_payment_id_payable_id", indexes);

        var foreignKeys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('dbo.ap_payment_source'), OBJECT_ID('dbo.ap_payment_settlement'), OBJECT_ID('dbo.ap_payment_allocation'))")
            .ToListAsync(token);
        Assert.Contains("fk_ap_payment_source_gl_journal_company_id_gl_journal_id", foreignKeys);
        Assert.Contains("fk_ap_payment_settlement_ap_payment_source_company_id_payment_id", foreignKeys);
        Assert.Contains("fk_ap_payment_allocation_ap_posted_payable_company_id_payable_id", foreignKeys);

        var triggers = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.triggers WHERE parent_id IN (OBJECT_ID('dbo.ap_payment_source'), OBJECT_ID('dbo.ap_payment_settlement'), OBJECT_ID('dbo.ap_payment_allocation'))")
            .ToListAsync(token);
        Assert.Contains("tr_ap_payment_source_origin", triggers);
        Assert.Contains("tr_ap_payment_settlement_immutable", triggers);
        Assert.Contains("tr_ap_payment_allocation_immutable", triggers);

        var amounts = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(OBJECT_NAME(object_id),'.',name,':',precision,':',scale) AS [Value] FROM sys.columns WHERE object_id IN (OBJECT_ID('dbo.ap_payment_source'), OBJECT_ID('dbo.ap_payment_settlement'), OBJECT_ID('dbo.ap_payment_allocation')) AND name IN ('amount','unapplied_amount','total_amount')")
            .ToListAsync(token);
        Assert.Contains("ap_payment_source.amount:19:4", amounts);
        Assert.Contains("ap_payment_source.unapplied_amount:19:4", amounts);
        Assert.Contains("ap_payment_settlement.total_amount:19:4", amounts);
        Assert.Contains("ap_payment_allocation.amount:19:4", amounts);

        var constraints = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('dbo.ap_payment_source'), OBJECT_ID('dbo.ap_payment_settlement'), OBJECT_ID('dbo.ap_payment_allocation'))")
            .ToListAsync(token);
        Assert.Contains("ck_ap_payment_source_amounts", constraints);
        Assert.Contains("ck_ap_payment_settlement_amount", constraints);
        Assert.Contains("ck_ap_payment_allocation_amount", constraints);
    }
}
