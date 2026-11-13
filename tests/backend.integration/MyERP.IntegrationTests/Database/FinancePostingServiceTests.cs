using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-GL-SQL-001～010（WU-FI-004a3x1b2b2）：經 DI 組成的 <see cref="IGlPostingService"/>（FinanceModule＋MasterDataModule＋
/// 真實 DbContext＋測試用 <see cref="IGlPostingMappingVersionSource"/>）對真實 SQL 的過帳行為：首次過帳、同指紋重放、
/// 不同指紋 409、權威變更被拒零列、跨公司 404、無映射來源 500、並行同鍵、死結重跑、KILL 連線後查詢解決、未提交回滾（ADR-103）。
/// </summary>
public sealed partial class FinancePostingServiceTests(TestDatabase database)
{
    private const int MappingVersion = 3;
    private static readonly DateOnly BusinessDate = new(2026, 11, 6);

    private static long NewCompany() => Random.Shared.NextInt64(99100000, 99900000);

    /// <summary>每次 Resolve 時在嘗試自己的 scope 執行（鎖後、讀 MD 科目前），可注入延遲、死結或 KILL。</summary>
    private sealed class MappingState
    {
        public int Version = MappingVersion;
        public int Calls;
        public Func<MyErpDbContext, int, CancellationToken, Task>? Hook;
    }

    private sealed class TestMapping(MappingState state, MyErpDbContext db) : IGlPostingMappingVersionSource
    {
        public string SourceModule => "SD";
        public string SourceType => "Invoice";

        public async Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref state.Calls);
            if (state.Hook is { } hook)
                await hook(db, call, cancellationToken);
            return state.Version;
        }
    }

    private ServiceProvider Provider(long companyId, MappingState? mapping, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new MasterDataModule().AddServices(services, configuration);
        new FinanceModule().AddServices(services, configuration);
        var company = new FixedCompany(companyId);
        services.AddSingleton<ICurrentCompany>(company);
        services.AddSingleton<ICurrentUser>(new FixedUser(7));
        services.AddSingleton<IClock>(new FixedClock(new DateTime(2026, 11, 6, 9, 30, 0)));
        services.AddScoped(sp => new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(sp.GetRequiredService<IClock>(),
                    sp.GetRequiredService<ICurrentUser>(), company))).Options,
            sp.GetServices<IModuleDbModel>(), company));
        if (mapping is not null)
        {
            services.AddSingleton(mapping);
            services.AddScoped<IGlPostingMappingVersionSource, TestMapping>();
        }
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed record Setup(long CompanyId, long PeriodId, long CashId, long SalesId);

    /// <summary>MD 本位幣 TWD、科目 1101／4101（上層 1／4），FI 開放期間 2026-11。</summary>
    private static async Task<Setup> SeedAsync(ServiceProvider provider, long companyId, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<CurrencyService>().CreateAsync("TWD", "New Taiwan Dollar", "NT$", 2, true, token);
        var accounts = sp.GetRequiredService<AccountService>();
        var assets = await accounts.CreateAsync("1", "Assets", null, AccountType.Asset, null, token);
        var cash = await accounts.CreateAsync("1101", "現金", assets.Id, null, null, token);
        var revenue = await accounts.CreateAsync("4", "Revenue", null, AccountType.Revenue, null, token);
        var sales = await accounts.CreateAsync("4101", "Sales", revenue.Id, null, null, token);
        var period = await sp.GetRequiredService<GlPeriodWriteService>().CreateAsync(companyId,
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), true, token);
        return new(companyId, period.Id, cash.Id, sales.Id);
    }

    /// <summary>來源轉接器的做法：經 MD 契約取科目快照，以 FI 規則建立已檢查的分錄草稿。</summary>
    private static async Task<GlPostingRequest> RequestAsync(ServiceProvider provider, long companyId, long sourceId,
        string fingerprint = "sha256:a", CancellationToken token = default)
    {
        await using var scope = provider.CreateAsyncScope();
        var chart = scope.ServiceProvider.GetRequiredService<IChartOfAccounts>();
        var cash = await chart.FindAsync("1101", token);
        var sales = await chart.FindAsync("4101", token);
        var draft = GlJournalLineDraftRules.Build(companyId, MappingVersion,
        [
            new(GlAccountSnapshotRules.Validate(companyId, cash!.Id, cash.Code, MappingVersion, MappingVersion, cash), 120.5m, 0),
            new(GlAccountSnapshotRules.Validate(companyId, sales!.Id, sales.Code, MappingVersion, MappingVersion, sales), 0, 120.5m),
        ]);
        Assert.Equal(GlJournalLineDraftStatus.Allowed, draft.Status);
        return new(companyId, new(companyId, "SD", "Invoice", sourceId, 1, fingerprint), BusinessDate, "TWD", draft,
            MappingVersion);
    }

    private static async Task<GlPostingOutcome> PostAsync(ServiceProvider provider, GlPostingRequest request,
        CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IGlPostingService>().PostAsync(request, token);
    }

    private async Task<(int Keys, int Journals, int Lines)> CountsAsync(long companyId, CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var keys = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.gl_source_key WHERE company_id={companyId}").SingleAsync(token);
        var journals = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal WHERE company_id={companyId}").SingleAsync(token);
        var lines = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal_line WHERE company_id={companyId}").SingleAsync(token);
        return (keys, journals, lines);
    }

    private static void AssertRejected(GlPostingOutcome outcome, int status, string code)
    {
        Assert.Equal((GlPostingOutcomeKind.Rejected, false, status, code, (long?)null),
            (outcome.Kind, outcome.Changed, outcome.HttpStatus, outcome.ErrorCode, outcome.JournalId));
    }

    [Fact]
    public async Task CommittedPostingRowsCannotBeUpdatedOrDeletedByDirectSql()
    {
        // FI-GL-SQL-013: all three permanent tables reject both mutations, including a no-op UPDATE.
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 613, token: token);
        Assert.Equal(GlPostingOutcomeKind.Posted, (await PostAsync(provider, request, token)).Kind);

        foreach (var table in new[] { "gl_source_key", "gl_journal", "gl_journal_line" })
        {
            await using var db = database.CreateRawContext();
            // The identifier comes only from the fixed literal list above; company ID remains parameterized.
#pragma warning disable EF1003
            var update = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo." + table + " SET version = version WHERE company_id = @companyId",
                [new SqlParameter("@companyId", companyId)], token));
            Assert.Equal(51023, update.Number);
            var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM dbo." + table + " WHERE company_id = @companyId",
                [new SqlParameter("@companyId", companyId)], token));
#pragma warning restore EF1003
            // A referenced parent can be rejected by its FK (547) before its trigger runs.
            Assert.True(delete.Number is 51023 or 547, $"Unexpected SQL error {delete.Number}.");
            // SQL Server invokes AFTER triggers even when no row matches; zero-row writes stay harmless.
#pragma warning disable EF1003
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo." + table + " SET version = version WHERE company_id = @companyId",
                [new SqlParameter("@companyId", -1L)], token));
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM dbo." + table + " WHERE company_id = @companyId",
                [new SqlParameter("@companyId", -1L)], token));
#pragma warning restore EF1003
            Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
        }

        var replay = await PostAsync(provider, request, token);
        Assert.Equal(GlPostingOutcomeKind.Unchanged, replay.Kind);
        Assert.False(replay.Changed);
    }

    [Fact]
    public async Task FirstPostCommitsOneJournalAndSameFingerprintReplaysUnchangedWhileAnotherIsConflict()
    {
        // FI-GL-SQL-001／002／003
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        var setup = await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 601, token: token);

        var first = await PostAsync(provider, request, token);
        Assert.Equal((GlPostingOutcomeKind.Posted, true, 200), (first.Kind, first.Changed, first.HttpStatus));
        Assert.True(first.JournalId > 0);
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var journal = await db.Set<GlJournal>().AsNoTracking().SingleAsync(token);
            Assert.Equal((first.JournalId!.Value, setup.PeriodId, BusinessDate, "TWD", GlJournalStatus.Posted, 120.5m, 120.5m, 7L),
                (journal.Id, journal.PeriodId, journal.BusinessDate, journal.BaseCurrency, journal.Status,
                    journal.TotalDebit, journal.TotalCredit, journal.CreatedBy));
            var lines = await db.Set<GlJournalLine>().AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(token);
            Assert.Equal([(setup.CashId, "1101", "現金", MappingVersion, 120.5m, 0m), (setup.SalesId, "4101", "Sales", MappingVersion, 0m, 120.5m)],
                lines.Select(x => (x.AccountId, x.AccountCode, x.AccountName, x.MappingVersion, x.Debit, x.Credit)).ToArray());
            var key = await db.Set<GlSourceKey>().AsNoTracking().SingleAsync(token);
            Assert.Equal((journal.SourceKeyId, "SD", "Invoice", 601L, "sha256:a"),
                (key.Id, key.SourceModule, key.SourceType, key.SourceId, key.Fingerprint));
        }

        var replay = await PostAsync(provider, request, token);
        Assert.Equal(new GlPostingOutcome(GlPostingOutcomeKind.Unchanged, false, 200, JournalId: first.JournalId), replay);

        var conflict = await PostAsync(provider, await RequestAsync(provider, companyId, 601, "sha256:b", token), token);
        AssertRejected(conflict, 409, GlPostingErrors.SourceConflict);
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task ClosedPeriodChangedAccountsAndMappingVersionAreRejectedWithoutRows()
    {
        // FI-GL-SQL-004
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var mapping = new MappingState();
        await using var provider = Provider(companyId, mapping);
        var setup = await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 602, token: token);

        // 映射版本在草稿建立後變更。
        mapping.Version = MappingVersion + 1;
        AssertRejected(await PostAsync(provider, request, token), 409, GlPostingErrors.MappingChanged);
        mapping.Version = MappingVersion;

        // 期間關閉。
        await using (var scope = provider.CreateAsyncScope())
        {
            var periods = scope.ServiceProvider.GetRequiredService<GlPeriodWriteService>();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var version = (await db.Set<GlPeriod>().AsNoTracking().SingleAsync(x => x.Id == setup.PeriodId, token)).Version;
            var closed = await periods.UpdateAsync(companyId, setup.PeriodId, version, new DateOnly(2026, 11, 1),
                new DateOnly(2026, 11, 30), false, token);
            AssertRejected(await PostAsync(provider, request, token), 422, GlPostingErrors.PeriodClosed);
            await periods.UpdateAsync(companyId, setup.PeriodId, closed.Version, new DateOnly(2026, 11, 1),
                new DateOnly(2026, 11, 30), true, token);
        }

        // MD 科目改名（快照變更）→ 409；停用 → 422 並指出分錄行。
        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var chart = scope.ServiceProvider.GetRequiredService<IChartOfAccounts>();
            var sales = (await chart.FindAsync("4101", token))!;
            var renamed = await accounts.UpdateAsync(setup.SalesId, "Sales revenue", sales.BalanceDirection, true, sales.Version, token);
            AssertRejected(await PostAsync(provider, request, token), 409, GlPostingErrors.AccountChanged);

            var renamedRequest = await RequestAsync(provider, companyId, 602, token: token);
            await accounts.UpdateAsync(setup.SalesId, "Sales revenue", sales.BalanceDirection, false, renamed.Version, token);
            var inactive = await PostAsync(provider, renamedRequest, token);
            Assert.Equal((GlPostingOutcomeKind.Rejected, false, 422, GlPostingErrors.AccountInvalid, 2),
                (inactive.Kind, inactive.Changed, inactive.HttpStatus, inactive.ErrorCode, inactive.InvalidLine));
        }

        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task ForeignCompanyIs404AndMissingMappingSourceIs500WithoutRows()
    {
        // FI-GL-SQL-005／006
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 603, token: token);

        var otherCompany = companyId + 1;
        await using (var foreign = Provider(otherCompany, new MappingState()))
            AssertRejected(await PostAsync(foreign, request, token), 404, GlPostingErrors.SourceNotFound);

        await using (var unmapped = Provider(companyId, null))
        {
            var outcome = await PostAsync(unmapped, request, token);
            Assert.Equal((GlPostingOutcomeKind.Fault, false, 500, GlPostingErrors.InvalidDecision),
                (outcome.Kind, outcome.Changed, outcome.HttpStatus, outcome.ErrorCode));
        }

        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
        Assert.Equal((0, 0, 0), await CountsAsync(otherCompany, token));
    }

    [Fact]
    public async Task ConcurrentPostsOfSameSourceCreateOneJournal()
    {
        // FI-GL-SQL-007：鎖內延遲令兩個請求重疊；後到者等鎖後讀到已提交來源鍵。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var mapping = new MappingState { Hook = (_, _, t) => Task.Delay(400, t) };
        await using var provider = Provider(companyId, mapping);
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 604, token: token);

        var outcomes = await Task.WhenAll(
            Task.Run(() => PostAsync(provider, request, token), token),
            Task.Run(() => PostAsync(provider, request, token), token));

        var posted = Assert.Single(outcomes, x => x.Kind == GlPostingOutcomeKind.Posted);
        Assert.True(posted.Changed);
        Assert.Equal(new GlPostingOutcome(GlPostingOutcomeKind.Unchanged, false, 200, JournalId: posted.JournalId),
            Assert.Single(outcomes, x => x.Kind != GlPostingOutcomeKind.Posted));
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task PeriodMoveWaitsForPostingAndRereadsItsCommittedBusinessDate()
    {
        // FI-GL-SQL-011: the posting holds a shared company period lock before reading authority.
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var postingAtLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePosting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapping = new MappingState
        {
            Hook = async (_, _, t) =>
            {
                postingAtLock.SetResult();
                await releasePosting.Task.WaitAsync(t);
            },
        };
        await using var provider = Provider(companyId, mapping);
        var setup = await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 608, token: token);

        var posting = PostAsync(provider, request, token);
        try
        {
            await postingAtLock.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            var move = Task.Run(async () =>
            {
                await using var scope = provider.CreateAsyncScope();
                return await Assert.ThrowsAsync<GlPeriodWriteException>(() =>
                    scope.ServiceProvider.GetRequiredService<GlPeriodWriteService>().UpdateAsync(companyId,
                        setup.PeriodId, 1, new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 30), true, token));
            }, token);
            Assert.NotSame(move, await Task.WhenAny(move, Task.Delay(200, token)));
            releasePosting.SetResult();

            var posted = await posting.WaitAsync(TimeSpan.FromSeconds(20), token);
            Assert.Equal(GlPostingOutcomeKind.Posted, posted.Kind);
            Assert.Equal(GlPeriodWriteStatus.ExcludesPostedDate,
                (await move.WaitAsync(TimeSpan.FromSeconds(20), token)).Status);
        }
        finally
        {
            releasePosting.TrySetResult();
        }

        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var saved = await verify.Set<GlPeriod>().AsNoTracking().SingleAsync(x => x.Id == setup.PeriodId, token);
        Assert.Equal((new DateOnly(2026, 11, 1), 1), (saved.StartDate, saved.Version));
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task PostingWaitsForPeriodLockAndRejectsCommittedClosure()
    {
        // FI-GL-SQL-012: an exclusive period transaction wins; posting reads its committed state.
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        var setup = await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 609, token: token);
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync(token);
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(token);
        await using (var command = new SqlCommand("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51020, 'GL period lock was not granted.', 1;
            UPDATE dbo.gl_period SET status = 'Closed', version = version + 1
                WHERE company_id = @company AND id = @period;
            """, holder, transaction))
        {
            command.Parameters.AddWithValue("@resource", GlPostingWriter.PeriodLockResource(companyId));
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@period", setup.PeriodId);
            await command.ExecuteNonQueryAsync(token);
        }

        var posting = PostAsync(provider, request, token);
        Assert.NotSame(posting, await Task.WhenAny(posting, Task.Delay(200, token)));
        await transaction.CommitAsync(token);
        AssertRejected(await posting.WaitAsync(TimeSpan.FromSeconds(20), token), 422, GlPostingErrors.PeriodClosed);
        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task DeadlockVictimAttemptIsRolledBackAndRerunOnce()
    {
        // FI-GL-SQL-008：第一次嘗試在過帳交易內與另一連線形成真實死結（1205，本嘗試為犧牲者）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var table = $"##tst_gl_deadlock_{companyId}";
        var victimReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapping = new MappingState
        {
            Hook = async (db, call, t) =>
            {
                if (call != 1) return;
#pragma warning disable EF1002 // table name is a test-generated temp table, values are constants
                await db.Database.ExecuteSqlRawAsync($"SET DEADLOCK_PRIORITY LOW; UPDATE {table} SET n = 2 WHERE id = 2;", t);
                victimReady.SetResult();
                await db.Database.ExecuteSqlRawAsync($"UPDATE {table} SET n = 2 WHERE id = 1;", t);
#pragma warning restore EF1002
            },
        };
        await using var provider = Provider(companyId, mapping);
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 605, token: token);

        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync(token);
        await ExecuteAsync(holder, null, $"CREATE TABLE {table} (id int PRIMARY KEY, n int NOT NULL); INSERT {table} VALUES (1, 0), (2, 0);", token);
        await using (var transaction = (SqlTransaction)await holder.BeginTransactionAsync(token))
        {
            await ExecuteAsync(holder, transaction, $"SET DEADLOCK_PRIORITY HIGH; UPDATE {table} SET n = 1 WHERE id = 1;", token);
            var posting = PostAsync(provider, request, token);
            await victimReady.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            // 與嘗試相反次序取第二列 → 死結；優先序令過帳交易成為犧牲者，本連線得以繼續。
            await ExecuteAsync(holder, transaction, $"UPDATE {table} SET n = 1 WHERE id = 2;", token);
            await transaction.CommitAsync(token);

            var outcome = await posting;
            Assert.Equal((GlPostingOutcomeKind.Posted, true), (outcome.Kind, outcome.Changed));
        }

        Assert.Equal(2, mapping.Calls);
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task KilledConnectionIsResolvedFromCommittedLookupThenRerun()
    {
        // FI-GL-SQL-009：鎖後 KILL 過帳連線（嚴重度 ≥20）→ 連線中斷 → 查詢已提交來源鍵（無）→ 重跑一次成功。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var mapping = new MappingState
        {
            Hook = async (db, call, t) =>
            {
                if (call != 1) return;
                var spid = await db.Database.SqlQuery<short>($"SELECT @@SPID AS [Value]").SingleAsync(t);
                await using var killer = new SqlConnection(database.ConnectionString);
                await killer.OpenAsync(t);
                await ExecuteAsync(killer, null, $"KILL {spid}", t);
            },
        };
        await using var provider = Provider(companyId, mapping);
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 606, token: token);

        var outcome = await PostAsync(provider, request, token);

        Assert.Equal((GlPostingOutcomeKind.Posted, true, 200), (outcome.Kind, outcome.Changed, outcome.HttpStatus));
        Assert.Equal(2, mapping.Calls);
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task DisposingAnUncommittedSessionRollsBackAndReleasesLocks()
    {
        // FI-GL-SQL-010
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, new MappingState());
        await SeedAsync(provider, companyId, token);
        var request = await RequestAsync(provider, companyId, 607, token: token);

        await using (var scope = provider.CreateAsyncScope())
        {
            var port = scope.ServiceProvider.GetRequiredService<IGlPostingTransactionPort>();
            var session = await port.BeginAsync(request, token);
            Assert.NotNull(session);
            await using (session)
            {
                await session.LockAsync(token);
                var authority = await session.ReadAuthorityAsync(token);
                Assert.NotNull(authority);
                Assert.Null(authority.Committed);
                var decision = GlPostingDecisionRules.Decide(companyId, request.Source, authority.Committed,
                    request.BusinessDate, request.BaseCurrency, authority.CompanyBaseCurrency, authority.Periods,
                    request.Draft, request.ExpectedMappingVersion, authority.CurrentMappingVersion, authority.CurrentAccounts);
                Assert.Equal(GlPostingDecisionStatus.Ready, decision.Status);
                var plan = GlPostingWritePlanRules.Build(companyId, decision, request.Source, request.BusinessDate,
                    authority.CompanyBaseCurrency, request.Draft);
                Assert.True(await session.WriteAsync(plan, token) > 0);
            }

            var lookup = await port.LookupCommittedAsync(request.Source, token);
            Assert.Equal(new GlCommittedLookup(true, null), lookup);
        }

        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
        // 鎖已隨回滾釋放：同一來源可立即過帳。
        var outcome = await PostAsync(provider, request, token);
        Assert.Equal((GlPostingOutcomeKind.Posted, true), (outcome.Kind, outcome.Changed));
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql,
        CancellationToken token)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
