using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-PUR-POST-001～008（WU-FI-003a2b，ADR-117）：<see cref="IPurchaseInvoicePostingService"/> 在呼叫者交易內以真實
/// SQL 把 PUR 採購發票過帳為 FI 應付＋GL 傳票。PUR 來源以測試假實作注入（PUR-008a 實作真版）；科目映射用正式的
/// <c>gl_purchase_invoice_account_map</c>（WU-FI-003a2a），所以 GL 映射版本與過帳映射讀同一列。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private const long PurchaseSupplierId = 701;

    /// <summary>測試用 PUR 發票來源：記錄讀取次數；讀取時（呼叫者交易內）可執行掛鉤檢查鎖或保持交易。</summary>
    private sealed class FakePurchaseInvoices
    {
        public readonly Dictionary<long, FinancePurchaseInvoiceSnapshot> Items = [];
        public int Reads;
        public Func<long, DbTransaction, CancellationToken, Task>? OnRead;
    }

    private sealed class FakePurchaseInvoiceSource(FakePurchaseInvoices invoices) : IFinancePurchaseInvoiceSource
    {
        public async Task<FinancePurchaseInvoiceSnapshot?> ReadInvoiceAsync(long companyId, long invoiceId,
            DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(transaction.Connection);
            Interlocked.Increment(ref invoices.Reads);
            if (invoices.OnRead is { } hook)
                await hook(invoiceId, transaction, cancellationToken);
            return invoices.Items.TryGetValue(invoiceId, out var item) && item.CompanyId == companyId ? item : null;
        }
    }

    private ServiceProvider PurchaseProvider(long companyId, FakePurchaseInvoices invoices, bool withSource = true) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(invoices);
            if (withSource)
                services.AddScoped<IFinancePurchaseInvoiceSource, FakePurchaseInvoiceSource>();
        });

    /// <summary>GL 種子（1101／4101、TWD、2026-11 期間）＋進項稅 1151、應付 2141（2 Liabilities）、進貨 5101（5 Expense）。</summary>
    private static async Task SeedPurchaseAccountsAsync(ServiceProvider provider, long companyId, CancellationToken token,
        bool withMap = true)
    {
        await SeedAsync(provider, companyId, token);
        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var assets = (await scope.ServiceProvider.GetRequiredService<IChartOfAccounts>().FindAsync("1", token))!;
            await accounts.CreateAsync("1151", "進項稅額", assets.Id, null, null, token);
            var liabilities = await accounts.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, token);
            await accounts.CreateAsync("2141", "應付帳款", liabilities.Id, null, null, token);
            var expense = await accounts.CreateAsync("5", "Expenses", null, AccountType.Expense, null, token);
            await accounts.CreateAsync("5101", "進貨", expense.Id, null, null, token);
        }
        if (withMap)
            await SavePurchaseMapAsync(provider, "2141", "5101", "1151", token);
    }

    private static FinancePurchaseInvoiceSnapshot PurchaseInvoice(long companyId, long invoiceId, decimal net = 100m,
        decimal tax = 5m, FinancePurchaseSourceState state = FinancePurchaseSourceState.Matched) =>
        new(companyId, invoiceId, 1, "sha256:pur", PurchaseSupplierId, state, BusinessDate, "TWD", "TWD", 1m,
            net, tax, net + tax, net, tax, net + tax, [new(1, net, tax, net + tax, net, tax, net + tax)]);

    private static PurchaseInvoicePostingCommand PurchaseCommand(long companyId, long invoiceId,
        string fingerprint = "sha256:pur", long supplierId = PurchaseSupplierId, int version = 1) =>
        new(companyId, invoiceId, supplierId, version, fingerprint);

    /// <summary>在新 scope 開交易、過帳，依 <paramref name="commit"/> 提交或回滾。</summary>
    private static async Task<PurchaseInvoicePostingResult> PostPurchaseAsync(ServiceProvider provider,
        PurchaseInvoicePostingCommand command, bool commit, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var result = await sp.GetRequiredService<IPurchaseInvoicePostingService>()
            .PostInCurrentTransactionAsync(command, token);
        if (db.Database.CurrentTransaction is null)
            return result;
        if (commit)
            await transaction.CommitAsync(token);
        else
            await transaction.RollbackAsync(token);
        return result;
    }

    private async Task<(int Sources, int Payables, int Journals, int Lines)> PurchaseCountsAsync(long companyId,
        CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var sources = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ap_purchase_source WHERE company_id={companyId}").SingleAsync(token);
        var payables = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ap_posted_payable WHERE company_id={companyId}").SingleAsync(token);
        var (_, journals, lines) = await CountsAsync(companyId, token);
        return (sources, payables, journals, lines);
    }

    private static void AssertPurchaseRejected(PurchaseInvoicePostingResult result, int status, string code)
    {
        Assert.Equal((PurchasePostingOutcomeKind.Rejected, false, status, code),
            (result.Kind, result.Changed, result.HttpStatus, result.ErrorCode));
        Assert.Null(result.Receipt);
    }

    [Fact]
    public async Task PurchaseInvoicePostsBalancedJournalSourceAndOpenPayableOnlyWhenTheCallerCommits()
    {
        // FI-PUR-POST-001（FI-PUR-001、BR-FI-018／019）＋呼叫者回滾不留任何列
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[901] = PurchaseInvoice(companyId, 901, 1000.1234m, 50.0066m);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);

        var rolledBack = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 901), commit: false, token);
        Assert.Equal((PurchasePostingOutcomeKind.Posted, true, 200),
            (rolledBack.Kind, rolledBack.Changed, rolledBack.HttpStatus));
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));

        var posted = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 901), commit: true, token);
        Assert.Equal((PurchasePostingOutcomeKind.Posted, true, 200, (string?)null),
            (posted.Kind, posted.Changed, posted.HttpStatus, posted.ErrorCode));
        var receipt = Assert.IsType<PurchaseInvoicePostingReceipt>(posted.Receipt);
        Assert.Equal((companyId, 901L, PurchaseSupplierId, 1, "sha256:pur", "TWD", 1050.13m, "TWD", 1050.13m),
            (receipt.CompanyId, receipt.InvoiceId, receipt.SupplierId, receipt.SourceVersion, receipt.Fingerprint,
                receipt.Currency, receipt.GrossAmount, receipt.BaseCurrency, receipt.BaseGrossAmount));
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var map = await db.Set<PurchaseInvoiceAccountMapping>().AsNoTracking().SingleAsync(token);
        var source = await db.Set<PurchaseSource>().AsNoTracking().SingleAsync(token);
        Assert.Equal((receipt.PurchaseSourceId, PurchaseSourceType.PurchaseInvoice, 901L, PurchaseSupplierId,
                receipt.JournalId, 1000.1234m, 50.0066m, 1050.13m, 1m, BusinessDate),
            (source.Id, source.SourceType, source.SourceId, source.SupplierId, source.GlJournalId, source.BaseNetAmount,
                source.BaseTaxAmount, source.BaseGrossAmount, source.ExchangeRate, source.BusinessDate));
        var payable = await db.Set<PostedPayable>().AsNoTracking().SingleAsync(token);
        Assert.Equal((receipt.PayableId, source.Id, PurchaseSupplierId, "TWD", 1050.13m, 1050.13m, 1050.13m, 1050.13m,
                BusinessDate),
            (payable.Id, payable.PurchaseSourceId, payable.SupplierId, payable.Currency, payable.OriginalAmount,
                payable.OpenAmount, payable.OriginalBaseAmount, payable.OpenBaseAmount, payable.PostingDate));
        var lines = await db.Set<GlJournalLine>().AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(token);
        Assert.Equal([("5101", 1000.1234m, 0m), ("1151", 50.0066m, 0m), ("2141", 0m, 1050.13m)],
            lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
        Assert.All(lines, x => Assert.Equal((receipt.JournalId, map.Version), (x.JournalId, x.MappingVersion)));
        var key = await db.Set<GlSourceKey>().AsNoTracking().SingleAsync(token);
        Assert.Equal(("PUR", "PurchaseInvoice", 901L, 1, "sha256:pur"),
            (key.SourceModule, key.SourceType, key.SourceId, key.SourceVersion, key.Fingerprint));
    }

    [Fact]
    public async Task ZeroTaxAndForeignCurrencyInvoicesKeepSavedComponents()
    {
        // FI-PUR-POST-002（FI-PUR-002 零稅不產生零額分錄；FI-PUR-005 外幣保存原幣與已存匯率，傳票用本位幣）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[902] = PurchaseInvoice(companyId, 902, 300m, 0m);
        invoices.Items[903] = new(companyId, 903, 3, "sha256:usd", PurchaseSupplierId, FinancePurchaseSourceState.Matched,
            BusinessDate, "USD", "TWD", 32.12345678m, 10m, 0.5m, 10.5m, 321.2346m, 16.0617m, 337.2963m,
            [new(1, 6m, 0.5m, 6.5m, 192.7407m, 16.0617m, 208.8024m), new(2, 4m, 0m, 4m, 128.4939m, 0m, 128.4939m)]);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);

        var zero = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 902), commit: true, token);
        Assert.Equal(PurchasePostingOutcomeKind.Posted, zero.Kind);
        var usd = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 903, "sha256:usd", version: 3), true, token);
        Assert.Equal(PurchasePostingOutcomeKind.Posted, usd.Kind);
        Assert.Equal(("USD", 10.5m, "TWD", 337.2963m),
            (usd.Receipt!.Currency, usd.Receipt.GrossAmount, usd.Receipt.BaseCurrency, usd.Receipt.BaseGrossAmount));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var zeroLines = await db.Set<GlJournalLine>().AsNoTracking().Where(x => x.JournalId == zero.Receipt!.JournalId)
            .OrderBy(x => x.Sequence).Select(x => new { x.AccountCode, x.Debit, x.Credit }).ToListAsync(token);
        Assert.Equal([("5101", 300m, 0m), ("2141", 0m, 300m)],
            zeroLines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());

        var source = await db.Set<PurchaseSource>().AsNoTracking().SingleAsync(x => x.SourceId == 903, token);
        Assert.Equal(("USD", 32.12345678m, 10m, 0.5m, 10.5m, 321.2346m, 16.0617m, 337.2963m, 3, "sha256:usd"),
            (source.Currency, source.ExchangeRate, source.NetAmount, source.TaxAmount, source.GrossAmount,
                source.BaseNetAmount, source.BaseTaxAmount, source.BaseGrossAmount, source.SourceVersion, source.Fingerprint));
        var payable = await db.Set<PostedPayable>().AsNoTracking().SingleAsync(x => x.Id == usd.Receipt.PayableId, token);
        Assert.Equal(("USD", 10.5m, 10.5m, "TWD", 337.2963m, 337.2963m),
            (payable.Currency, payable.OriginalAmount, payable.OpenAmount, payable.BaseCurrency,
                payable.OriginalBaseAmount, payable.OpenBaseAmount));
        var usdLines = await db.Set<GlJournalLine>().AsNoTracking().Where(x => x.JournalId == usd.Receipt.JournalId)
            .OrderBy(x => x.Sequence).Select(x => new { x.AccountCode, x.Debit, x.Credit }).ToListAsync(token);
        Assert.Equal([("5101", 321.2346m, 0m), ("1151", 16.0617m, 0m), ("2141", 0m, 337.2963m)],
            usdLines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
        var journal = await db.Set<GlJournal>().AsNoTracking().SingleAsync(x => x.Id == usd.Receipt.JournalId, token);
        Assert.Equal(("TWD", 337.2963m, 337.2963m), (journal.BaseCurrency, journal.TotalDebit, journal.TotalCredit));
    }

    [Fact]
    public async Task SamePurchaseClaimReplaysUnchangedWhileAnotherFingerprintVersionOrSupplierConflicts()
    {
        // FI-PUR-POST-003（FI-PUR-007／008／016：同指紋重放 changed=false、零新增列、不讀 PUR；其他主張 409）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[904] = PurchaseInvoice(companyId, 904);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var first = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 904), commit: true, token);
        Assert.Equal(PurchasePostingOutcomeKind.Posted, first.Kind);

        // PUR 之後標記 Posted；同一主張重放不再讀 PUR、不寫任何列，回傳已提交的收據。
        invoices.Items[904] = PurchaseInvoice(companyId, 904, state: FinancePurchaseSourceState.Posted);
        var reads = invoices.Reads;
        var replay = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 904), commit: true, token);
        Assert.Equal(new PurchaseInvoicePostingResult(PurchasePostingOutcomeKind.Unchanged, false, 200,
            Receipt: first.Receipt), replay);
        Assert.Equal(reads, invoices.Reads);

        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, 904, "sha256:other"), true, token),
            409, PurchasePostingErrors.SourceConflict);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, 904, version: 2), true, token),
            409, PurchasePostingErrors.SourceConflict);
        AssertPurchaseRejected(await PostPurchaseAsync(provider,
            PurchaseCommand(companyId, 904, supplierId: PurchaseSupplierId + 1), true, token),
            409, PurchasePostingErrors.SourceConflict);
        Assert.Equal(reads, invoices.Reads);
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task UnpostableMismatchedForeignAndMalformedPurchaseSourcesAreRejectedWithoutRows()
    {
        // FI-PUR-POST-004（FI-PUR-004／012／014／015：狀態 422、主張不符 409、金額 422 含 PUR 行號、跨公司 404、格式 400）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);

        var id = 910L;
        foreach (var state in new[] { FinancePurchaseSourceState.Draft, FinancePurchaseSourceState.Disputed,
                     FinancePurchaseSourceState.Rejected, FinancePurchaseSourceState.Deleted,
                     FinancePurchaseSourceState.Posted })
        {
            invoices.Items[++id] = PurchaseInvoice(companyId, id, state: state);
            AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, id), true, token),
                422, PurchasePostingErrors.SourceNotPostable);
        }

        invoices.Items[++id] = PurchaseInvoice(companyId, id);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, id, "sha256:stale"), true, token),
            409, PurchasePostingErrors.SourceMismatch);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, id, version: 2), true, token),
            409, PurchasePostingErrors.SourceMismatch);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, id, supplierId: 999), true, token),
            409, PurchasePostingErrors.SourceMismatch);

        invoices.Items[++id] = PurchaseInvoice(companyId, id) with { GrossAmount = 106m };
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, id), true, token),
            422, PurchasePostingErrors.InvalidAmounts);
        invoices.Items[++id] = PurchaseInvoice(companyId, id, 100m, 5m) with
        {
            Lines = [new(1, 60m, 3m, 63m, 60m, 3m, 63m), new(2, 40m, 2m, 42.0001m, 40m, 2m, 42.0001m)]
        };
        var line = await PostPurchaseAsync(provider, PurchaseCommand(companyId, id), true, token);
        AssertPurchaseRejected(line, 422, PurchasePostingErrors.InvalidAmounts);
        Assert.Equal(((int?)null, (int?)2), (line.InvalidLine, line.InvalidSourceLine));

        // 其他公司的來源或命令：404，不洩漏。
        invoices.Items[++id] = PurchaseInvoice(companyId + 1, id);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, id), true, token),
            404, PurchasePostingErrors.SourceNotFound);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId + 1, id), true, token),
            404, PurchasePostingErrors.SourceNotFound);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, 9_999), true, token),
            404, PurchasePostingErrors.SourceNotFound);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, id, " sha256:pur"), true, token),
            400, PurchasePostingErrors.InvalidInput);
        AssertPurchaseRejected(await PostPurchaseAsync(provider, null!, true, token), 400, PurchasePostingErrors.InvalidInput);

        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task MissingPurchaseMappingAccountsContractOrPeriodAreNamedRejectionsThatKeepTheCallerTransaction()
    {
        // FI-PUR-POST-005（FI-PUR-006／019：缺映射 422、PUR 契約不可用 500、MD 科目不存在／停用 422 含傳票行號、GL 期間拒絕原樣轉出）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[920] = PurchaseInvoice(companyId, 920);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedAsync(provider, companyId, token);

        AssertPurchaseRejected(await PostPurchaseAsync(provider, PurchaseCommand(companyId, 920), true, token),
            422, PurchasePostingErrors.MappingMissing);

        await using (var noSource = PurchaseProvider(companyId, invoices, withSource: false))
        {
            AssertPurchaseRejected(await PostPurchaseAsync(noSource, PurchaseCommand(companyId, 920), true, token),
                500, PurchasePostingErrors.SourceContractUnavailable);
        }

        // 映射已存但科目尚未建立：第 1 行（進貨）不存在。
        await SavePurchaseMapAsync(provider, "2141", "5101", "1151", token);
        var missing = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 920), true, token);
        AssertPurchaseRejected(missing, 422, PurchasePostingErrors.AccountInvalid);
        Assert.Equal(1, missing.InvalidLine);

        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var chart = scope.ServiceProvider.GetRequiredService<IChartOfAccounts>();
            var tax = await accounts.CreateAsync("1151", "進項稅額", (await chart.FindAsync("1", token))!.Id, null, null, token);
            var liabilities = await accounts.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, token);
            await accounts.CreateAsync("2141", "應付帳款", liabilities.Id, null, null, token);
            var expense = await accounts.CreateAsync("5", "Expenses", null, AccountType.Expense, null, token);
            await accounts.CreateAsync("5101", "進貨", expense.Id, null, null, token);
            await accounts.UpdateAsync(tax.Id, tax.Name, BalanceDirection.Debit, false, tax.Version, token);
        }
        var inactive = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 920), true, token);
        AssertPurchaseRejected(inactive, 422, PurchasePostingErrors.AccountInvalid);
        Assert.Equal(2, inactive.InvalidLine);

        // GL 拒絕（期間不存在）原樣轉出，呼叫者交易仍開啟。零稅避開停用的進項稅科目。
        invoices.Items[921] = PurchaseInvoice(companyId, 921, 300m, 0m) with { BusinessDate = new DateOnly(2027, 1, 5) };
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var period = await sp.GetRequiredService<IPurchaseInvoicePostingService>()
                .PostInCurrentTransactionAsync(PurchaseCommand(companyId, 921), token);
            AssertPurchaseRejected(period, 422, GlPostingErrors.PeriodMissing);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            await transaction.CommitAsync(token);
        }

        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task PurchasePostingRequiresACallerTransactionAndAGlFaultRollsBackEverything()
    {
        // FI-PUR-POST-006（FI-PUR-011／019 部分：無交易是程式錯誤；GL 失敗 → 整個呼叫者交易回滾，之後可重過帳）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[930] = PurchaseInvoice(companyId, 930);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);

        await using (var scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<IPurchaseInvoicePostingService>()
                .PostInCurrentTransactionAsync(PurchaseCommand(companyId, 930), token));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            // 呼叫者留下未儲存的變更 → GL 寫入前失敗（ADR-107）→ 整個呼叫者交易回滾。
            db.Set<CreditKey>().Add(new CreditKey { CustomerId = 1 });
            var fault = await sp.GetRequiredService<IPurchaseInvoicePostingService>()
                .PostInCurrentTransactionAsync(PurchaseCommand(companyId, 930), token);
            Assert.Equal((PurchasePostingOutcomeKind.Fault, false, 500, GlPostingErrors.WriteFailed),
                (fault.Kind, fault.Changed, fault.HttpStatus, fault.ErrorCode));
            Assert.Null(db.Database.CurrentTransaction);
            db.ChangeTracker.Clear();
            await transaction.DisposeAsync();
        }
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(0, await CreditKeysAsync(companyId, token));

        Assert.Equal(PurchasePostingOutcomeKind.Posted,
            (await PostPurchaseAsync(provider, PurchaseCommand(companyId, 930), true, token)).Kind);
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task PurchaseSourceKeyLockIsHeldByTheCallerTransactionBeforeThePurRead()
    {
        // FI-PUR-POST-007（BR-FI-020 鎖序：FI 來源鍵 applock 在讀 PUR 前取得，屬呼叫者交易）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[940] = PurchaseInvoice(companyId, 940);
        string? mode = null;
        invoices.OnRead = async (invoiceId, transaction, ct) =>
        {
            await using var command = new SqlCommand(
                "SELECT APPLOCK_MODE('public', @resource, 'Transaction')",
                (SqlConnection)transaction.Connection!, (SqlTransaction)transaction);
            command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = $"ap_purchase:{companyId}:{invoiceId}";
            mode = (string?)await command.ExecuteScalarAsync(ct);
        };
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);

        Assert.Equal(PurchasePostingOutcomeKind.Posted,
            (await PostPurchaseAsync(provider, PurchaseCommand(companyId, 940), true, token)).Kind);
        Assert.Equal("Exclusive", mode);
    }

    [Fact]
    public async Task MappingChangeAfterPostingDoesNotRestateTheJournalAndNewInvoicesUseTheNewVersion()
    {
        // FI-PUR-POST-008（FI-PUR-005／016：已過帳傳票不因映射變更而改寫；新發票用新映射與新版本）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[950] = PurchaseInvoice(companyId, 950);
        invoices.Items[951] = PurchaseInvoice(companyId, 951);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var first = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 950), true, token);
        Assert.Equal(PurchasePostingOutcomeKind.Posted, first.Kind);

        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var expense = (await scope.ServiceProvider.GetRequiredService<IChartOfAccounts>().FindAsync("5", token))!;
            await accounts.CreateAsync("5102", "費用", expense.Id, null, null, token);
        }
        var changed = await SavePurchaseMapAsync(provider, "2141", "5102", "1151", token);
        var second = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 951), true, token);
        Assert.Equal(PurchasePostingOutcomeKind.Posted, second.Kind);

        await using var read = provider.CreateAsyncScope();
        var lines = await read.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<GlJournalLine>().AsNoTracking()
            .Where(x => x.Sequence == 1).OrderBy(x => x.JournalId)
            .Select(x => new { x.JournalId, x.AccountCode, x.MappingVersion }).ToListAsync(token);
        Assert.Equal([(first.Receipt!.JournalId, "5101", changed.Version - 1), (second.Receipt!.JournalId, "5102", changed.Version)],
            lines.Select(x => (x.JournalId, x.AccountCode, x.MappingVersion)).ToArray());
    }
}
