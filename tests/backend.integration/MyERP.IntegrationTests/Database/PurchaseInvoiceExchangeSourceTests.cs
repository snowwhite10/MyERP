using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-PUR-008b2 (ADR-206): purchase-order rate snapshot columns and the in-transaction exchange source.</summary>
public sealed class PurchaseInvoiceExchangeSourceTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> InsertOrderAsync(MyErpDbContext db, long company, string currency,
        string? baseCurrency, decimal? rate)
    {
        // Raw interpolated decimals default to scale 2; pin decimal(19,8) so 8-decimal rates are not truncated.
        var rateParameter = new SqlParameter("@rate", System.Data.SqlDbType.Decimal)
            { Precision = 19, Scale = 8, Value = (object?)rate ?? DBNull.Value };
        await db.Database.ExecuteSqlAsync(
            $"INSERT dbo.pur_purchase_order(company_id,supplier_id,currency,base_currency_code,exchange_rate,status,is_deleted,created_at,created_by,version) VALUES ({company},7,{currency},{baseCurrency},{rateParameter},'Approved',0,'2026-01-01',1,1)", Ct);
        return await db.Database.SqlQuery<long>(
            $"SELECT MAX(id) AS [Value] FROM dbo.pur_purchase_order WHERE company_id={company}").SingleAsync(Ct);
    }

    [Fact]
    public async Task PUR_FXSRC_008_RateSnapshotColumnsAreTypedAndChecked()
    {
        await using var db = database.CreateRawContext();
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT c.name + ':' + t.name + ':' + CAST(c.max_length AS varchar(5)) + ':' + CAST(c.precision AS varchar(5)) + ':' + CAST(c.scale AS varchar(5)) + ':' + CAST(c.is_nullable AS varchar(1)) AS [Value] " +
            "FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id " +
            "WHERE c.object_id = OBJECT_ID('dbo.pur_purchase_order') AND c.name IN ('base_currency_code','exchange_rate')")
            .ToListAsync(Ct);
        columns.Should().BeEquivalentTo("base_currency_code:char:3:0:0:1", "exchange_rate:decimal:9:19:8:1");
        (await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = {"ck_pur_order_rate_snapshot"} AND parent_object_id = OBJECT_ID('dbo.pur_purchase_order')")
            .SingleAsync(Ct)).Should().Be(1);

        var company = TestCompanies.Next();
        // Rejected rows never persist, so they run outside the rolled-back transaction.
        foreach (var (currency, baseCurrency, rate) in new (string, string?, decimal?)[]
                 { ("USD", "TWD", null), ("USD", null, 31.5m), ("USD", "TWD", 0m), ("USD", "TWD", -1m), ("TWD", "TWD", 1.5m) })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => InsertOrderAsync(db, company, currency, baseCurrency, rate));
            error.Number.Should().Be(547, $"{currency}/{baseCurrency}/{rate} must violate the rate snapshot check");
            error.Message.Should().Contain("ck_pur_order_rate_snapshot");
        }

        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        await InsertOrderAsync(db, company, "USD", null, null);
        await InsertOrderAsync(db, company, "USD", "TWD", 31.12345678m);
        await InsertOrderAsync(db, company, "TWD", "TWD", 1m);
        (await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.pur_purchase_order WHERE company_id={company}").SingleAsync(Ct)).Should().Be(3);
        await tx.RollbackAsync(Ct);
    }

    [Fact]
    public async Task PUR_FXSRC_009_ReadsCompanyScopedSnapshotInsideTransactionAndValidatesBaseCurrency()
    {
        var company = TestCompanies.Next();
        await using var scoped = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString).Options, [new PurchaseInvoiceSourceDbModel()],
            new FixedCompany(company));
        var source = new PurchaseInvoiceExchangeSource(scoped, new Currencies("TWD"));

        var outside = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ReadAsync(company, 1, "USD", Ct));
        outside.Message.Should().Contain("transaction");

        await using var tx = await scoped.Database.BeginTransactionAsync(Ct);
        var foreign = await InsertOrderAsync(scoped, company, "USD", "TWD", 31.12345678m);
        var local = await InsertOrderAsync(scoped, company, "TWD", "TWD", 1m);
        var legacy = await InsertOrderAsync(scoped, company, "USD", null, null);
        var otherCompany = await InsertOrderAsync(scoped, TestCompanies.Next(), "USD", "TWD", 30m);

        (await source.ReadAsync(company, foreign, "USD", Ct))
            .Should().Be(new PurchaseInvoiceExchangeSnapshot("TWD", 31.12345678m));
        (await source.ReadAsync(company, local, "TWD", Ct))
            .Should().Be(new PurchaseInvoiceExchangeSnapshot("TWD", 1m));
        (await source.ReadAsync(company, otherCompany, "USD", Ct)).Should().BeNull();
        (await source.ReadAsync(company, long.MaxValue, "USD", Ct)).Should().BeNull();
        (await Assert.ThrowsAsync<BusinessRuleException>(() => source.ReadAsync(company, legacy, "USD", Ct)))
            .ErrorCode.Should().Be("PUR.INVOICE.RATE_SNAPSHOT_MISSING");

        var changed = new PurchaseInvoiceExchangeSource(scoped, new Currencies("HKD"));
        (await Assert.ThrowsAsync<BusinessRuleException>(() => changed.ReadAsync(company, foreign, "USD", Ct)))
            .ErrorCode.Should().Be("PUR.INVOICE.BASE_CURRENCY_CHANGED");
        var none = new PurchaseInvoiceExchangeSource(scoped, new Currencies(null));
        (await Assert.ThrowsAsync<BusinessRuleException>(() => none.ReadAsync(company, foreign, "USD", Ct)))
            .ErrorCode.Should().Be("PUR.INVOICE.BASE_CURRENCY_NOT_SET");
        await tx.RollbackAsync(Ct);
    }

    private sealed class Currencies(string? baseCurrency) : ICurrencyLookup
    {
        public Task<CurrencyInfo?> GetBaseCurrencyAsync(CancellationToken token = default) =>
            Task.FromResult(baseCurrency is null ? null : new CurrencyInfo(baseCurrency, baseCurrency, null, 0, true, true));
        public Task<CurrencyInfo?> FindCurrencyAsync(string currencyCode, CancellationToken token = default) => throw new NotSupportedException();
        public Task<decimal> GetRateAsync(string currencyCode, DateOnly date, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ExchangeRateSnapshot> GetRateSnapshotAsync(string currencyCode, DateOnly date, CancellationToken token = default) => throw new NotSupportedException();
        public Task<CurrencyPrecision> GetPrecisionAsync(CancellationToken token = default) => throw new NotSupportedException();
    }
}
