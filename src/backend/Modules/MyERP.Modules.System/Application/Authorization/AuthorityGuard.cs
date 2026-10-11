using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Application.Authorization;

/// <summary>
/// 跨公司管理動作的授權檢查（ADR-032）。<see cref="RequirePermissionAttribute"/> 只檢查 <c>X-Company-Id</c> 指定的公司；
/// 動作影響其他公司（指派他公司角色、修改他公司、管理同時屬於多間公司的用戶）時，操作者在受影響的每間公司都必須有相應權限，
/// 並且不可授予或移除超出自己權限的角色（防止權限提升）。
/// </summary>
public sealed class AuthorityGuard(MyErpDbContext db, UserAuthorizationStore store)
{
    public const string BeyondAuthority = "SYS.ROLE.BEYOND_AUTHORITY";

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
