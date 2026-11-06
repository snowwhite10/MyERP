using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-GL-WRITE-001～007（WU-FI-004a3w）：過帳寫入器在同一 SQL 交易內持有期間共用鎖與永久來源鍵獨占鎖，
/// 依 004a3r 寫入計畫依序插入 source key → journal → lines；任何失敗整筆回滾（BR-FI-027／028）。
/// 權威讀取、決策與對外結果屬 004a3x，不在本測試。
/// </summary>
public sealed class FinancePostingWriterTests(TestDatabase database)
{
    private static readonly FixedClock Clock = new(new DateTime(2026, 11, 6, 9, 30, 0));
    private static readonly FixedUser User = new(7);
    private static readonly DateOnly BusinessDate = new(2026, 11, 6);

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(Clock, User, company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new GlPeriodDbModel(), new GlJournalDbModel()], company);
    }

    private static long NewCompany() => Random.Shared.NextInt64(98100000, 98900000);

    private static GlPostingSource Source(long companyId, long sourceId, string fingerprint = "sha256:a") =>
        new(companyId, "SD", "Invoice", sourceId, 1, fingerprint);

    private async Task<long> OpenPeriodAsync(long companyId, CancellationToken token)
    {
        await using var db = Context(companyId);
        var period = await new GlPeriodWriteService(db, Clock, User).CreateAsync(companyId,
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), true, token);
        return period.Id;
    }

    private static GlPostingWritePlan Plan(long companyId, long periodId, GlPostingSource source)
    {
        var plan = GlPostingWritePlanRules.Build(companyId,
            new GlPostingDecision(GlPostingDecisionStatus.Ready, PeriodId: periodId), source, BusinessDate, "TWD",
            new GlJournalLineDraftResult(GlJournalLineDraftStatus.Allowed,
                [new(1, 11, "1101", "現金", 2, 3, 120.5m, 0), new(2, 12, "4101", "Sales", 1, 3, 0, 120.5m)],
                120.5m, 120.5m));
        Assert.Equal(GlPostingWritePlanStatus.Ready, plan.Status);
        return plan;
    }

    private async Task<(int Keys, int Journals, int Lines)> CountsAsync(long companyId, CancellationToken token)
    {
        await using var db = Context(companyId);
        var keys = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.gl_source_key WHERE company_id={companyId}").SingleAsync(token);
        var journals = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal WHERE company_id={companyId}").SingleAsync(token);
        var lines = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal_line WHERE company_id={companyId}").SingleAsync(token);
        return (keys, journals, lines);
    }

    private static void AssertNothingTracked(MyErpDbContext db)
    {
        Assert.Empty(db.ChangeTracker.Entries<GlSourceKey>());
        Assert.Empty(db.ChangeTracker.Entries<GlJournal>());
        Assert.Empty(db.ChangeTracker.Entries<GlJournalLine>());
    }

    [Fact]
    public async Task WritesSourceKeyJournalAndLinesInOrderWithinOneLockedTransaction()
    {
        // FI-GL-WRITE-001
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var periodId = await OpenPeriodAsync(companyId, token);
        var source = Source(companyId, 501);

        GlPostingWriteReceipt receipt;
        await using (var db = Context(companyId))
        {
            var writer = new GlPostingWriter(db);
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await writer.LockAsync(companyId, source, cancellationToken: token);
            receipt = await writer.WriteAsync(Plan(companyId, periodId, source), token);
            await transaction.CommitAsync(token);
        }

        await using var reader = Context(companyId);
        var key = await reader.Set<GlSourceKey>().AsNoTracking().SingleAsync(token);
        Assert.Equal((receipt.SourceKeyId, "SD", "Invoice", 501L, 1, "sha256:a", 7L, Clock.LocalNow, 1),
            (key.Id, key.SourceModule, key.SourceType, key.SourceId, key.SourceVersion, key.Fingerprint,
                key.CreatedBy, key.CreatedAt, key.Version));
        var journal = await reader.Set<GlJournal>().AsNoTracking().SingleAsync(token);
        Assert.Equal((receipt.JournalId, periodId, receipt.SourceKeyId, BusinessDate, "TWD", GlJournalStatus.Posted,
                120.5m, 120.5m, 7L, 1),
            (journal.Id, journal.PeriodId, journal.SourceKeyId, journal.BusinessDate, journal.BaseCurrency,
                journal.Status, journal.TotalDebit, journal.TotalCredit, journal.CreatedBy, journal.Version));
        var lines = await reader.Set<GlJournalLine>().AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(token);
        Assert.Equal(receipt.LineIds, lines.Select(x => x.Id).ToArray());
        Assert.Collection(lines,
            line => Assert.Equal((receipt.JournalId, 1, 11L, "1101", "現金", 2, 3, 120.5m, 0m, 7L, 1),
                (line.JournalId, line.Sequence, line.AccountId, line.AccountCode, line.AccountName,
                    line.AccountVersion, line.MappingVersion, line.Debit, line.Credit, line.CreatedBy, line.Version)),
            line => Assert.Equal((receipt.JournalId, 2, 12L, "4101", "Sales", 1, 3, 0m, 120.5m, 7L, 1),
                (line.JournalId, line.Sequence, line.AccountId, line.AccountCode, line.AccountName,
                    line.AccountVersion, line.MappingVersion, line.Debit, line.Credit, line.CreatedBy, line.Version)));
    }

    [Fact]
    public async Task RejectsMissingTransactionMissingLocksAndUnusablePlansWithoutSql()
    {
        // FI-GL-WRITE-002
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var periodId = await OpenPeriodAsync(companyId, token);
        var source = Source(companyId, 502);
        await using var db = Context(companyId);
        var writer = new GlPostingWriter(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(Plan(companyId, periodId, source), token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.LockAsync(companyId, source, cancellationToken: token));

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            // 未持鎖、只持另一來源的鎖都不可寫入。
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(Plan(companyId, periodId, source), token));
            await writer.LockAsync(companyId, Source(companyId, 999), cancellationToken: token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(Plan(companyId, periodId, source), token));
            await writer.LockAsync(companyId, source, cancellationToken: token);

            // 計畫不是 Ready、屬於其他公司、或已寫過（有 ID）都拒絕。
            var notReady = GlPostingWritePlanRules.Build(companyId, new(GlPostingDecisionStatus.Unchanged, JournalId: 5),
                source, BusinessDate, "TWD", null);
            await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(notReady, token));
            await Assert.ThrowsAsync<ArgumentNullException>(() => writer.WriteAsync(null!, token));
            var foreign = Plan(companyId + 1, periodId, Source(companyId + 1, 502));
            await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(foreign, token));
            var reused = Plan(companyId, periodId, source);
            reused.Journal!.Id = 44;
            await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(reused, token));
            // 鎖請求本身拒絕其他公司或無效來源。
            await Assert.ThrowsAsync<ArgumentException>(() => writer.LockAsync(companyId + 1, Source(companyId + 1, 502), cancellationToken: token));
            await Assert.ThrowsAsync<ArgumentException>(() => writer.LockAsync(companyId, Source(companyId, 0), cancellationToken: token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => writer.LockAsync(companyId, source, 0, token));
        }

        AssertNothingTracked(db);
        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task ConstraintFailureRollsBackWholeTransactionClearsTrackingAndContextStaysUsable()
    {
        // FI-GL-WRITE-003
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var periodId = await OpenPeriodAsync(companyId, token);
        var source = Source(companyId, 503);
        await using var db = Context(companyId);
        var writer = new GlPostingWriter(db);

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await writer.LockAsync(companyId, source, cancellationToken: token);
            var broken = Plan(companyId, periodId, source);
            broken.Lines[1].AccountName = " "; // 違反 ck_gl_journal_line_account_snapshot，於第三步（分錄）才失敗
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => writer.WriteAsync(broken, token));
            Assert.Equal(547, GlPostingWriter.SqlErrorNumber(failure));
            Assert.Null(db.Database.CurrentTransaction);
        }

        AssertNothingTracked(db);
        Assert.Equal((0, 0, 0), await CountsAsync(companyId, token));

        await using (var retry = await db.Database.BeginTransactionAsync(token))
        {
            await writer.LockAsync(companyId, source, cancellationToken: token);
            await writer.WriteAsync(Plan(companyId, periodId, source), token);
            await retry.CommitAsync(token);
        }
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
    }

    [Fact]
    public async Task CommittedSameSourceKeyMakesSecondWriterFailWithUniqueViolationAndNoExtraRows()
    {
        // FI-GL-WRITE-004
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var periodId = await OpenPeriodAsync(companyId, token);
        await using (var first = Context(companyId))
        {
            var writer = new GlPostingWriter(first);
            await using var transaction = await first.Database.BeginTransactionAsync(token);
            await writer.LockAsync(companyId, Source(companyId, 504), cancellationToken: token);
            await writer.WriteAsync(Plan(companyId, periodId, Source(companyId, 504)), token);
            await transaction.CommitAsync(token);
        }

        await using var second = Context(companyId);
        var secondWriter = new GlPostingWriter(second);
        await using (var transaction = await second.Database.BeginTransactionAsync(token))
        {
            var changed = Source(companyId, 504, "sha256:b");
            await secondWriter.LockAsync(companyId, changed, cancellationToken: token);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => secondWriter.WriteAsync(Plan(companyId, periodId, changed), token));
            var number = GlPostingWriter.SqlErrorNumber(failure);
            Assert.True(number is 2601 or 2627, $"unexpected SQL error {number}");
            Assert.Equal(GlPostingFailureAction.ResolveFromCommitted, GlPostingFailureRules.Classify(
                new(GlPostingFailurePhase.BeforeCommit, number), 1).Action);
            Assert.Null(second.Database.CurrentTransaction);
        }

        AssertNothingTracked(second);
        Assert.Equal((1, 1, 2), await CountsAsync(companyId, token));
        await using var reader = Context(companyId);
        Assert.Equal("sha256:a", (await reader.Set<GlSourceKey>().AsNoTracking().SingleAsync(token)).Fingerprint);
    }

    [Fact]
    public async Task InFlightWriterOfSameSourceBlocksSecondWriterButNotAnotherSource()
    {
        // FI-GL-WRITE-005
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var holder = Context(companyId);
        await using var holderTransaction = await holder.Database.BeginTransactionAsync(token);
        await new GlPostingWriter(holder).LockAsync(companyId, Source(companyId, 505), cancellationToken: token);

        await using var other = Context(companyId);
        var writer = new GlPostingWriter(other);
        await using (var blocked = await other.Database.BeginTransactionAsync(token))
        {
            var failure = await Assert.ThrowsAsync<SqlException>(
                () => writer.LockAsync(companyId, Source(companyId, 505, "sha256:other"), 300, token));
            Assert.Equal(GlPostingFailureRules.ApplockNotGranted, GlPostingWriter.SqlErrorNumber(failure));
            Assert.Equal(GlPostingFailureAction.RetryPosting, GlPostingFailureRules.Classify(
                new(GlPostingFailurePhase.BeforeCommit, GlPostingFailureRules.ApplockNotGranted), 1).Action);
            Assert.Null(other.Database.CurrentTransaction);
        }

        // 期間鎖為共用：同公司另一來源可並行取得。
        await using (var parallel = await other.Database.BeginTransactionAsync(token))
        {
            await writer.LockAsync(companyId, Source(companyId, 506), 300, token);
            await parallel.RollbackAsync(token);
        }
        // 其他公司的同一來源編號亦不受影響。
        await using var foreign = Context(companyId + 1);
        await using (var foreignTransaction = await foreign.Database.BeginTransactionAsync(token))
        {
            await new GlPostingWriter(foreign).LockAsync(companyId + 1, Source(companyId + 1, 505), 300, token);
            await foreignTransaction.RollbackAsync(token);
        }
        await holderTransaction.RollbackAsync(token);
    }

    [Fact]
    public async Task PeriodWriterExclusiveLockBlocksPostingAndPostingSharedLockBlocksPeriodWrite()
    {
        // FI-GL-WRITE-006
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var resource = GlPostingWriter.PeriodLockResource(companyId);
        Assert.Equal($"gl_period:{companyId}", resource);

        await using var periodHolder = Context(companyId);
        await using (var transaction = await periodHolder.Database.BeginTransactionAsync(token))
        {
            await periodHolder.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 1000;", token);
            await using var poster = Context(companyId);
            await using var posting = await poster.Database.BeginTransactionAsync(token);
            var failure = await Assert.ThrowsAsync<SqlException>(
                () => new GlPostingWriter(poster).LockAsync(companyId, Source(companyId, 507), 300, token));
            Assert.Equal(GlPostingFailureRules.ApplockNotGranted, GlPostingWriter.SqlErrorNumber(failure));
            await transaction.RollbackAsync(token);
        }

        await using var holder = Context(companyId);
        await using var holderTransaction = await holder.Database.BeginTransactionAsync(token);
        await new GlPostingWriter(holder).LockAsync(companyId, Source(companyId, 507), cancellationToken: token);
        await using var periodWriter = Context(companyId);
        await using (var transaction = await periodWriter.Database.BeginTransactionAsync(token))
        {
            var result = await periodWriter.Database.SqlQuery<int>(
                $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 300; SELECT @r AS [Value];").ToListAsync(token);
            Assert.Equal(-1, Assert.Single(result));
            await transaction.RollbackAsync(token);
        }
        await holderTransaction.RollbackAsync(token);
    }

    [Fact]
    public void SqlErrorNumberFindsNestedSqlExceptionOnly()
    {
        // FI-GL-WRITE-007
        Assert.Null(GlPostingWriter.SqlErrorNumber(null));
        Assert.Null(GlPostingWriter.SqlErrorNumber(new InvalidOperationException("x")));
        Assert.Null(GlPostingWriter.SqlErrorNumber(new DbUpdateException("x", new TimeoutException())));
        Assert.Equal(GlPostingFailureRules.ApplockNotGranted, 51021);
        Assert.Equal("gl_source:5:SD:Invoice:12", GlPostingWriter.SourceLockResource(Source(5, 12)));
    }
}
