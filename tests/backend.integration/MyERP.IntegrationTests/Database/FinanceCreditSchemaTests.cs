using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

/// <summary>Real SQL migration contract for the FI-owned credit key, limits and posted AR exposure.</summary>
public sealed class FinanceCreditSchemaTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreditSchemaHasPermanentKeyEffectiveLimitsAndPostedReceivables()
    {
        await using var db = database.CreateRawContext();
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.tables WHERE [name] IN ('fi_credit_key', 'fi_credit_limit', 'ar_posted_receivable')")
            .ToListAsync(Ct);
        Assert.Contains("fi_credit_key", tables);
        Assert.Contains("fi_credit_limit", tables);
        Assert.Contains("ar_posted_receivable", tables);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.indexes WHERE [object_id] IN (OBJECT_ID('dbo.fi_credit_key'), OBJECT_ID('dbo.fi_credit_limit'), OBJECT_ID('dbo.ar_posted_receivable'))")
            .ToListAsync(Ct);
        Assert.Contains("ux_fi_credit_key_company_id_customer_id", indexes);
        Assert.Contains("ix_fi_credit_limit_company_id_customer_id_effective_from_effective_to", indexes);
        Assert.Contains("ux_ar_posted_receivable_company_id_source_type_source_id", indexes);
        Assert.Contains("ix_ar_posted_receivable_company_id_customer_id", indexes);

        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.check_constraints WHERE [parent_object_id] IN (OBJECT_ID('dbo.fi_credit_limit'), OBJECT_ID('dbo.ar_posted_receivable'))")
            .ToListAsync(Ct);
        Assert.Contains("ck_fi_credit_limit_dates", checks);
        Assert.Contains("ck_fi_credit_limit_amount", checks);
        Assert.Contains("ck_ar_posted_receivable_open_amount", checks);
    }
}
