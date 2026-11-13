using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-SALES-INV-001～009（WU-FI-002a2b，ADR-108）：<see cref="ISalesInvoicePostingService"/> 在呼叫者交易內以真實 SQL
/// 把 SD 發票過帳為 FI 應收＋GL 傳票。SD 來源以測試假實作注入（SD-007a 實作真版），映射以測試假實作注入。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private const long InvoiceCustomerId = 601;

    /// <summary>測試用 SD 發票來源：記錄讀取時是否在呼叫者交易內。</summary>
    private sealed class FakeInvoices
    {
        public readonly Dictionary<long, FinanceSalesInvoiceSnapshot> Items = [];
        public int Reads;

        /// <summary>WU-FI-002a2c：讀取時（呼叫者交易內、信用鍵已鎖）的掛鉤，用於保持交易或記錄次序。</summary>
        public Func<long, CancellationToken, Task>? OnRead;
    }

    private sealed class FakeInvoiceSource(FakeInvoices invoices) : IFinanceSalesInvoiceSource
    {
        public async Task<FinanceSalesInvoiceSnapshot?> ReadInvoiceAsync(long companyId, long invoiceId,
            DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(transaction.Connection);
            Interlocked.Increment(ref invoices.Reads);
            if (invoices.OnRead is { } hook)
                await hook(invoiceId, cancellationToken);
            return invoices.Items.TryGetValue(invoiceId, out var item) && item.CompanyId == companyId ? item : null;
        }
    }

    private sealed class FakeAccountMap(MappingState state) : ISalesInvoiceAccountMapSource
    {
        public Task<SalesInvoiceAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken) =>
            Task.FromResult<SalesInvoiceAccountMap?>(new(state.Version, "1131", "4101", "2171"));
    }

    private ServiceProvider InvoiceProvider(long companyId, FakeInvoices invoices, bool withMap = true,
        bool withSource = true, int? creditLockTimeoutMilliseconds = null) =>
        Provider(companyId, new MappingState(), services =>
        {
            if (creditLockTimeoutMilliseconds is { } timeout)
                services.AddScoped<ICustomerCreditService>(sp => new CustomerCreditService(
                    sp.GetRequiredService<IClock>(), sp.GetRequiredService<ICurrentUser>())
                    { LockTimeoutMilliseconds = timeout });
            services.AddSingleton(invoices);
            if (withSource)
                services.AddScoped<IFinanceSalesInvoiceSource, FakeInvoiceSource>();
            if (withMap)
                services.AddScoped<ISalesInvoiceAccountMapSource, FakeAccountMap>();
        });

    /// <summary>GL 種子（1101／4101）＋應收 1131、銷項稅 2171（上層 2 Liabilities）。</summary>
    private static async Task SeedInvoiceAccountsAsync(ServiceProvider provider, long companyId, CancellationToken token)
    {
        await SeedAsync(provider, companyId, token);
        await using var scope = provider.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var assets = (await scope.ServiceProvider.GetRequiredService<IChartOfAccounts>().FindAsync("1", token))!;
        await accounts.CreateAsync("1131", "應收帳款", assets.Id, null, null, token);
        var liabilities = await accounts.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, token);
        await accounts.CreateAsync("2171", "銷項稅額", liabilities.Id, null, null, token);
    }

    private static FinanceSalesInvoiceSnapshot Invoice(long companyId, long invoiceId, decimal net = 100m,
        decimal tax = 5m, FinanceSalesSourceState state = FinanceSalesSourceState.Issuing) =>
        new(companyId, invoiceId, 1, "sha256:inv", InvoiceCustomerId, state, BusinessDate, "TWD", "TWD", 1m,
            net, tax, net + tax, net, tax, net + tax);

    private static SalesInvoicePostingCommand Command(long companyId, long invoiceId, string fingerprint = "sha256:inv",
        long customerId = InvoiceCustomerId, int version = 1) =>
        new(companyId, invoiceId, customerId, version, fingerprint);

    /// <summary>在新 scope 開交易、過帳，依 <paramref name="commit"/> 提交或回滾。</summary>
    private static async Task<SalesInvoicePostingResult> PostInvoiceAsync(ServiceProvider provider,
        SalesInvoicePostingCommand command, bool commit, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var result = await sp.GetRequiredService<ISalesInvoicePostingService>().PostInCurrentTransactionAsync(command, token);
        if (db.Database.CurrentTransaction is null)
            return result;
        if (commit)
            await transaction.CommitAsync(token);
        else
            await transaction.RollbackAsync(token);
        return result;
    }

    private async Task<(int Sources, int Receivables, int Journals, int Lines)> InvoiceCountsAsync(long companyId,
        CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var sources = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_sales_source WHERE company_id={companyId}").SingleAsync(token);
        var receivables = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_posted_receivable WHERE company_id={companyId}").SingleAsync(token);
        var (_, journals, lines) = await CountsAsync(companyId, token);
        return (sources, receivables, journals, lines);
    }

    private static void AssertSalesRejected(SalesInvoicePostingResult result, int status, string code)
    {
        Assert.Equal((SalesPostingOutcomeKind.Rejected, false, status, code),
            (result.Kind, result.Changed, result.HttpStatus, result.ErrorCode));
        Assert.Null(result.Receipt);
    }

    [Fact]
    public async Task InvoicePostsBalancedJournalSourceAndOpenReceivableOnlyWhenTheCallerCommits()
    {
        // FI-SALES-INV-001（FI-SALES-001）＋ INV-005（呼叫者回滾不留任何列）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[801] = Invoice(companyId, 801, 1000.1234m, 50.0066m);
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        var rolledBack = await PostInvoiceAsync(provider, Command(companyId, 801), commit: false, token);
        Assert.Equal((SalesPostingOutcomeKind.Posted, true, 200), (rolledBack.Kind, rolledBack.Changed, rolledBack.HttpStatus));
        Assert.Equal((0, 0, 0, 0), await InvoiceCountsAsync(companyId, token));
        Assert.Equal(0, await CreditKeysAsync(companyId, token));

        var posted = await PostInvoiceAsync(provider, Command(companyId, 801), commit: true, token);
        Assert.Equal((SalesPostingOutcomeKind.Posted, true, 200, (string?)null),
            (posted.Kind, posted.Changed, posted.HttpStatus, posted.ErrorCode));
        var receipt = Assert.IsType<SalesInvoicePostingReceipt>(posted.Receipt);
        Assert.Equal((companyId, 801L, InvoiceCustomerId, 1, "sha256:inv", "TWD", 1050.13m, "TWD", 1050.13m),
            (receipt.CompanyId, receipt.InvoiceId, receipt.CustomerId, receipt.SourceVersion, receipt.Fingerprint,
                receipt.Currency, receipt.GrossAmount, receipt.BaseCurrency, receipt.BaseGrossAmount));
        Assert.Equal((1, 1, 1, 3), await InvoiceCountsAsync(companyId, token));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var source = await db.Set<SalesSource>().AsNoTracking().SingleAsync(token);
        Assert.Equal((receipt.SalesSourceId, SalesSourceType.Invoice, 801L, receipt.JournalId, 1000.1234m, 50.0066m, 1050.13m, 1m),
            (source.Id, source.SourceType, source.SourceId, source.GlJournalId, source.BaseNetAmount,
                source.BaseTaxAmount, source.BaseGrossAmount, source.ExchangeRate));
        var receivable = await db.Set<PostedReceivable>().AsNoTracking().SingleAsync(token);
        Assert.Equal((receipt.ReceivableId, "Invoice", 801L, InvoiceCustomerId, 1050.13m, 1050.13m, BusinessDate),
            (receivable.Id, receivable.SourceType, receivable.SourceId, receivable.CustomerId,
                receivable.OriginalBaseAmount, receivable.OpenBaseAmount, receivable.PostingDate));
        var lines = await db.Set<GlJournalLine>().AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(token);
        Assert.Equal([("1131", 1050.13m, 0m), ("4101", 0m, 1000.1234m), ("2171", 0m, 50.0066m)],
            lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
        Assert.All(lines, x => Assert.Equal(receipt.JournalId, x.JournalId));
        var key = await db.Set<GlSourceKey>().AsNoTracking().SingleAsync(token);
        Assert.Equal(("SD", "Invoice", 801L, 1, "sha256:inv"),
            (key.SourceModule, key.SourceType, key.SourceId, key.SourceVersion, key.Fingerprint));
        Assert.Equal(1, await CreditKeysAsync(companyId, token));
    }

    [Fact]
    public async Task ZeroTaxInvoiceOmitsTheTaxLeg()
    {
        // FI-SALES-INV-002（FI-SALES-002 零稅部分）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[802] = Invoice(companyId, 802, 300m, 0m);
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        Assert.Equal(SalesPostingOutcomeKind.Posted,
            (await PostInvoiceAsync(provider, Command(companyId, 802), commit: true, token)).Kind);

        await using var scope = provider.CreateAsyncScope();
        var lines = await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<GlJournalLine>().AsNoTracking()
            .OrderBy(x => x.Sequence).Select(x => new { x.AccountCode, x.Debit, x.Credit }).ToListAsync(token);
        Assert.Equal([("1131", 300m, 0m), ("4101", 0m, 300m)], lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
    }

    [Fact]
    public async Task SameClaimReplaysUnchangedWhileAnotherFingerprintVersionOrCustomerConflicts()
    {
        // FI-SALES-INV-003（FI-SALES-009／010）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[803] = Invoice(companyId, 803);
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);
        var first = await PostInvoiceAsync(provider, Command(companyId, 803), commit: true, token);
        Assert.Equal(SalesPostingOutcomeKind.Posted, first.Kind);

        // SD 之後標記 Posted；同一主張重放不再讀 SD、不寫任何列，回傳已提交的收據。
        invoices.Items[803] = Invoice(companyId, 803, state: FinanceSalesSourceState.Posted);
        var reads = invoices.Reads;
        var replay = await PostInvoiceAsync(provider, Command(companyId, 803), commit: true, token);
        Assert.Equal(new SalesInvoicePostingResult(SalesPostingOutcomeKind.Unchanged, false, 200, Receipt: first.Receipt), replay);
        Assert.Equal(reads, invoices.Reads);

        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, 803, "sha256:other"), true, token),
            409, SalesPostingErrors.SourceConflict);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, 803, version: 2), true, token),
            409, SalesPostingErrors.SourceConflict);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, 803, customerId: InvoiceCustomerId + 1), true, token),
            409, SalesPostingErrors.SourceConflict);
        Assert.Equal((1, 1, 1, 3), await InvoiceCountsAsync(companyId, token));
    }

    [Fact]
    public async Task UnpostableMismatchedForeignAndMalformedSourcesAreRejectedWithoutRows()
    {
        // FI-SALES-INV-004（FI-SALES-006／015／016）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        var id = 810L;
        foreach (var state in new[] { FinanceSalesSourceState.Draft, FinanceSalesSourceState.Pending,
                     FinanceSalesSourceState.Rejected, FinanceSalesSourceState.Deleted })
        {
            invoices.Items[++id] = Invoice(companyId, id, state: state);
            AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, id), true, token),
                422, SalesPostingErrors.SourceNotPostable);
        }

        invoices.Items[++id] = Invoice(companyId, id);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, id, "sha256:stale"), true, token),
            409, SalesPostingErrors.SourceMismatch);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, id, version: 2), true, token),
            409, SalesPostingErrors.SourceMismatch);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, id, customerId: 999), true, token),
            409, SalesPostingErrors.SourceMismatch);

        invoices.Items[++id] = Invoice(companyId, id) with { GrossAmount = 106m };
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, id), true, token),
            422, SalesPostingErrors.InvalidAmounts);

        // 其他公司的來源或命令：404，不洩漏。
        invoices.Items[++id] = Invoice(companyId + 1, id);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, id), true, token),
            404, SalesPostingErrors.SourceNotFound);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId + 1, id), true, token),
            404, SalesPostingErrors.SourceNotFound);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, 9_999), true, token),
            404, SalesPostingErrors.SourceNotFound);
        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, id, " sha256:inv"), true, token),
            400, SalesPostingErrors.InvalidInput);
        AssertSalesRejected(await PostInvoiceAsync(provider, null!, true, token), 400, SalesPostingErrors.InvalidInput);

        Assert.Equal((0, 0, 0, 0), await InvoiceCountsAsync(companyId, token));
    }

    [Fact]
    public async Task MissingMappingAccountsContractOrPeriodAreNamedRejectionsThatKeepTheCallerTransaction()
    {
        // FI-SALES-INV-006（FI-SALES-020）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[820] = Invoice(companyId, 820);
        invoices.Items[821] = Invoice(companyId, 821) with { BusinessDate = new DateOnly(2027, 1, 5) };

        await using (var noMap = InvoiceProvider(companyId, invoices, withMap: false))
        {
            await SeedAsync(noMap, companyId, token);
            AssertSalesRejected(await PostInvoiceAsync(noMap, Command(companyId, 820), true, token),
                422, SalesPostingErrors.MappingMissing);
        }

        await using (var noSource = InvoiceProvider(companyId, invoices, withSource: false))
        {
            AssertSalesRejected(await PostInvoiceAsync(noSource, Command(companyId, 820), true, token),
                500, SalesPostingErrors.SourceContractUnavailable);
        }

        await using var provider = InvoiceProvider(companyId, invoices);
        // 尚未建立 1131：第 1 行應收科目不存在。
        var missing = await PostInvoiceAsync(provider, Command(companyId, 820), true, token);
        AssertSalesRejected(missing, 422, SalesPostingErrors.AccountInvalid);
        Assert.Equal(1, missing.InvalidLine);

        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var chart = scope.ServiceProvider.GetRequiredService<IChartOfAccounts>();
            await accounts.CreateAsync("1131", "應收帳款", (await chart.FindAsync("1", token))!.Id, null, null, token);
            var liabilities = await accounts.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, token);
            var tax = await accounts.CreateAsync("2171", "銷項稅額", liabilities.Id, null, null, token);
            await accounts.UpdateAsync(tax.Id, tax.Name, BalanceDirection.Credit, false, tax.Version, token);
        }
        var inactive = await PostInvoiceAsync(provider, Command(companyId, 820), true, token);
        AssertSalesRejected(inactive, 422, SalesPostingErrors.AccountInvalid);
        Assert.Equal(3, inactive.InvalidLine);

        // GL 拒絕（期間不存在）原樣轉出，呼叫者交易仍開啟。
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            invoices.Items[821] = Invoice(companyId, 821, 300m, 0m) with { BusinessDate = new DateOnly(2027, 1, 5) };
            var period = await sp.GetRequiredService<ISalesInvoicePostingService>()
                .PostInCurrentTransactionAsync(Command(companyId, 821), token);
            AssertSalesRejected(period, 422, GlPostingErrors.PeriodMissing);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            await transaction.CommitAsync(token);
        }

        Assert.Equal((0, 0, 0, 0), await InvoiceCountsAsync(companyId, token));
    }

    [Fact]
    public async Task InvoicePostingRequiresACallerTransactionAndAGlFaultRollsBackEverything()
    {
        // FI-SALES-INV-007（FI-SALES-012 部分：GL 失敗後連同信用鍵全部回滾）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[830] = Invoice(companyId, 830);
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        await using (var scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<ISalesInvoicePostingService>().PostInCurrentTransactionAsync(Command(companyId, 830), token));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            // 呼叫者留下未儲存的變更 → GL 寫入前失敗（ADR-107）→ 整個呼叫者交易回滾。
            db.Set<CreditKey>().Add(new CreditKey { CustomerId = InvoiceCustomerId + 1 });
            var fault = await sp.GetRequiredService<ISalesInvoicePostingService>()
                .PostInCurrentTransactionAsync(Command(companyId, 830), token);
            Assert.Equal((SalesPostingOutcomeKind.Fault, false, 500, GlPostingErrors.WriteFailed),
                (fault.Kind, fault.Changed, fault.HttpStatus, fault.ErrorCode));
            Assert.Null(db.Database.CurrentTransaction);
            db.ChangeTracker.Clear();
            await transaction.DisposeAsync();
        }

        Assert.Equal((0, 0, 0, 0), await InvoiceCountsAsync(companyId, token));
        Assert.Equal(0, await CreditKeysAsync(companyId, token));
    }

    [Fact]
    public async Task ForeignCurrencyInvoiceKeepsOriginalAmountsAndPostsBaseAmounts()
    {
        // FI-SALES-INV-008（FI-SALES-001 外幣部分）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[840] = new(companyId, 840, 3, "sha256:usd", InvoiceCustomerId, FinanceSalesSourceState.Issuing,
            BusinessDate, "USD", "TWD", 32.12345678m, 10m, 0.5m, 10.5m, 321.2346m, 16.0617m, 337.2963m);
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        var posted = await PostInvoiceAsync(provider, Command(companyId, 840, "sha256:usd", version: 3), true, token);
        Assert.Equal(SalesPostingOutcomeKind.Posted, posted.Kind);
        Assert.Equal(("USD", 10.5m, "TWD", 337.2963m),
            (posted.Receipt!.Currency, posted.Receipt.GrossAmount, posted.Receipt.BaseCurrency, posted.Receipt.BaseGrossAmount));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var source = await db.Set<SalesSource>().AsNoTracking().SingleAsync(token);
        Assert.Equal(("USD", 32.12345678m, 10m, 0.5m, 10.5m, 337.2963m),
            (source.Currency, source.ExchangeRate, source.NetAmount, source.TaxAmount, source.GrossAmount, source.BaseGrossAmount));
        Assert.Equal(337.2963m, (await db.Set<PostedReceivable>().AsNoTracking().SingleAsync(token)).OpenBaseAmount);
        var journal = await db.Set<GlJournal>().AsNoTracking().SingleAsync(token);
        Assert.Equal(("TWD", 337.2963m, 337.2963m), (journal.BaseCurrency, journal.TotalDebit, journal.TotalCredit));
    }
}
