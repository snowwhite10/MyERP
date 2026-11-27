using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-OP-DOC-001～008（WU-FI-008a3）：其他付款 Draft <c>ap_other_payment_doc</c> 與
/// <see cref="OtherPaymentDocumentService"/> 的建立／修改／軟刪除／明細，對真實 SQL。MD 費用類別、資金帳戶與科目為真實種子
/// （經 MD 公開契約核對）；SYS 單號與授權以測試替身注入。Draft 不建立任何 GL、AP 或應付餘額。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static readonly string[] AllOtherPaymentPermissions =
        ["AP.OTHER_PAYMENT.VIEW", "AP.OTHER_PAYMENT.CREATE", "AP.OTHER_PAYMENT.EDIT", "AP.OTHER_PAYMENT.DELETE"];

    private sealed class OtherPaymentActor
    {
        public string[] Permissions = AllOtherPaymentPermissions;
        public int Numbers;
    }

    private sealed class OtherPaymentAuthorization(OtherPaymentActor actor, ICurrentCompany company) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UserCompanyAuthorization?>(new UserCompanyAuthorization(7, company.CompanyId ?? 0, null, null,
                false, actor.Permissions.ToDictionary(x => x, _ => DataScope.Company)));
    }

    private sealed class OtherPaymentNumbers(OtherPaymentActor actor) : INumberGenerator
    {
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default)
        {
            Assert.Equal(OtherPaymentDocumentService.DocumentType, documentType);
            return Task.FromResult($"OP{documentDate:yyyyMM}{Interlocked.Increment(ref actor.Numbers):0000}");
        }
    }

    private sealed record OtherPaymentSetup(long BankId, long UsdBankId, long CourierId, long IncomeId, long RentId,
        long ExpenseLedgerId);

    private ServiceProvider OtherPaymentProvider(long companyId, OtherPaymentActor actor) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(actor);
            services.AddScoped<ICurrentAuthorization, OtherPaymentAuthorization>();
            services.AddScoped<INumberGenerator, OtherPaymentNumbers>();
        });

    /// <summary>收款種子（BANK1／CASHBOX／BANKUSD）＋費用科目 6／6101 與 MD 類別 COURIER（Expense）、SALEINC（Income）、RENT（Expense）。</summary>
    private static async Task<OtherPaymentSetup> SeedOtherPaymentAsync(ServiceProvider provider, long companyId,
        CancellationToken token)
    {
        var collection = await SeedCollectionAsync(provider, companyId, token, withMap: false);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<AccountService>();
        var expenses = await accounts.CreateAsync("6", "Expenses", null, AccountType.Expense, null, token);
        var courierLedger = await accounts.CreateAsync("6101", "運費", expenses.Id, null, null, token);
        var categories = sp.GetRequiredService<IncomeExpenseCategoryService>();
        var courier = await categories.CreateAsync("COURIER", "快遞費", IncomeExpenseDirection.Expense, courierLedger.Id, token);
        var income = await categories.CreateAsync("SALEINC", "雜項收入", IncomeExpenseDirection.Income, null, token);
        var rent = await categories.CreateAsync("RENT", "租金", IncomeExpenseDirection.Expense, null, token);
        return new(collection.BankId, collection.UsdBankId, courier.Id, income.Id, rent.Id, courierLedger.Id);
    }

    private static OtherPaymentDocumentInput OtherPaymentInput(OtherPaymentSetup setup, string? reference = "WD-0001",
        decimal amount = 300m, string? payee = "順豐速運", long? categoryId = null, long? bankId = null,
        string? remark = null) =>
        new(payee, categoryId ?? setup.CourierId, bankId ?? setup.BankId, reference, BusinessDate, amount, remark);

    private static async Task<T> OtherPaymentAsync<T>(ServiceProvider provider,
        Func<OtherPaymentDocumentService, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<OtherPaymentDocumentService>());
    }

    private static async Task OtherPaymentAsync(ServiceProvider provider, Func<OtherPaymentDocumentService, Task> action)
    {
        await using var scope = provider.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<OtherPaymentDocumentService>());
    }

    private async Task<int> OtherPaymentCountAsync(long companyId, bool includeDeleted = false)
    {
        await using var db = database.CreateRawContext();
        var deleted = includeDeleted ? 1 : 0;
        return await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ap_other_payment_doc WHERE company_id={companyId} AND is_deleted <= {deleted}")
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task SetOtherPaymentStatusAsync(long id, string status)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "UPDATE dbo.ap_other_payment_doc SET status=@s, version=version+1 WHERE id=@id", connection);
        command.Parameters.AddWithValue("@s", status);
        command.Parameters.AddWithValue("@id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OtherPaymentDraftIsSavedWithBaseCurrencyNumberTrimmedTextLabelsAndNoLedgerEffect()
    {
        // FI-OP-DOC-001（正常）：Draft、版本 1、單號經 SYS 在交易內取得、幣別取 MD 本位幣、收款對象／參考／備註修剪、
        // 明細帶 MD 類別與資金帳戶標籤；Draft 不產生 GL 分錄、過帳來源或任何應付（FI-AP-OTHER-001）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentProvider(companyId, new OtherPaymentActor());
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);

        var created = await OtherPaymentAsync(provider, s => s.CreateAsync(
            OtherPaymentInput(setup, "  WD-0001 ", 1234.5678m, "  順豐速運 ", remark: "  十一月運費 "), token));

        Assert.Equal(("OP2026110001", OtherPaymentDocumentStatus.Draft, 1, "TWD", 1234.5678m, "WD-0001", "順豐速運", "十一月運費"),
            (created.DocumentNo, created.Status, created.Version, created.Currency, created.Amount, created.BankReference,
                created.PayeeDescription, created.Remark));
        Assert.Equal((setup.CourierId, "COURIER", "快遞費", setup.BankId, "BANK1", "Mega TWD", BusinessDate),
            (created.ExpenseCategoryId, created.ExpenseCategoryCode, created.ExpenseCategoryName, created.BankAccountId,
                created.BankAccountCode, created.BankAccountName, created.BusinessDate));
        Assert.Equal((7L, (long?)null, (long?)null), (created.CreatedBy, created.UpdatedBy, created.JournalId));
        Assert.Equal(created, await OtherPaymentAsync(provider, s => s.GetAsync(created.Id, token)));

        var noLedger = await OtherPaymentAsync(provider, s => s.CreateAsync(
            OtherPaymentInput(setup, "WD-0002", categoryId: setup.RentId, remark: "   "), token));
        Assert.Equal(("OP2026110002", (string?)null), (noLedger.DocumentNo, noLedger.Remark));
        Assert.Equal(2, await OtherPaymentCountAsync(companyId));
        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
        await using var db = database.CreateRawContext();
        Assert.Equal(0, await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ap_other_payment_source WHERE company_id={companyId}").SingleAsync(token));
    }

    [Fact]
    public async Task InvalidOtherPaymentFieldsAre400WithEveryFieldAndSaveNothing()
    {
        // FI-OP-DOC-002（邊界）：收款對象、類別、資金帳戶、參考、日期、金額、備註逐欄位回報；0、負、5 位小數、溢位、
        // 201 字收款對象、51 字／控制字元參考各自 400；不取號、不存列。最大金額與 50 字參考／200 字收款對象精確保存。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = new OtherPaymentActor();
        await using var provider = OtherPaymentProvider(companyId, actor);
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);

        var all = await Assert.ThrowsAsync<RequestValidationException>(() => OtherPaymentAsync(provider, s =>
            s.CreateAsync(new("  ", 0, 0, " ", default, 0m, new string('x', 501)), token)));
        Assert.Equal(["amount", "bankAccountId", "bankReference", "businessDate", "expenseCategoryId", "payeeDescription",
                "remark"], all.Errors.Keys.Order(StringComparer.Ordinal));

        foreach (var input in new[]
                 {
                     OtherPaymentInput(setup, amount: -1m), OtherPaymentInput(setup, amount: 0.00001m),
                     OtherPaymentInput(setup, amount: 1_000_000_000_000_000m),
                     OtherPaymentInput(setup, payee: new string('P', 201)), OtherPaymentInput(setup, payee: null),
                     OtherPaymentInput(setup, reference: new string('R', 51)), OtherPaymentInput(setup, reference: "R\u0001X"),
                     OtherPaymentInput(setup, reference: null),
                 })
        {
            var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
                OtherPaymentAsync(provider, s => s.CreateAsync(input, token)));
            Assert.Single(error.Errors);
        }
        Assert.Equal(0, actor.Numbers);
        Assert.Equal(0, await OtherPaymentCountAsync(companyId, includeDeleted: true));

        var max = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, new string('R', 50),
            999_999_999_999_999.9999m, new string('P', 200)), token));
        await using var db = database.CreateRawContext();
        Assert.Equal("999999999999999.9999", await db.Database.SqlQuery<string>(
            $"SELECT CONVERT(varchar(40), amount) AS [Value] FROM dbo.ap_other_payment_doc WHERE id={max.Id}").SingleAsync(token));
        Assert.Equal((200, 50), (max.PayeeDescription.Length, max.BankReference.Length));
    }

    [Fact]
    public async Task OtherPaymentCategoryAndFundAccountAreCheckedThroughMasterDataContracts()
    {
        // FI-OP-DOC-003（關聯）：類別不存在／屬其他公司 422 NOT_FOUND、收入類別 422 NOT_EXPENSE、停用 422 INACTIVE；
        // 資金帳戶不存在／其他公司 422 NOT_FOUND、USD 422 CURRENCY_MISMATCH、停用 422 INACTIVE；全部不存列（FI-AP-OTHER-015）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentProvider(companyId, new OtherPaymentActor());
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        var otherCompany = NewCompany();
        await using var other = OtherPaymentProvider(otherCompany, new OtherPaymentActor());
        var otherSetup = await SeedOtherPaymentAsync(other, otherCompany, token);

        async Task Expect(string code, long? categoryId = null, long? bankId = null)
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => OtherPaymentAsync(provider, s =>
                s.CreateAsync(OtherPaymentInput(setup, categoryId: categoryId, bankId: bankId), token)));
            Assert.Equal(code, error.ErrorCode);
        }

        await Expect(OtherPaymentDocumentErrors.ExpenseCategoryNotFound, categoryId: 999_999_999);
        await Expect(OtherPaymentDocumentErrors.ExpenseCategoryNotFound, categoryId: otherSetup.CourierId);
        await Expect(OtherPaymentDocumentErrors.ExpenseCategoryNotExpense, categoryId: setup.IncomeId);
        await Expect(OtherPaymentDocumentErrors.FundAccountNotFound, bankId: 999_999_999);
        await Expect(OtherPaymentDocumentErrors.FundAccountNotFound, bankId: otherSetup.BankId);
        await Expect(OtherPaymentDocumentErrors.FundAccountCurrencyMismatch, bankId: setup.UsdBankId);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IncomeExpenseCategoryService>()
                .UpdateAsync(setup.RentId, "租金", null, isActive: false, version: 1, token);
        await Expect(OtherPaymentDocumentErrors.ExpenseCategoryInactive, categoryId: setup.RentId);
        var bank = await FundAsync(provider, setup.BankId, token);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FundAccountService>().UpdateAsync(bank.Id, bank.Name,
                new(FundAccountType.Bank, "Mega", "001", bank.LedgerAccountId), isActive: false, bank.Version, token);
        await Expect(OtherPaymentDocumentErrors.FundAccountInactive);
        Assert.Equal(0, await OtherPaymentCountAsync(companyId, includeDeleted: true));
    }

    [Fact]
    public async Task WithdrawalReferenceIsUniquePerBankAmongLiveDraftsIgnoringCase()
    {
        // FI-OP-DOC-004（重複／已刪除）：同公司同資金帳戶的未刪除 Draft 不得重複（不分大小寫）409；不同帳戶、其他公司可同參考；
        // 修改自己保留原參考不算重複；軟刪除 Draft 後參考釋放可再用（FI-AP-OTHER-006）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentProvider(companyId, new OtherPaymentActor());
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        var first = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WD-DUP"), token));

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => OtherPaymentAsync(provider, s =>
            s.CreateAsync(OtherPaymentInput(setup, "wd-dup"), token)));
        Assert.Equal(OtherPaymentDocumentErrors.BankReferenceDuplicate, duplicate.ErrorCode);
        var cash = await FundIdAsync(provider, "CASHBOX", token);
        var second = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WD-DUP", bankId: cash), token));
        var moved = await Assert.ThrowsAsync<ConflictException>(() => OtherPaymentAsync(provider, s =>
            s.ReplaceAsync(second.Id, second.Version, OtherPaymentInput(setup, "WD-DUP"), token)));
        Assert.Equal(OtherPaymentDocumentErrors.BankReferenceDuplicate, moved.ErrorCode);

        var kept = await OtherPaymentAsync(provider, s => s.ReplaceAsync(first.Id, first.Version,
            OtherPaymentInput(setup, "WD-DUP", 301m), token));
        Assert.Equal((2, 301m), (kept.Version, kept.Amount));
        await OtherPaymentAsync(provider, s => s.DeleteAsync(first.Id, kept.Version, token));
        var reused = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "wd-dup"), token));
        Assert.Equal("wd-dup", reused.BankReference);

        var otherCompany = NewCompany();
        await using var other = OtherPaymentProvider(otherCompany, new OtherPaymentActor());
        var otherSetup = await SeedOtherPaymentAsync(other, otherCompany, token);
        await OtherPaymentAsync(other, s => s.CreateAsync(OtherPaymentInput(otherSetup, "WD-DUP"), token));
        Assert.Equal(2, await OtherPaymentCountAsync(companyId));
        Assert.Equal(3, await OtherPaymentCountAsync(companyId, includeDeleted: true));
    }

    [Fact]
    public async Task PostedSourceKeepsWithdrawalReferenceReservedAfterItsDraftRowIsReleased()
    {
        // FI-OP-DOC-005（重複／已刪除歷史）：已過帳來源 ap_other_payment_source 保留的參考（即使其 Draft 列已釋放）在建立與修改時
        // 均 409；SQL 觸發器 51039 與唯一索引衝突（不經預先檢查的並行寫入）都轉為同一具名 409（FI-AP-OTHER-006/019）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentProvider(companyId, new OtherPaymentActor());
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        var posted = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WD-HIST", 10m), token));
        await using (var db = database.CreateRawContext())
        {
            var period = await db.Database.SqlQuery<long>(
                $"SELECT id AS [Value] FROM dbo.gl_period WHERE company_id={companyId}").SingleAsync(token);
            await db.Database.ExecuteSqlAsync(
                $"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({companyId},'FI','OtherPayment',{posted.Id},1,'fp','2026-11-06',1,1)", token);
            var key = await db.Database.SqlQuery<long>(
                $"SELECT id AS [Value] FROM dbo.gl_source_key WHERE company_id={companyId} AND source_type='OtherPayment'").SingleAsync(token);
            await db.Database.ExecuteSqlAsync(
                $"INSERT dbo.gl_journal (company_id,period_id,source_key_id,business_date,base_currency,status,total_debit,total_credit,created_at,created_by,version) VALUES ({companyId},{period},{key},'2026-11-06','TWD','Posted',10,10,'2026-11-06',1,1)", token);
            var journal = await db.Database.SqlQuery<long>(
                $"SELECT id AS [Value] FROM dbo.gl_journal WHERE company_id={companyId} AND source_key_id={key}").SingleAsync(token);
            await db.Database.ExecuteSqlAsync(
                $"INSERT dbo.ap_other_payment_source (company_id,source_type,source_id,source_version,fingerprint,gl_journal_id,business_date,expense_category_id,expense_account_id,mapping_version,bank_account_id,bank_reference,base_currency,amount,created_at,created_by,version) VALUES ({companyId},'OtherPayment',{posted.Id},1,'sha256:fp',{journal},'2026-11-06',{setup.CourierId},{setup.ExpenseLedgerId},1,{setup.BankId},N'WD-HIST','TWD',CAST(10 AS decimal(19,4)),'2026-11-06',1,1)", token);
            // 模擬 Draft 列已釋放的歷史：未刪除篩選唯一索引不再保護此參考，只剩過帳來源。
            await db.Database.ExecuteSqlAsync(
                $"UPDATE dbo.ap_other_payment_doc SET is_deleted=1, deleted_at=SYSDATETIME() WHERE id={posted.Id}", token);
        }

        var create = await Assert.ThrowsAsync<ConflictException>(() => OtherPaymentAsync(provider, s =>
            s.CreateAsync(OtherPaymentInput(setup, "wd-hist"), token)));
        Assert.Equal(OtherPaymentDocumentErrors.BankReferenceDuplicate, create.ErrorCode);
        var free = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WD-FREE"), token));
        var edit = await Assert.ThrowsAsync<ConflictException>(() => OtherPaymentAsync(provider, s =>
            s.ReplaceAsync(free.Id, free.Version, OtherPaymentInput(setup, "WD-HIST"), token)));
        Assert.Equal(OtherPaymentDocumentErrors.BankReferenceDuplicate, edit.ErrorCode);
        Assert.Equal(1, await OtherPaymentCountAsync(companyId));

        // 並行寫入繞過預先檢查時由 SQL 決定：觸發器 51039（過帳來源）與篩選唯一索引 2601（未刪除 Draft）。
        foreach (var (reference, number) in new[] { ("WD-HIST", 51039), ("WD-FREE", 2601) })
        {
            await using var raw = database.CreateRawContext();
            var sql = await Assert.ThrowsAsync<SqlException>(() => raw.Database.ExecuteSqlAsync(
                $"INSERT INTO dbo.ap_other_payment_doc (document_no,payee_description,expense_category_id,bank_account_id,bank_reference,business_date,currency,amount,status,is_deleted,company_id,created_at,created_by,version) VALUES ('RACE-1',N'Race',{setup.CourierId},{setup.BankId},{reference},'2026-11-06','TWD',CAST(1 AS decimal(19,4)),'Draft',0,{companyId},SYSDATETIME(),1,1)", token));
            Assert.Equal(number, sql.Number);
            var translated = OtherPaymentDocumentService.Translate(new DbUpdateException("race", sql));
            Assert.Equal(OtherPaymentDocumentErrors.BankReferenceDuplicate,
                Assert.IsType<ConflictException>(translated).ErrorCode);
        }
        Assert.IsType<ConflictException>(OtherPaymentDocumentService.Translate(new DbUpdateConcurrencyException("stale")));
    }

    [Fact]
    public async Task OnlyCurrentDraftOrRejectedOtherPaymentsCanBeEditedOrDeleted()
    {
        // FI-OP-DOC-006（狀態／並行）：舊版本 409；Pending／Approved／Posted 修改與刪除 422 INVALID_STATUS；Rejected 修改後回 Draft
        // 版本遞增，Rejected 可軟刪除；軟刪除後讀取／修改 404，列仍保留 deleted_at（FI-AP-OTHER-008/013）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = OtherPaymentProvider(companyId, new OtherPaymentActor());
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        var doc = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WD-S1"), token));

        await Assert.ThrowsAsync<ConflictException>(() => OtherPaymentAsync(provider, s =>
            s.ReplaceAsync(doc.Id, 2, OtherPaymentInput(setup, "WD-S1"), token)));
        await Assert.ThrowsAsync<ConflictException>(() => OtherPaymentAsync(provider, s => s.DeleteAsync(doc.Id, 0, token)));

        var version = doc.Version;
        foreach (var status in new[] { "Pending", "Approved", "Posted" })
        {
            await SetOtherPaymentStatusAsync(doc.Id, status);
            version++;
            var edit = await Assert.ThrowsAsync<BusinessRuleException>(() => OtherPaymentAsync(provider, s =>
                s.ReplaceAsync(doc.Id, version, OtherPaymentInput(setup, "WD-S1"), token)));
            var delete = await Assert.ThrowsAsync<BusinessRuleException>(() => OtherPaymentAsync(provider, s =>
                s.DeleteAsync(doc.Id, version, token)));
            Assert.Equal((OtherPaymentDocumentErrors.InvalidStatus, OtherPaymentDocumentErrors.InvalidStatus),
                (edit.ErrorCode, delete.ErrorCode));
        }

        await SetOtherPaymentStatusAsync(doc.Id, "Rejected");
        version++;
        var revised = await OtherPaymentAsync(provider, s => s.ReplaceAsync(doc.Id, version,
            OtherPaymentInput(setup, "WD-S1", 88.5m, "黑貓宅急便", remark: "改送"), token));
        Assert.Equal((OtherPaymentDocumentStatus.Draft, version + 1, 88.5m, "黑貓宅急便", "改送", 7L),
            (revised.Status, revised.Version, revised.Amount, revised.PayeeDescription, revised.Remark, revised.UpdatedBy));
        var unchanged = await OtherPaymentAsync(provider, s => s.ReplaceAsync(doc.Id, revised.Version,
            OtherPaymentInput(setup, "WD-S1", 88.5m, "黑貓宅急便", remark: "改送"), token));
        Assert.Equal(revised.Version + 1, unchanged.Version);

        await SetOtherPaymentStatusAsync(doc.Id, "Rejected");
        await OtherPaymentAsync(provider, s => s.DeleteAsync(doc.Id, unchanged.Version + 1, token));
        Assert.Equal(OtherPaymentDocumentErrors.NotFound, (await Assert.ThrowsAsync<NotFoundException>(() =>
            OtherPaymentAsync(provider, s => s.GetAsync(doc.Id, token)))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => OtherPaymentAsync(provider, s =>
            s.ReplaceAsync(doc.Id, unchanged.Version + 2, OtherPaymentInput(setup, "WD-S1"), token)));
        await using var db = database.CreateRawContext();
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ap_other_payment_doc WHERE id={doc.Id} AND is_deleted=1 AND deleted_at IS NOT NULL")
            .SingleAsync(token));
    }

    [Fact]
    public async Task OtherPaymentActionsNeedTheirOwnPermissionAndHideOtherCompanyDocuments()
    {
        // FI-OP-DOC-007（權限）：只有 VIEW 時建立／修改／刪除 403；無 VIEW 讀取 403；其他公司的單據以 404 隱藏（FI-AP-OTHER-011/012）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = new OtherPaymentActor();
        await using var provider = OtherPaymentProvider(companyId, actor);
        var setup = await SeedOtherPaymentAsync(provider, companyId, token);
        var doc = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WD-P1"), token));

        actor.Permissions = ["AP.OTHER_PAYMENT.VIEW"];
        await Assert.ThrowsAsync<ForbiddenException>(() => OtherPaymentAsync(provider, s =>
            s.CreateAsync(OtherPaymentInput(setup, "WD-P2"), token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => OtherPaymentAsync(provider, s =>
            s.ReplaceAsync(doc.Id, doc.Version, OtherPaymentInput(setup, "WD-P1"), token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => OtherPaymentAsync(provider, s => s.DeleteAsync(doc.Id, doc.Version, token)));
        Assert.Equal(doc.Id, (await OtherPaymentAsync(provider, s => s.GetAsync(doc.Id, token))).Id);
        actor.Permissions = ["AP.OTHER_PAYMENT.CREATE"];
        await Assert.ThrowsAsync<ForbiddenException>(() => OtherPaymentAsync(provider, s => s.GetAsync(doc.Id, token)));

        var otherCompany = NewCompany();
        await using var other = OtherPaymentProvider(otherCompany, new OtherPaymentActor());
        var otherSetup = await SeedOtherPaymentAsync(other, otherCompany, token);
        await Assert.ThrowsAsync<NotFoundException>(() => OtherPaymentAsync(other, s => s.GetAsync(doc.Id, token)));
        await Assert.ThrowsAsync<NotFoundException>(() => OtherPaymentAsync(other, s =>
            s.ReplaceAsync(doc.Id, doc.Version, OtherPaymentInput(otherSetup, "WD-P1"), token)));
        await Assert.ThrowsAsync<NotFoundException>(() => OtherPaymentAsync(other, s => s.DeleteAsync(doc.Id, doc.Version, token)));
        Assert.Equal(1, await OtherPaymentCountAsync(companyId));
    }
}
