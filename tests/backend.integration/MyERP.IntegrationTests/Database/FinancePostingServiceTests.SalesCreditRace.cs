using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-SALES-CR-I08～I11（WU-FI-002a3c，ADR-113，FI-SALES-011 信用部分／012）：兩條真實 SQL 連線同時把銷退過帳為
/// FI 信用時，FI 信用鍵序列化同一客戶；等待者在鎖後讀到已提交的分配與 open 扣減，最後餘量只可被沖一次；
/// 同銷退同主張的等待者以 FI 來源鍵重放、不再讀 SD；呼叫者在 Posted 後失敗並回滾 → 0 來源／分配、open 復原。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    /// <summary>第一條連線在讀 SD 銷退時停住（已持有信用鍵），第二條必須在信用鍵等待、未讀 SD，然後釋放。</summary>
    private static async Task<(SalesCreditPostingResult First, SalesCreditPostingResult Second)> CreditRaceAsync(
        ServiceProvider provider, FakeReturns returns, long heldReturnId, SalesCreditPostingCommand first,
        SalesCreditPostingCommand second, CancellationToken token)
    {
        var gate = new Gate();
        var held = 0;
        returns.OnRead = (id, ct) => id == heldReturnId && Interlocked.Exchange(ref held, 1) == 0
            ? gate.HoldAsync(ct)
            : Task.CompletedTask;

        var firstTask = Task.Run(() => PostCreditAsync(provider, first, commit: true, token), token);
        await gate.Entered.Task.WaitAsync(RaceTimeout, token);
        var readsWhileHeld = returns.Reads;
        var secondTask = Task.Run(() => PostCreditAsync(provider, second, commit: true, token), token);

        await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromMilliseconds(700), token));
        Assert.False(secondTask.IsCompleted, "the second connection must wait on the FI credit key");
        Assert.Equal(readsWhileHeld, returns.Reads);

        gate.Release.SetResult();
        var firstResult = await firstTask.WaitAsync(RaceTimeout, token);
        var secondResult = await secondTask.WaitAsync(RaceTimeout, token);
        return (firstResult, secondResult);
    }

    [Fact]
    public async Task TwoReturnsRacingForTheLastLineRemainderCreditOnceAndTheWaiterIsRejectedWithTheLine()
    {
        // FI-SALES-CR-I08（FI-SALES-011 信用部分、FI-SALES-007）：發票行已沖 8/10，兩張銷退各要剩下的 2。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (1101, 1000m, 50m));
        returns.Items[1151] = Return(companyId, 1151, "sha256:p", CreditLine(1101, 11011, 8m, 800m, 40m, 10m, 1000m, 50m));
        Assert.Equal(SalesPostingOutcomeKind.Posted,
            (await PostCreditAsync(provider, CreditCommand(companyId, 1151, "sha256:p"), true, token)).Kind);
        returns.Items[1152] = Return(companyId, 1152, "sha256:a", CreditLine(1101, 11011, 2m, 200m, 10m, 10m, 1000m, 50m));
        returns.Items[1153] = Return(companyId, 1153, "sha256:b", CreditLine(1101, 11011, 2m, 200m, 10m, 10m, 1000m, 50m));
        var readsBefore = returns.Reads;

        var (first, second) = await CreditRaceAsync(provider, returns, 1152,
            CreditCommand(companyId, 1152, "sha256:a"), CreditCommand(companyId, 1153, "sha256:b"), token);

        Assert.Equal((SalesPostingOutcomeKind.Posted, true), (first.Kind, first.Changed));
        Assert.Equal(1152L, first.Receipt!.ReturnId);
        AssertCreditRejected(second, 422, SalesCreditPostingErrors.CreditExceedsLine, 1);
        Assert.Equal(readsBefore + 2, returns.Reads); // 等待者在第一方提交後才讀 SD
        Assert.Equal((2, 2, 3, "0"), await CreditStateAsync(companyId, token));
        Assert.Equal(1, await CreditKeysAsync(companyId, token));
    }

    [Fact]
    public async Task TwoConnectionsWithTheSameReturnClaimCreditOnceAndTheWaiterReplaysUnchanged()
    {
        // FI-SALES-CR-I09（FI-SALES-011 信用部分、FI-SALES-009）：同銷退同主張；等待者以 FI 來源鍵重放，不再讀 SD。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (1111, 1000m, 50m));
        returns.Items[1161] = Return(companyId, 1161, "sha256:s", CreditLine(1111, 11111, 3m, 300m, 15m, 10m, 1000m, 50m));

        var (first, second) = await CreditRaceAsync(provider, returns, 1161,
            CreditCommand(companyId, 1161, "sha256:s"), CreditCommand(companyId, 1161, "sha256:s"), token);

        Assert.Equal((SalesPostingOutcomeKind.Posted, true), (first.Kind, first.Changed));
        Assert.Equal((SalesPostingOutcomeKind.Unchanged, false, 200), (second.Kind, second.Changed, second.HttpStatus));
        Assert.Equal(first.Receipt! with { Allocations = [] }, second.Receipt! with { Allocations = [] });
        Assert.Equal(first.Receipt!.Allocations, second.Receipt!.Allocations);
        Assert.Equal(1, returns.Reads);
        Assert.Equal((1, 1, 2, "735"), await CreditStateAsync(companyId, token));
    }

    [Fact]
    public async Task TwoConnectionsWithDifferentReturnFingerprintsCreditOnceAndTheWaiterConflicts()
    {
        // FI-SALES-CR-I10（FI-SALES-011 信用部分、FI-SALES-010）：同銷退不同指紋；等待者 409，不讀 SD。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (1121, 1000m, 50m));
        returns.Items[1171] = Return(companyId, 1171, "sha256:s", CreditLine(1121, 11211, 1m, 100m, 5m, 10m, 1000m, 50m));

        var (first, second) = await CreditRaceAsync(provider, returns, 1171,
            CreditCommand(companyId, 1171, "sha256:s"), CreditCommand(companyId, 1171, "sha256:edited"), token);

        Assert.Equal(("sha256:s", SalesPostingOutcomeKind.Posted), (first.Receipt!.Fingerprint, first.Kind));
        AssertCreditRejected(second, 409, SalesPostingErrors.SourceConflict);
        Assert.Equal(1, returns.Reads);
        Assert.Equal((1, 1, 2, "945"), await CreditStateAsync(companyId, token));
    }

    [Fact]
    public async Task CallerFailureAfterCreditPostingRollsBackSourcesAllocationsAndOpenAndCanBeRetried()
    {
        // FI-SALES-CR-I11（FI-SALES-012）：Posted 後 SD 標記（THROW）或稽核 SaveChanges 失敗並回滾 →
        // 0 銷退來源、0 分配、傳票只剩發票、open 復原；之後重試成功。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        var returns = new FakeReturns();
        await using var provider = CreditProvider(companyId, invoices, returns);
        await SeedCreditAsync(provider, companyId, invoices, token, (1131, 1000m, 50m), (1132, 500m, 25m));
        returns.Items[1181] = Return(companyId, 1181, "sha256:f",
            CreditLine(1131, 11311, 2m, 200m, 10m, 10m, 1000m, 50m), CreditLine(1132, 11321, 1m, 100m, 5m, 5m, 500m, 25m));

        Func<MyErpDbContext, Task>[] failures =
        [
            db => db.Database.ExecuteSqlRawAsync("THROW 50001, 'SD return issue failed after FI credit.', 1;", token),
            db =>
            {
                // 同公司同客戶的信用鍵已由發票過帳建立 → 唯一鍵衝突。
                db.Set<MyERP.Modules.Finance.Domain.CreditKey>()
                    .Add(new MyERP.Modules.Finance.Domain.CreditKey { CustomerId = InvoiceCustomerId });
                return db.SaveChangesAsync(token);
            }
        ];
        foreach (var failure in failures)
        {
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var posted = await sp.GetRequiredService<ISalesCreditPostingService>()
                .PostInCurrentTransactionAsync(CreditCommand(companyId, 1181, "sha256:f"), token);
            Assert.Equal(SalesPostingOutcomeKind.Posted, posted.Kind);

            var error = await Record.ExceptionAsync(() => failure(db));
            Assert.True(error is SqlException or DbUpdateException, error?.ToString());
            await transaction.RollbackAsync(token);

            Assert.Equal((0, 0, 2, "1050|525"), await CreditStateAsync(companyId, token));
            Assert.Equal(1, await CreditKeysAsync(companyId, token));
        }

        var retried = await PostCreditAsync(provider, CreditCommand(companyId, 1181, "sha256:f"), true, token);
        Assert.Equal((SalesPostingOutcomeKind.Posted, true), (retried.Kind, retried.Changed));
        Assert.Equal((1, 2, 3, "840|420"), await CreditStateAsync(companyId, token));
    }
}
