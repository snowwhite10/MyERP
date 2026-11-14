using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// Shared plumbing of the FI source posting services (SD invoice/return, PUR invoice, FI receipt and write-off;
/// WU-FI-005a2c review). Each service keeps its own result type, so only the type-independent parts live here.
/// </summary>
internal static class PostingServiceSupport
{
    private const int LockNotGranted = 51034;
    private const int SqlLockTimeout = 1222;

    /// <summary>The single registered implementation, or null when none or more than one is registered (fail closed).</summary>
    public static T? SingleOrNull<T>(IEnumerable<T> items) where T : class
    {
        T? match = null;
        foreach (var item in items)
        {
            if (match is not null)
                return null;
            match = item;
        }
        return match;
    }

    /// <summary>
    /// Rolls the caller's whole transaction back after a fault (BR-FI-037). A broken connection has already discarded the
    /// uncommitted work on the server, so a failing rollback is ignored.
    /// </summary>
    public static async Task RollbackCallerTransactionAsync(DbContext db)
    {
        if (db.Database.CurrentTransaction is not { } current)
            return;
        try
        {
            await current.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollback) when (rollback is DbException or InvalidOperationException)
        {
            // Nothing left to undo.
        }
    }

    /// <summary>
    /// Transaction-owned exclusive applock (released by the caller's commit or rollback). False on timeout or deadlock
    /// victim (SQL 51034/1222): a 409 the caller may retry; the transaction stays open.
    /// </summary>
    public static async Task<bool> TryLockAsync(DbContext db, string resource, int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {timeoutMilliseconds};
                IF @result < 0 THROW 51034, 'FI posting lock was not granted.', 1;
                """, cancellationToken);
            return true;
        }
        catch (SqlException ex) when (ex.Number is LockNotGranted or SqlLockTimeout)
        {
            return false;
        }
    }
}
