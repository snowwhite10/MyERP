using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-SALES-INV-009～013（WU-FI-002a2c，ADR-107/108，FI-SALES-011／012／020）：兩條真實 SQL 連線同時過帳發票時，
/// FI 信用鍵（BR-FI-013 第一把鎖）序列化同一客戶；等待者在鎖後以 FI 永久來源鍵判斷重放或衝突，絕不重複記帳；
/// 不同客戶互不阻擋；呼叫者在過帳後的 SD／稽核寫入失敗並回滾 → 0 列、0 信用鍵、之後可再過帳；信用鍵逾時 409。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static readonly TimeSpan RaceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>第一條連線在讀 SD 時停住（已持有信用鍵、尚未寫入），直到測試釋放。</summary>
    private sealed class Gate
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HoldAsync(CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(RaceTimeout, token);
        }
    }

    /// <summary>以 <paramref name="gate"/> 停住第一條連線，啟動第二條，證明第二條在信用鍵等待（未讀 SD），再釋放。</summary>
    private static async Task<(SalesInvoicePostingResult First, SalesInvoicePostingResult Second)> RaceAsync(
        ServiceProvider provider, FakeInvoices invoices, Gate gate, SalesInvoicePostingCommand first,
        SalesInvoicePostingCommand second, CancellationToken token, Task<SalesInvoicePostingResult>? started = null)
    {
        var firstTask = started ?? Task.Run(() => PostInvoiceAsync(provider, first, commit: true, token), token);
        await gate.Entered.Task.WaitAsync(RaceTimeout, token);
        var readsWhileHeld = invoices.Reads;
        var secondTask = Task.Run(() => PostInvoiceAsync(provider, second, commit: true, token), token);

        await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromMilliseconds(700), token));
        Assert.False(secondTask.IsCompleted, "the second connection must wait on the FI credit key");
        Assert.Equal(readsWhileHeld, invoices.Reads);

        gate.Release.SetResult();
        var firstResult = await firstTask.WaitAsync(RaceTimeout, token);
        var secondResult = await secondTask.WaitAsync(RaceTimeout, token);
        return (firstResult, secondResult);
    }

    private static Gate HoldFirstRead(FakeInvoices invoices, long invoiceId)
    {
        var gate = new Gate();
        var held = 0;
        invoices.OnRead = (id, token) => id == invoiceId && Interlocked.Exchange(ref held, 1) == 0
            ? gate.HoldAsync(token)
            : Task.CompletedTask;
        return gate;
    }

    [Fact]
    public async Task TwoConnectionsWithTheSameClaimPostOnceAndTheWaiterReplaysUnchanged()
    {
        // FI-SALES-INV-009（FI-SALES-011：同來源同主張）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[850] = Invoice(companyId, 850);
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);
        var gate = HoldFirstRead(invoices, 850);

        var (first, second) = await RaceAsync(provider, invoices, gate, Command(companyId, 850), Command(companyId, 850),
            token);

        Assert.Equal((SalesPostingOutcomeKind.Posted, true), (first.Kind, first.Changed));
        Assert.Equal(new SalesInvoicePostingResult(SalesPostingOutcomeKind.Unchanged, false, 200, Receipt: first.Receipt),
            second);
        Assert.Equal(1, invoices.Reads); // 等待者以 FI 來源鍵重放，不再讀 SD
        Assert.Equal((1, 1, 1, 3), await InvoiceCountsAsync(companyId, token));
        Assert.Equal(1, await CreditKeysAsync(companyId, token));
    }

    [Fact]
    public async Task TwoConnectionsWithDifferentFingerprintsPostOnceAndTheWaiterConflicts()
    {
        // FI-SALES-INV-010（FI-SALES-011：同來源不同指紋）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[851] = Invoice(companyId, 851);
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);
        var gate = HoldFirstRead(invoices, 851);

        var (first, second) = await RaceAsync(provider, invoices, gate, Command(companyId, 851),
            Command(companyId, 851, "sha256:edited"), token);

        Assert.Equal(SalesPostingOutcomeKind.Posted, first.Kind);
        Assert.Equal("sha256:inv", first.Receipt!.Fingerprint);
        AssertSalesRejected(second, 409, SalesPostingErrors.SourceConflict);
        Assert.Equal(1, invoices.Reads);
        Assert.Equal((1, 1, 1, 3), await InvoiceCountsAsync(companyId, token));
    }

    [Fact]
    public async Task SameCustomerInvoicesSerializeOnTheCreditKeyWhileAnotherCustomerProceeds()
    {
        // FI-SALES-INV-011（FI-SALES-011：同客戶不同發票序列化；不同客戶不受阻擋）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        const long otherCustomer = InvoiceCustomerId + 1;
        invoices.Items[852] = Invoice(companyId, 852);
        invoices.Items[853] = Invoice(companyId, 853, 200m, 10m);
        invoices.Items[854] = Invoice(companyId, 854, 50m, 0m) with { CustomerId = otherCustomer };
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        var gate = new Gate();
        var committedWhenSecondRead = -1;
        invoices.OnRead = async (id, ct) =>
        {
            if (id == 852)
                await gate.HoldAsync(ct);
            else if (id == 853)
                committedWhenSecondRead = (await InvoiceCountsAsync(companyId, ct)).Sources;
        };

        var firstTask = Task.Run(() => PostInvoiceAsync(provider, Command(companyId, 852), true, token), token);
        await gate.Entered.Task.WaitAsync(RaceTimeout, token);

        // 另一客戶在第一條交易仍持有 601 信用鍵時照常完成。
        var other = await PostInvoiceAsync(provider, Command(companyId, 854, customerId: otherCustomer), true, token)
            .WaitAsync(RaceTimeout, token);
        Assert.Equal(SalesPostingOutcomeKind.Posted, other.Kind);

        var (first, second) = await RaceAsync(provider, invoices, gate, Command(companyId, 852),
            Command(companyId, 853), token, firstTask);

        Assert.Equal((SalesPostingOutcomeKind.Posted, SalesPostingOutcomeKind.Posted), (first.Kind, second.Kind));
        Assert.Equal(2, committedWhenSecondRead); // 854 與 852 都已提交後，853 才讀 SD
        Assert.NotEqual(first.Receipt!.JournalId, second.Receipt!.JournalId);
        Assert.Equal((3, 3, 3, 8), await InvoiceCountsAsync(companyId, token));
        Assert.Equal(2, await CreditKeysAsync(companyId, token));
    }

    [Fact]
    public async Task CallerFailureAfterPostingRollsEverythingBackAndThePostingCanBeRetried()
    {
        // FI-SALES-INV-012（FI-SALES-012：SD 標記或稽核寫入在同交易失敗 → 全回滾）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[860] = Invoice(companyId, 860);
        await using var provider = InvoiceProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        // (a) SD 標記 Posted 的 SQL 失敗；(b) 稽核寫入 SaveChanges 違反唯一鍵（重複信用鍵）。
        Func<MyErpDbContext, Task>[] failures =
        [
            db => db.Database.ExecuteSqlRawAsync("THROW 50001, 'SD issue failed after FI posting.', 1;", token),
            db =>
            {
                db.Set<CreditKey>().Add(new CreditKey { CustomerId = InvoiceCustomerId });
                return db.SaveChangesAsync(token);
            }
        ];
        foreach (var failure in failures)
        {
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var posted = await sp.GetRequiredService<ISalesInvoicePostingService>()
                .PostInCurrentTransactionAsync(Command(companyId, 860), token);
            Assert.Equal(SalesPostingOutcomeKind.Posted, posted.Kind);

            var error = await Record.ExceptionAsync(() => failure(db));
            Assert.True(error is SqlException or DbUpdateException, error?.ToString());
            await transaction.RollbackAsync(token);

            Assert.Equal((0, 0, 0, 0), await InvoiceCountsAsync(companyId, token));
            Assert.Equal(0, await CreditKeysAsync(companyId, token));
        }

        var retried = await PostInvoiceAsync(provider, Command(companyId, 860), commit: true, token);
        Assert.Equal((SalesPostingOutcomeKind.Posted, true), (retried.Kind, retried.Changed));
        Assert.Equal((1, 1, 1, 3), await InvoiceCountsAsync(companyId, token));
        Assert.Equal(1, await CreditKeysAsync(companyId, token));
    }

    [Fact]
    public async Task CreditKeyLockTimeoutIsANamedConflictThatKeepsTheCallerTransactionWithoutRows()
    {
        // FI-SALES-INV-013（FI-SALES-020：信用鍵逾時 409 FI.CREDIT.LOCK_TIMEOUT）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[870] = Invoice(companyId, 870);
        await using var provider = InvoiceProvider(companyId, invoices, creditLockTimeoutMilliseconds: 300);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        await using (var holderScope = provider.CreateAsyncScope())
        {
            var holderDb = holderScope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            await using var holder = await holderDb.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await holderScope.ServiceProvider.GetRequiredService<ICustomerCreditService>()
                .AcquireCreditLockAsync(companyId, InvoiceCustomerId, holder.GetDbTransaction(), token);

            await using (var scope = provider.CreateAsyncScope())
            {
                var sp = scope.ServiceProvider;
                var db = sp.GetRequiredService<MyErpDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
                var timedOut = await sp.GetRequiredService<ISalesInvoicePostingService>()
                    .PostInCurrentTransactionAsync(Command(companyId, 870), token);
                AssertSalesRejected(timedOut, 409, FinanceModuleContracts.CreditLockTimeout);
                Assert.Same(transaction, db.Database.CurrentTransaction);
                Assert.Equal(1, await TranCountAsync(db, token));
                await transaction.CommitAsync(token);
            }
            Assert.Equal(0, invoices.Reads);
            await holder.RollbackAsync(token);
        }

        Assert.Equal((0, 0, 0, 0), await InvoiceCountsAsync(companyId, token));
        Assert.Equal(0, await CreditKeysAsync(companyId, token));
        Assert.Equal(SalesPostingOutcomeKind.Posted,
            (await PostInvoiceAsync(provider, Command(companyId, 870), commit: true, token)).Kind);
    }
}
