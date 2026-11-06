using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>Committed ids of one atomic GL write, in line-sequence order.</summary>
public sealed record GlPostingWriteReceipt(long SourceKeyId, long JournalId, IReadOnlyList<long> LineIds);

/// <summary>
/// SQL half of BR-FI-027/028 posting (WU-FI-004a3w). Inside the caller's transaction it takes the company period
/// lock in Shared mode (period writers take it Exclusive) and the permanent source-key lock Exclusive, then inserts
/// the rows of a <see cref="GlPostingWritePlanRules"/> plan in order: source key → journal → lines. Any failure
/// rolls back the whole transaction and detaches the plan rows; the caller classifies it with
/// <see cref="GlPostingFailureRules"/>. It never decides replay, reads authority or commits.
/// </summary>
public sealed class GlPostingWriter(MyErpDbContext db)
{
    public const int DefaultLockTimeoutMilliseconds = 15000;
    public const int MaxLockTimeoutMilliseconds = 60000;

    /// <summary>Same resource as <see cref="GlPeriodWriteService"/> so period edits and postings serialize.</summary>
    public static string PeriodLockResource(long companyId) => $"gl_period:{companyId}";

    /// <summary>Permanent source identity (company/module/type/sourceId, without version or fingerprint).</summary>
    public static string SourceLockResource(GlPostingSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return $"gl_source:{source.CompanyId}:{source.Module}:{source.SourceType}:{source.SourceId}";
    }

    private static string SourceLockResource(GlSourceKey key) =>
        $"gl_source:{key.CompanyId}:{key.SourceModule}:{key.SourceType}:{key.SourceId}";

    /// <summary>SQL error number of the first <see cref="SqlException"/> in the exception chain, if any.</summary>
    public static int? SqlErrorNumber(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
                return sql.Number;
        }
        return null;
    }

    /// <summary>
    /// Takes both posting locks for the transaction. Timeout or lock-deadlock raises SQL error
    /// <see cref="GlPostingFailureRules.ApplockNotGranted"/>, other lock failures
    /// <see cref="GlPostingFailureRules.ApplockFailed"/>; on any failure the transaction is rolled back.
    /// </summary>
    public async Task LockAsync(long companyId, GlPostingSource source,
        int lockTimeoutMilliseconds = DefaultLockTimeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(lockTimeoutMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lockTimeoutMilliseconds, MaxLockTimeoutMilliseconds);
        if (companyId <= 0 || db.CurrentCompanyId != companyId || source.CompanyId != companyId)
            throw new ArgumentException("Posting locks must be taken for the current company.", nameof(companyId));
        if (GlSourceReplayRules.Decide(source, null).Status != GlSourceReplayStatus.New)
            throw new ArgumentException("Posting source identity is not canonical.", nameof(source));
        var transaction = RequireTransaction();

        var period = PeriodLockResource(companyId);
        var key = SourceLockResource(source);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @period int, @source int;
                EXEC @period = sp_getapplock @Resource = {period}, @LockMode = 'Shared', @LockOwner = 'Transaction', @LockTimeout = {lockTimeoutMilliseconds};
                IF @period IN (-1, -3) THROW 51021, 'GL posting period lock was not granted.', 1;
                IF @period < 0 THROW 51022, 'GL posting period lock failed.', 1;
                EXEC @source = sp_getapplock @Resource = {key}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {lockTimeoutMilliseconds};
                IF @source IN (-1, -3) THROW 51021, 'GL posting source lock was not granted.', 1;
                IF @source < 0 THROW 51022, 'GL posting source lock failed.', 1;
                """, cancellationToken);
        }
        catch
        {
            await RollBackQuietlyAsync(transaction);
            throw;
        }
    }

    /// <summary>
    /// Inserts a Ready plan inside the caller's transaction, which must already hold both posting locks
    /// (verified with <c>APPLOCK_MODE</c>). The caller commits; nothing here is visible before that.
    /// </summary>
    public async Task<GlPostingWriteReceipt> WriteAsync(GlPostingWritePlan plan, CancellationToken cancellationToken = default)
    {
        var (key, journal, lines) = RequireWritablePlan(plan);
        var transaction = RequireTransaction();
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Pending changes must be saved before a GL posting write.");
        if (!await HoldsPostingLocksAsync(key, cancellationToken))
            throw new InvalidOperationException("The GL posting period and source locks must be held by this transaction.");

        try
        {
            db.Set<GlSourceKey>().Add(key);
            await db.SaveChangesAsync(cancellationToken);
            journal.SourceKeyId = key.Id;
            db.Set<GlJournal>().Add(journal);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var line in lines)
                line.JournalId = journal.Id;
            db.Set<GlJournalLine>().AddRange(lines);
            await db.SaveChangesAsync(cancellationToken);
            return new(key.Id, journal.Id, lines.Select(static line => line.Id).ToArray());
        }
        catch
        {
            Detach(key);
            Detach(journal);
            foreach (var line in lines)
                Detach(line);
            await RollBackQuietlyAsync(transaction);
            throw;
        }
    }

    private (GlSourceKey Key, GlJournal Journal, IReadOnlyList<GlJournalLine> Lines) RequireWritablePlan(GlPostingWritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan is not { Status: GlPostingWritePlanStatus.Ready, SourceKey: { } key, Journal: { } journal, Lines: { Count: >= 2 } lines })
            throw new ArgumentException("Only a Ready posting plan can be written.", nameof(plan));
        var companyId = key.CompanyId;
        if (companyId <= 0 || db.CurrentCompanyId != companyId || journal.CompanyId != companyId
            || lines.Any(line => line is null || line.CompanyId != companyId))
            throw new ArgumentException("The posting plan does not belong to the current company.", nameof(plan));
        if (key.Id != 0 || journal.Id != 0 || journal.SourceKeyId != 0
            || lines.Any(static line => line.Id != 0 || line.JournalId != 0))
            throw new ArgumentException("A posting plan can be written only once; build a new plan to retry.", nameof(plan));
        return (key, journal, lines);
    }

    private IDbContextTransaction RequireTransaction() => db.Database.CurrentTransaction
        ?? throw new InvalidOperationException("GL posting must run inside the caller's SQL transaction.");

    private async Task<bool> HoldsPostingLocksAsync(GlSourceKey key, CancellationToken token)
    {
        var period = PeriodLockResource(key.CompanyId);
        var source = SourceLockResource(key);
        var held = await db.Database.SqlQuery<int>($"""
            SELECT CASE WHEN APPLOCK_MODE('public', {period}, 'Transaction') <> 'NoLock'
                         AND APPLOCK_MODE('public', {source}, 'Transaction') = 'Exclusive' THEN 1 ELSE 0 END AS [Value]
            """).ToListAsync(token);
        return held is [1];
    }

    private void Detach(object entity)
    {
        var entry = db.Entry(entity);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }

    private static async Task RollBackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollback) when (rollback is DbException or InvalidOperationException)
        {
            // The original failure is rethrown; a broken connection has already discarded the transaction.
        }
    }
}
