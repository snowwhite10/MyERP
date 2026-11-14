using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-COLL-DOC-001～011（WU-FI-005b1，ADR-126）：FI 收款單據草稿 <c>ar_collection_receipt_doc</c> 與
/// <see cref="CollectionReceiptDocumentService"/> 的建立／修改／刪除／明細，對真實 SQL。MD 資金帳戶與科目為真實種子；
/// MD 客戶查詢、SYS 單號與授權以測試替身注入（各自在 MD／SYS 測試已驗）。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private const long UnapprovedCustomerId = 899;
    private static readonly string[] AllReceiptDocPermissions =
        ["AR.COLLECTION.VIEW", "AR.COLLECTION.CREATE", "AR.COLLECTION.EDIT", "AR.COLLECTION.DELETE"];

    private sealed class ReceiptDocActor
    {
        public string[] Permissions = AllReceiptDocPermissions;
        public long? AuthorizedCompany;
        public bool FailNumbering;
        public int Numbers;
    }

    private sealed class ReceiptDocAuthorization(ReceiptDocActor actor, ICurrentCompany company) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UserCompanyAuthorization?>(new UserCompanyAuthorization(7,
                actor.AuthorizedCompany ?? company.CompanyId ?? 0, null, null, false,
                actor.Permissions.ToDictionary(x => x, _ => DataScope.Company)));
    }

    private sealed class ReceiptDocCustomers : ICustomerLookup
    {
        public Task<CustomerIdentity> RequireApprovedAsync(long id, CancellationToken token = default) =>
            id == UnapprovedCustomerId
                ? throw new BusinessRuleException("MD.CUSTOMER.NOT_APPROVED", "Customer is not approved and active.")
                : Task.FromResult(new CustomerIdentity(id, "C" + id, "Customer " + id));
    }

    /// <summary>單號替身：在呼叫者交易內取號（與 SYS 相同），可模擬規則不存在的 422。</summary>
    private sealed class ReceiptDocNumbers(ReceiptDocActor actor) : INumberGenerator
    {
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default)
        {
            Assert.Equal(CollectionReceiptDocumentService.DocumentType, documentType);
            if (actor.FailNumbering)
                throw new BusinessRuleException("SYS.NUMBER_RULE.NOT_FOUND", "Number rule is missing.");
            return Task.FromResult($"RC{documentDate:yyyyMM}{Interlocked.Increment(ref actor.Numbers):0000}");
        }
    }

    private ServiceProvider ReceiptDocProvider(long companyId, ReceiptDocActor actor, FakeCollectionReceipts? receipts = null) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(actor);
            services.AddSingleton(receipts ?? new FakeCollectionReceipts());
            services.AddScoped<IFinanceCollectionReceiptSource, FakeCollectionReceiptSource>();
            services.AddScoped<ICurrentAuthorization, ReceiptDocAuthorization>();
            services.AddScoped<ICustomerLookup, ReceiptDocCustomers>();
            services.AddScoped<INumberGenerator, ReceiptDocNumbers>();
        });

    private static CollectionReceiptDocumentInput ReceiptDocInput(long bankId, string? reference = "TT-0001",
        decimal amount = 500m, long customerId = CollectionCustomerId, string? remark = null) =>
        new(customerId, BusinessDate, amount, bankId, reference, remark);

    private static async Task<T> ReceiptDocAsync<T>(ServiceProvider provider,
        Func<CollectionReceiptDocumentService, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CollectionReceiptDocumentService>());
    }

    private static async Task ReceiptDocAsync(ServiceProvider provider, Func<CollectionReceiptDocumentService, Task> action)
    {
        await using var scope = provider.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<CollectionReceiptDocumentService>());
    }

    private async Task<int> ReceiptDocCountAsync(long companyId, bool includeDeleted = false)
    {
        await using var db = database.CreateRawContext();
        var sql = includeDeleted
            ? $"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_receipt_doc WHERE company_id={companyId}"
            : $"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_receipt_doc WHERE company_id={companyId} AND is_deleted=0";
        return await db.Database.SqlQueryRaw<int>(sql).SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task SetReceiptDocStatusAsync(long id, string status)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "UPDATE dbo.ar_collection_receipt_doc SET status=@s, version=version+1 WHERE id=@id", connection);
        command.Parameters.AddWithValue("@s", status);
        command.Parameters.AddWithValue("@id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReceiptDraftIsSavedWithServerBaseCurrencyNumberTrimmedTextAndLabels()
    {
        // FI-COLL-DOC-001（正常）：Draft、版本 1、單號在交易內取得、幣別取 MD 本位幣、參考與備註修剪、明細帶 MD 標籤；
        // 無對應科目的現金帳戶也可存草稿（過帳時才檢查科目，BR-FI-033）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = ReceiptDocProvider(companyId, new ReceiptDocActor());
        var setup = await SeedCollectionAsync(provider, companyId, token);

        var created = await ReceiptDocAsync(provider, s => s.CreateAsync(
            ReceiptDocInput(setup.BankId, "  TT-0001 ", 1234.5678m, remark: "  首期款 "), token));

        Assert.Equal(("RC2026110001", FinanceCollectionReceiptState.Draft, 1, "TWD", 1234.5678m, "TT-0001", "首期款"),
            (created.DocumentNo, created.Status, created.Version, created.Currency, created.Amount, created.BankReference,
                created.Remark));
        Assert.Equal((CollectionCustomerId, BusinessDate, setup.BankId, "BANK1", "Mega TWD"),
            (created.CustomerId, created.BusinessDate, created.BankAccountId, created.BankAccountCode, created.BankAccountName));
        Assert.Equal((7L, (long?)null), (created.CreatedBy, created.UpdatedBy));

        var read = await ReceiptDocAsync(provider, s => s.GetAsync(created.Id, token));
        Assert.Equal(created with { CustomerCode = read.CustomerCode, CustomerName = read.CustomerName }, read);
        var cash = await ReceiptDocAsync(provider, s => s.CreateAsync(
            ReceiptDocInput(setup.CashBoxId, "CASH-1", remark: "   "), token));
        Assert.Equal(("RC2026110002", (string?)null), (cash.DocumentNo, cash.Remark));
        Assert.Equal(2, await ReceiptDocCountAsync(companyId));
    }

    [Fact]
    public async Task InvalidReceiptFieldsAre400WithEveryFieldAndSaveNothing()
    {
        // FI-COLL-DOC-002（欄位邊界）：客戶、日期、金額（0、負、5 位小數、溢位）、資金帳戶、參考（空白、超長、控制字元）、
        // 備註超長逐欄位回報；不取號、不存列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = new ReceiptDocActor();
        await using var provider = ReceiptDocProvider(companyId, actor);
        await SeedCollectionAsync(provider, companyId, token);

        var all = await Assert.ThrowsAsync<RequestValidationException>(() => ReceiptDocAsync(provider, s =>
            s.CreateAsync(new(0, default, 0m, 0, "  ", new string('x', 501)), token)));
        Assert.Equal(["amount", "bankAccountId", "bankReference", "businessDate", "customerId", "remark"],
            all.Errors.Keys.Order(StringComparer.Ordinal));

        foreach (var (amount, reference) in new[]
                 {
                     (-1m, "R1"), (0.00001m, "R1"), (1_000_000_000_000_000m, "R1"),
                     (1m, new string('R', 51)), (1m, "R\u0001X"), (1m, null),
                 })
        {
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => ReceiptDocAsync(provider, s =>
                s.CreateAsync(new(CollectionCustomerId, BusinessDate, amount, 5, reference, null), token)));
            Assert.Single(error.Errors);
        }
        Assert.Equal(0, actor.Numbers);
        Assert.Equal(0, await ReceiptDocCountAsync(companyId, includeDeleted: true));
        var bankId = await FundIdAsync(provider, "BANK1", token);
        var max = await ReceiptDocAsync(provider, s => s.CreateAsync(
            ReceiptDocInput(bankId, new string('R', 50), 999_999_999_999_999.9999m), token));
        Assert.Equal(999_999_999_999_999.9999m, max.Amount);
    }

    private static async Task<long> FundIdAsync(ServiceProvider provider, string code, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<FundAccount>().AsNoTracking()
            .Where(x => x.Code == code).Select(x => x.Id).SingleAsync(token);
    }

    [Fact]
    public async Task ReceiptFundAccountMustExistBeActiveAndUseTheBaseCurrency()
    {
        // FI-COLL-DOC-003（關聯）：不存在／其他公司 422 NOT_FOUND、USD 帳戶 422 CURRENCY_MISMATCH、停用 422 INACTIVE；
        // 未核准客戶由 MD 拒絕；全部不存列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = ReceiptDocProvider(companyId, new ReceiptDocActor());
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var otherCompany = NewCompany();
        await using var other = ReceiptDocProvider(otherCompany, new ReceiptDocActor());
        var otherSetup = await SeedCollectionAsync(other, otherCompany, token, withMap: false);

        async Task Expect(long bankId, string code, long customerId = CollectionCustomerId)
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ReceiptDocAsync(provider, s =>
                s.CreateAsync(ReceiptDocInput(bankId, customerId: customerId), token)));
            Assert.Equal(code, error.ErrorCode);
        }

        await Expect(999_999_999, CollectionReceiptDocumentErrors.FundAccountNotFound);
        await Expect(otherSetup.BankId, CollectionReceiptDocumentErrors.FundAccountNotFound);
        await Expect(setup.UsdBankId, CollectionReceiptDocumentErrors.FundAccountCurrencyMismatch);
        await Expect(setup.BankId, "MD.CUSTOMER.NOT_APPROVED", UnapprovedCustomerId);
        var bank = await FundAsync(provider, setup.BankId, token);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FundAccountService>().UpdateAsync(bank.Id, bank.Name,
                new(FundAccountType.Bank, "Mega", "001", setup.BankLedgerId), isActive: false, bank.Version, token);
        await Expect(setup.BankId, CollectionReceiptDocumentErrors.FundAccountInactive);
        Assert.Equal(0, await ReceiptDocCountAsync(companyId, includeDeleted: true));
    }

    [Fact]
    public async Task BankReferenceIsUniquePerBankAmongLiveDraftsAndPostedReceiptsIgnoringCase()
    {
        // FI-COLL-DOC-004（重複代碼／已刪除記錄）：同公司同資金帳戶的未刪除單據與已過帳收款不得重複（不分大小寫）409；
        // 不同帳戶可同參考；刪除後參考可再用；修改自己保留原參考不算重複。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = ReceiptDocProvider(companyId, new ReceiptDocActor(), receipts);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var first = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "REF-A"), token));

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => ReceiptDocAsync(provider, s =>
            s.CreateAsync(ReceiptDocInput(setup.BankId, "ref-a"), token)));
        Assert.Equal(CollectionReceiptDocumentErrors.BankReferenceDuplicate, duplicate.ErrorCode);
        await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.CashBoxId, "REF-A"), token));
        var same = await ReceiptDocAsync(provider, s => s.ReplaceAsync(first.Id, first.Version,
            ReceiptDocInput(setup.BankId, "REF-A", 600m), token));
        Assert.Equal((2, 600m), (same.Version, same.Amount));

        await ReceiptDocAsync(provider, s => s.DeleteAsync(first.Id, same.Version, token));
        var reused = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "Ref-A"), token));
        Assert.Equal("Ref-A", reused.BankReference);

        // 已過帳收款（005a2b 服務寫入 ar_collection_receipt）佔用的參考同樣 409。
        receipts.Items[2301] = CollectionReceipt(companyId, 2301, setup.BankId, reference: "TT-POSTED");
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 2301), commit: true, token)).Kind);
        var posted = await Assert.ThrowsAsync<ConflictException>(() => ReceiptDocAsync(provider, s =>
            s.CreateAsync(ReceiptDocInput(setup.BankId, "tt-posted"), token)));
        Assert.Equal(CollectionReceiptDocumentErrors.BankReferenceDuplicate, posted.ErrorCode);
        Assert.Equal(2, await ReceiptDocCountAsync(companyId));
    }

    [Fact]
    public async Task LiveDuplicateBankReferenceIsRejectedBySqlEvenWithoutTheServiceCheck()
    {
        // FI-COLL-DOC-005（併發守衛）：過濾唯一索引在 SQL 層拒絕兩張未刪除單據同帳戶同參考（不分大小寫），
        // 已刪除列不佔用；刪除列狀態必須為 Draft／Rejected 且有刪除時間。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = ReceiptDocProvider(companyId, new ReceiptDocActor());
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var first = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "DUP-1"), token));

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(token);
        async Task<int> CopyAsync(string reference)
        {
            await using var command = new SqlCommand($"""
                INSERT dbo.ar_collection_receipt_doc (company_id, document_no, customer_id, business_date, currency, amount,
                    bank_account_id, bank_reference, remark, status, is_deleted, deleted_at, custom_fields,
                    created_at, created_by, version)
                SELECT company_id, document_no + 'X', customer_id, business_date, currency, amount, bank_account_id,
                    @r, remark, status, 0, NULL, custom_fields, created_at, created_by, 1
                FROM dbo.ar_collection_receipt_doc WHERE id = @id
                """, connection);
            command.Parameters.AddWithValue("@r", reference);
            command.Parameters.AddWithValue("@id", first.Id);
            return await command.ExecuteNonQueryAsync(token);
        }

        var unique = await Assert.ThrowsAsync<SqlException>(() => CopyAsync("dup-1"));
        Assert.True(unique.Number is 2601 or 2627, unique.Message);
        await using (var bad = new SqlCommand(
            "UPDATE dbo.ar_collection_receipt_doc SET is_deleted = 1 WHERE id = @id", connection))
        {
            bad.Parameters.AddWithValue("@id", first.Id);
            var check = await Assert.ThrowsAsync<SqlException>(() => bad.ExecuteNonQueryAsync(token));
            Assert.Equal(547, check.Number);
        }
        await using (var json = new SqlCommand(
            "UPDATE dbo.ar_collection_receipt_doc SET custom_fields = N'not json' WHERE id = @id", connection))
        {
            json.Parameters.AddWithValue("@id", first.Id);
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => json.ExecuteNonQueryAsync(token))).Number);
        }
        await ReceiptDocAsync(provider, s => s.DeleteAsync(first.Id, first.Version, token));
        Assert.Equal(1, await CopyAsync("DUP-1"));
    }

    [Fact]
    public async Task ReplaceUsesVersionAndOnlyDraftOrRejectedWhichReturnsToDraft()
    {
        // FI-COLL-DOC-006（狀態限制／版本）：舊版本 409 不改列；Rejected 修改後回 Draft；Pending／Approved／Posted 422；
        // 零變更修改仍遞增版本（草稿保存，不是過帳結果）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = ReceiptDocProvider(companyId, new ReceiptDocActor());
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var created = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "E-1"), token));

        var updated = await ReceiptDocAsync(provider, s => s.ReplaceAsync(created.Id, 1,
            ReceiptDocInput(setup.CashBoxId, "E-2", 88.12m, 802, "改帳戶"), token));
        Assert.Equal((2, 802L, setup.CashBoxId, "E-2", 88.12m, "改帳戶", (long?)7),
            (updated.Version, updated.CustomerId, updated.BankAccountId, updated.BankReference, updated.Amount,
                updated.Remark, updated.UpdatedBy));
        Assert.Equal(created.DocumentNo, updated.DocumentNo);
        var stale = await Assert.ThrowsAsync<ConflictException>(() => ReceiptDocAsync(provider, s =>
            s.ReplaceAsync(created.Id, 1, ReceiptDocInput(setup.BankId, "E-3"), token)));
        Assert.Equal(CommonErrorCodes.ConcurrencyConflict, stale.ErrorCode);
        Assert.Equal("E-2", (await ReceiptDocAsync(provider, s => s.GetAsync(created.Id, token))).BankReference);

        await SetReceiptDocStatusAsync(created.Id, "Rejected");
        var redraft = await ReceiptDocAsync(provider, s => s.ReplaceAsync(created.Id, 3,
            ReceiptDocInput(setup.BankId, "E-4"), token));
        Assert.Equal((FinanceCollectionReceiptState.Draft, 4), (redraft.Status, redraft.Version));

        foreach (var status in new[] { "Pending", "Approved", "Posted" })
        {
            await SetReceiptDocStatusAsync(created.Id, status);
            var version = (await ReceiptDocAsync(provider, s => s.GetAsync(created.Id, token))).Version;
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ReceiptDocAsync(provider, s =>
                s.ReplaceAsync(created.Id, version, ReceiptDocInput(setup.BankId, "E-5"), token)));
            Assert.Equal(CollectionReceiptDocumentErrors.InvalidStatus, error.ErrorCode);
            Assert.Equal("E-4", (await ReceiptDocAsync(provider, s => s.GetAsync(created.Id, token))).BankReference);
        }
    }

    [Fact]
    public async Task DeleteSoftDeletesDraftOrRejectedByVersionAndHidesTheDocument()
    {
        // FI-COLL-DOC-007（狀態限制／已刪除記錄）：Draft、Rejected 可刪（軟刪除，保留單號歷史）；舊版本 409；
        // Pending 422；刪除後讀取、修改、再刪除皆 404。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = ReceiptDocProvider(companyId, new ReceiptDocActor());
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var draft = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "D-1"), token));
        var rejected = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "D-2"), token));
        var pending = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "D-3"), token));
        await SetReceiptDocStatusAsync(rejected.Id, "Rejected");
        await SetReceiptDocStatusAsync(pending.Id, "Pending");

        await Assert.ThrowsAsync<ConflictException>(() => ReceiptDocAsync(provider, s => s.DeleteAsync(draft.Id, 9, token)));
        await ReceiptDocAsync(provider, s => s.DeleteAsync(draft.Id, 1, token));
        await ReceiptDocAsync(provider, s => s.DeleteAsync(rejected.Id, 2, token));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ReceiptDocAsync(provider, s => s.DeleteAsync(pending.Id, 2, token)));
        Assert.Equal(CollectionReceiptDocumentErrors.InvalidStatus, error.ErrorCode);

        foreach (var id in new[] { draft.Id, rejected.Id })
        {
            Assert.Equal(CollectionReceiptDocumentErrors.NotFound, (await Assert.ThrowsAsync<NotFoundException>(() =>
                ReceiptDocAsync(provider, s => s.GetAsync(id, token)))).ErrorCode);
            await Assert.ThrowsAsync<NotFoundException>(() => ReceiptDocAsync(provider, s =>
                s.ReplaceAsync(id, 2, ReceiptDocInput(setup.BankId, "D-9"), token)));
            await Assert.ThrowsAsync<NotFoundException>(() => ReceiptDocAsync(provider, s => s.DeleteAsync(id, 2, token)));
        }
        Assert.Equal((1, 3), (await ReceiptDocCountAsync(companyId), await ReceiptDocCountAsync(companyId, includeDeleted: true)));
    }

    [Fact]
    public async Task ReceiptDocumentCommandsNeedTheirOwnPermissionAndCompany()
    {
        // FI-COLL-DOC-008（權限）：缺少各動作權限 403 且不取號；授權公司與目前公司不同 403；其他公司的 ID 404。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = new ReceiptDocActor();
        await using var provider = ReceiptDocProvider(companyId, actor);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var created = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "P-1"), token));

        actor.Permissions = ["AR.COLLECTION.VIEW", "AR.CREDIT.CREATE", "AR.CREDIT.EDIT", "AR.CREDIT.DEACTIVATE"];
        await Assert.ThrowsAsync<ForbiddenException>(() => ReceiptDocAsync(provider, s =>
            s.CreateAsync(ReceiptDocInput(setup.BankId, "P-2"), token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => ReceiptDocAsync(provider, s =>
            s.ReplaceAsync(created.Id, 1, ReceiptDocInput(setup.BankId, "P-2"), token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => ReceiptDocAsync(provider, s => s.DeleteAsync(created.Id, 1, token)));
        Assert.Equal(created.Id, (await ReceiptDocAsync(provider, s => s.GetAsync(created.Id, token))).Id);
        actor.Permissions = ["AR.COLLECTION.CREATE"];
        await Assert.ThrowsAsync<ForbiddenException>(() => ReceiptDocAsync(provider, s => s.GetAsync(created.Id, token)));
        actor.Permissions = AllReceiptDocPermissions;
        actor.AuthorizedCompany = companyId + 1;
        await Assert.ThrowsAsync<ForbiddenException>(() => ReceiptDocAsync(provider, s => s.GetAsync(created.Id, token)));
        Assert.Equal(1, actor.Numbers);

        await using var other = ReceiptDocProvider(NewCompany(), new ReceiptDocActor());
        await Assert.ThrowsAsync<NotFoundException>(() => ReceiptDocAsync(other, s => s.GetAsync(created.Id, token)));
        await Assert.ThrowsAsync<NotFoundException>(() => ReceiptDocAsync(other, s =>
            s.ReplaceAsync(created.Id, 1, ReceiptDocInput(setup.BankId, "P-3"), token)));
        await Assert.ThrowsAsync<NotFoundException>(() => ReceiptDocAsync(other, s => s.DeleteAsync(created.Id, 1, token)));
        actor.AuthorizedCompany = null;
        var kept = await ReceiptDocAsync(provider, s => s.GetAsync(created.Id, token));
        Assert.Equal(("P-1", 1), (kept.BankReference, kept.Version));
    }

    [Fact]
    public async Task NumberingFailureOrMissingBaseCurrencySavesNothing()
    {
        // FI-COLL-DOC-009（錯誤／回滾）：單號規則不存在（SYS 422）→ 不存列；公司未設本位幣 → 422 BASE_CURRENCY_MISSING。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = new ReceiptDocActor { FailNumbering = true };
        await using var provider = ReceiptDocProvider(companyId, actor);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ReceiptDocAsync(provider, s =>
            s.CreateAsync(ReceiptDocInput(setup.BankId), token)));
        Assert.Equal("SYS.NUMBER_RULE.NOT_FOUND", error.ErrorCode);
        Assert.Equal(0, await ReceiptDocCountAsync(companyId, includeDeleted: true));

        var bare = NewCompany();
        await using var empty = ReceiptDocProvider(bare, new ReceiptDocActor());
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => ReceiptDocAsync(empty, s =>
            s.CreateAsync(ReceiptDocInput(setup.BankId), token)));
        Assert.Equal(CollectionReceiptDocumentErrors.BaseCurrencyMissing, missing.ErrorCode);
    }
}
