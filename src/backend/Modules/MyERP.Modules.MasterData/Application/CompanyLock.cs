using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>
/// 公司級的交易 applock；用來序列化需要先讀整張圖再寫入的維護（換算路徑、分類樹），
/// 以及「刪除前檢查引用」與「驗證被引用資料後寫入引用」（MD-B03）：刪除端對被引用表取排他鎖，
/// 引用端在驗證到保存之間對被引用表取共享鎖，兩者互斥，引用端之間不互斥。
/// </summary>
internal static class CompanyLock
{
    public const string CustomerCategory = "md_customer_category";
    public const string SupplierCategory = "md_supplier_category";
    public const string ItemBrand = "md_item_brand";
    public const string ItemCategory = "md_item_category";
    public const string Uom = "md_uom";
    public const string PaymentTerm = "md_payment_term";
    public const string SettlementMethod = "md_settlement_method";

    /// <summary>
    /// 在交易內取得 <c>{table}:{companyId}</c> 排他鎖後執行 <paramref name="action"/>；沒有外部交易時自行開啟並提交，
    /// 鎖在交易結束時釋放；15 秒內取不到即失敗。
    /// </summary>
    public static Task<T> RunAsync<T>(
        MyErpDbContext db, string table, long companyId, Func<Task<T>> action, CancellationToken token) =>
        RunCoreAsync(db, [table], "Exclusive", companyId, action, token);

    /// <summary>
    /// 引用端：對 <paramref name="referencedTables"/> 取共享鎖後執行 <paramref name="action"/>。
    /// 依名稱排序取得，避免兩個引用端以不同順序等待同一個刪除端而死結；
    /// 已持有排他鎖時須先取排他鎖、再巢狀呼叫本方法（不可反向，否則鎖升級可能死結）。
    /// </summary>
    public static Task<T> RunSharedAsync<T>(
        MyErpDbContext db, IEnumerable<string> referencedTables, long companyId, Func<Task<T>> action, CancellationToken token) =>
        RunCoreAsync(db, [.. referencedTables.Distinct().Order(StringComparer.Ordinal)], "Shared", companyId, action, token);

    private static async Task<T> RunCoreAsync<T>(MyErpDbContext db, IReadOnlyList<string> tables, string mode, long companyId,
        Func<Task<T>> action, CancellationToken token)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(token) : null;
        foreach (var table in tables)
        {
            var resource = $"{table}:{companyId}";
            await db.Database.ExecuteSqlAsync($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = {mode}, @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @result < 0 THROW 51000, 'Company lock was not granted.', 1;
                """, token);
        }
        var result = await action();
        if (transaction is not null) await transaction.CommitAsync(token);
        return result;
    }
}
