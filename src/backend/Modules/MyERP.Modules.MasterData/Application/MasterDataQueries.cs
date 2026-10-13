using System.Linq.Expressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>MD 應用服務共用的檢查與查詢：目前公司、唯一約束衝突、代碼／名稱排序（MD-B12）。</summary>
internal static class MasterDataQueries
{
    /// <summary>回傳目前公司 ID；沒有公司時回 403 <c>COMMON.COMPANY.ACCESS_DENIED</c>。</summary>
    public static long RequireCompany(MyErpDbContext db) => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    /// <summary>保存時違反唯一索引或唯一約束（SQL Server 2601／2627）。</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex?.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>
    /// 按 <c>name</c> 或代碼排序，再以 Id 固定次序。<paramref name="sort"/> 欄位為 <c>name</c> 時用 <paramref name="name"/>，
    /// 其他（已由 <see cref="SortSpec"/> 白名單限制）一律用 <paramref name="code"/>。
    /// </summary>
    public static IQueryable<T> OrderByCodeOrName<T>(
        this IQueryable<T> query, SortField sort, Expression<Func<T, string>> code, Expression<Func<T, string>> name)
        where T : Entity
    {
        ArgumentNullException.ThrowIfNull(sort);
        var selector = sort.Field == "name" ? name : code;
        return (sort.Descending ? query.OrderByDescending(selector) : query.OrderBy(selector)).ThenBy(x => x.Id);
    }
}
