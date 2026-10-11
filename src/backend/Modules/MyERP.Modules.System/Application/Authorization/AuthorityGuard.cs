using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.Modules.System.Application.Authorization;

/// <summary>
/// 跨公司管理動作的授權檢查（ADR-032）。<see cref="RequirePermissionAttribute"/> 只檢查 <c>X-Company-Id</c> 指定的公司；
/// 動作影響其他公司（指派他公司角色、修改他公司、管理同時屬於多間公司的用戶）時，操作者在受影響的每間公司都必須有相應權限，
/// 並且不可授予或移除超出自己權限的角色（防止權限提升）。
/// </summary>
public sealed class AuthorityGuard(MyErpDbContext db, UserAuthorizationStore store)
{
    public const string BeyondAuthority = "SYS.ROLE.BEYOND_AUTHORITY";
    public const string FullAccessRequired = "SYS.AUTHORITY.FULL_ACCESS_REQUIRED";

    /// <summary>操作者在公司（含已停用公司）必須有指定權限，否則 403 <c>COMMON.PERMISSION.DENIED</c>。</summary>
    public async Task<UserCompanyAuthorization> EnsurePermissionAsync(
        long operatorId, long companyId, string permission, CancellationToken cancellationToken)
    {
        var authorization = await store.GetAsync(operatorId, companyId, includeInactiveCompany: true, cancellationToken);
        if (authorization is null || !authorization.Has(permission))
        {
            throw new ForbiddenException(PermissionErrorCodes.PermissionDenied, "The current user does not have the required permission in the target company.");
        }

        return authorization;
    }

    /// <summary>
    /// 角色必須在操作者權限之內：全權角色只可由全權操作者處理；一般角色的每個權限，操作者都要有而且資料範圍不窄於角色。
    /// 否則 403 <c>SYS.ROLE.BEYOND_AUTHORITY</c>。
    /// </summary>
    public async Task EnsureCanGrantAsync(UserCompanyAuthorization operatorAuthorization, IReadOnlyCollection<long> roleIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operatorAuthorization);
        ArgumentNullException.ThrowIfNull(roleIds);
        if (operatorAuthorization.IsFullAccess || roleIds.Count == 0)
        {
            return;
        }

        var ids = roleIds.ToArray();
        var roles = await db.Set<SystemRole>().AsNoTracking().IgnoreQueryFilters()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.IsFullAccess, r.DataScope })
            .ToListAsync(cancellationToken);
        var grants = await db.Set<SystemRoleGrant>().AsNoTracking()
            .Where(g => ids.Contains(g.RoleId))
            .Select(g => new { g.RoleId, g.PermissionCode })
            .ToListAsync(cancellationToken);
        var covered = roles.All(r => !r.IsFullAccess && grants.Where(g => g.RoleId == r.Id)
            .All(g => operatorAuthorization.ScopeOf(g.PermissionCode) is { } scope && scope >= r.DataScope));
        if (!covered)
        {
            throw new ForbiddenException(BeyondAuthority, "The role grants permissions beyond the current user's own.");
        }
    }

    /// <summary>
    /// 全系統資源（角色、IP 白名單）的變更會影響多間公司（ADR-033）：操作者在每間受影響公司（含已停用公司）都必須全權，
    /// 否則 403 <c>SYS.AUTHORITY.FULL_ACCESS_REQUIRED</c>。<paramref name="companyIds"/> 為 null 表示影響全部公司；空集合不檢查。
    /// </summary>
    public async Task EnsureFullAccessAsync(long operatorId, IReadOnlyCollection<long>? companyIds, CancellationToken cancellationToken)
    {
        if (companyIds is { Count: 0 })
        {
            return;
        }

        var required = companyIds is null
            ? await db.Set<SystemCompany>().AsNoTracking().Select(c => c.Id).ToListAsync(cancellationToken)
            : [.. companyIds.Distinct()];
        var full = await FullAccessCompaniesAsync(operatorId, cancellationToken);
        if (!required.All(full.Contains))
        {
            throw new ForbiddenException(FullAccessRequired, "Changing a system-wide resource requires full access in every affected company.");
        }
    }

    /// <summary>角色變更影響的公司：角色已指派給用戶的每間公司。</summary>
    public async Task<IReadOnlyList<long>> CompaniesUsingRoleAsync(long roleId, CancellationToken cancellationToken) =>
        await db.Set<SystemUserRole>().AsNoTracking()
            .Where(ur => ur.RoleId == roleId).Select(ur => ur.CompanyId).Distinct().ToListAsync(cancellationToken);

    /// <summary>用戶層級規則影響的公司：用戶有關聯的每間公司（含已停用關聯）。</summary>
    public async Task<IReadOnlyList<long>> CompaniesOfUserAsync(long userId, CancellationToken cancellationToken) =>
        await db.Set<SystemUserCompany>().AsNoTracking()
            .Where(uc => uc.UserId == userId).Select(uc => uc.CompanyId).ToListAsync(cancellationToken);

    private async Task<HashSet<long>> FullAccessCompaniesAsync(long operatorId, CancellationToken cancellationToken) =>
        [.. await db.Set<SystemUserCompany>().AsNoTracking()
            .Where(uc => uc.UserId == operatorId && uc.IsActive)
            .Where(uc => db.Set<SystemUser>().Any(u => u.Id == operatorId && u.IsActive))
            .Where(uc => db.Set<SystemUserRole>().Any(ur => ur.UserId == operatorId && ur.CompanyId == uc.CompanyId &&
                db.Set<SystemRole>().Any(r => r.Id == ur.RoleId && r.IsActive && r.IsFullAccess)))
            .Select(uc => uc.CompanyId)
            .ToListAsync(cancellationToken)];

    /// <summary>
    /// 管理用戶帳號（啟用／停用、重設密碼、解除鎖定）會影響用戶所屬的每間公司：用戶必須屬於目前公司（否則 404），
    /// 操作者在用戶每間已啟用關聯的公司都要有 <paramref name="permission"/>，而且用戶在該公司的角色不超出操作者權限。
    /// 操作者處理自己的帳號時不檢查（自我停用另由服務拒絕）。
    /// </summary>
    public async Task EnsureCanManageUserAsync(
        long operatorId, long targetUserId, long currentCompanyId, string permission, CancellationToken cancellationToken)
    {
        var links = await db.Set<SystemUserCompany>().AsNoTracking()
            .Where(uc => uc.UserId == targetUserId)
            .Select(uc => new { uc.CompanyId, uc.IsActive })
            .ToListAsync(cancellationToken);
        if (links.All(l => l.CompanyId != currentCompanyId))
        {
            throw new NotFoundException("SYS.USER.NOT_FOUND", "User", targetUserId);
        }

        if (targetUserId == operatorId)
        {
            return;
        }

        var roles = await db.Set<SystemUserRole>().AsNoTracking()
            .Where(ur => ur.UserId == targetUserId)
            .Select(ur => new { ur.CompanyId, ur.RoleId })
            .ToListAsync(cancellationToken);
        foreach (var companyId in links.Where(l => l.IsActive).Select(l => l.CompanyId))
        {
            var authorization = await EnsurePermissionAsync(operatorId, companyId, permission, cancellationToken);
            await EnsureCanGrantAsync(authorization, [.. roles.Where(r => r.CompanyId == companyId).Select(r => r.RoleId)], cancellationToken);
        }
    }
}
