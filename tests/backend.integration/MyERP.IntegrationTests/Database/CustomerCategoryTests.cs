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
            [new CustomerCategoryDbModel()], company);
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
}
