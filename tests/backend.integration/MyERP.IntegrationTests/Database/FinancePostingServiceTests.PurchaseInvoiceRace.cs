using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-PUR-POST-009～013（WU-FI-003a3，ADR-117，FI-PUR-009／011）：兩條真實 SQL 連線同時過帳同一張採購發票時，
/// FI 來源鍵 applock <c>ap_purchase:{公司}:{發票}</c>（BR-FI-020）序列化；等待者在鎖後以 FI 永久來源鍵判斷重放或衝突，
/// 絕不產生兩筆應付；不同發票互不阻擋；呼叫者在過帳後的 PUR／稽核寫入失敗並回滾 → 0 列、之後可再過帳；鎖逾時 409。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private ServiceProvider PurchaseProvider(long companyId, FakePurchaseInvoices invoices, int lockTimeoutMilliseconds) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(invoices);
            services.AddScoped<IFinancePurchaseInvoiceSource, FakePurchaseInvoiceSource>();
            services.AddScoped<IPurchaseInvoicePostingService>(sp =>
            {
                var service = ActivatorUtilities.CreateInstance<PurchaseInvoicePostingService>(sp);
                service.LockTimeoutMilliseconds = lockTimeoutMilliseconds;
                return service;
            });
        });

    /// <summary>第一條連線在讀 PUR 時停住（已持有 FI 來源鍵、尚未寫入），直到測試釋放。</summary>
    private static Gate HoldFirstPurchaseRead(FakePurchaseInvoices invoices, long invoiceId)
    {
        var gate = new Gate();
        var held = 0;
        invoices.OnRead = (id, _, token) => id == invoiceId && Interlocked.Exchange(ref held, 1) == 0
            ? gate.HoldAsync(token)
            : Task.CompletedTask;
        return gate;
    }

    /// <summary>停住第一條連線，啟動第二條，證明第二條在 FI 來源鍵等待（未讀 PUR），再釋放。</summary>
    private static async Task<(PurchaseInvoicePostingResult First, PurchaseInvoicePostingResult Second)>
        PurchaseRaceAsync(ServiceProvider provider, FakePurchaseInvoices invoices, Gate gate,
            PurchaseInvoicePostingCommand first, PurchaseInvoicePostingCommand second, CancellationToken token)
    {
        var firstTask = Task.Run(() => PostPurchaseAsync(provider, first, commit: true, token), token);
        await gate.Entered.Task.WaitAsync(RaceTimeout, token);
        var readsWhileHeld = invoices.Reads;
        var secondTask = Task.Run(() => PostPurchaseAsync(provider, second, commit: true, token), token);

        await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromMilliseconds(700), token));
        Assert.False(secondTask.IsCompleted, "the second connection must wait on the FI purchase source key");
        Assert.Equal(readsWhileHeld, invoices.Reads);

        gate.Release.SetResult();
        return (await firstTask.WaitAsync(RaceTimeout, token), await secondTask.WaitAsync(RaceTimeout, token));
    }

    [Fact]
    public async Task TwoConnectionsPostingTheSamePurchaseClaimCreateOnePayableAndTheWaiterReplaysUnchanged()
    {
        // FI-PUR-POST-009（FI-PUR-009：同來源同主張）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[960] = PurchaseInvoice(companyId, 960);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var gate = HoldFirstPurchaseRead(invoices, 960);

        var (first, second) = await PurchaseRaceAsync(provider, invoices, gate, PurchaseCommand(companyId, 960),
            PurchaseCommand(companyId, 960), token);

        Assert.Equal((PurchasePostingOutcomeKind.Posted, true), (first.Kind, first.Changed));
        Assert.Equal(new PurchaseInvoicePostingResult(PurchasePostingOutcomeKind.Unchanged, false, 200,
            Receipt: first.Receipt), second);
        Assert.Equal(1, invoices.Reads); // 等待者以 FI 來源鍵重放，不再讀 PUR
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task TwoConnectionsWithDifferentPurchaseFingerprintsPostOnceAndTheWaiterConflicts()
    {
        // FI-PUR-POST-010（FI-PUR-009：同來源不同指紋／版本）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[961] = PurchaseInvoice(companyId, 961);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var gate = HoldFirstPurchaseRead(invoices, 961);

        var (first, second) = await PurchaseRaceAsync(provider, invoices, gate, PurchaseCommand(companyId, 961),
            PurchaseCommand(companyId, 961, "sha256:edited", version: 2), token);

        Assert.Equal(PurchasePostingOutcomeKind.Posted, first.Kind);
        Assert.Equal(("sha256:pur", 1), (first.Receipt!.Fingerprint, first.Receipt.SourceVersion));
        AssertPurchaseRejected(second, 409, PurchasePostingErrors.SourceConflict);
        Assert.Equal(1, invoices.Reads);
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task AnotherInvoiceOfTheSameSupplierIsNotBlockedByAHeldPurchaseSourceKey()
    {
        // FI-PUR-POST-011（BR-FI-020：鎖粒度為發票，同供應商另一張發票照常完成，無死結）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[962] = PurchaseInvoice(companyId, 962);
        invoices.Items[963] = PurchaseInvoice(companyId, 963, 200m, 10m);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var gate = HoldFirstPurchaseRead(invoices, 962);

        var firstTask = Task.Run(() => PostPurchaseAsync(provider, PurchaseCommand(companyId, 962), true, token), token);
        await gate.Entered.Task.WaitAsync(RaceTimeout, token);
        var other = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 963), true, token)
            .WaitAsync(RaceTimeout, token);
        Assert.Equal(PurchasePostingOutcomeKind.Posted, other.Kind);
        Assert.False(firstTask.IsCompleted);

        gate.Release.SetResult();
        var first = await firstTask.WaitAsync(RaceTimeout, token);
        Assert.Equal(PurchasePostingOutcomeKind.Posted, first.Kind);
        Assert.NotEqual(first.Receipt!.PayableId, other.Receipt!.PayableId);
        Assert.Equal((2, 2, 2, 6), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task CallerFailureAfterPurchasePostingRollsEverythingBackAndThePostingCanBeRetried()
    {
        // FI-PUR-POST-012（FI-PUR-011：PUR 標記 Posted 或稽核寫入在同交易失敗 → 來源、應付、傳票、GL 來源鍵全回滾）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[964] = PurchaseInvoice(companyId, 964);
        await using var provider = PurchaseProvider(companyId, invoices);
        await SeedPurchaseAccountsAsync(provider, companyId, token);

        // (a) PUR 標記 Posted 的 SQL 失敗；(b) 稽核寫入 SaveChanges 違反唯一鍵（重複 FI 來源鍵）。
        Func<MyErpDbContext, Task>[] failures =
        [
            db => db.Database.ExecuteSqlRawAsync("THROW 50001, 'PUR invoice update failed after FI posting.', 1;", token),
            async db =>
            {
                var saved = await db.Set<PurchaseSource>().AsNoTracking().SingleAsync(token);
                db.Set<PurchaseSource>().Add(new PurchaseSource
                {
                    SupplierId = saved.SupplierId, SourceType = saved.SourceType, SourceId = saved.SourceId,
                    SourceVersion = saved.SourceVersion, Fingerprint = saved.Fingerprint, GlJournalId = saved.GlJournalId,
                    BusinessDate = saved.BusinessDate, Currency = saved.Currency, BaseCurrency = saved.BaseCurrency,
                    ExchangeRate = saved.ExchangeRate, NetAmount = saved.NetAmount, TaxAmount = saved.TaxAmount,
                    GrossAmount = saved.GrossAmount, BaseNetAmount = saved.BaseNetAmount,
                    BaseTaxAmount = saved.BaseTaxAmount, BaseGrossAmount = saved.BaseGrossAmount
                });
                await db.SaveChangesAsync(token);
            }
        ];
        foreach (var failure in failures)
        {
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var posted = await sp.GetRequiredService<IPurchaseInvoicePostingService>()
                .PostInCurrentTransactionAsync(PurchaseCommand(companyId, 964), token);
            Assert.Equal(PurchasePostingOutcomeKind.Posted, posted.Kind);

            var error = await Record.ExceptionAsync(() => failure(db));
            Assert.True(error is SqlException or DbUpdateException, error?.ToString());
            await transaction.RollbackAsync(token);

            Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
            Assert.Equal(0, await PurchaseGlSourceKeysAsync(companyId, token));
        }

        var retried = await PostPurchaseAsync(provider, PurchaseCommand(companyId, 964), commit: true, token);
        Assert.Equal((PurchasePostingOutcomeKind.Posted, true), (retried.Kind, retried.Changed));
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(1, await PurchaseGlSourceKeysAsync(companyId, token));
    }

    [Fact]
    public async Task PurchaseSourceKeyLockTimeoutIsANamedConflictThatKeepsTheCallerTransactionWithoutRows()
    {
        // FI-PUR-POST-013（BR-FI-020：來源鍵逾時 409 FI.PURCHASE_POSTING.LOCK_TIMEOUT，不讀 PUR、不寫列、交易仍開啟）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakePurchaseInvoices();
        invoices.Items[965] = PurchaseInvoice(companyId, 965);
        await using var provider = PurchaseProvider(companyId, invoices, lockTimeoutMilliseconds: 300);
        await SeedPurchaseAccountsAsync(provider, companyId, token);

        await using (var holderScope = provider.CreateAsyncScope())
        {
            var holderDb = holderScope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            await using var holder = await holderDb.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var resource = $"ap_purchase:{companyId}:965";
            var granted = await holderDb.Database.SqlQuery<int>($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
                SELECT @result AS [Value];
                """).ToListAsync(token);
            Assert.Equal([0], granted);

            await using (var scope = provider.CreateAsyncScope())
            {
                var sp = scope.ServiceProvider;
                var db = sp.GetRequiredService<MyErpDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
                var timedOut = await sp.GetRequiredService<IPurchaseInvoicePostingService>()
                    .PostInCurrentTransactionAsync(PurchaseCommand(companyId, 965), token);
                AssertPurchaseRejected(timedOut, 409, PurchasePostingErrors.LockTimeout);
                Assert.Same(transaction, db.Database.CurrentTransaction);
                Assert.Equal(1, await TranCountAsync(db, token));
                await transaction.CommitAsync(token);
            }
            Assert.Equal(0, invoices.Reads);
            await holder.RollbackAsync(token);
        }

        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));
        Assert.Equal(PurchasePostingOutcomeKind.Posted,
            (await PostPurchaseAsync(provider, PurchaseCommand(companyId, 965), commit: true, token)).Kind);
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
    }

    private async Task<int> PurchaseGlSourceKeysAsync(long companyId, CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM dbo.gl_source_key WHERE company_id={companyId} AND source_module='PUR'")
            .SingleAsync(token);
    }
}
