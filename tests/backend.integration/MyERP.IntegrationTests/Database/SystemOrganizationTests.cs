using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class SystemOrganizationTests(TestDatabase database)
{
    [Fact]
    public async Task OrganizationListsArePagedScopedAndExcludeDeletedRows()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var first = new SystemCompany { Code = $"Q{suffix}", Name = "First" };
        var second = new SystemCompany { Code = $"R{suffix}", Name = "Second" };
        setup.AddRange(first, second);
        await setup.SaveChangesAsync(token);
        setup.AddRange(
            new SystemBranch { CompanyId = first.Id, Code = $"A{suffix}", Name = "Match one" },
            new SystemBranch { CompanyId = first.Id, Code = $"B{suffix}", Name = "Match two" },
            new SystemBranch { CompanyId = second.Id, Code = $"C{suffix}", Name = "Match other" },
            new SystemBranch { CompanyId = first.Id, Code = $"D{suffix}", Name = "Match deleted", IsDeleted = true },
            new SystemDepartment { CompanyId = first.Id, Code = $"A{suffix}", Name = "Match one" },
            new SystemDepartment { CompanyId = second.Id, Code = $"B{suffix}", Name = "Match other" });
        await setup.SaveChangesAsync(token);

        await using var scoped = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(first.Id));
        var service = new OrganizationService(scoped);
        var page = await service.ListBranchesAsync(new PagedRequest { Page = 2, PageSize = 1, Keyword = "Match" }, token);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal($"B{suffix}", Assert.Single(page.Items).Code);
        var departments = await service.ListDepartmentsAsync(new PagedRequest { Keyword = $"A{suffix}" }, token);
        Assert.Equal(1, departments.TotalCount);
        Assert.Equal(first.Id, Assert.Single(departments.Items).CompanyId);
        var descending = await service.ListBranchesAsync(new PagedRequest { PageSize = 2, Keyword = "Match", Sort = "-name" }, token);
        Assert.Equal($"B{suffix}", descending.Items[0].Code);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ListBranchesAsync(
            new PagedRequest { Sort = "code,name" }, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ListDepartmentsAsync(
            new PagedRequest { PageSize = 201 }, token));

        await using var noCompany = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        await Assert.ThrowsAsync<ForbiddenException>(() => new OrganizationService(noCompany)
            .ListBranchesAsync(new PagedRequest(), token));
    }

    [Fact]
    public async Task DatabaseEnforcesOrganizationCodesAndJsonForAllThreeTables()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var company = new SystemCompany { Code = $"K{suffix}", Name = "Company" };
        db.Add(company);
        await db.SaveChangesAsync(token);
        var branch = new SystemBranch { CompanyId = company.Id, Code = $"B{suffix}", Name = "Branch" };
        var department = new SystemDepartment { CompanyId = company.Id, Code = $"D{suffix}", Name = "Department" };
        db.AddRange(branch, department);
        await db.SaveChangesAsync(token);

        foreach (object duplicate in new object[]
        {
            new SystemBranch { CompanyId = company.Id, Code = branch.Code, Name = "Duplicate" },
            new SystemDepartment { CompanyId = company.Id, Code = department.Code, Name = "Duplicate" },
        })
        {
            db.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
            db.ChangeTracker.Clear();
        }

        foreach (object invalidJson in new object[]
        {
            new SystemCompany { Code = $"L{suffix}", Name = "Invalid", CustomFields = "{bad}" },
            new SystemDepartment { CompanyId = company.Id, Code = $"E{suffix}", Name = "Invalid", CustomFields = "{bad}" },
        })
        {
            db.Add(invalidJson);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
            db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task DepartmentQueryIsCompanyScopedAndRetainsHistoryWhenCompanyIsInactive()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var first = new SystemCompany { Code = $"M{suffix}", Name = "First" };
        var second = new SystemCompany { Code = $"N{suffix}", Name = "Second" };
        setup.AddRange(first, second);
        await setup.SaveChangesAsync(token);
        setup.AddRange(
            new SystemDepartment { CompanyId = first.Id, Code = $"A{suffix}", Name = "Visible" },
            new SystemDepartment { CompanyId = second.Id, Code = $"B{suffix}", Name = "Other" },
            new SystemDepartment { CompanyId = first.Id, Code = $"C{suffix}", Name = "Deleted", IsDeleted = true });
        await setup.SaveChangesAsync(token);
        first.IsActive = false;
        await setup.SaveChangesAsync(token);

        await using var scoped = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(first.Id));
        Assert.Equal(new[] { $"A{suffix}" }, await scoped.Set<SystemDepartment>().Select(x => x.Code).ToArrayAsync(token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new OrganizationService(scoped).CreateDepartmentAsync($"D{suffix}", "New", null, null, token));
        await using var noCompany = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        Assert.Empty(await noCompany.Set<SystemDepartment>().ToArrayAsync(token));
        Assert.Equal(3, await noCompany.Set<SystemDepartment>().IgnoreQueryFilters().CountAsync(x => x.Code.EndsWith(suffix), token));
    }

    [Fact]
    public async Task CompanyAndDepartmentUpdatesStampAuditAndRejectStaleVersions()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var clock = new FixedClock(new DateTime(2026, 10, 10, 18, 0, 0));
        var companyContext = new FixedCompany(null);
        var stamper = new AuditStamper(clock, new FixedUser(7), companyContext);
        var auditedOptions = new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options;
        await using var db = new MyErpDbContext(auditedOptions, [new SystemOrganizationDbModel()], companyContext);
        var companyService = new OrganizationService(db);
        var company = await companyService.CreateCompanyAsync($"P{suffix}", "Before", token);
        Assert.Equal(1, company.Version);
        await companyService.UpdateCompanyAsync(company.Id, "After", true, company.Version, token);
        Assert.Equal(2, company.Version);
        Assert.Equal(7, company.UpdatedBy);
        Assert.Equal(clock.LocalNow, company.UpdatedAt);
        await Assert.ThrowsAsync<ConflictException>(() => companyService.UpdateCompanyAsync(company.Id, "Stale", true, 1, token));

        await using var departmentDb = new MyErpDbContext(auditedOptions, [new SystemOrganizationDbModel()], new FixedCompany(company.Id));
        var departmentService = new OrganizationService(departmentDb);
        var department = await departmentService.CreateDepartmentAsync($"D{suffix}", "Before", null, null, token);
        Assert.Equal(1, department.Version);
        await departmentService.UpdateDepartmentAsync(department.Id, "After", null, null, true, department.Version, token);
        Assert.Equal(2, department.Version);
        await Assert.ThrowsAsync<ConflictException>(() => departmentService.UpdateDepartmentAsync(department.Id, "Stale", null, null, true, 1, token));

        await using var firstWriter = new MyErpDbContext(auditedOptions, [new SystemOrganizationDbModel()], new FixedCompany(company.Id));
        await using var staleWriter = new MyErpDbContext(auditedOptions, [new SystemOrganizationDbModel()], new FixedCompany(company.Id));
        var original = await staleWriter.Set<SystemDepartment>().SingleAsync(x => x.Id == department.Id, token);
        var current = await firstWriter.Set<SystemDepartment>().SingleAsync(x => x.Id == department.Id, token);
        await new OrganizationService(firstWriter).UpdateDepartmentAsync(current.Id, "Newest", null, null, true, current.Version, token);
        await Assert.ThrowsAsync<ConflictException>(() => new OrganizationService(staleWriter)
            .UpdateDepartmentAsync(original.Id, "Lost", null, null, true, original.Version, token));
    }
    [Fact]
    public async Task CompanyServiceCreatesUpdatesAndDeactivatesWithVersionChecks()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var service = new OrganizationService(db);
        var company = await service.CreateCompanyAsync($"C{suffix}", "Company", token);
        Assert.True(company.IsActive);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateCompanyAsync($"C{suffix}", "Duplicate", token));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateCompanyAsync(company.Id, "Changed", false, company.Version + 1, token));
        await service.UpdateCompanyAsync(company.Id, "Changed", false, company.Version, token);
        Assert.False(company.IsActive);
        Assert.Equal("Changed", company.Name);
        Assert.Equal("Changed", (await db.Set<SystemCompany>().SingleAsync(x => x.Id == company.Id, token)).Name);
        await using var inactive = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(company.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => new OrganizationService(inactive).CreateBranchAsync($"B{suffix}", "Branch", token));
    }

    [Fact]
    public async Task DepartmentCanBeDeactivatedAndInactiveParentCannotBeSelected()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var company = new SystemCompany { Code = $"D{suffix}", Name = "Company" };
        setup.Add(company);
        await setup.SaveChangesAsync(token);
        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(company.Id));
        var service = new OrganizationService(db);
        var parent = await service.CreateDepartmentAsync($"P{suffix}", "Parent", null, null, token);
        await service.UpdateDepartmentAsync(parent.Id, "Parent", null, null, false, parent.Version, token);
        Assert.False(parent.IsActive);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateDepartmentAsync($"C{suffix}", "Child", null, parent.Id, token));
    }

    [Fact]
    public async Task OrganizationCodesRejectInvalidCharactersAndDatabaseEnforcesUniquenessAndJson()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var service = new OrganizationService(db);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateCompanyAsync("bad code", "Name", token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateCompanyAsync("中文", "Name", token));
        var company = await service.CreateCompanyAsync($"J{suffix}", "Company", token);
        db.Add(new SystemCompany { Code = company.Code, Name = "Duplicate" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
        db.ChangeTracker.Clear();
        db.Add(new SystemBranch { CompanyId = company.Id, Code = $"B{suffix}", Name = "Bad JSON", CustomFields = "{bad}" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
    }
    [Fact]
    public async Task MigrationCreatesOrganizationTablesAndCompanyScopedRows()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => database.UseTestDatabase(b));
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();

        var names = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.tables WHERE [name] IN ('sys_company', 'sys_branch', 'sys_department')")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, names.Count);

        // 公司以外的主檔都由共用 DbContext 加上公司與軟刪除篩選。
        Assert.NotNull(db.Model.FindEntityType(typeof(SystemBranch))?.FindProperty("CompanyId"));
        Assert.NotNull(db.Model.FindEntityType(typeof(SystemDepartment))?.FindProperty("CompanyId"));
        Assert.Equal(2, db.Model.FindEntityType(typeof(SystemBranch))?.GetDeclaredQueryFilters().Count);
        Assert.Equal(2, db.Model.FindEntityType(typeof(SystemDepartment))?.GetDeclaredQueryFilters().Count);
    }

    [Fact]
    public async Task BranchQueryIsLimitedToCurrentCompanyAndExcludesDeletedRows()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var companyA = new SystemCompany { Code = $"A{suffix}", Name = "A" };
        var companyB = new SystemCompany { Code = $"B{suffix}", Name = "B" };
        setup.AddRange(companyA, companyB);
        await setup.SaveChangesAsync(token);
        setup.AddRange(
            new SystemBranch { CompanyId = companyA.Id, Code = $"A{suffix}", Name = "A" },
            new SystemBranch { CompanyId = companyB.Id, Code = $"B{suffix}", Name = "B" },
            new SystemBranch { CompanyId = companyA.Id, Code = $"D{suffix}", Name = "Deleted", IsDeleted = true });
        await setup.SaveChangesAsync(token);

        await using var companyAContext = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(companyA.Id));
        var visible = await companyAContext.Set<SystemBranch>().Select(x => x.Code).ToListAsync(token);
        Assert.Contains($"A{suffix}", visible);
        Assert.DoesNotContain($"B{suffix}", visible);
        Assert.DoesNotContain($"D{suffix}", visible);

        await using var noCompanyContext = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        Assert.Empty(await noCompanyContext.Set<SystemBranch>().ToListAsync(token));
    }

    [Fact]
    public async Task CompositeForeignKeysRejectCrossCompanyDepartmentReferences()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var first = new SystemCompany { Code = $"C{suffix}", Name = "First" };
        var second = new SystemCompany { Code = $"D{suffix}", Name = "Second" };
        setup.AddRange(first, second);
        await setup.SaveChangesAsync(token);
        var otherBranch = new SystemBranch { CompanyId = second.Id, Code = $"B{suffix}", Name = "Other" };
        var otherParent = new SystemDepartment { CompanyId = second.Id, Code = $"P{suffix}", Name = "Other" };
        setup.AddRange(otherBranch, otherParent);
        await setup.SaveChangesAsync(token);

        setup.Add(new SystemDepartment { CompanyId = first.Id, Code = $"X{suffix}", Name = "Invalid", BranchId = otherBranch.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => setup.SaveChangesAsync(token));
        setup.ChangeTracker.Clear();
        setup.Add(new SystemDepartment { CompanyId = first.Id, Code = $"Y{suffix}", Name = "Invalid", ParentDepartmentId = otherParent.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => setup.SaveChangesAsync(token));
    }

    [Fact]
    public async Task OrganizationServiceRejectsCrossCompanyParentAndCycles()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var first = new SystemCompany { Code = $"E{suffix}", Name = "First" };
        var second = new SystemCompany { Code = $"F{suffix}", Name = "Second" };
        setup.AddRange(first, second);
        await setup.SaveChangesAsync(token);
        var otherParent = new SystemDepartment { CompanyId = second.Id, Code = $"O{suffix}", Name = "Other" };
        setup.Add(otherParent);
        await setup.SaveChangesAsync(token);

        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(first.Id));
        var service = new OrganizationService(db);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateDepartmentAsync($"X{suffix}", "Invalid", null, otherParent.Id, token));
        var parent = await service.CreateDepartmentAsync($"P{suffix}", "Parent", null, null, token);
        var child = await service.CreateDepartmentAsync($"C{suffix}", "Child", null, parent.Id, token);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UpdateDepartmentAsync(parent.Id, "Parent", null, child.Id, true, parent.Version, token));
    }

    [Fact]
    public async Task OrganizationServiceValidatesCodeAndConcurrency()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var company = new SystemCompany { Code = $"G{suffix}", Name = "Company" };
        setup.Add(company);
        await setup.SaveChangesAsync(token);
        await using var db = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(company.Id));
        var service = new OrganizationService(db);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateBranchAsync(" ", "Name", token));
        var branch = await service.CreateBranchAsync($"B{suffix}", "Branch", token);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateBranchAsync($"B{suffix}", "Duplicate", token));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateBranchAsync(branch.Id, "Changed", false, branch.Version - 1, token));
        await service.UpdateBranchAsync(branch.Id, "Changed", false, branch.Version, token);
        Assert.False((await db.Set<SystemBranch>().SingleAsync(x => x.Id == branch.Id, token)).IsActive);
    }

    [Fact]
    public async Task OrganizationServiceRequiresActiveCompanyAndReservesDeletedCodes()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var active = new SystemCompany { Code = $"H{suffix}", Name = "Active" };
        var inactive = new SystemCompany { Code = $"I{suffix}", Name = "Inactive", IsActive = false };
        setup.AddRange(active, inactive);
        await setup.SaveChangesAsync(token);
        setup.Add(new SystemBranch { CompanyId = active.Id, Code = $"R{suffix}", Name = "Old", IsDeleted = true });
        await setup.SaveChangesAsync(token);

        await using var noCompany = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        await Assert.ThrowsAsync<ForbiddenException>(() => new OrganizationService(noCompany).CreateBranchAsync($"N{suffix}", "Name", token));
        await using var inactiveCompany = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(inactive.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => new OrganizationService(inactiveCompany).CreateBranchAsync($"N{suffix}", "Name", token));
        await using var activeCompany = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(active.Id));
        await Assert.ThrowsAsync<ConflictException>(() => new OrganizationService(activeCompany).CreateBranchAsync($"R{suffix}", "Reused", token));
    }

    [Fact]
    public async Task OrganizationServiceAdvancesVersionAndRejectsStaleEdit()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new MyErpDbContext(options, [new SystemOrganizationDbModel()], new FixedCompany(null));
        var company = new SystemCompany { Code = $"J{suffix}", Name = "Company" };
        setup.Add(company);
        await setup.SaveChangesAsync(token);

        var currentCompany = new FixedCompany(company.Id);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 10, 18, 0, 0)), new FixedUser(7), currentCompany);
        var auditedOptions = new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options;
        await using var db = new MyErpDbContext(auditedOptions, [new SystemOrganizationDbModel()], currentCompany);
        var service = new OrganizationService(db);
        var branch = await service.CreateBranchAsync($"V{suffix}", "First", token);
        Assert.Equal(1, branch.Version);
        await service.UpdateBranchAsync(branch.Id, "Second", true, branch.Version, token);
        Assert.Equal(2, branch.Version);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateBranchAsync(branch.Id, "Stale", true, 1, token));
    }
}
