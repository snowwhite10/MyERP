using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-COLL-RACE-001～007（WU-FI-005a3，ADR-125，FI-AR-COLL-011／012／023）：兩條真實 SQL 連線同時核銷同一張收款或同一張
/// 發票時，信用鍵 <c>ar_credit:{公司}:{客戶}</c> 與其後的收款／應收／核銷鍵（BR-FI-036）序列化；等待者在鎖後重讀餘額，
/// 輸者 422（收款不足／分配無效）或同主張重放，絕不超額核銷；呼叫者在過帳後的 SD／稽核寫入失敗並回滾 → 收款、核銷、
/// 分配、傳票、GL 來源鍵與餘額全部還原，之後可再過帳；收款或應收鍵逾時 409 且呼叫者交易仍開啟。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private ServiceProvider CollectionRaceProvider(long companyId, FakeCollectionReceipts receipts,
        FakeCollectionWriteOffs writeOffs, int lockTimeoutMilliseconds = 15_000) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(receipts);
            services.AddScoped<IFinanceCollectionReceiptSource, FakeCollectionReceiptSource>();
            services.AddSingleton(writeOffs);
            services.AddScoped<IFinanceCollectionWriteOffSource, FakeCollectionWriteOffSource>();
            services.AddScoped<ICollectionReceiptPostingService>(sp =>
            {
                var service = ActivatorUtilities.CreateInstance<CollectionReceiptPostingService>(sp);
                service.LockTimeoutMilliseconds = lockTimeoutMilliseconds;
                return service;
            });
            services.AddScoped<ICollectionWriteOffPostingService>(sp =>
            {
                var service = ActivatorUtilities.CreateInstance<CollectionWriteOffPostingService>(sp);
                service.LockTimeoutMilliseconds = lockTimeoutMilliseconds;
                return service;
            });
        });

    /// <summary>第一條連線在讀核銷單據時停住（已持有信用鍵、尚未鎖收款或寫入），直到測試釋放。</summary>
    private static Gate HoldFirstWriteOffRead(FakeCollectionWriteOffs writeOffs, long settlementId)
    {
        var gate = new Gate();
        var held = 0;
        writeOffs.OnRead = (id, _, token) => id == settlementId && Interlocked.Exchange(ref held, 1) == 0
            ? gate.HoldAsync(token)
            : Task.CompletedTask;
        return gate;
    }

    /// <summary>停住第一條連線，啟動第二條，證明第二條在信用鍵等待（未讀單據），再釋放。</summary>
    private static async Task<(CollectionWriteOffPostingResult First, CollectionWriteOffPostingResult Second)>
        WriteOffRaceAsync(ServiceProvider provider, FakeCollectionWriteOffs writeOffs, Gate gate,
            CollectionWriteOffPostingCommand first, CollectionWriteOffPostingCommand second, CancellationToken token)
    {
        var firstTask = Task.Run(() => PostWriteOffAsync(provider, first, commit: true, token), token);
        await gate.Entered.Task.WaitAsync(RaceTimeout, token);
        var readsWhileHeld = writeOffs.Reads;
        var secondTask = Task.Run(() => PostWriteOffAsync(provider, second, commit: true, token), token);

        await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromMilliseconds(700), token));
        Assert.False(secondTask.IsCompleted, "the second connection must wait on the FI credit key");
        Assert.Equal(readsWhileHeld, writeOffs.Reads);

        gate.Release.SetResult();
        return (await firstTask.WaitAsync(RaceTimeout, token), await secondTask.WaitAsync(RaceTimeout, token));
    }

    /// <summary>另一條連線在交易內持有 <paramref name="resource"/>（Exclusive），回傳該交易供測試結束時回滾。</summary>
    private async Task<(SqlConnection Connection, SqlTransaction Transaction)> HoldAppLockAsync(string resource,
        CancellationToken token)
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(token);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var command = new SqlCommand("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
            SELECT @result;
            """, connection, transaction);
        command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = resource;
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync(token))!);
        return (connection, transaction);
    }

    private async Task<int> CollectionGlSourceKeysAsync(long companyId, string sourceType, CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM dbo.gl_source_key WHERE company_id={companyId} AND source_type={sourceType}")
            .SingleAsync(token);
    }

    [Fact]
    public async Task TwoConnectionsWritingOffTheSameReceiptSerializeAndTheLoserCannotOverApplyIt()
    {
        // FI-COLL-RACE-001（FI-AR-COLL-011：同收款；收款 1,000 兩張核銷各 600 → 一張過帳、另一張在鎖後重讀未核銷 400 → 422）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = CollectionRaceProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5101, 600m, token: token);
        var b = await AddReceivableAsync(provider, 5102, 600m, token: token);
        writeOffs.Items[2101] = WriteOff(companyId, 2101, [(a, 600m)]);
        writeOffs.Items[2102] = WriteOff(companyId, 2102, [(b, 600m)]);
        var gate = HoldFirstWriteOffRead(writeOffs, 2101);

        var (first, second) = await WriteOffRaceAsync(provider, writeOffs, gate, WriteOffCommand(companyId, 2101),
            WriteOffCommand(companyId, 2102), token);

        Assert.Equal((CollectionPostingOutcomeKind.Posted, true, 400m),
            (first.Kind, first.Changed, first.Receipt!.ReceiptUnappliedAmount));
        AssertWriteOffRejected(second, 422, CollectionWriteOffPostingErrors.ReceiptInsufficient);
        Assert.Equal((1, 1, 2, 400m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([0m, 600m], await OpenAmountsAsync(companyId, token, a, b));
        Assert.Equal(1, await CollectionGlSourceKeysAsync(companyId, "CollectionWriteOff", token));
    }

    [Fact]
    public async Task TwoConnectionsWritingOffTheSameInvoiceSerializeAndTheLoserCannotOverSettleIt()
    {
        // FI-COLL-RACE-002（FI-AR-COLL-011：同發票；應收 500 兩張核銷各 300 → 一張過帳、另一張重讀未清 200 → 422 第 1 行）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = CollectionRaceProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5111, 500m, token: token);
        writeOffs.Items[2111] = WriteOff(companyId, 2111, [(a, 300m)]);
        writeOffs.Items[2112] = WriteOff(companyId, 2112, [(a, 300m)]);
        var gate = HoldFirstWriteOffRead(writeOffs, 2111);

        var (first, second) = await WriteOffRaceAsync(provider, writeOffs, gate, WriteOffCommand(companyId, 2111),
            WriteOffCommand(companyId, 2112), token);

        Assert.Equal(CollectionPostingOutcomeKind.Posted, first.Kind);
        Assert.Equal([new CollectionWriteOffAllocationReceipt(a, 300m, 200m)], first.Receipt!.Allocations);
        AssertWriteOffRejected(second, 422, CollectionWriteOffPostingErrors.AllocationInvalid);
        Assert.Equal([1], second.LineErrors!.Select(x => x.Line).ToArray());
        Assert.Equal((1, 1, 2, 700m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([200m], await OpenAmountsAsync(companyId, token, a));

        // 輸者改以剛好的未清餘額（200）另開核銷即可過帳，應收歸零（無超額）。
        writeOffs.Items[2113] = WriteOff(companyId, 2113, [(a, 200m)]);
        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2113), true, token)).Kind);
        Assert.Equal([0m], await OpenAmountsAsync(companyId, token, a));
        Assert.Equal((2, 2, 3, 500m), await WriteOffStateAsync(companyId, token));
    }

    [Fact]
    public async Task TwoConnectionsPostingTheSameWriteOffClaimCreateOneSettlementAndTheWaiterReplays()
    {
        // FI-COLL-RACE-003（FI-AR-COLL-009／011、BR-FI-038：同核銷同主張 → 一筆核銷，等待者 Unchanged 且不再讀單據）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = CollectionRaceProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5121, 500m, token: token);
        writeOffs.Items[2121] = WriteOff(companyId, 2121, [(a, 250m)]);
        var gate = HoldFirstWriteOffRead(writeOffs, 2121);

        var (first, second) = await WriteOffRaceAsync(provider, writeOffs, gate, WriteOffCommand(companyId, 2121),
            WriteOffCommand(companyId, 2121), token);

        Assert.Equal((CollectionPostingOutcomeKind.Posted, true), (first.Kind, first.Changed));
        Assert.Equal((CollectionPostingOutcomeKind.Unchanged, false, 200),
            (second.Kind, second.Changed, second.HttpStatus));
        Assert.Equal(first.Receipt!.CollectionSettlementId, second.Receipt!.CollectionSettlementId);
        Assert.Equal(first.Receipt.Allocations, second.Receipt.Allocations);
        Assert.Equal(1, writeOffs.Reads);
        Assert.Equal((1, 1, 2, 750m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([250m], await OpenAmountsAsync(companyId, token, a));
    }

    [Fact]
    public async Task CallerFailureAfterWriteOffPostingRestoresBalancesAndTheWriteOffCanBeRetried()
    {
        // FI-COLL-RACE-004（FI-AR-COLL-012／023、BR-FI-037：核銷過帳後 (a) SD 標記 THROW 或 (b) 稽核／重複鍵寫入失敗，
        // 呼叫者回滾 → 0 核銷、0 分配、核銷傳票與 GL 來源鍵皆無、收款未核銷與應收未清還原；之後可再過帳）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = CollectionRaceProvider(companyId, receipts, writeOffs);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5131, 500m, token: token);
        var b = await AddReceivableAsync(provider, 5132, 300m, token: token);
        writeOffs.Items[2131] = WriteOff(companyId, 2131, [(a, 400m), (b, 300m)]);

        Func<MyErpDbContext, Task>[] failures =
        [
            db => db.Database.ExecuteSqlRawAsync("THROW 50001, 'SD settlement update failed after FI posting.', 1;", token),
            async db =>
            {
                db.Set<CreditKey>().Add(new CreditKey { CustomerId = CollectionCustomerId });
                await db.SaveChangesAsync(token);
            }
        ];
        foreach (var failure in failures)
        {
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var posted = await sp.GetRequiredService<ICollectionWriteOffPostingService>()
                .PostInCurrentTransactionAsync(WriteOffCommand(companyId, 2131), token);
            Assert.Equal((CollectionPostingOutcomeKind.Posted, 300m),
                (posted.Kind, posted.Receipt!.ReceiptUnappliedAmount));

            var error = await Record.ExceptionAsync(() => failure(db));
            Assert.True(error is SqlException or DbUpdateException, error?.ToString());
            await transaction.RollbackAsync(token);

            Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
            Assert.Equal([500m, 300m], await OpenAmountsAsync(companyId, token, a, b));
            Assert.Equal(0, await CollectionGlSourceKeysAsync(companyId, "CollectionWriteOff", token));
        }

        var retried = await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2131), commit: true, token);
        Assert.Equal((CollectionPostingOutcomeKind.Posted, true), (retried.Kind, retried.Changed));
        Assert.Equal((1, 2, 2, 300m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([100m, 0m], await OpenAmountsAsync(companyId, token, a, b));
        Assert.Equal(1, await CollectionGlSourceKeysAsync(companyId, "CollectionWriteOff", token));
    }

    [Fact]
    public async Task CallerFailureAfterReceiptPostingRollsBackTheReceiptJournalAndCreditKeyAndItCanBeRetried()
    {
        // FI-COLL-RACE-005（FI-AR-COLL-023：收款過帳後 SD 標記失敗 → 0 收款、0 傳票、0 GL 來源鍵、0 信用鍵；之後可再過帳）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = CollectionRaceProvider(companyId, receipts, writeOffs);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[1141] = CollectionReceipt(companyId, 1141, setup.BankId, 800m, "TT-1141");

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            Assert.Equal(CollectionPostingOutcomeKind.Posted, (await sp.GetRequiredService<ICollectionReceiptPostingService>()
                .PostInCurrentTransactionAsync(CollectionCommand(companyId, 1141), token)).Kind);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "THROW 50001, 'SD receipt update failed after FI posting.', 1;", token));
            await transaction.RollbackAsync(token);
        }
        Assert.Equal((0, 0, 0, 0), await CollectionCountsAsync(companyId, token));
        Assert.Equal(0, await CollectionGlSourceKeysAsync(companyId, "SalesCollection", token));
        await using (var raw = database.CreateRawContext())
        {
            Assert.Equal(0, await raw.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM dbo.ar_credit_key WHERE company_id={companyId}").SingleAsync(token));
        }

        var retried = await PostCollectionAsync(provider, CollectionCommand(companyId, 1141), commit: true, token);
        Assert.Equal((CollectionPostingOutcomeKind.Posted, true), (retried.Kind, retried.Changed));
        Assert.Equal((1, 1, 2, 0), await CollectionCountsAsync(companyId, token));
        Assert.Equal(1, await CollectionGlSourceKeysAsync(companyId, "SalesCollection", token));
    }

    [Fact]
    public async Task HeldReceiptOrReceivableKeyTimesOutTheWriteOffAsANamedConflictWithoutRows()
    {
        // FI-COLL-RACE-006（BR-FI-036：收款鍵或應收鍵被他交易持有 → 409 FI.COLLECTION_WRITEOFF.LOCK_TIMEOUT，
        // 呼叫者交易仍開啟（@@TRANCOUNT=1）、0 列；持有者結束後照常過帳）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = CollectionRaceProvider(companyId, receipts, writeOffs, lockTimeoutMilliseconds: 300);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5151, 500m, token: token);
        writeOffs.Items[2151] = WriteOff(companyId, 2151, [(a, 100m)]);

        string[] heldKeys =
        [
            CollectionReceiptPostingService.SourceLockResource(companyId, WriteOffReceiptId),
            CollectionWriteOffPostingService.ReceivableLockResource(companyId, a)
        ];
        foreach (var heldKey in heldKeys)
        {
            var (holderConnection, holder) = await HoldAppLockAsync(heldKey, token);
            await using (holderConnection)
            await using (holder)
            {
                await using var scope = provider.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var db = sp.GetRequiredService<MyErpDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
                var timedOut = await sp.GetRequiredService<ICollectionWriteOffPostingService>()
                    .PostInCurrentTransactionAsync(WriteOffCommand(companyId, 2151), token);
                AssertWriteOffRejected(timedOut, 409, CollectionWriteOffPostingErrors.LockTimeout);
                Assert.Same(transaction, db.Database.CurrentTransaction);
                Assert.Equal(1, await TranCountAsync(db, token));
                await transaction.CommitAsync(token);
                await holder.RollbackAsync(token);
            }
            Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
            Assert.Equal([500m], await OpenAmountsAsync(companyId, token, a));
        }

        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostWriteOffAsync(provider, WriteOffCommand(companyId, 2151), true, token)).Kind);
        Assert.Equal((1, 1, 2, 900m), await WriteOffStateAsync(companyId, token));
    }

    [Fact]
    public async Task HeldReceiptSourceKeyTimesOutTheReceiptPostingBeforeTheDocumentRead()
    {
        // FI-COLL-RACE-007（BR-FI-036：收款來源鍵被他交易持有 → 409 FI.COLLECTION_POSTING.LOCK_TIMEOUT，不讀單據、0 列、
        // 呼叫者交易仍開啟；持有者結束後照常過帳）
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var (receipts, writeOffs) = (new FakeCollectionReceipts(), new FakeCollectionWriteOffs());
        await using var provider = CollectionRaceProvider(companyId, receipts, writeOffs, lockTimeoutMilliseconds: 300);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        receipts.Items[1161] = CollectionReceipt(companyId, 1161, setup.BankId, 800m, "TT-1161");

        var (holderConnection, holder) = await HoldAppLockAsync(
            CollectionReceiptPostingService.SourceLockResource(companyId, 1161), token);
        await using (holderConnection)
        await using (holder)
        {
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var timedOut = await sp.GetRequiredService<ICollectionReceiptPostingService>()
                .PostInCurrentTransactionAsync(CollectionCommand(companyId, 1161), token);
            AssertCollectionRejected(timedOut, 409, CollectionPostingErrors.LockTimeout);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            Assert.Equal(1, await TranCountAsync(db, token));
            await transaction.CommitAsync(token);
            await holder.RollbackAsync(token);
        }
        Assert.Equal(0, receipts.Reads);
        Assert.Equal((0, 0, 0, 0), await CollectionCountsAsync(companyId, token));

        Assert.Equal(CollectionPostingOutcomeKind.Posted,
            (await PostCollectionAsync(provider, CollectionCommand(companyId, 1161), true, token)).Kind);
        Assert.Equal((1, 1, 2, 0), await CollectionCountsAsync(companyId, token));
    }
}
