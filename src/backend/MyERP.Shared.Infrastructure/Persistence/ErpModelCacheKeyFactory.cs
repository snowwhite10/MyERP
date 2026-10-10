using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MyERP.Shared.Infrastructure.Persistence;

/// <summary>
/// 模型快取鍵 = DbContext 型別 + 模組組合（<see cref="MyErpDbContext.ModuleSetKey"/>）+ 是否設計期。
/// EF 預設只以型別為鍵；同一程序內以不同模組組合建立 <see cref="MyErpDbContext"/>（整合測試的正式模型 vs. 測試模型）時，
/// 若共用快取會拿到錯誤的模型。由 <see cref="PersistenceServiceCollectionExtensions.UseMyErpSqlServer{TBuilder}"/> 套用。
/// </summary>
public sealed class ErpModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context is MyErpDbContext erp
            ? (context.GetType(), erp.ModuleSetKey, designTime)
            : (context.GetType(), designTime);
    }
}
