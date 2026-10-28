using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesQuotationSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task QuotationTablesAndCompanyScopedKeysExist()
    {
        await using var db = database.CreateRawContext();
        var names = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.tables WHERE [name] IN ('sal_quotation','sal_quotation_line')")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("sal_quotation", names);
        Assert.Contains("sal_quotation_line", names);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.indexes WHERE [object_id] IN (OBJECT_ID('dbo.sal_quotation'), OBJECT_ID('dbo.sal_quotation_line'))")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("ix_sal_quotation_company_id_document_no", indexes);
        Assert.Contains("ix_sal_quotation_line_company_id_quotation_id_line_no", indexes);
        Assert.Contains("ix_sal_quotation_company_id_branch_id_department_id_owner_user_id", indexes);

        var keys = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.foreign_keys WHERE [parent_object_id] = OBJECT_ID('dbo.sal_quotation_line')")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("fk_sal_quotation_line_sal_quotation_company_id_quotation_id", keys);

        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.check_constraints WHERE [parent_object_id] IN (OBJECT_ID('dbo.sal_quotation'), OBJECT_ID('dbo.sal_quotation_line'))")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("ck_sal_quotation_dates", checks);
        Assert.Contains("ck_sal_quotation_deleted", checks);
        Assert.Contains("ck_sal_quotation_line_quantity", checks);
    }
}
