using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-GL-JOIN-001～004（WU-FI-002a2a，ADR-107）：<see cref="IGlJoinedPostingService"/> 在呼叫者已開啟的 SQL 交易內過帳。
/// 與信用鍵同交易原子提交或回滾；服務本身不提交；重放與衝突在後續呼叫者交易判斷；拒絕保留呼叫者交易；
/// 任何 Fault 回滾整個呼叫者交易（BR-FI-013）。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private const long JoinedCustomerId = 501;

    private async Task<int> CreditKeysAsync(long companyId, CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_credit_key WHERE company_id={companyId}").SingleAsync(token);
    }

    private static Task<int> TranCountAsync(MyErpDbContext db, CancellationToken token) =>
        db.Database.SqlQuery<int>($"SELECT @@TRANCOUNT AS [Value]").SingleAsync(token);

    [Fact]
    public async Task JoinedPostingIsAtomicWithCallerWorkAndCallerRollbackLeavesNoRows()
    {
        // FI-GL-JOIN-001
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 701, token: token);

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await sp.GetRequiredService<ICustomerCreditService>()
                .AcquireCreditLockAsync(companyId, JoinedCustomerId, transaction.GetDbTransaction(), token);

            var outcome = await sp.GetRequiredService<IGlJoinedPostingService>().PostInCurrentTransactionAsync(request, token);

            Assert.Equal((GlPostingOutcomeKind.Posted, true, 200), (outcome.Kind, outcome.Changed, outcome.HttpStatus));
            Assert.True(outcome.JournalId > 0);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            Assert.Equal(1, await TranCountAsync(db, token));
            Assert.Equal(2, await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal_line WHERE company_id={companyId}").SingleAsync(token));
            await transaction.RollbackAsync(token);
        }

        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
        Assert.Equal(0, await CreditKeysAsync(companyId, token));

        // 回滾不會佔用永久來源鍵：之後的獨立過帳正常寫入。
        Assert.Equal(GlPostingOutcomeKind.Posted, (await PostAsync(provider, request, token)).Kind);
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task JoinedPostingCommitsOnlyWithTheCallerAndLaterCallsReplayOrConflict()
    {
        // FI-GL-JOIN-002
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 702, token: token);

        long journalId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await sp.GetRequiredService<ICustomerCreditService>()
                .AcquireCreditLockAsync(companyId, JoinedCustomerId, transaction.GetDbTransaction(), token);
            var outcome = await sp.GetRequiredService<IGlJoinedPostingService>().PostInCurrentTransactionAsync(request, token);
            Assert.Equal(GlPostingOutcomeKind.Posted, outcome.Kind);
            journalId = outcome.JournalId!.Value;
            await transaction.CommitAsync(token);
        }
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
        Assert.Equal(1, await CreditKeysAsync(companyId, token));

        var conflictRequest = await RequestAsync(provider, companyId, 702, "sha256:b", token);
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            var joined = sp.GetRequiredService<IGlJoinedPostingService>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);

            var replay = await joined.PostInCurrentTransactionAsync(request, token);
            Assert.Equal(new GlPostingOutcome(GlPostingOutcomeKind.Unchanged, false, 200, JournalId: journalId), replay);
            Assert.Same(transaction, db.Database.CurrentTransaction);

            // 同交易內再次以不同指紋過帳：409，交易仍由呼叫者持有（拒絕不回滾）。
            AssertRejected(await joined.PostInCurrentTransactionAsync(conflictRequest, token), 409,
                GlPostingErrors.SourceConflict);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            Assert.Equal(1, await TranCountAsync(db, token));
            await transaction.RollbackAsync(token);
        }
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task JoinedPostingRequiresACallerTransactionAndRejectionsKeepItOpenWithoutRows()
    {
        // FI-GL-JOIN-003
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 703, token: token);

        await using (var scope = provider.CreateAsyncScope())
        {
            var joined = scope.ServiceProvider.GetRequiredService<IGlJoinedPostingService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => joined.PostInCurrentTransactionAsync(request, token));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            var joined = sp.GetRequiredService<IGlJoinedPostingService>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);

            var other = companyId + 1;
            AssertRejected(await joined.PostInCurrentTransactionAsync(
                request with { CompanyId = other, Source = request.Source with { CompanyId = other } }, token),
                404, GlPostingErrors.SourceNotFound);
            AssertRejected(await joined.PostInCurrentTransactionAsync(
                request with { BusinessDate = new DateOnly(2027, 1, 5) }, token), 422, GlPostingErrors.PeriodMissing);
            AssertRejected(await joined.PostInCurrentTransactionAsync(request with { CompanyId = 0 }, token), 400,
                GlPostingErrors.InvalidInput);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            Assert.Equal(1, await TranCountAsync(db, token));
            await transaction.CommitAsync(token);
        }
        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task JoinedFaultRollsBackTheWholeCallerTransaction()
    {
        // FI-GL-JOIN-004：呼叫者留下未儲存的追蹤變更 → 寫入前失敗 → 500 WRITE_FAILED，連同已鎖的信用鍵一起回滾。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 704, token: token);

        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await sp.GetRequiredService<ICustomerCreditService>()
                .AcquireCreditLockAsync(companyId, JoinedCustomerId, transaction.GetDbTransaction(), token);
            db.Set<CreditKey>().Add(new CreditKey { CustomerId = JoinedCustomerId + 1 });

            var outcome = await sp.GetRequiredService<IGlJoinedPostingService>().PostInCurrentTransactionAsync(request, token);

            Assert.Equal((GlPostingOutcomeKind.Fault, false, 500, GlPostingErrors.WriteFailed, (long?)null),
                (outcome.Kind, outcome.Changed, outcome.HttpStatus, outcome.ErrorCode, outcome.JournalId));
            Assert.Null(db.Database.CurrentTransaction);
            db.ChangeTracker.Clear();
            Assert.Equal(0, await TranCountAsync(db, token));
            await transaction.DisposeAsync();
        }

        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
        Assert.Equal(0, await CreditKeysAsync(companyId, token));
    }
}
