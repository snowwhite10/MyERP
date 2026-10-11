using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.Modules.System.Application.Authorization;

/// <summary>用戶可存取的公司（公司切換清單）。</summary>
public sealed record AuthorizedCompany(long Id, string Code, string Name, bool IsDefault);

/// <summary>
/// 由資料庫計算用戶在公司的授權：用戶、公司、用戶公司關聯都必須啟用且未刪除；只計啟用角色；
/// 同一權限取各角色最寬的資料範圍；已不在權限目錄的代碼忽略（模組移除權限後不殘留）。
/// </summary>
public sealed class UserAuthorizationStore(MyErpDbContext db, PermissionCatalog catalog) : IUserAuthorizationStore
{
    public Task<UserCompanyAuthorization?> GetAsync(long userId, long companyId, CancellationToken cancellationToken) =>
        GetAsync(userId, companyId, includeInactiveCompany: false, cancellationToken);

    /// <summary>
    /// 同 <see cref="GetAsync(long, long, CancellationToken)"/>；<paramref name="includeInactiveCompany"/> 為 true 時
    /// 已停用公司亦計算授權，供管理端檢查（例如重新啟用公司、管理用戶在各公司的授權，ADR-032）。
    /// </summary>
    public async Task<UserCompanyAuthorization?> GetAsync(long userId, long companyId, bool includeInactiveCompany, CancellationToken cancellationToken)
    {
        var link = await (includeInactiveCompany ? UserLinks(userId) : ActiveLinks(userId))
            .Where(uc => uc.CompanyId == companyId)
            .Select(uc => new { uc.BranchId, uc.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (link is null)
        {
            return null;
        }

        var roles = db.Set<SystemUserRole>().AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.CompanyId == companyId)
            .Join(db.Set<SystemRole>().Where(r => r.IsActive), ur => ur.RoleId, r => r.Id, (_, r) => r);
        var isFullAccess = await roles.AnyAsync(r => r.IsFullAccess, cancellationToken);
        var grants = await roles
            .Join(db.Set<SystemRoleGrant>(), r => r.Id, rp => rp.RoleId, (r, rp) => new { rp.PermissionCode, r.DataScope })
            .ToListAsync(cancellationToken);

        var permissions = grants
            .Where(g => catalog.Contains(g.PermissionCode))
            .GroupBy(g => g.PermissionCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(x => x.DataScope), StringComparer.Ordinal);
        return new UserCompanyAuthorization(userId, companyId, link.BranchId, link.DepartmentId, isFullAccess, permissions);
    }

    /// <summary>用戶可切換的公司，預設公司排第一，其餘依代碼。</summary>
    public async Task<IReadOnlyList<AuthorizedCompany>> ListCompaniesAsync(long userId, CancellationToken cancellationToken) =>
        await ActiveLinks(userId)
            .Join(db.Set<SystemCompany>(), uc => uc.CompanyId, c => c.Id, (uc, c) => new { uc.IsDefault, c.Id, c.Code, c.Name })
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Code)
            .Select(x => new AuthorizedCompany(x.Id, x.Code, x.Name, x.IsDefault))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// 用戶獲授權的公司 ID（含已停用公司），供公司維護畫面列出及重新啟用；
    /// 用戶與用戶公司關聯仍須啟用（ADR-015）。
    /// </summary>
    public async Task<IReadOnlyList<long>> ListManagedCompanyIdsAsync(long userId, CancellationToken cancellationToken) =>
        await UserLinks(userId).Select(uc => uc.CompanyId).ToListAsync(cancellationToken);

    private IQueryable<SystemUserCompany> ActiveLinks(long userId) =>
        UserLinks(userId).Where(uc => db.Set<SystemCompany>().Any(c => c.Id == uc.CompanyId && c.IsActive));

    private IQueryable<SystemUserCompany> UserLinks(long userId) =>
        db.Set<SystemUserCompany>().AsNoTracking()
            .Where(uc => uc.UserId == userId && uc.IsActive)
            .Where(uc => db.Set<SystemUser>().Any(u => u.Id == userId && u.IsActive));
}
