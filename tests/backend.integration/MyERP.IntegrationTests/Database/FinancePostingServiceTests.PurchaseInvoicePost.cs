using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// PUR-POST-001～006（WU-PUR-008b4，ADR-208）：<see cref="PurchaseInvoicePostService"/> 以正式 PUR 來源埠、FI 過帳服務與
/// 真實 SQL 在一個 Serializable 交易內把 Matched 發票過帳；預留轉已開票量、發票 Posted 帶 FI 參照；任何錯誤都不留列。
/// PUR 來源列以 SQL 安排（採購單／收貨尚無寫入 API，ADR-204／206）。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private sealed class PostActor
    {
        public string[] Permissions = [PurchasingPermissions.InvoicePost];
        public DataScope Scope = DataScope.Company;
    }

    private sealed class PostAuthorization(PostActor actor, ICurrentCompany company) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UserCompanyAuthorization?>(new UserCompanyAuthorization(7, company.CompanyId ?? 0, null,
                null, false, actor.Permissions.ToDictionary(x => x, _ => actor.Scope)));
    }

    private ServiceProvider PostProvider(long companyId, PostActor actor) =>
        Provider(companyId, mapping: null, services =>
        {
            new PurchasingModule().AddServices(services,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            services.AddSingleton(actor);
            services.AddScoped<ICurrentAuthorization, PostAuthorization>();
        });

    private static async Task<PurchaseInvoicePostSaved> PostInvoiceAsync(ServiceProvider provider, long invoiceId,
        int version, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PurchaseInvoicePostService>()
            .PostAsync(invoiceId, version, token);
    }

    private sealed record PostRow(string Status, int Version, int? PostedFromVersion, long? SourceId, long? PayableId,
        long? JournalId, DateTime? PostedAt, decimal MatchedReserved, decimal PostedInvoiced);

    private async Task<PostRow> PostRowAsync(PurArrange arranged, CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQuery<PostRow>($"""
            SELECT i.status AS status, i.version AS version, i.posted_from_version AS posted_from_version,
                   i.fi_purchase_source_id AS source_id, i.fi_payable_id AS payable_id, i.fi_journal_id AS journal_id,
                   i.posted_at AS posted_at, r.matched_reserved AS matched_reserved, r.posted_invoiced AS posted_invoiced
            FROM dbo.pur_purchase_invoice i JOIN dbo.pur_purchase_receipt_line r ON r.id = {arranged.ReceiptLineId}
            WHERE i.id = {arranged.InvoiceId}
            """).SingleAsync(token);
    }

    private static readonly PostRow MatchedV2 = new("Matched", 2, null, null, null, null, null, 5m, 0m);

    [Fact]
    public async Task PUR_POST_001_MatchedForeignInvoicePostsThroughFiMovesReservationAndReplays()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = PostProvider(companyId, new PostActor());
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);

        var saved = await PostInvoiceAsync(provider, arranged.InvoiceId, 2, token);
        Assert.Equal((arranged.InvoiceId, 3, "Posted", false, new DateTime(2026, 11, 6, 9, 30, 0)),
            (saved.Id, saved.Version, saved.Status, saved.Replayed, saved.PostedAt));
        Assert.True(saved is { FiPurchaseSourceId: > 0, FiPayableId: > 0, FiJournalId: > 0 });
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(new PostRow("Posted", 3, 2, saved.FiPurchaseSourceId, saved.FiPayableId, saved.FiJournalId,
            saved.PostedAt, 0m, 5m), await PostRowAsync(arranged, token));

        // FI 收據的本位幣金額來自採購單快照 31.5（ADR-206）：應付以 396.90 入帳。
        await using (var db = database.CreateRawContext())
            Assert.Equal(396.90m, await db.Database.SqlQuery<decimal>(
                $"SELECT base_gross_amount AS [Value] FROM dbo.ap_purchase_source WHERE id={saved.FiPurchaseSourceId}")
                .SingleAsync(token));

        // 重放：過帳請求的版本（2）或 Posted 結果的版本（3）都回保存結果，不呼叫 FI、不寫入。
        foreach (var version in new[] { 2, 3 })
        {
            var replay = await PostInvoiceAsync(provider, arranged.InvoiceId, version, token);
            Assert.Equal(saved with { Replayed = true }, replay);
        }
        await Assert.ThrowsAsync<ConflictException>(() => PostInvoiceAsync(provider, arranged.InvoiceId, 1, token));
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(3, (await PostRowAsync(arranged, token)).Version);
    }

    [Fact]
    public async Task PUR_POST_002_RequestPermissionStatusAndVersionErrorsWriteNothing()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = new PostActor();
        await using var provider = PostProvider(companyId, actor);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var matched = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);
        var draft = await ArrangePurchaseInvoiceAsync(companyId, "Draft", 2m, token: token);
        var disputed = await ArrangePurchaseInvoiceAsync(companyId, "Disputed", 2.1m, token: token);
        var deleted = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2m, deleted: true, token: token);
        var foreign = await ArrangePurchaseInvoiceAsync(NewCompany(), "Matched", 2m, token: token);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            PostInvoiceAsync(provider, matched.InvoiceId, 0, token));
        await Assert.ThrowsAsync<ConflictException>(() => PostInvoiceAsync(provider, matched.InvoiceId, 1, token));
        await Assert.ThrowsAsync<ConflictException>(() => PostInvoiceAsync(provider, matched.InvoiceId, 3, token));
        foreach (var missing in new[] { foreign.InvoiceId, deleted.InvoiceId, long.MaxValue })
        {
            var error = await Assert.ThrowsAsync<NotFoundException>(() =>
                PostInvoiceAsync(provider, missing, 1, token));
            Assert.Equal("PUR.INVOICE.NOT_FOUND", error.ErrorCode);
        }
        foreach (var invoice in new[] { draft, disputed })
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                PostInvoiceAsync(provider, invoice.InvoiceId, 1, token));
            Assert.Equal(PurchaseInvoicePostService.InvalidPostStatus, error.ErrorCode);
        }

        // 只有 MATCH 權限，或 POST 權限但只限本人範圍 → 403（過帳影響全公司應付）。
        actor.Permissions = [PurchasingPermissions.InvoiceMatch];
        await Assert.ThrowsAsync<ForbiddenException>(() => PostInvoiceAsync(provider, matched.InvoiceId, 2, token));
        actor.Permissions = [PurchasingPermissions.InvoicePost];
        actor.Scope = DataScope.Self;
        await Assert.ThrowsAsync<ForbiddenException>(() => PostInvoiceAsync(provider, matched.InvoiceId, 2, token));

        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(MatchedV2, await PostRowAsync(matched, token));
    }

    [Fact]
    public async Task PUR_POST_003_MissingRateSnapshotIsPurError422BeforeFiAndKeepsReservation()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = PostProvider(companyId, new PostActor());
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var noRate = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, baseCurrency: null, rate: null,
            acceptedAtVersion: 1, token: token);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PostInvoiceAsync(provider, noRate.InvoiceId, 2, token));
        Assert.Equal("PUR.INVOICE.RATE_SNAPSHOT_MISSING", error.ErrorCode);
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(MatchedV2, await PostRowAsync(noRate, token));
    }

    [Fact]
    public async Task PUR_POST_004_FiRefusalKeepsFiCodeAndRollsBackEverything()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = PostProvider(companyId, new PostActor());
        await SeedPurchaseAccountsAsync(provider, companyId, token, withMap: false);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PostInvoiceAsync(provider, arranged.InvoiceId, 2, token));
        Assert.Equal(PurchasePostingErrors.MappingMissing, error.ErrorCode);
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(MatchedV2, await PostRowAsync(arranged, token));

        // 設定映射後同一版本可過帳（前一次沒有留下半套資料或鎖）。
        await SavePurchaseMapAsync(provider, "2141", "5101", "1151", token);
        var saved = await PostInvoiceAsync(provider, arranged.InvoiceId, 2, token);
        Assert.Equal(("Posted", false), (saved.Status, saved.Replayed));
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task PUR_POST_005_SavedEvidenceChangedAfterMatchIsRejectedBeforeFi()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = PostProvider(companyId, new PostActor());
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        // Matched 但價差沒有 RESOLVE 接受決定：證據不一致，PUR 預檢 422，FI 不寫入。
        var unresolved = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, token: token);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PostInvoiceAsync(provider, unresolved.InvoiceId, 1, token));
        Assert.StartsWith("PUR.INVOICE.", error.ErrorCode);
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(MatchedV2 with { Version = 1 }, await PostRowAsync(unresolved, token));
    }

    [Fact]
    public async Task PUR_POST_006_PostedCheckConstraintRequiresFiReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2m, token: token);
        await using var db = database.CreateRawContext();
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.pur_purchase_invoice SET status='Posted' WHERE id={arranged.InvoiceId}", token));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.pur_purchase_invoice SET fi_journal_id=1 WHERE id={arranged.InvoiceId}", token));
        await db.Database.ExecuteSqlAsync($"""
            UPDATE dbo.pur_purchase_invoice SET status='Posted', posted_at='2026-11-06', posted_from_version=1,
                fi_purchase_source_id=1, fi_payable_id=1, fi_journal_id=1 WHERE id={arranged.InvoiceId}
            """, token);
    }
}
