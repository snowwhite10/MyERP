using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-OP-POST-001～008（WU-FI-008a4，ADR-211）：<see cref="IOtherPaymentPostingService"/> 在呼叫者交易內以真實 SQL
/// 把已存其他付款（Draft／Pending）過帳：借對照費用科目、貸資金帳戶對應科目同額，寫 <c>ap_other_payment_source</c>
/// 並把單據轉 Posted。重放、指紋／版本衝突、類別／資金帳戶／對照／科目／期間拒絕、呼叫者回滾、雙連線競態與鎖逾時。
/// MD 類別、資金帳戶與科目為真實種子；對照列以 EF 直接寫入（維護 API 屬 008b 以後）。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private ServiceProvider OtherPaymentPostingProvider(long companyId, int lockTimeoutMilliseconds = 15_000) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(new OtherPaymentActor());
            services.AddScoped<ICurrentAuthorization, OtherPaymentAuthorization>();
            services.AddScoped<INumberGenerator, OtherPaymentNumbers>();
            services.AddScoped<IOtherPaymentPostingService>(sp =>
            {
                var service = ActivatorUtilities.CreateInstance<OtherPaymentPostingService>(sp);
                service.LockTimeoutMilliseconds = lockTimeoutMilliseconds;
                return service;
            });
        });

    /// <summary>其他付款種子＋COURIER → 6101 對照；回傳 setup。</summary>
    private static async Task<OtherPaymentSetup> SeedOtherPaymentPostingAsync(ServiceProvider provider, long companyId,
        CancellationToken token, bool withMap = true)
    {
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        if (withMap)
            await SaveOtherPaymentMapAsync(provider, setup.CourierId, setup.ExpenseLedgerId, token);
        return setup;
    }

    /// <summary>新增或改寫（版本遞增）一個費用類別對照列。</summary>
    private static async Task SaveOtherPaymentMapAsync(ServiceProvider provider, long categoryId, long accountId,
        CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var row = await db.Set<OtherPaymentAccountMapping>().SingleOrDefaultAsync(x => x.ExpenseCategoryId == categoryId, token);
        if (row is null)
            db.Add(new OtherPaymentAccountMapping { ExpenseCategoryId = categoryId, ExpenseAccountId = accountId });
        else
            row.ExpenseAccountId = accountId;
        await db.SaveChangesAsync(token);
    }

    private static async Task<long> CreateAccountAsync(ServiceProvider provider, string code, string parentCode,
        CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var parent = (await sp.GetRequiredService<IChartOfAccounts>().FindAsync(parentCode, token))!;
        return (await sp.GetRequiredService<AccountService>().CreateAsync(code, code, parent.Id, null, null, token)).Id;
    }

    /// <summary>依目前已存單據組成呼叫者主張（版本＋指紋）。</summary>
    private static async Task<OtherPaymentPostingCommand> OtherPaymentClaimAsync(ServiceProvider provider, long id,
        CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var document = await db.Set<OtherPaymentDocument>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(x => x.Id == id, token);
        return new(document.CompanyId, document.Id, document.Version, OtherPaymentFingerprint.Create(document));
    }

    private static async Task<OtherPaymentPostingResult> PostOtherPaymentAsync(ServiceProvider provider,
        OtherPaymentPostingCommand? command, bool commit, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var result = await sp.GetRequiredService<IOtherPaymentPostingService>()
            .PostInCurrentTransactionAsync(command, token);
        if (db.Database.CurrentTransaction is null)
            return result;
        if (commit)
            await transaction.CommitAsync(token);
        else
            await transaction.RollbackAsync(token);
        return result;
    }

    private async Task<(int Sources, int Journals, int Lines, int Keys)> OtherPaymentPostingCountsAsync(long companyId,
        CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var sources = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ap_other_payment_source WHERE company_id={companyId}").SingleAsync(token);
        var (keys, journals, lines) = await CountsAsync(companyId, token);
        return (sources, journals, lines, keys);
    }

    private async Task<(string Status, int Version)> OtherPaymentStateAsync(long id, CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var status = await db.Database.SqlQuery<string>(
            $"SELECT status AS [Value] FROM dbo.ap_other_payment_doc WHERE id={id}").SingleAsync(token);
        var version = await db.Database.SqlQuery<int>(
            $"SELECT version AS [Value] FROM dbo.ap_other_payment_doc WHERE id={id}").SingleAsync(token);
        return (status, version);
    }

    private static void AssertOtherPaymentRejected(OtherPaymentPostingResult result, int status, string? code,
        int? line = null)
    {
        Assert.Equal((OtherPaymentPostingOutcomeKind.Rejected, false, status, code, line),
            (result.Kind, result.Changed, result.HttpStatus, result.ErrorCode, result.InvalidLine));
        Assert.Null(result.Receipt);
    }

    [Fact]
    public async Task OtherPaymentPostsExpenseDebitBankCreditSourceAndPostedStatusOnlyWhenTheCallerCommits()
    {
        // FI-OP-POST-001（正常，BR-FI-061／063）：借 6101／貸 1102 同額；來源列快照類別、費用科目 ID、對照版本、銀行參考；
        // 單據 Posted；呼叫者回滾不留列；不產生任何應付、付款核銷或未核銷付款。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, amount: 12_345.6789m), token));
        var claim = await OtherPaymentClaimAsync(provider, draft.Id, token);
        Assert.StartsWith(OtherPaymentFingerprint.Prefix, claim.Fingerprint, StringComparison.Ordinal);

        var rolledBack = await PostOtherPaymentAsync(provider, claim, commit: false, token);
        Assert.Equal((OtherPaymentPostingOutcomeKind.Posted, true, 200), (rolledBack.Kind, rolledBack.Changed, rolledBack.HttpStatus));
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));
        Assert.Equal(("Draft", 1), await OtherPaymentStateAsync(draft.Id, token));

        var posted = await PostOtherPaymentAsync(provider, claim, commit: true, token);
        Assert.Equal((OtherPaymentPostingOutcomeKind.Posted, true, 200, (string?)null),
            (posted.Kind, posted.Changed, posted.HttpStatus, posted.ErrorCode));
        var receipt = Assert.IsType<OtherPaymentPostingReceipt>(posted.Receipt);
        Assert.Equal((companyId, draft.Id, 1, claim.Fingerprint, BusinessDate, setup.CourierId, setup.ExpenseLedgerId, 1,
                setup.BankId, "WD-0001", "TWD", 12_345.6789m),
            (receipt.CompanyId, receipt.PaymentId, receipt.SourceVersion, receipt.Fingerprint, receipt.BusinessDate,
                receipt.ExpenseCategoryId, receipt.ExpenseAccountId, receipt.MappingVersion, receipt.BankAccountId,
                receipt.BankReference, receipt.BaseCurrency, receipt.Amount));
        Assert.Equal((1, 1, 2, 1), await OtherPaymentPostingCountsAsync(companyId, token));
        Assert.Equal(("Posted", 2), await OtherPaymentStateAsync(draft.Id, token));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var row = await db.Set<OtherPaymentSource>().AsNoTracking().SingleAsync(token);
        Assert.Equal((receipt.SourceRowId, "OtherPayment", draft.Id, receipt.JournalId, 12_345.6789m, "TWD"),
            (row.Id, row.SourceType, row.SourceId, row.GlJournalId, row.Amount, row.BaseCurrency));
        var lines = await db.Set<GlJournalLine>().AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(token);
        Assert.Equal([("6101", 12_345.6789m, 0m), ("1102", 0m, 12_345.6789m)],
            lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
        Assert.All(lines, x => Assert.Equal((receipt.JournalId, 1), (x.JournalId, x.MappingVersion)));
        var key = await db.Set<GlSourceKey>().AsNoTracking().SingleAsync(token);
        Assert.Equal(("FI", "OtherPayment", draft.Id, 1, claim.Fingerprint),
            (key.SourceModule, key.SourceType, key.SourceId, key.SourceVersion, key.Fingerprint));
        var journal = await db.Set<GlJournal>().AsNoTracking().SingleAsync(token);
        Assert.Equal(("TWD", 12_345.6789m, 12_345.6789m, BusinessDate),
            (journal.BaseCurrency, journal.TotalDebit, journal.TotalCredit, journal.BusinessDate));
        await using var raw = database.CreateRawContext();
        Assert.Equal(0, await raw.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ap_posted_payable WHERE company_id={companyId}").SingleAsync(token));

        var detail = await OtherPaymentAsync(provider, s => s.GetAsync(draft.Id, token));
        Assert.Equal((OtherPaymentDocumentStatus.Posted, (long?)receipt.JournalId), (detail.Status, detail.JournalId));
    }

    [Fact]
    public async Task SameOtherPaymentClaimReplaysUnchangedAndAnotherVersionOrFingerprintConflicts()
    {
        // FI-OP-POST-002（BR-FI-062）：同版本／指紋重放 changed=false、回原 ID、不新增 GL、不改單據（版本不變＝無稽核）；
        // 其他版本或指紋 409 SOURCE_CONFLICT；已過帳單據不可再修改或刪除（422）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup), token));
        var claim = await OtherPaymentClaimAsync(provider, draft.Id, token);
        var first = await PostOtherPaymentAsync(provider, claim, true, token);
        Assert.Equal(OtherPaymentPostingOutcomeKind.Posted, first.Kind);
        var state = await OtherPaymentStateAsync(draft.Id, token);

        var replay = await PostOtherPaymentAsync(provider, claim, true, token);
        Assert.Equal(new OtherPaymentPostingResult(OtherPaymentPostingOutcomeKind.Unchanged, false, 200,
            Receipt: first.Receipt), replay);
        Assert.Equal(state, await OtherPaymentStateAsync(draft.Id, token));
        Assert.Equal((1, 1, 2, 1), await OtherPaymentPostingCountsAsync(companyId, token));

        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, claim with { Fingerprint = "FI-OTHER-PAYMENT-v1:x" },
            true, token), 409, OtherPaymentPostingErrors.SourceConflict);
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, claim with { SourceVersion = 2 }, true, token),
            409, OtherPaymentPostingErrors.SourceConflict);
        var edit = await Assert.ThrowsAsync<BusinessRuleException>(() => OtherPaymentAsync(provider,
            s => s.ReplaceAsync(draft.Id, state.Version, OtherPaymentInput(setup, amount: 1m), token)));
        Assert.Equal(OtherPaymentDocumentErrors.InvalidStatus, edit.ErrorCode);
        Assert.Equal((1, 1, 2, 1), await OtherPaymentPostingCountsAsync(companyId, token));
    }

    [Fact]
    public async Task OtherPaymentStateClaimIdentityAndInputAreCheckedBeforeAnyRowIsWritten()
    {
        // FI-OP-POST-003（狀態／主張／身分）：Pending 可過帳；舊版本或指紋 409 SOURCE_MISMATCH；Rejected 與已刪除 422；
        // 不存在、其他公司的單據或命令 404；格式錯誤 400；全部零列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);

        var stale = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "S-1"), token));
        var staleClaim = await OtherPaymentClaimAsync(provider, stale.Id, token);
        await OtherPaymentAsync(provider, s => s.ReplaceAsync(stale.Id, 1, OtherPaymentInput(setup, "S-1", 301m), token));
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, staleClaim, true, token), 409,
            OtherPaymentPostingErrors.SourceMismatch);
        var fresh = await OtherPaymentClaimAsync(provider, stale.Id, token);
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, fresh with { Fingerprint = staleClaim.Fingerprint },
            true, token), 409, OtherPaymentPostingErrors.SourceMismatch);

        var rejected = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "S-2"), token));
        await SetOtherPaymentStatusAsync(rejected.Id, "Rejected");
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, await OtherPaymentClaimAsync(provider, rejected.Id, token),
            true, token), 422, OtherPaymentPostingErrors.SourceNotPostable);
        var deleted = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "S-3"), token));
        await OtherPaymentAsync(provider, s => s.DeleteAsync(deleted.Id, 1, token));
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, await OtherPaymentClaimAsync(provider, deleted.Id, token),
            true, token), 422, OtherPaymentPostingErrors.SourceNotPostable);

        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, fresh with { PaymentId = 987_654_321 }, true, token),
            404, OtherPaymentPostingErrors.SourceNotFound);
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, fresh with { CompanyId = companyId + 1 }, true, token),
            404, OtherPaymentPostingErrors.SourceNotFound);
        var otherCompany = NewCompany();
        await using (var other = OtherPaymentPostingProvider(otherCompany))
        {
            var otherSetup = await SeedOtherPaymentPostingAsync(other, otherCompany, token);
            var foreign = await OtherPaymentAsync(other, s => s.CreateAsync(OtherPaymentInput(otherSetup, "F-1"), token));
            var foreignClaim = await OtherPaymentClaimAsync(other, foreign.Id, token);
            AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, foreignClaim with { CompanyId = companyId },
                true, token), 404, OtherPaymentPostingErrors.SourceNotFound);
        }
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, fresh with { Fingerprint = " x" }, true, token),
            400, OtherPaymentPostingErrors.InvalidInput);
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, fresh with { SourceVersion = 0 }, true, token),
            400, OtherPaymentPostingErrors.InvalidInput);
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, null, true, token), 400,
            OtherPaymentPostingErrors.InvalidInput);
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));

        // Pending（SYS 最終審批的呼叫點）可過帳。
        await SetOtherPaymentStatusAsync(stale.Id, "Pending");
        var pending = await PostOtherPaymentAsync(provider, await OtherPaymentClaimAsync(provider, stale.Id, token), true, token);
        Assert.Equal((OtherPaymentPostingOutcomeKind.Posted, 301m), (pending.Kind, pending.Receipt!.Amount));
        Assert.Equal("Posted", (await OtherPaymentStateAsync(stale.Id, token)).Status);
    }

    [Fact]
    public async Task CategoryFundMappingAndExcludedAccountsAreNamedRejectionsThatKeepTheCallerTransaction()
    {
        // FI-OP-POST-004（BR-FI-060）：缺對照 422 MAPPING_MISSING；對照到應付控制（負債）、暫付（負債）、預付（資產）、
        // 有子科目、停用科目 422 ACCOUNT_INVALID 第 1 行；類別停用 422 CATEGORY_INVALID；資金帳戶停用 422；呼叫者交易仍開啟、零列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token, withMap: false);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup), token));
        var claim = await OtherPaymentClaimAsync(provider, draft.Id, token);

        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, claim, true, token), 422,
            OtherPaymentPostingErrors.MappingMissing);

        var apControl = await CreateAccountAsync(provider, "2101", "2", token);
        var prepaid = await CreateAccountAsync(provider, "1141", "1", token);
        var inactiveExpense = await CreateAccountAsync(provider, "6102", "6", token);
        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var row = await db.Set<Account>().AsNoTracking().SingleAsync(x => x.Id == inactiveExpense, token);
            await accounts.UpdateAsync(row.Id, row.Name, BalanceDirection.Debit, false, row.Version, token);
        }
        long parentExpense;
        await using (var scope = provider.CreateAsyncScope())
            parentExpense = (await scope.ServiceProvider.GetRequiredService<IChartOfAccounts>().FindAsync("6", token))!.Id;
        long unapplied;
        await using (var scope = provider.CreateAsyncScope())
            unapplied = (await scope.ServiceProvider.GetRequiredService<IChartOfAccounts>().FindAsync("2191", token))!.Id;
        foreach (var account in new[] { apControl, unapplied, prepaid, parentExpense, inactiveExpense, 987_654_321L })
        {
            await SaveOtherPaymentMapAsync(provider, setup.CourierId, account, token);
            AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, claim, true, token), 422,
                OtherPaymentPostingErrors.AccountInvalid, 1);
        }
        await SaveOtherPaymentMapAsync(provider, setup.CourierId, setup.ExpenseLedgerId, token);

        await using (var scope = provider.CreateAsyncScope())
        {
            var categories = scope.ServiceProvider.GetRequiredService<IncomeExpenseCategoryService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var category = await db.Set<IncomeExpenseCategory>().AsNoTracking().SingleAsync(x => x.Id == setup.CourierId, token);
            await categories.UpdateAsync(category.Id, category.Name, category.LedgerAccountId, false, category.Version, token);
        }
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, claim, true, token), 422,
            OtherPaymentPostingErrors.CategoryInvalid);
        await using (var scope = provider.CreateAsyncScope())
        {
            var categories = scope.ServiceProvider.GetRequiredService<IncomeExpenseCategoryService>();
            var funds = scope.ServiceProvider.GetRequiredService<FundAccountService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var category = await db.Set<IncomeExpenseCategory>().AsNoTracking().SingleAsync(x => x.Id == setup.CourierId, token);
            await categories.UpdateAsync(category.Id, category.Name, category.LedgerAccountId, true, category.Version, token);
            var fund = await db.Set<FundAccount>().AsNoTracking().SingleAsync(x => x.Id == setup.BankId, token);
            await funds.UpdateAsync(fund.Id, fund.Name, new(FundAccountType.Bank, "Mega", "001", fund.LedgerAccountId),
                false, fund.Version, token);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            AssertOtherPaymentRejected(await sp.GetRequiredService<IOtherPaymentPostingService>()
                .PostInCurrentTransactionAsync(claim, token), 422, OtherPaymentPostingErrors.FundAccountInvalid);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            await transaction.CommitAsync(token);
        }
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));
        Assert.Equal(("Draft", 1), await OtherPaymentStateAsync(draft.Id, token));

        await using (var scope = provider.CreateAsyncScope())
        {
            var funds = scope.ServiceProvider.GetRequiredService<FundAccountService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var fund = await db.Set<FundAccount>().AsNoTracking().SingleAsync(x => x.Id == setup.BankId, token);
            await funds.UpdateAsync(fund.Id, fund.Name, new(FundAccountType.Bank, "Mega", "001", fund.LedgerAccountId),
                true, fund.Version, token);
        }
        var posted = await PostOtherPaymentAsync(provider, claim, true, token);
        Assert.Equal((OtherPaymentPostingOutcomeKind.Posted, 7), (posted.Kind, posted.Receipt!.MappingVersion));
    }

    [Fact]
    public async Task ClosedOrMissingPeriodRejectsAndLaterMappingChangesDoNotRewriteHistory()
    {
        // FI-OP-POST-005（BR-FI-061／064）：保存日期無期間 422 PERIOD_MISSING、期間關閉 422 PERIOD_CLOSED，零列；
        // 過帳後改對照（新版本）不改已存來源快照與傳票。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var future = await OtherPaymentAsync(provider, s => s.CreateAsync(
            OtherPaymentInput(setup, "P-1") with { BusinessDate = new DateOnly(2027, 1, 5) }, token));
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, await OtherPaymentClaimAsync(provider, future.Id, token),
            true, token), 422, GlPostingErrors.PeriodMissing);

        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "P-2"), token));
        var claim = await OtherPaymentClaimAsync(provider, draft.Id, token);
        await using (var scope = provider.CreateAsyncScope())
        {
            var periods = scope.ServiceProvider.GetRequiredService<GlPeriodWriteService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var period = await db.Set<GlPeriod>().AsNoTracking().SingleAsync(token);
            await periods.UpdateAsync(companyId, period.Id, period.Version, period.StartDate, period.EndDate, false, token);
        }
        AssertOtherPaymentRejected(await PostOtherPaymentAsync(provider, claim, true, token), 422, GlPostingErrors.PeriodClosed);
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));
        await using (var scope = provider.CreateAsyncScope())
        {
            var periods = scope.ServiceProvider.GetRequiredService<GlPeriodWriteService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var period = await db.Set<GlPeriod>().AsNoTracking().SingleAsync(token);
            await periods.UpdateAsync(companyId, period.Id, period.Version, period.StartDate, period.EndDate, true, token);
        }

        var posted = await PostOtherPaymentAsync(provider, claim, true, token);
        Assert.Equal(OtherPaymentPostingOutcomeKind.Posted, posted.Kind);
        var otherExpense = await CreateAccountAsync(provider, "6103", "6", token);
        await SaveOtherPaymentMapAsync(provider, setup.CourierId, otherExpense, token);

        await using var check = provider.CreateAsyncScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var row = await dbCheck.Set<OtherPaymentSource>().AsNoTracking().SingleAsync(token);
        Assert.Equal((setup.ExpenseLedgerId, 1), (row.ExpenseAccountId, row.MappingVersion));
        Assert.Equal(2, (await dbCheck.Set<OtherPaymentAccountMapping>().AsNoTracking().SingleAsync(token)).Version);
        Assert.Equal("6101", (await dbCheck.Set<GlJournalLine>().AsNoTracking().OrderBy(x => x.Sequence).FirstAsync(token)).AccountCode);
        // 已存來源不可改寫（觸發器 51039）。
        await using var raw = database.CreateRawContext();
        var error = await Assert.ThrowsAsync<SqlException>(() => raw.Database.ExecuteSqlAsync(
            $"UPDATE dbo.ap_other_payment_source SET expense_account_id={otherExpense} WHERE id={row.Id}", token));
        Assert.Equal(51039, error.Number);
    }

    [Fact]
    public async Task OtherPaymentPostingNeedsACallerTransactionAndACallerFailureRollsEverythingBack()
    {
        // FI-OP-POST-006（BR-FI-061）：無交易是程式錯誤；過帳後呼叫者（SYS／稽核）失敗回滾 → 來源、傳票、GL 來源鍵、
        // Posted 狀態全部還原，之後以同一主張可再過帳。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup), token));
        var claim = await OtherPaymentClaimAsync(provider, draft.Id, token);

        await using (var scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<IOtherPaymentPostingService>().PostInCurrentTransactionAsync(claim, token));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var result = await sp.GetRequiredService<IOtherPaymentPostingService>().PostInCurrentTransactionAsync(claim, token);
            Assert.Equal(OtherPaymentPostingOutcomeKind.Posted, result.Kind);
            // 呼叫者後續寫入失敗（模擬 SYS 審批記錄違反約束）→ 整筆回滾。
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.ap_other_payment_doc SET amount = -1 WHERE id = {0}", [draft.Id], token));
            await transaction.RollbackAsync(token);
        }
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));
        Assert.Equal(("Draft", 1), await OtherPaymentStateAsync(draft.Id, token));

        var again = await PostOtherPaymentAsync(provider, claim, true, token);
        Assert.Equal((OtherPaymentPostingOutcomeKind.Posted, true), (again.Kind, again.Changed));
        Assert.Equal((1, 1, 2, 1), await OtherPaymentPostingCountsAsync(companyId, token));
    }

    [Fact]
    public async Task TwoConnectionsPostingTheSameOtherPaymentSerializeIntoOnePostingAndOneUnchangedReplay()
    {
        // FI-OP-POST-007（雙連線競態，BR-FI-061／062）：外部連線先持有銀行參考鍵，兩條連線同時以同一主張過帳並在鍵上等待；
        // 釋放後一條 Posted、另一條在鎖後看見已提交來源 → Unchanged，只有一張傳票、一個來源列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "RACE-1"), token));
        var claim = await OtherPaymentClaimAsync(provider, draft.Id, token);

        var (holder, held) = await HoldAppLockAsync(
            OtherPaymentPostingService.BankLockResource(companyId, setup.BankId, "race-1"), token);
        var a = Task.Run(() => PostOtherPaymentAsync(provider, claim, true, token), token);
        var b = Task.Run(() => PostOtherPaymentAsync(provider, claim, true, token), token);
        await Task.WhenAny(Task.WhenAll(a, b), Task.Delay(TimeSpan.FromMilliseconds(700), token));
        Assert.False(a.IsCompleted || b.IsCompleted, "both connections must wait on the bank-reference key");
        await held.RollbackAsync(token);
        await holder.DisposeAsync();

        var results = new[] { await a.WaitAsync(RaceTimeout, token), await b.WaitAsync(RaceTimeout, token) };
        Assert.Equal([OtherPaymentPostingOutcomeKind.Posted, OtherPaymentPostingOutcomeKind.Unchanged],
            results.Select(x => x.Kind).Order().ToArray());
        Assert.Equal(results[0].Receipt, results[1].Receipt);
        Assert.Equal((1, 1, 2, 1), await OtherPaymentPostingCountsAsync(companyId, token));
        Assert.Equal("Posted", (await OtherPaymentStateAsync(draft.Id, token)).Status);
    }

    [Fact]
    public async Task HeldBankReferenceOrSourceKeyTimesOutAs409AndKeepsTheCallerTransaction()
    {
        // FI-OP-POST-008（鎖逾時）：另一交易持有銀行參考鍵或來源鍵 → 409 LOCK_TIMEOUT、呼叫者交易仍開啟、零列；釋放後可過帳。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentPostingProvider(companyId, lockTimeoutMilliseconds: 300);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "LOCK-1"), token));
        var claim = await OtherPaymentClaimAsync(provider, draft.Id, token);

        foreach (var resource in new[]
                 {
                     OtherPaymentPostingService.BankLockResource(companyId, setup.BankId, "LOCK-1"),
                     OtherPaymentPostingService.SourceLockResource(companyId, draft.Id),
                 })
        {
            var (holder, held) = await HoldAppLockAsync(resource, token);
            await using (var scope = provider.CreateAsyncScope())
            {
                var sp = scope.ServiceProvider;
                var db = sp.GetRequiredService<MyErpDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
                AssertOtherPaymentRejected(await sp.GetRequiredService<IOtherPaymentPostingService>()
                    .PostInCurrentTransactionAsync(claim, token), 409, OtherPaymentPostingErrors.LockTimeout);
                Assert.Same(transaction, db.Database.CurrentTransaction);
                await transaction.RollbackAsync(token);
            }
            await held.RollbackAsync(token);
            await holder.DisposeAsync();
        }
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));
        Assert.Equal(OtherPaymentPostingOutcomeKind.Posted,
            (await PostOtherPaymentAsync(provider, claim, true, token)).Kind);
    }
}
