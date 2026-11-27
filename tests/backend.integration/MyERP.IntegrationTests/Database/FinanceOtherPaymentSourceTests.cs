using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-FI-008a2: the Posted other payment source is permanent, matches its saved Draft and keeps the
/// withdrawal reference reserved even after the Draft row is soft deleted; the FI category→expense
/// account map is one row per company/category with an increasing version.
/// </summary>
public sealed class FinanceOtherPaymentSourceTests(TestDatabase database)
{
    private const int GuardError = 51039;

    [Fact]
    public async Task SourceAndMapTablesHavePermanentKeysChecksAndNoCrossModuleForeignKeys()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.tables WHERE name IN ('ap_other_payment_source','gl_other_payment_account_map')")
            .ToListAsync(token);
        Assert.Equal(2, tables.Count);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN (OBJECT_ID('dbo.ap_other_payment_source'), OBJECT_ID('dbo.gl_other_payment_account_map'))")
            .ToListAsync(token);
        Assert.Contains("ux_ap_other_payment_source_company_id_source_type_source_id", indexes);
        Assert.Contains("ux_ap_other_payment_source_company_id_bank_account_id_bank_reference", indexes);
        Assert.Contains("ix_ap_other_payment_source_company_id_expense_category_id_business_date", indexes);
        Assert.Contains("ux_ap_other_payment_source_gl_journal_id", indexes);
        Assert.Contains("ux_gl_other_payment_account_map_company_id_expense_category_id", indexes);

        var filtered = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ap_other_payment_source') AND has_filter = 1")
            .SingleAsync(token);
        Assert.Equal(0, filtered);

        var foreignKeys = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(OBJECT_NAME(parent_object_id), '->', OBJECT_NAME(referenced_object_id)) AS [Value] FROM sys.foreign_keys " +
            "WHERE parent_object_id IN (OBJECT_ID('dbo.ap_other_payment_source'), OBJECT_ID('dbo.gl_other_payment_account_map'))")
            .ToListAsync(token);
        Assert.Equal(
            ["ap_other_payment_source->ap_other_payment_doc", "ap_other_payment_source->gl_journal"],
            foreignKeys.Order(StringComparer.Ordinal).ToList());

        var triggers = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.triggers WHERE parent_id IN (OBJECT_ID('dbo.ap_other_payment_source'), OBJECT_ID('dbo.gl_other_payment_account_map'), OBJECT_ID('dbo.ap_other_payment_doc'))")
            .ToListAsync(token);
        Assert.Contains("tr_ap_other_payment_source_immutable", triggers);
        Assert.Contains("tr_gl_other_payment_account_map_guard", triggers);
        Assert.Contains("tr_ap_other_payment_doc_posted_reference", triggers);

        var constraints = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('dbo.ap_other_payment_source'), OBJECT_ID('dbo.gl_other_payment_account_map'))")
            .ToListAsync(token);
        Assert.Contains("ck_ap_other_payment_source_identity", constraints);
        Assert.Contains("ck_ap_other_payment_source_type", constraints);
        Assert.Contains("ck_ap_other_payment_source_refs", constraints);
        Assert.Contains("ck_ap_other_payment_source_currency", constraints);
        Assert.Contains("ck_ap_other_payment_source_amount", constraints);
        Assert.Contains("ck_gl_other_payment_account_map_identity", constraints);

        var amountType = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(TYPE_NAME(system_type_id), '(', precision, ',', scale, ')') AS [Value] FROM sys.columns " +
            "WHERE object_id = OBJECT_ID('dbo.ap_other_payment_source') AND name = 'amount'").SingleAsync(token);
        Assert.Equal("decimal(19,4)", amountType);
    }

    // A trigger THROW aborts the caller transaction, so each guard case is the last statement of its own transaction.
    [Theory]
    [InlineData("UPDATE dbo.ap_other_payment_source SET amount = 1 WHERE company_id = {0}")]
    [InlineData("UPDATE dbo.ap_other_payment_source SET expense_account_id = 4102 WHERE company_id = {0}")]
    [InlineData("UPDATE dbo.ap_other_payment_source SET mapping_version = 3 WHERE company_id = {0}")]
    [InlineData("UPDATE dbo.ap_other_payment_source SET created_by = 2 WHERE company_id = {0}")]
    [InlineData("DELETE FROM dbo.ap_other_payment_source WHERE company_id = {0}")]
    public async Task PostedSourceSnapshotsDraftAndIsImmutable(string mutation)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, 880201, token);
        var doc = await InsertDocAsync(db, f.Company, "OP-S1", "WD-1", 123.4567m, token);
        await InsertSourceAsync(db, f.Company, doc, f.Journal1, "WD-1", 123.4567m, token);

        var stored = await db.Database.SqlQuery<string>(
            $"SELECT CONCAT(source_type,'|',expense_category_id,'|',expense_account_id,'|',mapping_version,'|',amount) AS [Value] FROM dbo.ap_other_payment_source WHERE company_id = {f.Company}")
            .SingleAsync(token);
        Assert.Equal("OtherPayment|31|4101|2|123.4567", stored);

        await AssertGuardAsync(db, string.Format(System.Globalization.CultureInfo.InvariantCulture, mutation, f.Company), token);
    }

    [Fact]
    public async Task SecondSourceForSameDraftOrSameWithdrawalReferenceIsRejected()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, 880202, token);
        var doc = await InsertDocAsync(db, f.Company, "OP-D1", "WD-DUP", 10m, token);
        await InsertSourceAsync(db, f.Company, doc, f.Journal1, "WD-DUP", 10m, token);
        // Unique-key violations are statement errors and keep the transaction usable.
        await Assert.ThrowsAsync<SqlException>(
            () => InsertSourceAsync(db, f.Company, doc, f.Journal2, "WD-DUP", 10m, token));
        var count = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ap_other_payment_source WHERE company_id = {f.Company}").SingleAsync(token);
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("amount", "9.9999")]
    [InlineData("bank_reference", "N'WD-OTHER'")]
    [InlineData("bank_reference", "N'wd-m'")]
    [InlineData("bank_account_id", "8")]
    [InlineData("expense_category_id", "32")]
    [InlineData("business_date", "'2026-03-30'")]
    [InlineData("base_currency", "'USD'")]
    public async Task SourceMustMatchSavedDraft(string column, string value)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, 880203, token);
        var doc = await InsertDocAsync(db, f.Company, "OP-M1", "WD-M", 10m, token);
        var values = SourceValues(f.Company, doc, f.Journal1, "WD-M", 10m);
        values[column] = value;
        await AssertGuardAsync(db, SourceSql(values), token);
    }

    [Theory]
    [InlineData("source_type", "'PurchasePayment'")]
    [InlineData("amount", "0")]
    [InlineData("expense_account_id", "0")]
    [InlineData("mapping_version", "0")]
    [InlineData("source_version", "0")]
    [InlineData("fingerprint", "' '")]
    [InlineData("base_currency", "'twd'")]
    public async Task SourceRejectsInvalidColumn(string column, string value)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, 880204, token);
        var doc = await InsertDocAsync(db, f.Company, "OP-C1", "WD-C", 10m, token);
        var values = SourceValues(f.Company, doc, f.Journal1, "WD-C", 10m);
        values[column] = value;
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(SourceSql(values), token));
    }

    [Fact]
    public async Task SourceCannotPointAtAnotherCompanyJournal()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, 880211, token);
        var other = await SeedAsync(db, 880212, token);
        var doc = await InsertDocAsync(db, f.Company, "OP-J1", "WD-J", 10m, token);
        await Assert.ThrowsAsync<SqlException>(
            () => InsertSourceAsync(db, f.Company, doc, other.Journal1, "WD-J", 10m, token));
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    public async Task PostedReferenceStaysReservedAfterItsDraftRowIsSoftDeleted(string mode)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, 880205, token);
        var doc = await InsertDocAsync(db, f.Company, "OP-H1", "WD-HIST", 10m, token);
        await InsertSourceAsync(db, f.Company, doc, f.Journal1, "WD-HIST", 10m, token);
        // Simulates history whose Draft row was released: the live filtered index no longer protects the reference.
        await db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.ap_other_payment_doc SET is_deleted = 1, deleted_at = SYSDATETIME() WHERE id = {doc}", token);

        if (mode == "insert")
        {
            var ex = await Assert.ThrowsAsync<SqlException>(
                () => InsertDocAsync(db, f.Company, "OP-H2", "wd-hist", 10m, token));
            Assert.Equal(GuardError, ex.Number);
            return;
        }

        var other = await InsertDocAsync(db, f.Company, "OP-H3", "WD-FREE", 10m, token);
        await AssertGuardAsync(db, $"UPDATE dbo.ap_other_payment_doc SET bank_reference = N'WD-HIST' WHERE id = {other}", token);
    }

    [Fact]
    public async Task PostedReferenceIsScopedToItsBankAndCompany()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, 880206, token);
        var doc = await InsertDocAsync(db, f.Company, "OP-B1", "WD-SCOPE", 10m, token);
        await InsertSourceAsync(db, f.Company, doc, f.Journal1, "WD-SCOPE", 10m, token);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.ap_other_payment_doc SET is_deleted = 1, deleted_at = SYSDATETIME() WHERE id = {doc}", token);

        var otherBank = await InsertDocAsync(db, f.Company, "OP-B2", "WD-FREE", 10m, token);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.ap_other_payment_doc SET bank_account_id = 8, bank_reference = N'WD-SCOPE' WHERE id = {otherBank}", token);
        await InsertDocAsync(db, 880213, "OP-B1", "WD-SCOPE", 10m, token);
        var count = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ap_other_payment_doc WHERE bank_reference = N'WD-SCOPE' AND company_id IN ({f.Company}, 880213)")
            .SingleAsync(token);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task PostedDraftCanStillChangeStatusWithoutTrippingReferenceGuard()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, 880207, token);
        var doc = await InsertDocAsync(db, f.Company, "OP-P1", "WD-OWN", 10m, token);
        await InsertSourceAsync(db, f.Company, doc, f.Journal1, "WD-OWN", 10m, token);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.ap_other_payment_doc SET status = 'Posted', version = version + 1 WHERE id = {doc}", token);
        var status = await db.Database.SqlQuery<string>(
            $"SELECT status AS [Value] FROM dbo.ap_other_payment_doc WHERE id = {doc}").SingleAsync(token);
        Assert.Equal("Posted", status);
    }

    [Theory]
    [InlineData("UPDATE dbo.gl_other_payment_account_map SET expense_account_id = 4103 WHERE company_id = {0} AND expense_category_id = 31")]
    [InlineData("UPDATE dbo.gl_other_payment_account_map SET expense_category_id = 33, version = version + 1 WHERE company_id = {0} AND expense_category_id = 31")]
    [InlineData("UPDATE dbo.gl_other_payment_account_map SET company_id = 880209, version = version + 1 WHERE company_id = {0} AND expense_category_id = 31")]
    [InlineData("UPDATE dbo.gl_other_payment_account_map SET version = 1 WHERE company_id = {0} AND expense_category_id = 31")]
    [InlineData("DELETE FROM dbo.gl_other_payment_account_map WHERE company_id = {0} AND expense_category_id = 32")]
    public async Task AccountMapIsUniquePerCategoryAndOnlyMovesForward(string mutation)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        const long company = 880208;
        await InsertMapAsync(db, company, 31, 4101, token);
        await InsertMapAsync(db, company, 32, 4101, token);
        await Assert.ThrowsAsync<SqlException>(() => InsertMapAsync(db, company, 31, 4102, token));

        await db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.gl_other_payment_account_map SET expense_account_id = 4102, version = version + 1 WHERE company_id = {company} AND expense_category_id = 31", token);
        var mapped = await db.Database.SqlQuery<string>(
            $"SELECT CONCAT(expense_account_id, '@', version) AS [Value] FROM dbo.gl_other_payment_account_map WHERE company_id = {company} AND expense_category_id = 31")
            .SingleAsync(token);
        Assert.Equal("4102@2", mapped);

        await AssertGuardAsync(db, string.Format(System.Globalization.CultureInfo.InvariantCulture, mutation, company), token);
    }

    [Theory]
    [InlineData(0, 4101)]
    [InlineData(31, 0)]
    public async Task AccountMapRejectsNonPositiveIds(long category, long account)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await Assert.ThrowsAsync<SqlException>(() => InsertMapAsync(db, 880210, category, account, token));
    }

    private sealed record Fixture(long Company, long Journal1, long Journal2);

    private static async Task<Fixture> SeedAsync(DbContext db, long company, CancellationToken token)
    {
        await db.Database.ExecuteSqlAsync(
            $"INSERT dbo.gl_period (company_id,start_date,end_date,status,created_at,created_by,version) VALUES ({company},'2026-03-01','2026-03-31','Open','2026-01-01',1,1)", token);
        var period = await db.Database.SqlQuery<long>(
            $"SELECT id AS [Value] FROM dbo.gl_period WHERE company_id={company}").SingleAsync(token);
        var journals = new List<long>();
        foreach (var id in new long[] { 1, 2 })
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({company},'FI','OtherPayment',{id},1,'fp','2026-01-01',1,1)", token);
            var key = await db.Database.SqlQuery<long>(
                $"SELECT id AS [Value] FROM dbo.gl_source_key WHERE company_id={company} AND source_type='OtherPayment' AND source_id={id}").SingleAsync(token);
            await db.Database.ExecuteSqlAsync(
                $"INSERT dbo.gl_journal (company_id,period_id,source_key_id,business_date,base_currency,status,total_debit,total_credit,created_at,created_by,version) VALUES ({company},{period},{key},'2026-03-31','TWD','Posted',10,10,'2026-01-01',1,1)", token);
            journals.Add(await db.Database.SqlQuery<long>(
                $"SELECT id AS [Value] FROM dbo.gl_journal WHERE company_id={company} AND source_key_id={key}").SingleAsync(token));
        }

        return new Fixture(company, journals[0], journals[1]);
    }

    private static async Task<long> InsertDocAsync(
        DbContext db, long company, string no, string reference, decimal amount, CancellationToken token)
    {
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO dbo.ap_other_payment_doc (document_no, payee_description, expense_category_id, bank_account_id, bank_reference, business_date, currency, amount, status, is_deleted, company_id, created_at, created_by, version) VALUES ({no}, N'Courier', 31, 7, {reference}, '2026-03-31', 'TWD', CAST({amount.ToString(System.Globalization.CultureInfo.InvariantCulture)} AS decimal(19,4)), 'Draft', 0, {company}, SYSDATETIME(), 1, 1)",
            token);
        return await db.Database.SqlQuery<long>(
            $"SELECT id AS [Value] FROM dbo.ap_other_payment_doc WHERE company_id = {company} AND document_no = {no}").SingleAsync(token);
    }

    private static Dictionary<string, string> SourceValues(long company, long doc, long journal, string reference, decimal amount) =>
        new()
        {
            ["company_id"] = company.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["source_type"] = "'OtherPayment'",
            ["source_id"] = doc.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["source_version"] = "3",
            ["fingerprint"] = "'sha256:abc'",
            ["gl_journal_id"] = journal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["business_date"] = "'2026-03-31'",
            ["expense_category_id"] = "31",
            ["expense_account_id"] = "4101",
            ["mapping_version"] = "2",
            ["bank_account_id"] = "7",
            ["bank_reference"] = $"N'{reference}'",
            ["base_currency"] = "'TWD'",
            ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["created_at"] = "SYSDATETIME()",
            ["created_by"] = "1",
            ["version"] = "1",
        };

    private static string SourceSql(Dictionary<string, string> values) =>
        "INSERT INTO dbo.ap_other_payment_source (" + string.Join(", ", values.Keys) + ") VALUES (" +
        string.Join(", ", values.Values) + ")";

    private static Task<int> InsertSourceAsync(
        DbContext db, long company, long doc, long journal, string reference, decimal amount, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync(SourceSql(SourceValues(company, doc, journal, reference, amount)), token);

    private static Task<int> InsertMapAsync(DbContext db, long company, long category, long account, CancellationToken token) =>
        db.Database.ExecuteSqlAsync(
            $"INSERT INTO dbo.gl_other_payment_account_map (company_id, expense_category_id, expense_account_id, created_at, created_by, version) VALUES ({company}, {category}, {account}, SYSDATETIME(), 1, 1)",
            token);

    private static async Task AssertGuardAsync(DbContext db, string sql, CancellationToken token)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, token));
        Assert.Equal(GuardError, ex.Number);
    }
}
