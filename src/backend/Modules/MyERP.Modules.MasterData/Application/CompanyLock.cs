using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>公司級的交易 applock；用來序列化需要先讀整張圖再寫入的維護（換算路徑、分類樹）。</summary>
internal static class CompanyLock
{
    /// <summary>
    /// 在交易內取得 <c>{table}:{companyId}</c> 排他鎖後執行 <paramref name="action"/>；沒有外部交易時自行開啟並提交，
    /// 鎖在交易結束時釋放；15 秒內取不到即失敗。
    /// </summary>
    public static async Task<T> RunAsync<T>(
        MyErpDbContext db, string table, long companyId, Func<Task<T>> action, CancellationToken token)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(token) : null;
        var resource = $"{table}:{companyId}";
        await db.Database.ExecuteSqlAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, 'Company lock was not granted.', 1;
            """, token);
        var result = await action();
        if (transaction is not null) await transaction.CommitAsync(token);
        return result;
    }
}
