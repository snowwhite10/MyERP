using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
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

/// <summary>角色與用戶公司授權的應用服務（REQ_F4118.25）。呼叫端以 <see cref="RequirePermissionAttribute"/> 檢查權限。</summary>
public sealed class RoleService(MyErpDbContext db, PermissionCatalog catalog)
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

        role.Name = name;
        role.Description = description;
        role.DataScope = input.DataScope;
        role.IsFullAccess = input.IsFullAccess;
        role.IsActive = input.IsActive;
        db.Entry(role).Property(x => x.Version).OriginalValue = input.Version;

        var existing = await db.Set<SystemRoleGrant>().Where(p => p.RoleId == id).ToListAsync(cancellationToken);
        db.RemoveRange(existing.Where(p => !permissions.Contains(p.PermissionCode, StringComparer.Ordinal)));
        var kept = existing.Select(p => p.PermissionCode).ToHashSet(StringComparer.Ordinal);
        db.AddRange(permissions.Where(p => !kept.Contains(p)).Select(p => new SystemRoleGrant { RoleId = id, PermissionCode = p }));

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
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
