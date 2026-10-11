using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Auditing;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.Modules.System.Application.Authorization;

public sealed record RoleInput(string Code, string Name, string? Description, DataScope DataScope, bool IsFullAccess, IReadOnlyCollection<string> Permissions);

public sealed record RoleUpdate(string Name, string? Description, DataScope DataScope, bool IsFullAccess, bool IsActive, IReadOnlyCollection<string> Permissions, int Version);

public sealed record RoleDetail(
    long Id, string Code, string Name, string? Description, DataScope DataScope, bool IsFullAccess, bool IsActive, int Version, IReadOnlyList<string> Permissions);

/// <summary>用戶在一間公司的授權設定：組織歸屬、是否預設公司、角色（整組取代）。</summary>
public sealed record UserCompanyAssignment(long? BranchId, long? DepartmentId, bool IsDefault, bool IsActive, IReadOnlyCollection<long> RoleIds);

/// <summary>用戶在一間公司的現有授權（用戶權限頁顯示用）。</summary>
public sealed record UserCompanyAssignmentDetail(
    long CompanyId, string CompanyCode, string CompanyName, long? BranchId, long? DepartmentId, bool IsDefault, bool IsActive, IReadOnlyList<long> RoleIds);

/// <summary>角色與用戶公司授權的應用服務（REQ_F4118.25）。呼叫端以 <see cref="RequirePermissionAttribute"/> 檢查權限。</summary>
public sealed class RoleService(
    MyErpDbContext db,
    PermissionCatalog catalog,
    UserAuthorizationStore authorizations,
    AuthorityGuard guard,
    IAuditTrail audit,
    ICurrentUser currentUser,
    ICurrentCompany currentCompany)
{
    public const string DuplicateCode = "SYS.ROLE.DUPLICATE_CODE";
    public const string RoleNotFound = "SYS.ROLE.NOT_FOUND";
    public const string RoleInUse = "SYS.ROLE.IN_USE";

    public async Task<PagedResult<RoleDetail>> ListAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var query = db.Set<SystemRole>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword));
        }

        var fields = SortSpec.Parse(request.Sort, ["code", "name"], "code");
        if (fields.Count != 1)
        {
            throw new RequestValidationException("sort", "Choose one role sort field.");
        }

        query = fields[0] switch
        {
            { Field: "name", Descending: true } => query.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            { Field: "name" } => query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            { Descending: true } => query.OrderByDescending(x => x.Code).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Code).ThenBy(x => x.Id),
        };
        var count = await query.LongCountAsync(cancellationToken);
        var roles = await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        var ids = roles.Select(r => r.Id).ToArray();
        var permissions = (await db.Set<SystemRoleGrant>().AsNoTracking()
                .Where(p => ids.Contains(p.RoleId)).Select(p => new { p.RoleId, p.PermissionCode }).ToListAsync(cancellationToken))
            .ToLookup(p => p.RoleId, p => p.PermissionCode);
        return PagedResult.Create(roles.Select(r => ToDetail(r, permissions[r.Id])).ToList(), request, count);
    }

    public async Task<RoleDetail> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var role = await db.Set<SystemRole>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(RoleNotFound, "Role", id);
        return ToDetail(role, await PermissionsOfAsync(id, cancellationToken));
    }

    public async Task<RoleDetail> CreateAsync(RoleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = ValidateCode(input.Code);
        var name = ValidateText(input.Name, 200, "name");
        var description = ValidateOptional(input.Description, 500, "description");
        var permissions = ValidatePermissions(input.Permissions, input.DataScope);
        if (await db.Set<SystemRole>().IgnoreQueryFilters().AnyAsync(x => x.Code == code, cancellationToken))
        {
            throw new ConflictException(DuplicateCode, "Role code already exists.");
        }

        var role = new SystemRole { Code = code, Name = name, Description = description, DataScope = input.DataScope, IsFullAccess = input.IsFullAccess };
        db.Add(role);
        // 角色與權限在同一交易建立：權限寫入失敗時不留下沒有權限的角色
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(DuplicateCode, "Role code already exists.");
        }

        db.AddRange(permissions.Select(p => new SystemRoleGrant { RoleId = role.Id, PermissionCode = p }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDetail(role, permissions);
    }

    public async Task<RoleDetail> UpdateAsync(long id, RoleUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = ValidateText(input.Name, 200, "name");
        var description = ValidateOptional(input.Description, 500, "description");
        var permissions = ValidatePermissions(input.Permissions, input.DataScope);
        var role = await db.Set<SystemRole>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(RoleNotFound, "Role", id);
        if (input.Version < 0 || role.Version != input.Version)
        {
            throw new ConflictException();
        }

        var scopeChanged = role.DataScope != input.DataScope || role.IsFullAccess != input.IsFullAccess || role.IsActive != input.IsActive;
        role.Name = name;
        role.Description = description;
        role.DataScope = input.DataScope;
        role.IsFullAccess = input.IsFullAccess;
        role.IsActive = input.IsActive;
        db.Entry(role).Property(x => x.Version).OriginalValue = input.Version;

        var existing = await db.Set<SystemRoleGrant>().Where(p => p.RoleId == id).ToListAsync(cancellationToken);
        var removed = existing.Where(p => !permissions.Contains(p.PermissionCode, StringComparer.Ordinal)).ToList();
        db.RemoveRange(removed);
        var kept = existing.Select(p => p.PermissionCode).ToHashSet(StringComparer.Ordinal);
        var added = permissions.Where(p => !kept.Contains(p)).ToList();
        db.AddRange(added.Select(p => new SystemRoleGrant { RoleId = id, PermissionCode = p }));
        if (scopeChanged || removed.Count > 0 || added.Count > 0)
        {
            AddPermissionChange(currentCompany.CompanyId, new Dictionary<string, object?>
            {
                ["roleId"] = id,
                ["added"] = added.ToArray(),
                ["removed"] = removed.Select(p => p.PermissionCode).ToArray(),
                ["dataScope"] = input.DataScope,
                ["isFullAccess"] = input.IsFullAccess,
                ["isActive"] = input.IsActive,
            });
        }

        // 角色版本與權限集合在同一交易中變更：版本衝突時權限亦不改
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }

        await transaction.CommitAsync(cancellationToken);
        return ToDetail(role, permissions);
    }

    /// <summary>軟刪除角色；仍有用戶使用時拒絕（422），應先移除授權或改為停用。</summary>
    public async Task DeleteAsync(long id, int version, CancellationToken cancellationToken = default)
    {
        var role = await db.Set<SystemRole>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(RoleNotFound, "Role", id);
        if (version < 0 || role.Version != version)
        {
            throw new ConflictException();
        }

        if (await db.Set<SystemUserRole>().AnyAsync(x => x.RoleId == id, cancellationToken))
        {
            throw new BusinessRuleException(RoleInUse, "The role is still assigned to users.");
        }

        role.IsDeleted = true;
        role.IsActive = false;
        db.Entry(role).Property(x => x.Version).OriginalValue = version;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }
    }

    /// <summary>
    /// 列出用戶在操作者獲授權公司內的授權（依公司代碼）。用戶必須屬於目前公司，否則 404（ADR-016、ADR-017）；
    /// 操作者無權管理的公司不列出，避免透露用戶在其他公司的設定。
    /// </summary>
    public async Task<IReadOnlyList<UserCompanyAssignmentDetail>> ListAssignmentsInScopeAsync(
        long userId, long operatorId, long currentCompanyId, CancellationToken cancellationToken = default)
    {
        await EnsureUserInCompanyAsync(userId, currentCompanyId, cancellationToken);
        var managed = (await authorizations.ListCompaniesAsync(operatorId, cancellationToken)).Select(c => c.Id).ToArray();
        var links = await db.Set<SystemUserCompany>().AsNoTracking()
            .Where(uc => uc.UserId == userId && managed.Contains(uc.CompanyId))
            .Join(db.Set<SystemCompany>(), uc => uc.CompanyId, c => c.Id, (uc, c) => new { Link = uc, c.Code, c.Name })
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var roles = await db.Set<SystemUserRole>().AsNoTracking()
            .Where(ur => ur.UserId == userId && managed.Contains(ur.CompanyId))
            .Select(ur => new { ur.CompanyId, ur.RoleId })
            .ToListAsync(cancellationToken);
        return [.. links.Select(x => new UserCompanyAssignmentDetail(
            x.Link.CompanyId, x.Code, x.Name, x.Link.BranchId, x.Link.DepartmentId, x.Link.IsDefault, x.Link.IsActive,
            [.. roles.Where(r => r.CompanyId == x.Link.CompanyId).Select(r => r.RoleId).Order()]))];
    }

    /// <summary>
    /// 由管理端設定用戶在公司的授權（ADR-017）：用戶必須屬於目前公司（否則 404 <c>SYS.USER.NOT_FOUND</c>），
    /// 目標公司必須是操作者獲授權的公司（否則 404 <c>SYS.COMPANY.NOT_FOUND</c>），不可停用自己的授權（422）。
    /// 操作者在目標公司亦須有 <c>SYS.USER.EDIT</c>（否則 403）；新增或移除的角色不可超出操作者權限（403），
    /// 也不可變更自己的角色（422 <c>SYS.USER.SELF_ROLE_CHANGE</c>），ADR-032。
    /// </summary>
    public async Task AssignInScopeAsync(
        long userId, long companyId, UserCompanyAssignment input, long operatorId, long currentCompanyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RoleIds);
        await EnsureUserInCompanyAsync(userId, currentCompanyId, cancellationToken);
        var managed = await authorizations.ListCompaniesAsync(operatorId, cancellationToken);
        if (managed.All(c => c.Id != companyId))
        {
            throw new NotFoundException("SYS.COMPANY.NOT_FOUND", "Company", companyId);
        }

        if (userId == operatorId && !input.IsActive)
        {
            throw new BusinessRuleException("SYS.USER.SELF_DEACTIVATION", "You cannot deactivate your own company access.");
        }

        var operatorAuthorization = await guard.EnsurePermissionAsync(operatorId, companyId, SystemPermissions.UserEdit, cancellationToken);
        var current = await db.Set<SystemUserRole>().AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.CompanyId == companyId).Select(ur => ur.RoleId).ToListAsync(cancellationToken);
        var requested = input.RoleIds.Distinct().ToArray();
        var changed = requested.Except(current).Concat(current.Except(requested)).ToArray();
        if (changed.Length > 0 && userId == operatorId)
        {
            throw new BusinessRuleException("SYS.USER.SELF_ROLE_CHANGE", "You cannot change your own roles.");
        }

        await guard.EnsureCanGrantAsync(operatorAuthorization, changed, cancellationToken);
        await AssignAsync(userId, companyId, input, cancellationToken);
    }

    /// <summary>設定用戶在公司的授權（新增或整組取代角色）。公司必須啟用；分店／部門必須屬於該公司；角色必須存在且啟用。</summary>
    public async Task AssignAsync(long userId, long companyId, UserCompanyAssignment input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RoleIds);
        if (!await db.Set<SystemUser>().AnyAsync(u => u.Id == userId, cancellationToken))
        {
            throw new NotFoundException("SYS.USER.NOT_FOUND", "User", userId);
        }

        if (!await db.Set<SystemCompany>().AnyAsync(c => c.Id == companyId && c.IsActive, cancellationToken))
        {
            throw new RequestValidationException("companyId", "Company does not exist or is inactive.");
        }

        if (input.BranchId is { } branchId &&
            !await db.Set<SystemBranch>().IgnoreQueryFilters().AnyAsync(b => b.Id == branchId && b.CompanyId == companyId && !b.IsDeleted && b.IsActive, cancellationToken))
        {
            throw new RequestValidationException("branchId", "Branch does not belong to the company or is inactive.");
        }

        if (input.DepartmentId is { } departmentId &&
            !await db.Set<SystemDepartment>().IgnoreQueryFilters().AnyAsync(d => d.Id == departmentId && d.CompanyId == companyId && !d.IsDeleted && d.IsActive, cancellationToken))
        {
            throw new RequestValidationException("departmentId", "Department does not belong to the company or is inactive.");
        }

        var roleIds = input.RoleIds.Distinct().ToArray();
        if (await db.Set<SystemRole>().CountAsync(r => roleIds.Contains(r.Id) && r.IsActive, cancellationToken) != roleIds.Length)
        {
            throw new RequestValidationException("roleIds", "One or more roles do not exist or are inactive.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (input.IsDefault)
        {
            // 先取消其他預設公司，避免違反「每位用戶最多一間預設公司」唯一索引
            await db.Set<SystemUserCompany>()
                .Where(uc => uc.UserId == userId && uc.CompanyId != companyId && uc.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(uc => uc.IsDefault, false), cancellationToken);
        }

        var link = await db.Set<SystemUserCompany>().SingleOrDefaultAsync(uc => uc.UserId == userId && uc.CompanyId == companyId, cancellationToken);
        if (link is null)
        {
            link = new SystemUserCompany { UserId = userId, CompanyId = companyId };
            db.Add(link);
        }

        link.BranchId = input.BranchId;
        link.DepartmentId = input.DepartmentId;
        link.IsDefault = input.IsDefault;
        link.IsActive = input.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        var current = await db.Set<SystemUserRole>().Where(ur => ur.UserId == userId && ur.CompanyId == companyId).ToListAsync(cancellationToken);
        db.RemoveRange(current.Where(ur => !roleIds.Contains(ur.RoleId)));
        db.AddRange(roleIds.Where(id => current.All(ur => ur.RoleId != id))
            .Select(id => new SystemUserRole { UserId = userId, CompanyId = companyId, RoleId = id }));
        AddPermissionChange(companyId, new Dictionary<string, object?>
        {
            ["targetUserId"] = userId,
            ["companyId"] = companyId,
            ["roleIds"] = roleIds.Order().ToArray(),
            ["branchId"] = input.BranchId,
            ["departmentId"] = input.DepartmentId,
            ["isDefault"] = input.IsDefault,
            ["isActive"] = input.IsActive,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>權限變更安全事件（ADR-019），與變更同一交易寫入；操作者為目前用戶，背景程序為系統。</summary>
    private void AddPermissionChange(long? companyId, IReadOnlyDictionary<string, object?> detail) =>
        audit.AddSecurityEvent(new SecurityAuditEvent(
            AuditActions.PermissionChange, currentUser.UserId ?? MyERP.Shared.Security.SystemUser.Id, null, companyId, detail));

    private async Task EnsureUserInCompanyAsync(long userId, long companyId, CancellationToken cancellationToken)
    {
        if (!await db.Set<SystemUserCompany>().AnyAsync(uc => uc.UserId == userId && uc.CompanyId == companyId, cancellationToken))
        {
            throw new NotFoundException("SYS.USER.NOT_FOUND", "User", userId);
        }
    }

    private async Task<string[]> PermissionsOfAsync(long roleId, CancellationToken cancellationToken) =>
        await db.Set<SystemRoleGrant>().AsNoTracking().Where(p => p.RoleId == roleId)
            .OrderBy(p => p.PermissionCode).Select(p => p.PermissionCode).ToArrayAsync(cancellationToken);

    private static RoleDetail ToDetail(SystemRole role, IEnumerable<string> permissions) =>
        new(role.Id, role.Code, role.Name, role.Description, role.DataScope, role.IsFullAccess, role.IsActive, role.Version,
            [.. permissions.Order(StringComparer.Ordinal)]);

    private string[] ValidatePermissions(IReadOnlyCollection<string>? permissions, DataScope dataScope)
    {
        if (!Enum.IsDefined(dataScope))
        {
            throw new RequestValidationException("dataScope", "Unknown data scope.");
        }

        var codes = (permissions ?? []).Select(p => p?.Trim().ToUpperInvariant() ?? "").Distinct(StringComparer.Ordinal).ToArray();
        var unknown = codes.Where(c => !catalog.Contains(c)).ToArray();
        if (unknown.Length > 0)
        {
            throw new RequestValidationException("permissions", $"Unknown permission codes: {string.Join(", ", unknown)}.");
        }

        return codes;
    }

    private static string ValidateText(string? value, int maximum, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > maximum)
        {
            throw new RequestValidationException(field, $"Required; maximum {maximum} characters.");
        }

        return trimmed;
    }

    private static string? ValidateOptional(string? value, int maximum, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > maximum ? throw new RequestValidationException(field, $"Maximum {maximum} characters.") : trimmed;
    }

    private static string ValidateCode(string? value)
    {
        var code = ValidateText(value, 50, "code").ToUpperInvariant();
        if (code.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not '_' and not '-'))
        {
            throw new RequestValidationException("code", "Use ASCII letters, digits, hyphen or underscore only.");
        }

        return code;
    }
}
