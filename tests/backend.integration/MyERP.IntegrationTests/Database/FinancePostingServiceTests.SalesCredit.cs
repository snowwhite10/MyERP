using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

// 本檔的原始 SQL 片段全由測試常數與 Random 產生的公司 ID 組成，沒有外部輸入。
#pragma warning disable EF1002

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-SALES-CR-I01～I07（WU-FI-002a3b，ADR-113）：<see cref="ISalesCreditPostingService"/> 在呼叫者交易內以真實 SQL
/// 把 SD 已開票銷退過帳為 FI 信用來源：逐發票行累計上限、原應收餘額、信用傳票、分配與 open 扣減、重放與衝突。
/// SD 發票與銷退來源以測試假實作注入（SD-007a／008a 實作真版）；科目映射用正式的 <c>gl_sales_invoice_account_map</c>。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private sealed class FakeReturns
    {
        public readonly Dictionary<long, FinanceSalesReturnSnapshot> Items = [];
        public int Reads;
    }

    private sealed class FakeReturnSource(FakeReturns returns) : IFinanceSalesReturnSource
    {
        public Task<FinanceSalesReturnSnapshot?> ReadReturnAsync(long companyId, long returnId,
            DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(transaction.Connection);
            Interlocked.Increment(ref returns.Reads);
            return Task.FromResult(returns.Items.TryGetValue(returnId, out var item) && item.CompanyId == companyId
                ? item
                : null);
        }
    }

    private ServiceProvider CreditProvider(long companyId, FakeInvoices invoices, FakeReturns returns,
        bool withReturnSource = true) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(invoices);
            services.AddSingleton(returns);
            services.AddScoped<IFinanceSalesInvoiceSource, FakeInvoiceSource>();
            if (withReturnSource)
                services.AddScoped<IFinanceSalesReturnSource, FakeReturnSource>();
        });

    /// <summary>科目、期間與正式映射（1131／4101／2171），並以正式映射過帳指定發票（各 net／tax，TWD）。</summary>
    private static async Task SeedCreditAsync(ServiceProvider provider, long companyId, FakeInvoices invoices,
        CancellationToken token, params (long Id, decimal Net, decimal Tax)[] postedInvoices)
    {
        await SeedInvoiceAccountsAsync(provider, companyId, token);
        await SaveMapAsync(provider, "1131", "4101", "2171", token);
        foreach (var (id, net, tax) in postedInvoices)
        {
            invoices.Items[id] = Invoice(companyId, id, net, tax);
            Assert.Equal(SalesPostingOutcomeKind.Posted,
                (await PostInvoiceAsync(provider, Command(companyId, id), commit: true, token)).Kind);
        }
    }

    /// <summary>銷退行（TWD，本位幣＝原幣）；上限是 SD 保存的原發票行數量／淨額／稅額。</summary>
    private static FinanceSalesReturnLine CreditLine(long invoiceId, long lineId, decimal quantity, decimal net,
        decimal tax, decimal capQuantity, decimal capNet, decimal capTax) =>
        new(invoiceId, lineId, quantity, net, tax, net + tax, net, tax, net + tax,
            new FinanceSalesInvoiceLineCap(capQuantity, capNet, capTax, capNet, capTax));

    private static FinanceSalesReturnSnapshot Return(long companyId, long returnId, string fingerprint,
        params FinanceSalesReturnLine[] lines)
    {
        var net = lines.Sum(x => x.NetAmount);
        var tax = lines.Sum(x => x.TaxAmount);
        return new(companyId, returnId, 1, fingerprint, InvoiceCustomerId, FinanceSalesSourceState.Issuing,
            BusinessDate, "TWD", "TWD", 1m, net, tax, net + tax, net, tax, net + tax, lines);
    }

    private static SalesCreditPostingCommand CreditCommand(long companyId, long returnId, string fingerprint,
        long customerId = InvoiceCustomerId, int version = 1) =>
        new(companyId, returnId, customerId, version, fingerprint);

    private static async Task<SalesCreditPostingResult> PostCreditAsync(ServiceProvider provider,
        SalesCreditPostingCommand? command, bool commit, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var result = await sp.GetRequiredService<ISalesCreditPostingService>()
            .PostInCurrentTransactionAsync(command, token);
        if (db.Database.CurrentTransaction is null)
            return result;
        if (commit)
            await transaction.CommitAsync(token);
        else
            await transaction.RollbackAsync(token);
        return result;
    }

    /// <summary>(銷退來源數, 分配數, 傳票數, 各應收 open 依發票 ID 升冪)。</summary>
    private async Task<(int Returns, int Allocations, int Journals, string Open)> CreditStateAsync(long companyId,
        CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var returns = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_sales_source WHERE company_id={companyId} AND source_type='Return'").SingleAsync(token);
        var allocations = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_credit_allocation WHERE company_id={companyId}").SingleAsync(token);
        var journals = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal WHERE company_id={companyId}").SingleAsync(token);
        var open = await db.Database.SqlQuery<decimal>($"SELECT open_base_amount AS [Value] FROM dbo.ar_posted_receivable WHERE company_id={companyId} ORDER BY source_id").ToListAsync(token);
        return (returns, allocations, journals,
            string.Join("|", open.Select(x => x.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static void AssertCreditRejected(SalesCreditPostingResult result, int status, string code, int? line = null)
    {
        Assert.Equal((SalesPostingOutcomeKind.Rejected, false, status, code, line),
            (result.Kind, result.Changed, result.HttpStatus, result.ErrorCode, result.InvalidLine));
        Assert.Null(result.Receipt);
    }

    [Fact]
    public async Task ReturnAcrossTwoInvoicesPostsOneCreditJournalAllocationsAndReducesOnlyCreditedOpenAr()
    {
        // FI-SALES-CR-I01（FI-SALES-004／005）：一張銷退、兩張發票、三行；呼叫者回滾不留任何列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (901, 1000m, 50m), (902, 2000m, 100m));
        returns.Items[951] = Return(companyId, 951, "sha256:r951",
            CreditLine(901, 9011, 2m, 200m, 10m, 10m, 600m, 30m),
            CreditLine(902, 9021, 1.5m, 150m, 7.5m, 4m, 400m, 20m),
            CreditLine(901, 9012, 0.000001m, 0.0001m, 0m, 1m, 400m, 20m));

        var rolledBack = await PostCreditAsync(provider, CreditCommand(companyId, 951, "sha256:r951"), false, token);
        Assert.Equal((SalesPostingOutcomeKind.Posted, true, 200), (rolledBack.Kind, rolledBack.Changed, rolledBack.HttpStatus));
        Assert.Equal((0, 0, 2, "1050|2100"), await CreditStateAsync(companyId, token));

        var posted = await PostCreditAsync(provider, CreditCommand(companyId, 951, "sha256:r951"), true, token);
        Assert.Equal((SalesPostingOutcomeKind.Posted, true, 200, (string?)null, (int?)null),
            (posted.Kind, posted.Changed, posted.HttpStatus, posted.ErrorCode, posted.InvalidLine));
        var receipt = Assert.IsType<SalesCreditPostingReceipt>(posted.Receipt);
        Assert.Equal((companyId, 951L, InvoiceCustomerId, 1, "sha256:r951", "TWD", 367.5001m, "TWD", 367.5001m),
            (receipt.CompanyId, receipt.ReturnId, receipt.CustomerId, receipt.SourceVersion, receipt.Fingerprint,
                receipt.Currency, receipt.GrossAmount, receipt.BaseCurrency, receipt.BaseGrossAmount));
        Assert.Equal([(901L, 9011L, 2m, 210m), (902L, 9021L, 1.5m, 157.5m), (901L, 9012L, 0.000001m, 0.0001m)],
            receipt.Allocations.Select(x => (x.InvoiceId, x.InvoiceLineId, x.Quantity, x.BaseGrossAmount)).ToArray());
        Assert.Equal(receipt.Allocations[0].ReceivableId, receipt.Allocations[2].ReceivableId);
        Assert.NotEqual(receipt.Allocations[0].ReceivableId, receipt.Allocations[1].ReceivableId);
        Assert.Equal((1, 3, 3, "839.9999|1942.5"), await CreditStateAsync(companyId, token));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var source = await db.Set<SalesSource>().AsNoTracking()
            .SingleAsync(x => x.SourceType == SalesSourceType.Return, token);
        Assert.Equal((receipt.SalesSourceId, 951L, receipt.JournalId, 350.0001m, 17.5m, 367.5001m),
            (source.Id, source.SourceId, source.GlJournalId, source.BaseNetAmount, source.BaseTaxAmount,
                source.BaseGrossAmount));
        var lines = await db.Set<GlJournalLine>().AsNoTracking().Where(x => x.JournalId == receipt.JournalId)
            .OrderBy(x => x.Sequence).ToListAsync(token);
        Assert.Equal([("4101", 350.0001m, 0m), ("2171", 17.5m, 0m), ("1131", 0m, 367.5001m)],
            lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
        var key = await db.Set<GlSourceKey>().AsNoTracking().SingleAsync(x => x.SourceType == "Return", token);
        Assert.Equal(("SD", 951L, 1, "sha256:r951"), (key.SourceModule, key.SourceId, key.SourceVersion, key.Fingerprint));
        var map = await db.Set<SalesInvoiceAccountMapping>().AsNoTracking().SingleAsync(token);
        Assert.All(lines, x => Assert.Equal(map.Version, x.MappingVersion));
        var allocations = await db.Set<CreditAllocation>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(token);
        Assert.All(allocations, x => Assert.Equal(receipt.SalesSourceId, x.CreditSourceId));
        Assert.Equal([(9011L, 200m, 10m, 210m), (9021L, 150m, 7.5m, 157.5m), (9012L, 0.0001m, 0m, 0.0001m)],
            allocations.Select(x => (x.InvoiceLineId, x.NetAmount, x.TaxAmount, x.GrossAmount)).ToArray());
    }

    [Fact]
    public async Task SecondReturnUsesTheExactSavedRemainderAndAnyExcessIsRejectedWithItsLine()
    {
        // FI-SALES-CR-I02（FI-SALES-007）：先前已提交分配在信用鍵下累計；剛好餘量通過，多 0.000001 或 0.0001 被拒。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (911, 1000m, 50m));
        returns.Items[961] = Return(companyId, 961, "sha256:a", CreditLine(911, 9111, 8m, 800m, 40m, 10m, 1000m, 50m));
        Assert.Equal(SalesPostingOutcomeKind.Posted,
            (await PostCreditAsync(provider, CreditCommand(companyId, 961, "sha256:a"), true, token)).Kind);

        returns.Items[962] = Return(companyId, 962, "sha256:b", CreditLine(911, 9111, 2.000001m, 200m, 10m, 10m, 1000m, 50m));
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 962, "sha256:b"), true, token),
            422, SalesCreditPostingErrors.CreditExceedsLine, 1);
        returns.Items[962] = Return(companyId, 962, "sha256:b", CreditLine(911, 9111, 2m, 200.0001m, 10m, 10m, 1000m, 50m));
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 962, "sha256:b"), true, token),
            422, SalesCreditPostingErrors.CreditExceedsLine, 1);
        Assert.Equal((1, 1, 2, "210"), await CreditStateAsync(companyId, token));

        // SD 線上限被誇大時，FI 保存的發票總額仍是最後防線。
        returns.Items[962] = Return(companyId, 962, "sha256:b", CreditLine(911, 9111, 2m, 200.0001m, 10m, 10m, 2000m, 50m));
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 962, "sha256:b"), true, token),
            422, SalesCreditPostingErrors.CreditExceedsInvoice, 1);

        returns.Items[962] = Return(companyId, 962, "sha256:b", CreditLine(911, 9111, 2m, 200m, 10m, 10m, 1000m, 50m));
        Assert.Equal(SalesPostingOutcomeKind.Posted,
            (await PostCreditAsync(provider, CreditCommand(companyId, 962, "sha256:b"), true, token)).Kind);
        Assert.Equal((2, 2, 3, "0"), await CreditStateAsync(companyId, token));

        // 發票淨額已全數沖銷：另一行即使在其 SD 行上限內，也超過 FI 保存的發票總額。
        returns.Items[963] = Return(companyId, 963, "sha256:c", CreditLine(911, 9112, 1m, 0.0001m, 0m, 1m, 1m, 0m));
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 963, "sha256:c"), true, token),
            422, SalesCreditPostingErrors.CreditExceedsInvoice, 1);
        Assert.Equal((2, 2, 3, "0"), await CreditStateAsync(companyId, token));
    }

    [Fact]
    public async Task SameClaimReplaysUnchangedReceiptWhileAnotherClaimConflicts()
    {
        // FI-SALES-CR-I03（FI-SALES-009／010）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (921, 1000m, 50m), (922, 500m, 25m));
        returns.Items[971] = Return(companyId, 971, "sha256:r",
            CreditLine(922, 9221, 1m, 100m, 5m, 5m, 500m, 25m), CreditLine(921, 9211, 1m, 100m, 5m, 10m, 1000m, 50m));
        var first = await PostCreditAsync(provider, CreditCommand(companyId, 971, "sha256:r"), true, token);
        Assert.Equal(SalesPostingOutcomeKind.Posted, first.Kind);

        returns.Items[971] = returns.Items[971] with { State = FinanceSalesSourceState.Posted };
        var reads = returns.Reads;
        var replay = await PostCreditAsync(provider, CreditCommand(companyId, 971, "sha256:r"), true, token);
        Assert.Equal((SalesPostingOutcomeKind.Unchanged, false, 200), (replay.Kind, replay.Changed, replay.HttpStatus));
        Assert.Equal(first.Receipt! with { Allocations = [] }, replay.Receipt! with { Allocations = [] });
        Assert.Equal(first.Receipt!.Allocations, replay.Receipt!.Allocations);
        Assert.Equal(reads, returns.Reads);

        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 971, "sha256:x"), true, token),
            409, SalesPostingErrors.SourceConflict);
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 971, "sha256:r", version: 2), true, token),
            409, SalesPostingErrors.SourceConflict);
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 971, "sha256:r", InvoiceCustomerId + 1), true, token),
            409, SalesPostingErrors.SourceConflict);
        Assert.Equal((1, 2, 3, "945|420"), await CreditStateAsync(companyId, token));
    }

    [Fact]
    public async Task UnpostedForeignCustomerOrCurrencyOriginalsAreRejectedWithTheLineAndNoRows()
    {
        // FI-SALES-CR-I04（FI-SALES-018）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (931, 1000m, 50m));
        // 另一客戶的已過帳發票。
        invoices.Items[932] = Invoice(companyId, 932) with { CustomerId = InvoiceCustomerId + 5 };
        Assert.Equal(SalesPostingOutcomeKind.Posted, (await PostInvoiceAsync(provider,
            Command(companyId, 932, customerId: InvoiceCustomerId + 5), true, token)).Kind);

        var good = CreditLine(931, 9311, 1m, 100m, 5m, 10m, 1000m, 50m);
        returns.Items[981] = Return(companyId, 981, "sha256:r", good, CreditLine(939, 9391, 1m, 10m, 0m, 1m, 10m, 0m));
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 981, "sha256:r"), true, token),
            422, SalesCreditPostingErrors.OriginalNotPosted, 2);
        returns.Items[981] = Return(companyId, 981, "sha256:r", good, CreditLine(932, 9321, 1m, 10m, 0m, 1m, 100m, 5m));
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 981, "sha256:r"), true, token),
            422, SalesCreditPostingErrors.OriginalMismatch, 2);
        // 原發票 TWD、銷退 USD → 幣別不符（本位幣＝TWD）。
        returns.Items[981] = Return(companyId, 981, "sha256:r", good) with { Currency = "USD", ExchangeRate = 1m };
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 981, "sha256:r"), true, token),
            422, SalesCreditPostingErrors.OriginalMismatch, 1);

        Assert.Equal((0, 0, 2, "1050|105"), await CreditStateAsync(companyId, token));
    }

    [Fact]
    public async Task CreditOverTheCurrentOpenAmountIsRejectedWhileTheExactOpenRemainderPasses()
    {
        // FI-SALES-CR-I05（FI-SALES-019）：已收款（模擬：open 只剩 100）後只可沖 open 內。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (941, 1000m, 50m));
        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlAsync(
                $"UPDATE dbo.ar_posted_receivable SET open_base_amount = 100 WHERE company_id={companyId}", token);

        returns.Items[991] = Return(companyId, 991, "sha256:o",
            CreditLine(941, 9411, 0.5m, 50m, 2.5m, 5m, 500m, 25m), CreditLine(941, 9412, 0.5m, 45.2381m, 2.2620m, 5m, 500m, 25m));
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 991, "sha256:o"), true, token),
            422, SalesCreditPostingErrors.CreditExceedsOpen, 2);
        Assert.Equal((0, 0, 1, "100"), await CreditStateAsync(companyId, token));

        returns.Items[991] = Return(companyId, 991, "sha256:o",
            CreditLine(941, 9411, 0.5m, 50m, 2.5m, 5m, 500m, 25m), CreditLine(941, 9412, 0.5m, 45.2381m, 2.2619m, 5m, 500m, 25m));
        Assert.Equal(SalesPostingOutcomeKind.Posted,
            (await PostCreditAsync(provider, CreditCommand(companyId, 991, "sha256:o"), true, token)).Kind);
        Assert.Equal((1, 2, 2, "0"), await CreditStateAsync(companyId, token));
    }

    [Fact]
    public async Task SnapshotStateContractAndTransactionFailuresAreNamedAndWriteNothing()
    {
        // FI-SALES-CR-I06（FI-SALES-006／015／016／018）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (951, 1000m, 50m));
        var line = CreditLine(951, 9511, 1m, 100m, 5m, 10m, 1000m, 50m);

        AssertCreditRejected(await PostCreditAsync(provider, null, true, token), 400, SalesPostingErrors.InvalidInput);
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 1001, " sha256:r"), true, token),
            400, SalesPostingErrors.InvalidInput);
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 1001, "sha256:r"), true, token),
            404, SalesPostingErrors.SourceNotFound);
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId + 1, 1001, "sha256:r"), true, token),
            404, SalesPostingErrors.SourceNotFound);
        returns.Items[1001] = Return(companyId, 1001, "sha256:r", line) with { State = FinanceSalesSourceState.Pending };
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 1001, "sha256:r"), true, token),
            422, SalesPostingErrors.SourceNotPostable);
        returns.Items[1001] = Return(companyId, 1001, "sha256:r", line);
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 1001, "sha256:stale"), true, token),
            409, SalesPostingErrors.SourceMismatch);
        returns.Items[1001] = Return(companyId, 1001, "sha256:r", line, line);
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 1001, "sha256:r"), true, token),
            422, SalesCreditPostingErrors.DuplicateLine, 2);

        await using (var noSource = CreditProvider(companyId, invoices, returns, withReturnSource: false))
            AssertCreditRejected(await PostCreditAsync(noSource, CreditCommand(companyId, 1001, "sha256:r"), true, token),
                500, SalesPostingErrors.SourceContractUnavailable);

        await using (var scope = provider.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<ISalesCreditPostingService>()
                .PostInCurrentTransactionAsync(CreditCommand(companyId, 1001, "sha256:r"), token));

        Assert.Equal((0, 0, 1, "1050"), await CreditStateAsync(companyId, token));
    }

    [Fact]
    public async Task ReturnUsesTheInvoiceMapRowVersionAndMissingOrInvalidAccountsAreRejected()
    {
        // FI-SALES-CR-I07（ADR-113）：GL SD/Return 映射版本＝發票映射列版本；改映射後新版本即時生效；
        // 映射指向停用科目 → 422 ACCOUNT_INVALID（含分錄行）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (961, 1000m, 50m));

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var gl = GlPostingMappingVersionRules.Select(sp.GetServices<IGlPostingMappingVersionSource>(),
                new GlPostingSource(companyId, "SD", "Return", 1, 1, "fp"));
            Assert.NotNull(gl);
            var map = await sp.GetRequiredService<MyErpDbContext>().Set<SalesInvoiceAccountMapping>().AsNoTracking()
                .SingleAsync(token);
            Assert.Equal(map.Version, await gl!.ReadCurrentVersionAsync(companyId, token));
            Assert.Equal(0, await gl.ReadCurrentVersionAsync(companyId + 1, token));
        }

        returns.Items[1011] = Return(companyId, 1011, "sha256:m", CreditLine(961, 9611, 1m, 100m, 5m, 10m, 1000m, 50m));
        await SaveMapAsync(provider, "1131", "9999", "2171", token);
        AssertCreditRejected(await PostCreditAsync(provider, CreditCommand(companyId, 1011, "sha256:m"), true, token),
            422, SalesPostingErrors.AccountInvalid, 1);

        var saved = await SaveMapAsync(provider, "1131", "4101", "2171", token);
        var posted = await PostCreditAsync(provider, CreditCommand(companyId, 1011, "sha256:m"), true, token);
        Assert.Equal(SalesPostingOutcomeKind.Posted, posted.Kind);
        await using (var scope = provider.CreateAsyncScope())
        {
            var lines = await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<GlJournalLine>()
                .AsNoTracking().Where(x => x.JournalId == posted.Receipt!.JournalId).ToListAsync(token);
            Assert.All(lines, x => Assert.Equal(saved.Version, x.MappingVersion));
        }
    }
}
