using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-FI-008a1: other payment Draft storage is company scoped and has no AP/GL effect.</summary>
public sealed class FinanceOtherPaymentFoundationTests(TestDatabase database)
{
    [Fact]
    public async Task OtherPaymentDraftHasCompanyScopedNumberLiveReferenceAndChecks()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.tables WHERE name = 'ap_other_payment_doc'").ToListAsync(token);
        Assert.Single(tables);
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ap_other_payment_doc')")
            .ToListAsync(token);
        Assert.Contains("ux_ap_other_payment_doc_company_id_document_no", indexes);
        Assert.Contains("ux_ap_other_payment_doc_company_id_bank_account_id_bank_reference", indexes);
        Assert.Contains("ix_ap_other_payment_doc_company_id_expense_category_id_business_date", indexes);
        var constraints = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.ap_other_payment_doc')")
            .ToListAsync(token);
        Assert.Contains("ck_ap_other_payment_doc_amount", constraints);
        Assert.Contains("ck_ap_other_payment_doc_currency", constraints);
        Assert.Contains("ck_ap_other_payment_doc_custom_fields_json", constraints);
        Assert.Contains("ck_ap_other_payment_doc_deleted", constraints);
        Assert.Contains("ck_ap_other_payment_doc_refs", constraints);
        Assert.Contains("ck_ap_other_payment_doc_status", constraints);
        var amountType = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(TYPE_NAME(system_type_id), '(', precision, ',', scale, ')') AS [Value] FROM sys.columns " +
            "WHERE object_id = OBJECT_ID('dbo.ap_other_payment_doc') AND name = 'amount'").SingleAsync(token);
        Assert.Equal("decimal(19,4)", amountType);
        var crossModuleFks = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.ap_other_payment_doc')")
            .SingleAsync(token);
        Assert.Equal(0, crossModuleFks);
    }

    [Theory]
    [InlineData("amount", "0")]
    [InlineData("currency", "'twd'")]
    [InlineData("payee_description", "N'  '")]
    [InlineData("expense_category_id", "0")]
    [InlineData("status", "'Void'")]
    [InlineData("custom_fields", "N'not json'")]
    public async Task OtherPaymentDraftRejectsInvalidColumn(string column, string value)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var values = new Dictionary<string, string>
        {
            ["document_no"] = "'OP-CHECK-1'", ["payee_description"] = "N'Courier'", ["expense_category_id"] = "1",
            ["bank_account_id"] = "1", ["bank_reference"] = "N'REF-1'", ["business_date"] = "'2026-11-27'",
            ["currency"] = "'TWD'", ["amount"] = "10.0000", ["status"] = "'Draft'", ["custom_fields"] = "NULL",
        };
        values[column] = value;
        var sql = "INSERT INTO dbo.ap_other_payment_doc (" + string.Join(", ", values.Keys) +
            ", is_deleted, company_id, created_at, created_by, version) VALUES (" + string.Join(", ", values.Values) +
            ", 0, 1, SYSDATETIME(), 1, 1)";
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
            () => db.Database.ExecuteSqlRawAsync(sql, token));
    }

    [Fact]
    public async Task OtherPaymentDraftReleasesReferenceOnlyAfterDraftSoftDelete()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        static string Row(string no, string status, string deleted, string deletedAt) =>
            "INSERT INTO dbo.ap_other_payment_doc (document_no, payee_description, expense_category_id, " +
            "bank_account_id, bank_reference, business_date, currency, amount, status, is_deleted, deleted_at, company_id, " +
            $"created_at, created_by, version) VALUES ({no}, N'Courier', 1, 7, N'REF-LIVE', '2026-11-27', 'TWD', 1.2345, {status}, " +
            $"{deleted}, {deletedAt}, 1, SYSDATETIME(), 1, 1)";
        await db.Database.ExecuteSqlRawAsync(Row("'OP-A'", "'Draft'", "0", "NULL"), token);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync(
            Row("'OP-B'", "'Draft'", "0", "NULL"), token));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync(
            Row("'OP-A'", "'Draft'", "1", "SYSDATETIME()"), token));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync(
            Row("'OP-P'", "'Posted'", "1", "SYSDATETIME()"), token));
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.ap_other_payment_doc SET is_deleted = 1, deleted_at = SYSDATETIME() WHERE document_no = 'OP-A'", token);
        await db.Database.ExecuteSqlRawAsync(Row("'OP-C'", "'Draft'", "0", "NULL"), token);
        var stored = await db.Database.SqlQueryRaw<decimal>(
            "SELECT amount AS [Value] FROM dbo.ap_other_payment_doc WHERE document_no = 'OP-C'").SingleAsync(token);
        Assert.Equal(1.2345m, stored);
    }

    [Fact]
    public async Task OtherPaymentDraftAllowsSameReferenceOnOtherBankOrCompany()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        static string Row(string no, long bank, long company) =>
            "INSERT INTO dbo.ap_other_payment_doc (document_no, payee_description, expense_category_id, " +
            "bank_account_id, bank_reference, business_date, currency, amount, status, is_deleted, company_id, " +
            $"created_at, created_by, version) VALUES ('{no}', N'Courier', 1, {bank}, N'WD-SHARED', '2026-11-27', 'TWD', " +
            $"999999999999999.9999, 'Draft', 0, {company}, SYSDATETIME(), 1, 1)";
        await db.Database.ExecuteSqlRawAsync(Row("OP-X", 7, 1), token);
        await db.Database.ExecuteSqlRawAsync(Row("OP-Y", 8, 1), token);
        await db.Database.ExecuteSqlRawAsync(Row("OP-X", 7, 2), token);
        var amounts = await db.Database.SqlQueryRaw<decimal>(
            "SELECT amount AS [Value] FROM dbo.ap_other_payment_doc WHERE bank_reference = N'WD-SHARED'").ToListAsync(token);
        Assert.Equal(3, amounts.Count);
        Assert.All(amounts, x => Assert.Equal(999999999999999.9999m, x));
    }
}
