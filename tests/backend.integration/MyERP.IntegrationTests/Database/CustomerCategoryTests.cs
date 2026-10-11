using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class CustomerCategoryTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new CustomerCategoryDbModel(), new CustomerDbModel()], company);
    }

    [Fact]
    public async Task CategoryLifecycleIsCompanyScopedAndDeletedCodesStayReserved()
    {
        var token = TestContext.Current.CancellationToken;
        await using var firstDb = Context(101);
        await using var secondDb = Context(202);
        var first = new CustomerCategoryService(firstDb);
        var second = new CustomerCategoryService(secondDb);
        var code = ("C" + Guid.NewGuid().ToString("N")[..12]).ToUpperInvariant();
        var created = await first.CreateAsync(code.ToLowerInvariant(), " Retail ", token);
        Assert.Equal(code, created.Code);
        Assert.Equal("Retail", created.Name);
        Assert.Equal(0, (await second.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => second.UpdateAsync(created.Id, "Other", false, created.Version, token));
        var other = await second.CreateAsync(code, "Separate company", token);
        Assert.NotEqual(created.Id, other.Id);
        await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Duplicate", token));
        var staleVersion = created.Version;
        var updated = await first.UpdateAsync(created.Id, "Wholesale", false, created.Version, token);
        await Assert.ThrowsAsync<ConflictException>(() => first.UpdateAsync(created.Id, "Stale", true, staleVersion, token));
        await first.DeleteAsync(created.Id, updated.Version, token);
        Assert.Equal(0, (await first.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Reused", token));
        await Assert.ThrowsAsync<NotFoundException>(() => first.DeleteAsync(created.Id, updated.Version, token));
    }

    [Fact]
    public async Task ListValidatesSortAndPagesActiveAndInactiveCategories()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(100000, 1000000);
        await using var db = Context(companyId);
        var service = new CustomerCategoryService(db);
        var prefix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(prefix + "A", "Alpha", token);
        var second = await service.CreateAsync(prefix + "B", "Beta", token);
        await service.UpdateAsync(second.Id, second.Name, false, second.Version, token);
        var page = await service.ListAsync(new PagedRequest { Page = 2, PageSize = 1, Sort = "code", Keyword = prefix }, token);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(prefix + "B", Assert.Single(page.Items).Code);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ListAsync(new PagedRequest { Sort = "createdAt" }, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync("bad code", "Name", token));
    }

    [Fact]
    public async Task ConcurrentEditsRejectTheStaleWriterAndMissingCompanyCannotRead()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(1000000, 2000000);
        await using var setup = Context(companyId);
        var category = await new CustomerCategoryService(setup).CreateAsync(
            "C" + Guid.NewGuid().ToString("N")[..12], "Before", token);
        await using var firstDb = Context(companyId);
        await using var staleDb = Context(companyId);
        var first = await firstDb.Set<CustomerCategory>().SingleAsync(x => x.Id == category.Id, token);
        var stale = await staleDb.Set<CustomerCategory>().SingleAsync(x => x.Id == category.Id, token);
        await new CustomerCategoryService(firstDb).UpdateAsync(first.Id, "First", true, first.Version, token);
        await Assert.ThrowsAsync<ConflictException>(() => new CustomerCategoryService(staleDb)
            .UpdateAsync(stale.Id, "Stale", true, stale.Version, token));
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new CustomerCategoryService(noCompany)
            .ListAsync(new PagedRequest(), token));
    }

    [Fact]
    public async Task CustomerRequiresSameCompanyCategoryAndPreventsCategoryDeletion()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(2000000, 3000000);
        await using var db = Context(companyId);
        var categories = new CustomerCategoryService(db);
        var category = await categories.CreateAsync("T" + Guid.NewGuid().ToString("N")[..12], "Trade", token);
        var customers = new CustomerService(db);
        var code = "C" + Guid.NewGuid().ToString("N")[..12];
        var created = await customers.CreateAsync(code, "First customer", category.Id, token);
        Assert.Equal(category.Id, created.CategoryId);
        await Assert.ThrowsAsync<ConflictException>(() => categories.DeleteAsync(category.Id, category.Version, token));
        await using var other = Context(companyId + 1);
        await Assert.ThrowsAsync<RequestValidationException>(() => new CustomerService(other)
            .CreateAsync(code, "Cross company", category.Id, token));
        await customers.UpdateAsync(created.Id, "Renamed", false, null, created.Version, token);
        var page = await customers.ListAsync(new PagedRequest { Keyword = code }, token);
        Assert.Equal("Renamed", Assert.Single(page.Items).Name);
        Assert.False(page.Items[0].IsActive);
        Assert.Equal(0, (await new CustomerService(other).ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await using var direct = Context(companyId + 1);
        direct.Add(new Customer { CompanyId = companyId + 1, Code = code + "X", Name = "Invalid FK", CategoryId = category.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync(token));
    }

    [Fact]
    public async Task CustomerCodeVersionAndInactiveCategoryRulesAreEnforced()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(3000000, 4000000);
        await using var db = Context(companyId);
        var categories = new CustomerCategoryService(db);
        var category = await categories.CreateAsync("K" + Guid.NewGuid().ToString("N")[..12], "Key", token);
        var customers = new CustomerService(db);
        var code = "C" + Guid.NewGuid().ToString("N")[..12];
        var created = await customers.CreateAsync(code.ToLowerInvariant(), " Original ", category.Id, token);
        Assert.Equal(code.ToUpperInvariant(), created.Code);
        await Assert.ThrowsAsync<ConflictException>(() => customers.CreateAsync(code, "Duplicate", null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => customers.CreateAsync("bad code", "Name", null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => customers.CreateAsync(code + "2", "  ", null, token));
        await categories.UpdateAsync(category.Id, category.Name, false, category.Version, token);
        await Assert.ThrowsAsync<RequestValidationException>(() => customers.CreateAsync(code + "2", "New", category.Id, token));
        var staleVersion = created.Version;
        var updated = await customers.UpdateAsync(created.Id, " Kept ", true, category.Id, created.Version, token);
        Assert.Equal("Kept", updated.Name);
        await Assert.ThrowsAsync<ConflictException>(() => customers.UpdateAsync(created.Id, "Stale", true, null, staleVersion, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => customers.ListAsync(new PagedRequest { Sort = "createdAt" }, token));
    }
}
