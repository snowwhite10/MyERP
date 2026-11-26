using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// PUR-POST-105～107（WU-PUR-008b6，008b4 延後驗收，BR-PUR-043）：同一張 Matched 發票在多條真實 SQL 連線同時過帳時，
/// 供應商應用鎖 <c>pur_invoice_draft:{公司}:{供應商}</c> 先序列化；第一個寫入 FI 並 Posted，等待者在鎖後讀到 Posted，
/// 以過帳請求版本重放（200，同一組 FI 參照）或以過期版本 409；FI 來源／應付／傳票各 1、預留只轉一次。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    /// <summary>以獨立連線持有供應商發票鎖（與 Draft／MATCH／RESOLVE／POST 同一資源），模擬正在處理中的交易。</summary>
    private async Task<(SqlConnection Connection, SqlTransaction Transaction)> HoldSupplierInvoiceLockAsync(
        long companyId, CancellationToken token)
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(token);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await using var command = new SqlCommand("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction',
                @LockTimeout=5000;
            IF @result < 0 THROW 51099, 'test could not take the supplier invoice lock', 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@resource",
            PurchaseInvoiceDraftSqlSession.LockResource(companyId, PurchaseSupplierId));
        await command.ExecuteNonQueryAsync(token);
        return (connection, transaction);
    }

    private static async Task<object> PostOutcomeAsync(ServiceProvider provider, long invoiceId, int version,
        CancellationToken token)
    {
        try
        {
            return await PostInvoiceAsync(provider, invoiceId, version, token);
        }
        catch (ConflictException error)
        {
            return error;
        }
    }

    [Fact]
    public async Task PUR_POST_105_TwoConcurrentPostsWaitOnTheSupplierLockAndTheWaiterReplaysTheSameFiReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = PostProvider(companyId, new PostActor());
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);

        var (holder, held) = await HoldSupplierInvoiceLockAsync(companyId, token);
        Task<object>[] posts;
        await using (holder)
        await using (held)
        {
            posts =
            [
                Task.Run(() => PostOutcomeAsync(provider, arranged.InvoiceId, 2, token), token),
                Task.Run(() => PostOutcomeAsync(provider, arranged.InvoiceId, 2, token), token),
            ];
            await Task.WhenAny(Task.WhenAll(posts), Task.Delay(TimeSpan.FromMilliseconds(800), token));
            Assert.All(posts, post => Assert.False(post.IsCompleted, "POST must wait on the supplier invoice lock"));
            Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
            await held.CommitAsync(token);
        }

        var outcomes = await Task.WhenAll(posts).WaitAsync(RaceTimeout, token);
        var saved = Assert.Single(outcomes.OfType<PurchaseInvoicePostSaved>(), x => !x.Replayed);
        var replay = Assert.Single(outcomes.OfType<PurchaseInvoicePostSaved>(), x => x.Replayed);
        Assert.Equal(saved with { Replayed = true }, replay);
        Assert.Equal(("Posted", 3), (saved.Status, saved.Version));
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(new PostRow("Posted", 3, 2, saved.FiPurchaseSourceId, saved.FiPayableId, saved.FiJournalId,
            saved.PostedAt, 0m, 5m), await PostRowAsync(arranged, token));
    }

    [Fact]
    public async Task PUR_POST_106_ConcurrentCurrentAndStaleVersionPostOnceAndTheStaleRequestConflicts()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = PostProvider(companyId, new PostActor());
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);

        var (holder, held) = await HoldSupplierInvoiceLockAsync(companyId, token);
        Task<object> current, stale;
        await using (holder)
        await using (held)
        {
            stale = Task.Run(() => PostOutcomeAsync(provider, arranged.InvoiceId, 1, token), token);
            current = Task.Run(() => PostOutcomeAsync(provider, arranged.InvoiceId, 2, token), token);
            await Task.WhenAny(Task.WhenAll(stale, current), Task.Delay(TimeSpan.FromMilliseconds(800), token));
            Assert.False(stale.IsCompleted || current.IsCompleted, "POST must wait on the supplier invoice lock");
            await held.CommitAsync(token);
        }

        // 不論哪一個先取得鎖：版本 1 在 Matched v2 或 Posted v3（過帳自 v2）時都是過期請求。
        var saved = Assert.IsType<PurchaseInvoicePostSaved>(await current.WaitAsync(RaceTimeout, token));
        Assert.False(saved.Replayed);
        Assert.IsType<ConflictException>(await stale.WaitAsync(RaceTimeout, token));
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(3, (await PostRowAsync(arranged, token)).Version);
    }

    [Fact]
    public async Task PUR_POST_107_ThreeUnsynchronizedPostsOfTheSameInvoiceCreateOnePayable()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = PostProvider(companyId, new PostActor());
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);

        using var start = new ManualResetEventSlim();
        var posts = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            start.Wait(token);
            return await PostOutcomeAsync(provider, arranged.InvoiceId, 2, token);
        }, token)).ToArray();
        start.Set();

        var outcomes = await Task.WhenAll(posts).WaitAsync(RaceTimeout, token);
        // 等待者都在供應商鎖後讀到 Posted 並重放；不允許 409 以外的錯誤，也不允許第二筆應付。
        var saved = Assert.Single(outcomes.OfType<PurchaseInvoicePostSaved>(), x => !x.Replayed);
        Assert.All(outcomes.Where(x => !ReferenceEquals(x, saved)), outcome =>
        {
            if (outcome is PurchaseInvoicePostSaved replay) Assert.Equal(saved with { Replayed = true }, replay);
            else Assert.IsType<ConflictException>(outcome);
        });
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(new PostRow("Posted", 3, 2, saved.FiPurchaseSourceId, saved.FiPayableId, saved.FiJournalId,
            saved.PostedAt, 0m, 5m), await PostRowAsync(arranged, token));
    }
}

/// <summary>PUR-POST-108（WU-PUR-008b6，REQ_IO_A4.19.5）：明細 GET 帶出過帳時間與 FI 三個參照，未過帳為 null。</summary>
public sealed partial class FinancePostingServiceTests
{
    [Fact]
    public async Task PUR_POST_108_DetailShowsPostingTimeAndFiReferencesOnlyAfterPosting()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = new PostActor { Permissions = [PurchasingPermissions.InvoicePost, PurchasingPermissions.InvoiceView] };
        await using var provider = PostProvider(companyId, actor);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);

        async Task<PurchaseInvoiceDetail> DetailAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<PurchaseInvoiceQueryService>()
                .GetAsync(arranged.InvoiceId, token);
        }

        var matched = await DetailAsync();
        Assert.Equal((null, null, null, null),
            (matched.PostedAt, matched.FiPurchaseSourceId, matched.FiPayableId, matched.FiJournalId));

        var saved = await PostInvoiceAsync(provider, arranged.InvoiceId, 2, token);
        var posted = await DetailAsync();
        Assert.Equal((PurchaseInvoiceStatus.Posted, 3), (posted.Status, posted.Version));
        Assert.Equal(((DateTime?)saved.PostedAt, (long?)saved.FiPurchaseSourceId, (long?)saved.FiPayableId,
            (long?)saved.FiJournalId), (posted.PostedAt, posted.FiPurchaseSourceId, posted.FiPayableId,
            posted.FiJournalId));
    }
}
