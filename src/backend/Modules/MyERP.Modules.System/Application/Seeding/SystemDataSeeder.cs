using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Infrastructure.Seeding;
using MyERP.Shared.Time;
using DataScope = MyERP.Shared.Security.DataScope;

namespace MyERP.Modules.System.Application.Seeding;

/// <summary>種子執行結果：各類資料本次新增的筆數（重複執行時應全部為 0）。</summary>
public sealed record SystemSeedResult(
    int Companies, int Branches, int Departments, int Roles, int RolePermissions, int Users, int UserCompanies, int UserRoles)
{
    public int Total => Companies + Branches + Departments + Roles + RolePermissions + Users + UserCompanies + UserRoles;
}

/// <summary>
/// SYS 種子：示範公司、分店、部門、角色（權限依目錄展開）、測試帳號及其公司授權與角色（WU-SYS-013、ADR-023）。
/// 冪等：以代碼／帳號比對，只新增缺少的資料；不改既有資料（含已修改的密碼、名稱與角色權限）。
/// 種子密碼是公開的示範值，刻意不套用密碼政策；只在 Development 執行（<see cref="DataSeedRunner"/>）。
/// </summary>
public sealed partial class SystemDataSeeder(
    MyErpDbContext db,
    PermissionCatalog catalog,
    IPasswordHasher<SystemUser> hasher,
    IClock clock,
    IConfiguration configuration,
    ILogger<SystemDataSeeder> logger) : IDataSeeder
{
    public int Order => 100;

    public string Name => "SYS";

    async Task IDataSeeder.SeedAsync(CancellationToken cancellationToken) => await SeedAsync(cancellationToken);

    public async Task<SystemSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var seedUsers = ReadSeedUsers();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var (companies, companyCount) = await EnsureCompaniesAsync(cancellationToken);
        var (branches, branchCount) = await EnsureBranchesAsync(companies, cancellationToken);
        var (departments, departmentCount) = await EnsureDepartmentsAsync(companies, cancellationToken);
        var (roles, roleCount, grantCount) = await EnsureRolesAsync(cancellationToken);
        var (users, userCount) = await EnsureUsersAsync(seedUsers, cancellationToken);
        var (userCompanyCount, userRoleCount) = await EnsureAssignmentsAsync(
            seedUsers, users, companies, branches, departments, roles, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var result = new SystemSeedResult(
            companyCount, branchCount, departmentCount, roleCount, grantCount, userCount, userCompanyCount, userRoleCount);
        SeedCompleted(logger, result.Total, result);
        return result;
    }

    private List<SeedUserOptions> ReadSeedUsers()
    {
        var users = configuration.GetSection(SeedUserOptions.SectionName).Get<List<SeedUserOptions>>() ?? [];
        var roleCodes = SystemSeedData.Roles.Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrEmpty(user.Password) || !roleCodes.Contains(user.Role))
            {
                throw new InvalidOperationException(
                    $"Invalid {SeedUserOptions.SectionName} entry '{user.Username}': username, password and a known role ({string.Join(", ", roleCodes)}) are required.");
            }
        }

        var duplicate = users.GroupBy(u => u.Username.Trim().ToUpperInvariant()).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"{SeedUserOptions.SectionName} contains '{duplicate.Key}' more than once.");
        }

        return users;
    }

    private async Task<(Dictionary<string, long> Ids, int Created)> EnsureCompaniesAsync(CancellationToken cancellationToken)
    {
        var codes = SystemSeedData.Companies.Select(c => c.Code).ToList();
        var existing = await db.Set<SystemCompany>().IgnoreQueryFilters()
            .Where(c => codes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var added = SystemSeedData.Companies
            .Where(c => !existing.ContainsKey(c.Code))
            .Select(c => new SystemCompany { Code = c.Code, Name = c.Name })
            .ToList();
        db.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return (existing.Values.Concat(added).ToDictionary(c => c.Code, c => c.Id, StringComparer.OrdinalIgnoreCase), added.Count);
    }

    private async Task<(Dictionary<(long CompanyId, string Code), long> Ids, int Created)> EnsureBranchesAsync(
        Dictionary<string, long> companies, CancellationToken cancellationToken)
    {
        var companyIds = companies.Values.ToList();
        var existing = await db.Set<SystemBranch>().IgnoreQueryFilters()
            .Where(b => companyIds.Contains(b.CompanyId))
            .ToListAsync(cancellationToken);
        var added = SystemSeedData.Companies
            .SelectMany(c => c.Branches.Select(b => (CompanyId: companies[c.Code], Branch: b)))
            .Where(x => !existing.Any(e => e.CompanyId == x.CompanyId && SameCode(e.Code, x.Branch.Code)))
            .Select(x => new SystemBranch { CompanyId = x.CompanyId, Code = x.Branch.Code, Name = x.Branch.Name })
            .ToList();
        db.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return (existing.Concat(added).ToDictionary(b => (b.CompanyId, b.Code.ToUpperInvariant()), b => b.Id), added.Count);
    }

    private async Task<(Dictionary<(long CompanyId, string Code), long> Ids, int Created)> EnsureDepartmentsAsync(
        Dictionary<string, long> companies, CancellationToken cancellationToken)
    {
        var companyIds = companies.Values.ToList();
        var existing = await db.Set<SystemDepartment>().IgnoreQueryFilters()
            .Where(d => companyIds.Contains(d.CompanyId))
            .ToListAsync(cancellationToken);
        var added = companyIds
            .SelectMany(companyId => SystemSeedData.Departments.Select(d => (CompanyId: companyId, Department: d)))
            .Where(x => !existing.Any(e => e.CompanyId == x.CompanyId && SameCode(e.Code, x.Department.Code)))
            .Select(x => new SystemDepartment { CompanyId = x.CompanyId, Code = x.Department.Code, Name = x.Department.Name })
            .ToList();
        db.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return (existing.Concat(added).ToDictionary(d => (d.CompanyId, d.Code.ToUpperInvariant()), d => d.Id), added.Count);
    }

    private async Task<(Dictionary<string, long> Ids, int Created, int Grants)> EnsureRolesAsync(CancellationToken cancellationToken)
    {
        var codes = SystemSeedData.Roles.Select(r => r.Code).ToList();
        var existing = await db.Set<SystemRole>().IgnoreQueryFilters()
            .Where(r => codes.Contains(r.Code))
            .ToDictionaryAsync(r => r.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var added = SystemSeedData.Roles
            .Where(r => !existing.ContainsKey(r.Code))
            .Select(r => new SystemRole
            {
                Code = r.Code,
                Name = r.Name,
                Description = r.Description,
                DataScope = DataScope.Company,
                IsFullAccess = r.IsFullAccess,
            })
            .ToList();
        db.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        var roles = existing.Values.Concat(added).ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);

        // 只補上目錄中新出現的權限，不移除管理員後來加減的授權；全權角色不必逐一列出
        var roleIds = roles.Values.Select(r => r.Id).ToList();
        var granted = (await db.Set<SystemRoleGrant>()
                .Where(g => roleIds.Contains(g.RoleId))
                .Select(g => new { g.RoleId, g.PermissionCode })
                .ToListAsync(cancellationToken))
            .Select(g => (g.RoleId, g.PermissionCode))
            .ToHashSet();
        var newGrants = SystemSeedData.Roles
            .Where(r => !r.IsFullAccess && !roles[r.Code].IsFullAccess)
            .SelectMany(r => catalog.All.Where(r.Grants).Select(p => (RoleId: roles[r.Code].Id, p.Code)))
            .Where(x => !granted.Contains((x.RoleId, x.Code)))
            .Select(x => new SystemRoleGrant { RoleId = x.RoleId, PermissionCode = x.Code })
            .ToList();
        db.AddRange(newGrants);
        await db.SaveChangesAsync(cancellationToken);
        return (roles.ToDictionary(r => r.Key, r => r.Value.Id, StringComparer.OrdinalIgnoreCase), added.Count, newGrants.Count);
    }

    private async Task<(Dictionary<string, SystemUser> Users, int Created)> EnsureUsersAsync(
        List<SeedUserOptions> seedUsers, CancellationToken cancellationToken)
    {
        var normalized = seedUsers.Select(u => u.Username.Trim().ToUpperInvariant()).ToList();
        var existing = await db.Set<SystemUser>().IgnoreQueryFilters()
            .Where(u => normalized.Contains(u.NormalizedUsername))
            .ToDictionaryAsync(u => u.NormalizedUsername, StringComparer.Ordinal, cancellationToken);
        var added = new List<SystemUser>();
        foreach (var seed in seedUsers.Where(u => !existing.ContainsKey(u.Username.Trim().ToUpperInvariant())))
        {
            var name = seed.Username.Trim();
            var user = new SystemUser
            {
                Username = name,
                NormalizedUsername = name.ToUpperInvariant(),
                PasswordHash = "",
                PasswordChangedAt = clock.LocalNow,
            };
            user.PasswordHash = hasher.HashPassword(user, seed.Password);
            added.Add(user);
        }

        db.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return (existing.Values.Concat(added).ToDictionary(u => u.NormalizedUsername, StringComparer.Ordinal), added.Count);
    }

    /// <summary>每個種子帳號都可存取全部示範公司：預設第一間、歸屬總公司（第一間分店）及角色對應的部門。</summary>
    private async Task<(int UserCompanies, int UserRoles)> EnsureAssignmentsAsync(
        List<SeedUserOptions> seedUsers,
        Dictionary<string, SystemUser> users,
        Dictionary<string, long> companies,
        Dictionary<(long CompanyId, string Code), long> branches,
        Dictionary<(long CompanyId, string Code), long> departments,
        Dictionary<string, long> roles,
        CancellationToken cancellationToken)
    {
        var active = seedUsers
            .Select(s => (Seed: s, User: users[s.Username.Trim().ToUpperInvariant()]))
            .Where(x => !x.User.IsDeleted)
            .ToList();
        var userIds = active.Select(x => x.User.Id).ToList();
        var links = await db.Set<SystemUserCompany>()
            .Where(l => userIds.Contains(l.UserId))
            .ToListAsync(cancellationToken);
        var defaultCompanyId = companies[SystemSeedData.Companies[0].Code];

        var addedLinks = new List<SystemUserCompany>();
        foreach (var (seed, user) in active)
        {
            var role = SystemSeedData.Roles.Single(r => r.Code == seed.Role);
            var hasDefault = links.Any(l => l.UserId == user.Id && l.IsDefault);
            foreach (var company in SystemSeedData.Companies)
            {
                var companyId = companies[company.Code];
                if (links.Any(l => l.UserId == user.Id && l.CompanyId == companyId))
                {
                    continue;
                }

                addedLinks.Add(new SystemUserCompany
                {
                    UserId = user.Id,
                    CompanyId = companyId,
                    BranchId = branches[(companyId, company.Branches[0].Code.ToUpperInvariant())],
                    DepartmentId = departments[(companyId, role.DepartmentCode.ToUpperInvariant())],
                    IsDefault = !hasDefault && companyId == defaultCompanyId,
                });
            }
        }

        db.AddRange(addedLinks);
        await db.SaveChangesAsync(cancellationToken);

        var existingRoles = (await db.Set<SystemUserRole>()
                .Where(r => userIds.Contains(r.UserId))
                .Select(r => new { r.UserId, r.CompanyId, r.RoleId })
                .ToListAsync(cancellationToken))
            .Select(r => (r.UserId, r.CompanyId, r.RoleId))
            .ToHashSet();
        var addedRoles = active
            .SelectMany(x => companies.Values.Select(companyId => (x.User.Id, CompanyId: companyId, RoleId: roles[x.Seed.Role])))
            .Where(x => !existingRoles.Contains(x))
            .Select(x => new SystemUserRole { UserId = x.Id, CompanyId = x.CompanyId, RoleId = x.RoleId })
            .ToList();
        db.AddRange(addedRoles);
        await db.SaveChangesAsync(cancellationToken);
        return (addedLinks.Count, addedRoles.Count);
    }

    private static bool SameCode(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Information, Message = "SYS seed completed: {Total} rows added ({Result})")]
    private static partial void SeedCompleted(ILogger logger, int total, SystemSeedResult result);
}
