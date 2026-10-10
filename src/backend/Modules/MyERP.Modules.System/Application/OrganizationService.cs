using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.System.Application;

/// <summary>
/// 組織主檔的內部應用服務。登入與資料範圍授權完成前不註冊為公開 API；
/// 呼叫端仍須先確認用戶可操作目前公司。
/// </summary>
public sealed class OrganizationService(MyErpDbContext db)
{
    public async Task<SystemBranch> CreateBranchAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        var companyId = await ActiveCompanyIdAsync(cancellationToken);
        code = ValidateText(code, 30, nameof(code));
        name = ValidateText(name, 200, nameof(name));
        if (await db.Set<SystemBranch>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == code, cancellationToken))
            throw new ConflictException("SYS.BRANCH.DUPLICATE_CODE", "Branch code already exists in this company.");

        var branch = new SystemBranch { CompanyId = companyId, Code = code, Name = name };
        db.Add(branch);
        await SaveCreateAsync("SYS.BRANCH.DUPLICATE_CODE", cancellationToken);
        return branch;
    }

    public async Task<SystemBranch> UpdateBranchAsync(long id, string name, bool isActive, int version, CancellationToken cancellationToken = default)
    {
        await ActiveCompanyIdAsync(cancellationToken);
        name = ValidateText(name, 200, nameof(name));
        var branch = await db.Set<SystemBranch>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("SYS.BRANCH.NOT_FOUND", "Branch", id);
        CheckVersion(branch.Version, version);
        branch.Name = name;
        branch.IsActive = isActive;
        db.Entry(branch).Property(x => x.Version).OriginalValue = version;
        await SaveUpdateAsync(cancellationToken);
        return branch;
    }

    public async Task<SystemDepartment> CreateDepartmentAsync(
        string code, string name, long? branchId, long? parentDepartmentId, CancellationToken cancellationToken = default)
    {
        var companyId = await ActiveCompanyIdAsync(cancellationToken);
        code = ValidateText(code, 30, nameof(code));
        name = ValidateText(name, 200, nameof(name));
        if (await db.Set<SystemDepartment>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == code, cancellationToken))
            throw new ConflictException("SYS.DEPARTMENT.DUPLICATE_CODE", "Department code already exists in this company.");
        await ValidateReferencesAsync(branchId, parentDepartmentId, null, cancellationToken);

        var department = new SystemDepartment
        {
            CompanyId = companyId, Code = code, Name = name,
            BranchId = branchId, ParentDepartmentId = parentDepartmentId,
        };
        db.Add(department);
        await SaveCreateAsync("SYS.DEPARTMENT.DUPLICATE_CODE", cancellationToken);
        return department;
    }

    public async Task<SystemDepartment> UpdateDepartmentAsync(
        long id, string name, long? branchId, long? parentDepartmentId, int version, CancellationToken cancellationToken = default)
    {
        await ActiveCompanyIdAsync(cancellationToken);
        name = ValidateText(name, 200, nameof(name));
        var department = await db.Set<SystemDepartment>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("SYS.DEPARTMENT.NOT_FOUND", "Department", id);
        CheckVersion(department.Version, version);
        await ValidateReferencesAsync(branchId, parentDepartmentId, id, cancellationToken);
        department.Name = name;
        department.BranchId = branchId;
        department.ParentDepartmentId = parentDepartmentId;
        db.Entry(department).Property(x => x.Version).OriginalValue = version;
        await SaveUpdateAsync(cancellationToken);
        return department;
    }

    private async Task<long> ActiveCompanyIdAsync(CancellationToken token)
    {
        var id = db.CurrentCompanyId;
        if (id is null || !await db.Set<SystemCompany>().AnyAsync(x => x.Id == id && x.IsActive, token))
            throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");
        return id.Value;
    }

    private async Task ValidateReferencesAsync(long? branchId, long? parentId, long? selfId, CancellationToken token)
    {
        if (branchId is not null && !await db.Set<SystemBranch>().AnyAsync(x => x.Id == branchId && x.IsActive, token))
            throw new RequestValidationException("branchId", "Branch must be active and belong to the current company.");

        var visited = new HashSet<long>();
        while (parentId is not null)
        {
            if (parentId == selfId || !visited.Add(parentId.Value))
                throw new RequestValidationException("parentDepartmentId", "Department hierarchy cannot contain a cycle.");
            var parent = await db.Set<SystemDepartment>().AsNoTracking()
                .Where(x => x.Id == parentId && x.IsActive)
                .Select(x => new { x.ParentDepartmentId })
                .SingleOrDefaultAsync(token);
            if (parent is null)
                throw new RequestValidationException("parentDepartmentId", "Parent must be active and belong to the current company.");
            parentId = parent.ParentDepartmentId;
        }
    }

    private async Task SaveUpdateAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }
    }

    private async Task SaveCreateAsync(string duplicateCode, CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(duplicateCode, "Organization code already exists.");
        }
    }

    private static void CheckVersion(int actual, int expected)
    {
        if (expected < 0 || actual != expected) throw new ConflictException();
    }

    private static string ValidateText(string? value, int maximum, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > maximum)
            throw new RequestValidationException(field, $"Required; maximum {maximum} characters.");
        return trimmed;
    }
}
