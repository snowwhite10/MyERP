using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class SystemOrganizationTests(TestDatabase database)
{
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
}
