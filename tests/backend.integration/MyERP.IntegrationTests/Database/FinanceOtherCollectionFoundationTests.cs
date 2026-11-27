using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-FI-007a1: other collection Draft storage is company scoped and has no AR/GL effect.</summary>
public sealed class FinanceOtherCollectionFoundationTests(TestDatabase database)
{
    [Fact]
    public async Task OtherCollectionDraftHasCompanyScopedNumberLiveReferenceAndChecks()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.tables WHERE name = 'ar_other_collection_doc'").ToListAsync(token);
        Assert.Single(tables);
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ar_other_collection_doc')")
            .ToListAsync(token);
        Assert.Contains("ux_ar_other_collection_doc_company_id_document_no", indexes);
        Assert.Contains("ux_ar_other_collection_doc_company_id_bank_account_id_bank_reference", indexes);
        Assert.Contains("ix_ar_other_collection_doc_company_id_income_category_id_business_date", indexes);
        var constraints = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.ar_other_collection_doc')")
            .ToListAsync(token);
        Assert.Contains("ck_ar_other_collection_doc_amount", constraints);
        Assert.Contains("ck_ar_other_collection_doc_currency", constraints);
        Assert.Contains("ck_ar_other_collection_doc_custom_fields_json", constraints);
        Assert.Contains("ck_ar_other_collection_doc_deleted", constraints);
        Assert.Contains("ck_ar_other_collection_doc_refs", constraints);
        Assert.Contains("ck_ar_other_collection_doc_status", constraints);
        var amountType = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(TYPE_NAME(system_type_id), '(', precision, ',', scale, ')') AS [Value] FROM sys.columns " +
            "WHERE object_id = OBJECT_ID('dbo.ar_other_collection_doc') AND name = 'amount'").SingleAsync(token);
        Assert.Equal("decimal(19,4)", amountType);
        var crossModuleFks = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.ar_other_collection_doc')")
            .SingleAsync(token);
        Assert.Equal(0, crossModuleFks);
    }

    [Theory]
    [InlineData("amount", "0")]
    [InlineData("currency", "'twd'")]
    [InlineData("payer_description", "N'  '")]
    [InlineData("income_category_id", "0")]
    [InlineData("status", "'Void'")]
    [InlineData("custom_fields", "N'not json'")]
    public async Task OtherCollectionDraftRejectsInvalidColumn(string column, string value)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var values = new Dictionary<string, string>
        {
            ["document_no"] = "'OC-CHECK-1'", ["payer_description"] = "N'Rent'", ["income_category_id"] = "1",
            ["bank_account_id"] = "1", ["bank_reference"] = "N'REF-1'", ["business_date"] = "'2026-11-27'",
            ["currency"] = "'TWD'", ["amount"] = "10.0000", ["status"] = "'Draft'", ["custom_fields"] = "NULL",
        };
        values[column] = value;
        var sql = "INSERT INTO dbo.ar_other_collection_doc (" + string.Join(", ", values.Keys) +
            ", is_deleted, company_id, created_at, created_by, version) VALUES (" + string.Join(", ", values.Values) +
            ", 0, 1, SYSDATETIME(), 1, 1)";
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
            () => db.Database.ExecuteSqlRawAsync(sql, token));
    }

    [Fact]
    public async Task OtherCollectionDraftReleasesReferenceOnlyAfterDraftSoftDelete()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        static string Row(string no, string status, string deleted, string deletedAt) =>
            "INSERT INTO dbo.ar_other_collection_doc (document_no, payer_description, income_category_id, " +
            "bank_account_id, bank_reference, business_date, currency, amount, status, is_deleted, deleted_at, company_id, " +
            $"created_at, created_by, version) VALUES ({no}, N'Rent', 1, 7, N'REF-LIVE', '2026-11-27', 'TWD', 1.2345, {status}, " +
            $"{deleted}, {deletedAt}, 1, SYSDATETIME(), 1, 1)";
        await db.Database.ExecuteSqlRawAsync(Row("'OC-A'", "'Draft'", "0", "NULL"), token);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync(
            Row("'OC-B'", "'Draft'", "0", "NULL"), token));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync(
            Row("'OC-A'", "'Draft'", "1", "SYSDATETIME()"), token));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync(
            Row("'OC-P'", "'Posted'", "1", "SYSDATETIME()"), token));
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.ar_other_collection_doc SET is_deleted = 1, deleted_at = SYSDATETIME() WHERE document_no = 'OC-A'", token);
        await db.Database.ExecuteSqlRawAsync(Row("'OC-C'", "'Draft'", "0", "NULL"), token);
        var stored = await db.Database.SqlQueryRaw<decimal>(
            "SELECT amount AS [Value] FROM dbo.ar_other_collection_doc WHERE document_no = 'OC-C'").SingleAsync(token);
        Assert.Equal(1.2345m, stored);
    }
}
