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
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-COLL-POST-001～008（WU-FI-005a2b，ADR-121）：<see cref="ICollectionReceiptPostingService"/> 在呼叫者交易內以真實
/// SQL 把已核准的 FI 銷售收款過帳為 GL 傳票（借資金帳戶對應科目／貸未核銷款暫收）與 <c>ar_collection_receipt</c>
/// （未核銷＝金額）。收款單據來源以測試假實作注入（005b 實作真版）；科目映射用正式的 <c>gl_sales_collection_account_map</c>，
/// 資金帳戶經 MD 契約依 ID 讀取。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private const long CollectionCustomerId = 801;

    /// <summary>測試用收款單據來源：記錄讀取次數；讀取時（呼叫者交易內）可執行掛鉤檢查鎖。</summary>
    private sealed class FakeCollectionReceipts
    {
        public readonly Dictionary<long, FinanceCollectionReceiptSnapshot> Items = [];
        public int Reads;
        public Func<long, DbTransaction, CancellationToken, Task>? OnRead;
    }

    private sealed class FakeCollectionReceiptSource(FakeCollectionReceipts receipts) : IFinanceCollectionReceiptSource
    {
        public async Task<FinanceCollectionReceiptSnapshot?> ReadReceiptAsync(long companyId, long receiptId,
            DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(transaction.Connection);
            Interlocked.Increment(ref receipts.Reads);
            if (receipts.OnRead is { } hook)
                await hook(receiptId, transaction, cancellationToken);
            return receipts.Items.TryGetValue(receiptId, out var item) && item.CompanyId == companyId ? item : null;
        }
    }

    private sealed record CollectionSetup(long BankId, long CashBoxId, long UsdBankId, long BankLedgerId);

    private ServiceProvider CollectionProvider(long companyId, FakeCollectionReceipts receipts, bool withSource = true) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(receipts);
            if (withSource)
                services.AddScoped<IFinanceCollectionReceiptSource, FakeCollectionReceiptSource>();
            else
                foreach (var real in services.Where(x => x.ServiceType == typeof(IFinanceCollectionReceiptSource)).ToList())
                    services.Remove(real); // 主機沒有任何收款來源（含 005b3a 正式版）→ 500 SOURCE_CONTRACT_UNAVAILABLE

        });

    /// <summary>
    /// GL 種子（TWD、1101／4101、2026-11 期間）＋銀行存款 1102、應收 1131、暫收 2191（2 Liabilities）；資金帳戶
    /// BANK1（TWD、對應 1102）、CASHBOX（TWD、無對應科目）、BANKUSD（USD、對應 1102）；映射 2191／1131。
    /// </summary>
    private static async Task<CollectionSetup> SeedCollectionAsync(ServiceProvider provider, long companyId,
        CancellationToken token, bool withMap = true)
    {
        await SeedAsync(provider, companyId, token);
        CollectionSetup setup;
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var accounts = sp.GetRequiredService<AccountService>();
            var assets = (await sp.GetRequiredService<IChartOfAccounts>().FindAsync("1", token))!;
            var bankLedger = await accounts.CreateAsync("1102", "銀行存款", assets.Id, null, null, token);
            await accounts.CreateAsync("1131", "應收帳款", assets.Id, null, null, token);
            var liabilities = await accounts.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, token);
            await accounts.CreateAsync("2191", "暫收款", liabilities.Id, null, null, token);
            var usd = await sp.GetRequiredService<CurrencyService>().CreateAsync("USD", "US Dollar", "$", 2, false, token);
            var db = sp.GetRequiredService<MyErpDbContext>();
            var twd = await db.Set<Currency>().AsNoTracking().SingleAsync(x => x.Code == "TWD", token);
            var funds = sp.GetRequiredService<FundAccountService>();
            var bank = await funds.CreateAsync("BANK1", "Mega TWD", twd.Id,
                new(FundAccountType.Bank, "Mega", "001", bankLedger.Id), token);
            var cashBox = await funds.CreateAsync("CASHBOX", "Cash box", twd.Id,
                new(FundAccountType.Cash, null, null, null), token);
            var usdBank = await funds.CreateAsync("BANKUSD", "Mega USD", usd.Id,
                new(FundAccountType.Bank, "Mega", "002", bankLedger.Id), token);
            setup = new(bank.Id, cashBox.Id, usdBank.Id, bankLedger.Id);
        }
        if (withMap)
            await SaveCollectionMapAsync(provider, "2191", "1131", token);
        return setup;
    }

    private static FinanceCollectionReceiptSnapshot CollectionReceipt(long companyId, long receiptId, long bankId,
        decimal amount = 500m, string reference = "TT-0001",
        FinanceCollectionReceiptState state = FinanceCollectionReceiptState.Approved) =>
        new(companyId, receiptId, 1, "sha256:coll", CollectionCustomerId, state, BusinessDate, "TWD", "TWD", amount,
            bankId, reference);

    private static CollectionReceiptPostingCommand CollectionCommand(long companyId, long receiptId,
        string fingerprint = "sha256:coll", long customerId = CollectionCustomerId, int version = 1) =>
        new(companyId, receiptId, customerId, version, fingerprint);

    /// <summary>在新 scope 開交易、過帳，依 <paramref name="commit"/> 提交或回滾。</summary>
    private static async Task<CollectionReceiptPostingResult> PostCollectionAsync(ServiceProvider provider,
        CollectionReceiptPostingCommand command, bool commit, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var result = await sp.GetRequiredService<ICollectionReceiptPostingService>()
            .PostInCurrentTransactionAsync(command, token);
        if (db.Database.CurrentTransaction is null)
            return result;
        if (commit)
            await transaction.CommitAsync(token);
        else
            await transaction.RollbackAsync(token);
        return result;
    }

    private async Task<(int Receipts, int Journals, int Lines, int Receivables)> CollectionCountsAsync(long companyId,
        CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var receipts = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_receipt WHERE company_id={companyId}").SingleAsync(token);
        var receivables = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_posted_receivable WHERE company_id={companyId}").SingleAsync(token);
        var (_, journals, lines) = await CountsAsync(companyId, token);
        return (receipts, journals, lines, receivables);
    }

    private static void AssertCollectionRejected(CollectionReceiptPostingResult result, int status, string code)
    {
        Assert.Equal((CollectionPostingOutcomeKind.Rejected, false, status, code),
            (result.Kind, result.Changed, result.HttpStatus, result.ErrorCode));
        Assert.Null(result.Receipt);
    }

    [Fact]
    public async Task CollectionReceiptPostsBankDebitClearingCreditAndFullUnappliedOnlyWhenTheCallerCommits()
    {
        // FI-COLL-POST-001（FI-AR-COLL-002、BR-FI-033／034：借銀行／貸暫收、未核銷＝金額、應收不變）＋呼叫者回滾不留列
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[1001] = CollectionReceipt(companyId, 1001, setup.BankId, 12_345.6789m);

        var rolledBack = await PostCollectionAsync(provider, CollectionCommand(companyId, 1001), commit: false, token);
        Assert.Equal((CollectionPostingOutcomeKind.Posted, true, 200),
            (rolledBack.Kind, rolledBack.Changed, rolledBack.HttpStatus));
        Assert.Equal((0, 0, 0, 0), await CollectionCountsAsync(companyId, token));
        Assert.Equal(0, await CreditKeysAsync(companyId, token));

        var posted = await PostCollectionAsync(provider, CollectionCommand(companyId, 1001), commit: true, token);
        Assert.Equal((CollectionPostingOutcomeKind.Posted, true, 200, (string?)null),
            (posted.Kind, posted.Changed, posted.HttpStatus, posted.ErrorCode));
        var receipt = Assert.IsType<CollectionReceiptPostingReceipt>(posted.Receipt);
        Assert.Equal((companyId, 1001L, CollectionCustomerId, 1, "sha256:coll", "TWD", 12_345.6789m, 12_345.6789m,
                setup.BankId, "TT-0001"),
            (receipt.CompanyId, receipt.ReceiptId, receipt.CustomerId, receipt.SourceVersion, receipt.Fingerprint,
                receipt.BaseCurrency, receipt.Amount, receipt.UnappliedAmount, receipt.BankAccountId, receipt.BankReference));
        // 收款不減應收（BR-FI-038）：沒有任何 ar_posted_receivable 列被建立或改動。
        Assert.Equal((1, 1, 2, 0), await CollectionCountsAsync(companyId, token));
        Assert.Equal(1, await CreditKeysAsync(companyId, token));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var map = await db.Set<SalesCollectionAccountMapping>().AsNoTracking().SingleAsync(token);
        var row = await db.Set<CollectionReceipt>().AsNoTracking().SingleAsync(token);
        Assert.Equal((receipt.CollectionReceiptId, "SalesCollection", 1001L, CollectionCustomerId, receipt.JournalId,
                BusinessDate, setup.BankId, "TT-0001", "TWD", 12_345.6789m, 12_345.6789m, 1, "sha256:coll"),
            (row.Id, row.SourceType, row.SourceId, row.CustomerId, row.GlJournalId, row.BusinessDate, row.BankAccountId,
                row.BankReference, row.BaseCurrency, row.Amount, row.UnappliedAmount, row.SourceVersion, row.Fingerprint));
        var lines = await db.Set<GlJournalLine>().AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(token);
        Assert.Equal([("1102", 12_345.6789m, 0m), ("2191", 0m, 12_345.6789m)],
            lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
        Assert.All(lines, x => Assert.Equal((receipt.JournalId, map.Version), (x.JournalId, x.MappingVersion)));
        var key = await db.Set<GlSourceKey>().AsNoTracking().SingleAsync(token);
        Assert.Equal(("FI", "SalesCollection", 1001L, 1, "sha256:coll"),
            (key.SourceModule, key.SourceType, key.SourceId, key.SourceVersion, key.Fingerprint));
        var journal = await db.Set<GlJournal>().AsNoTracking().SingleAsync(token);
        Assert.Equal(("TWD", 12_345.6789m, 12_345.6789m, BusinessDate),
            (journal.BaseCurrency, journal.TotalDebit, journal.TotalCredit, journal.BusinessDate));
    }

    [Fact]
    public async Task SameCollectionClaimReplaysUnchangedWhileAnotherFingerprintVersionOrCustomerConflicts()
    {
        // FI-COLL-POST-002（FI-AR-COLL-009、BR-FI-038：同指紋重放 changed=false、零新增列、不再讀單據；其他主張 409）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[1002] = CollectionReceipt(companyId, 1002, setup.BankId);
        var first = await PostCollectionAsync(provider, CollectionCommand(companyId, 1002), commit: true, token);
        Assert.Equal(CollectionPostingOutcomeKind.Posted, first.Kind);

        // 單據之後標記 Posted；同一主張重放不再讀單據、不寫任何列，回傳已提交的收據。
        receipts.Items[1002] = CollectionReceipt(companyId, 1002, setup.BankId, state: FinanceCollectionReceiptState.Posted);
        var reads = receipts.Reads;
        var replay = await PostCollectionAsync(provider, CollectionCommand(companyId, 1002), commit: true, token);
        Assert.Equal(new CollectionReceiptPostingResult(CollectionPostingOutcomeKind.Unchanged, false, 200,
            Receipt: first.Receipt), replay);
        Assert.Equal(reads, receipts.Reads);

        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, 1002, "sha256:other"), true, token),
            409, CollectionPostingErrors.SourceConflict);
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, 1002, version: 2), true, token),
            409, CollectionPostingErrors.SourceConflict);
        AssertCollectionRejected(await PostCollectionAsync(provider,
            CollectionCommand(companyId, 1002, customerId: CollectionCustomerId + 1), true, token),
            409, CollectionPostingErrors.SourceConflict);
        Assert.Equal(reads, receipts.Reads);
        Assert.Equal((1, 1, 2, 0), await CollectionCountsAsync(companyId, token));
    }

    [Fact]
    public async Task UnpostableMismatchedInvalidForeignAndMalformedReceiptsAreRejectedWithoutRows()
    {
        // FI-COLL-POST-003（FI-AR-COLL-001／005：非 Approved 422、主張不符 409、內容 422、跨公司 404、格式 400）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);

        var id = 1010L;
        foreach (var state in new[] { FinanceCollectionReceiptState.Draft, FinanceCollectionReceiptState.Pending,
                     FinanceCollectionReceiptState.Rejected, FinanceCollectionReceiptState.Deleted,
                     FinanceCollectionReceiptState.Posted })
        {
            receipts.Items[++id] = CollectionReceipt(companyId, id, setup.BankId, reference: $"S{id}", state: state);
            AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, id), true, token),
                422, CollectionPostingErrors.SourceNotPostable);
        }

        receipts.Items[++id] = CollectionReceipt(companyId, id, setup.BankId, reference: $"S{id}");
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, id, "sha256:stale"), true, token),
            409, CollectionPostingErrors.SourceMismatch);
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, id, version: 2), true, token),
            409, CollectionPostingErrors.SourceMismatch);
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, id, customerId: 999), true, token),
            409, CollectionPostingErrors.SourceMismatch);

        foreach (var invalid in new[]
                 {
                     CollectionReceipt(companyId, ++id, setup.BankId, 0m),
                     CollectionReceipt(companyId, ++id, setup.BankId, 1.00001m),
                     CollectionReceipt(companyId, ++id, setup.BankId) with { Currency = "USD" },
                     CollectionReceipt(companyId, ++id, setup.BankId, reference: " TT-0001"),
                     CollectionReceipt(companyId, ++id, 0)
                 })
        {
            receipts.Items[invalid.ReceiptId] = invalid;
            AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, invalid.ReceiptId), true, token),
                422, CollectionPostingErrors.InvalidSource);
        }

        // 其他公司的單據或命令：404，不洩漏。
        receipts.Items[++id] = CollectionReceipt(companyId + 1, id, setup.BankId);
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, id), true, token),
            404, CollectionPostingErrors.SourceNotFound);
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId + 1, id), true, token),
            404, CollectionPostingErrors.SourceNotFound);
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, 9_999), true, token),
            404, CollectionPostingErrors.SourceNotFound);
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, id, "sha256:coll "), true, token),
            400, CollectionPostingErrors.InvalidInput);
        AssertCollectionRejected(await PostCollectionAsync(provider, null!, true, token), 400, CollectionPostingErrors.InvalidInput);

        Assert.Equal((0, 0, 0, 0), await CollectionCountsAsync(companyId, token));
    }

    [Fact]
    public async Task BankReferenceIsUniquePerFundAccountIgnoringCaseButIndependentAcrossFundAccounts()
    {
        // FI-COLL-POST-004（FI-AR-COLL-008、BR-FI-033：同公司同資金帳戶的銀行參考已過帳 → 409；大小寫不同視為相同；其他帳戶獨立）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        // CASHBOX 給予對應科目，作為第二個可用資金帳戶。
        await using (var scope = provider.CreateAsyncScope())
        {
            var funds = scope.ServiceProvider.GetRequiredService<FundAccountService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var cashBox = await db.Set<FundAccount>().AsNoTracking().SingleAsync(x => x.Id == setup.CashBoxId, token);
            await funds.UpdateAsync(cashBox.Id, cashBox.Name, new(FundAccountType.Cash, null, null, setup.BankLedgerId),
                true, cashBox.Version, token);
        }
        receipts.Items[1031] = CollectionReceipt(companyId, 1031, setup.BankId, reference: "TT-9");
        receipts.Items[1032] = CollectionReceipt(companyId, 1032, setup.BankId, reference: "tt-9") with { CustomerId = CollectionCustomerId + 1 };
        receipts.Items[1033] = CollectionReceipt(companyId, 1033, setup.CashBoxId, reference: "TT-9");

        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 1031), true, token)).Kind);
        AssertCollectionRejected(await PostCollectionAsync(provider,
                CollectionCommand(companyId, 1032, customerId: CollectionCustomerId + 1), true, token),
            409, CollectionPostingErrors.BankReferenceDuplicate);
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 1033), true, token)).Kind);
        Assert.Equal((2, 2, 4, 0), await CollectionCountsAsync(companyId, token));
    }

    [Fact]
    public async Task MissingMappingContractFundAccountAccountsOrPeriodAreNamedRejectionsThatKeepTheCallerTransaction()
    {
        // FI-COLL-POST-005（FI-AR-COLL-006、BR-FI-033：缺映射 422、單據契約不可用 500、資金帳戶不存在／停用／他幣別／
        // 無對應科目 422、科目停用 422 含傳票行號、銀行科目等於暫收 422、GL 期間拒絕原樣轉出）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token, withMap: false);
        receipts.Items[1040] = CollectionReceipt(companyId, 1040, setup.BankId);

        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, 1040), true, token),
            422, CollectionPostingErrors.MappingMissing);
        await using (var noSource = CollectionProvider(companyId, receipts, withSource: false))
        {
            AssertCollectionRejected(await PostCollectionAsync(noSource, CollectionCommand(companyId, 1040), true, token),
                500, CollectionPostingErrors.SourceContractUnavailable);
        }

        await SaveCollectionMapAsync(provider, "2191", "1131", token);
        foreach (var (receiptId, bankId) in new[] { (1041L, 9_999_999L), (1042L, setup.CashBoxId), (1043L, setup.UsdBankId) })
        {
            receipts.Items[receiptId] = CollectionReceipt(companyId, receiptId, bankId, reference: $"F{receiptId}");
            AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, receiptId), true, token),
                422, CollectionPostingErrors.FundAccountInvalid);
        }

        // 銀行科目與暫收科目相同（映射錯誤）：第 2 行不可用。
        await SaveCollectionMapAsync(provider, "1102", "1131", token);
        var same = await PostCollectionAsync(provider, CollectionCommand(companyId, 1040), true, token);
        AssertCollectionRejected(same, 422, CollectionPostingErrors.AccountInvalid);
        Assert.Equal(2, same.InvalidLine);

        // 暫收科目不存在：第 2 行。
        await SaveCollectionMapAsync(provider, "2199", "1131", token);
        var missing = await PostCollectionAsync(provider, CollectionCommand(companyId, 1040), true, token);
        AssertCollectionRejected(missing, 422, CollectionPostingErrors.AccountInvalid);
        Assert.Equal(2, missing.InvalidLine);

        // 銀行對應科目停用：第 1 行。
        await SaveCollectionMapAsync(provider, "2191", "1131", token);
        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var bank = await db.Set<Account>().AsNoTracking().SingleAsync(x => x.Id == setup.BankLedgerId, token);
            await accounts.UpdateAsync(bank.Id, bank.Name, BalanceDirection.Debit, false, bank.Version, token);
        }
        var inactive = await PostCollectionAsync(provider, CollectionCommand(companyId, 1040), true, token);
        AssertCollectionRejected(inactive, 422, CollectionPostingErrors.AccountInvalid);
        Assert.Equal(1, inactive.InvalidLine);

        // 資金帳戶停用 → 422；GL 拒絕（期間不存在）原樣轉出，呼叫者交易仍開啟。
        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var funds = scope.ServiceProvider.GetRequiredService<FundAccountService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var bank = await db.Set<Account>().AsNoTracking().SingleAsync(x => x.Id == setup.BankLedgerId, token);
            await accounts.UpdateAsync(bank.Id, bank.Name, BalanceDirection.Debit, true, bank.Version, token);
            var fund = await db.Set<FundAccount>().AsNoTracking().SingleAsync(x => x.Id == setup.BankId, token);
            await funds.UpdateAsync(fund.Id, fund.Name, new(FundAccountType.Bank, "Mega", "001", setup.BankLedgerId),
                false, fund.Version, token);
        }
        AssertCollectionRejected(await PostCollectionAsync(provider, CollectionCommand(companyId, 1040), true, token),
            422, CollectionPostingErrors.FundAccountInvalid);
        await using (var scope = provider.CreateAsyncScope())
        {
            var funds = scope.ServiceProvider.GetRequiredService<FundAccountService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var fund = await db.Set<FundAccount>().AsNoTracking().SingleAsync(x => x.Id == setup.BankId, token);
            await funds.UpdateAsync(fund.Id, fund.Name, new(FundAccountType.Bank, "Mega", "001", setup.BankLedgerId),
                true, fund.Version, token);
        }

        receipts.Items[1044] = CollectionReceipt(companyId, 1044, setup.BankId, reference: "P1044") with
        {
            BusinessDate = new DateOnly(2027, 1, 5)
        };
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var period = await sp.GetRequiredService<ICollectionReceiptPostingService>()
                .PostInCurrentTransactionAsync(CollectionCommand(companyId, 1044), token);
            AssertCollectionRejected(period, 422, GlPostingErrors.PeriodMissing);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            await transaction.CommitAsync(token);
        }

        Assert.Equal((0, 0, 0, 0), await CollectionCountsAsync(companyId, token));
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 1040), true, token)).Kind);
    }

    [Fact]
    public async Task CollectionPostingRequiresACallerTransactionAndAGlFaultRollsBackEverything()
    {
        // FI-COLL-POST-006（BR-FI-037 部分：無交易是程式錯誤；GL 失敗 → 整個呼叫者交易回滾，之後可重過帳）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[1050] = CollectionReceipt(companyId, 1050, setup.BankId);

        await using (var scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<ICollectionReceiptPostingService>()
                .PostInCurrentTransactionAsync(CollectionCommand(companyId, 1050), token));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            // 呼叫者留下未儲存的變更 → GL 寫入前失敗（ADR-107）→ 整個呼叫者交易回滾（含信用鍵）。
            db.Set<CreditKey>().Add(new CreditKey { CustomerId = 1 });
            var fault = await sp.GetRequiredService<ICollectionReceiptPostingService>()
                .PostInCurrentTransactionAsync(CollectionCommand(companyId, 1050), token);
            Assert.Equal((CollectionPostingOutcomeKind.Fault, false, 500, GlPostingErrors.WriteFailed),
                (fault.Kind, fault.Changed, fault.HttpStatus, fault.ErrorCode));
            Assert.Null(db.Database.CurrentTransaction);
            db.ChangeTracker.Clear();
            await transaction.DisposeAsync();
        }
        Assert.Equal((0, 0, 0, 0), await CollectionCountsAsync(companyId, token));
        Assert.Equal(0, await CreditKeysAsync(companyId, token));

        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 1050), true, token)).Kind);
        Assert.Equal((1, 1, 2, 0), await CollectionCountsAsync(companyId, token));
    }

    [Fact]
    public async Task CreditAndReceiptSourceLocksAreHeldByTheCallerTransactionBeforeTheReceiptRead()
    {
        // FI-COLL-POST-007（BR-FI-036 鎖序：信用鍵 → 收款來源鍵，皆在讀單據前取得、屬呼叫者交易；讀後才取銀行參考鍵）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[1060] = CollectionReceipt(companyId, 1060, setup.BankId, reference: "Lock-1");
        var modes = new List<string?>();
        receipts.OnRead = async (receiptId, transaction, ct) =>
        {
            foreach (var resource in new[]
                     {
                         $"ar_credit:{companyId}:{CollectionCustomerId}", $"ar_collection:{companyId}:{receiptId}",
                         $"ar_collection_bank:{companyId}:{setup.BankId}:LOCK-1"
                     })
            {
                await using var command = new SqlCommand("SELECT APPLOCK_MODE('public', @resource, 'Transaction')",
                    (SqlConnection)transaction.Connection!, (SqlTransaction)transaction);
                command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = resource;
                modes.Add((string?)await command.ExecuteScalarAsync(ct));
            }
        };

        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 1060), true, token)).Kind);
        Assert.Equal(["Exclusive", "Exclusive", "NoLock"], modes);
    }

    [Fact]
    public async Task CollectionMappingChangeAfterPostingDoesNotRestateTheJournalAndNewReceiptsUseTheNewVersion()
    {
        // FI-COLL-POST-008（BR-FI-151：已過帳傳票不因映射變更而改寫；新收款用新映射與新版本）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = CollectionProvider(companyId, receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[1070] = CollectionReceipt(companyId, 1070, setup.BankId, reference: "M1");
        receipts.Items[1071] = CollectionReceipt(companyId, 1071, setup.BankId, reference: "M2");
        var first = await PostCollectionAsync(provider, CollectionCommand(companyId, 1070), true, token);
        Assert.Equal(CollectionPostingOutcomeKind.Posted, first.Kind);

        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var liabilities = (await scope.ServiceProvider.GetRequiredService<IChartOfAccounts>().FindAsync("2", token))!;
            await accounts.CreateAsync("2192", "其他暫收", liabilities.Id, null, null, token);
        }
        var changed = await SaveCollectionMapAsync(provider, "2192", "1131", token);
        var second = await PostCollectionAsync(provider, CollectionCommand(companyId, 1071), true, token);
        Assert.Equal(CollectionPostingOutcomeKind.Posted, second.Kind);

        await using var read = provider.CreateAsyncScope();
        var lines = await read.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<GlJournalLine>().AsNoTracking()
            .Where(x => x.Sequence == 2).OrderBy(x => x.JournalId)
            .Select(x => new { x.JournalId, x.AccountCode, x.MappingVersion }).ToListAsync(token);
        Assert.Equal([(first.Receipt!.JournalId, "2191", changed.Version - 1), (second.Receipt!.JournalId, "2192", changed.Version)],
            lines.Select(x => (x.JournalId, x.AccountCode, x.MappingVersion)).ToArray());
    }
}
