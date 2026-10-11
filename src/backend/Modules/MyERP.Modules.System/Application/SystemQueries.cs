using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.System.Application;

/// <summary>SYS 應用服務共用的查詢：目前公司檢查、批次查用戶名稱、代碼／名稱排序。</summary>
internal static class SystemQueries
{
    public const string CompanyUnavailable = "SYS.COMPANY.UNAVAILABLE";

    /// <summary>回傳目前公司 ID；沒有公司或公司已停用時回 403 <c>SYS.COMPANY.UNAVAILABLE</c>。</summary>
    public static async Task<long> ActiveCompanyIdAsync(MyErpDbContext db, CancellationToken token)
    {
        var id = db.CurrentCompanyId;
        if (id is null || !await db.Set<SystemCompany>().AnyAsync(x => x.Id == id && x.IsActive, token))
            throw new ForbiddenException(CompanyUnavailable, "An active company context is required.");
        return id.Value;
    }

    /// <summary>以一次查詢取得用戶名稱（含已停用或已刪除的用戶，歷史紀錄仍要顯示）。</summary>
    public static async Task<Dictionary<long, string>> UsernamesAsync(MyErpDbContext db, IEnumerable<long> ids, CancellationToken token)
    {
        var distinct = ids.Distinct().ToArray();
        return distinct.Length == 0
            ? []
            : await db.Set<SystemUser>().AsNoTracking().IgnoreQueryFilters()
                .Where(u => distinct.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, token);
    }

    /// <summary>
    /// 按 <c>name</c> 或代碼欄位排序，再以 Id 固定次序。<paramref name="sort"/> 欄位為 <c>name</c> 時用 <paramref name="name"/>，
    /// 其他（已由 <see cref="SortSpec"/> 白名單限制）一律用 <paramref name="key"/>。
    /// </summary>
    public static IQueryable<T> OrderByKeyOrName<T>(
        this IQueryable<T> query, SortField sort, Expression<Func<T, string>> key, Expression<Func<T, string>> name)
        where T : Entity
    {
        ArgumentNullException.ThrowIfNull(sort);
        var selector = sort.Field == "name" ? name : key;
        return (sort.Descending ? query.OrderByDescending(selector) : query.OrderBy(selector)).ThenBy(x => x.Id);
    }
}
