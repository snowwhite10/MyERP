using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class SupplierCategoryTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new PaymentSetupDbModel(), new SupplierCategoryDbModel(), new SupplierDbModel()], company);
    }

    [Fact]
    public async Task SupplierCategoryLifecycleIsCompanyScopedAndDeletedCodesStayReserved()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(4000000, 5000000);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId + 1);
        var first = new SupplierCategoryService(firstDb);
        var second = new SupplierCategoryService(secondDb);
        var code = ("S" + Guid.NewGuid().ToString("N")[..12]).ToUpperInvariant();
        var created = await first.CreateAsync(code.ToLowerInvariant(), " Raw material ", token);
        Assert.Equal(code, created.Code);
        Assert.Equal("Raw material", created.Name);
        Assert.True(created.IsActive);
        Assert.Equal(7, created.CreatedBy);
        Assert.Equal(0, (await second.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => second.UpdateAsync(created.Id, "Other", false, created.Version, token));
        await Assert.ThrowsAsync<NotFoundException>(() => second.DeleteAsync(created.Id, created.Version, token));
        Assert.NotEqual(created.Id, (await second.CreateAsync(code, "Separate company", token)).Id);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Duplicate", token));
        Assert.Equal("MD.SUPPLIER_CATEGORY.DUPLICATE_CODE", duplicate.ErrorCode);
        var staleVersion = created.Version;
        var updated = await first.UpdateAsync(created.Id, " Services ", false, created.Version, token);
        Assert.Equal("Services", updated.Name);
        Assert.False(updated.IsActive);
        await Assert.ThrowsAsync<ConflictException>(() => first.UpdateAsync(created.Id, "Stale", true, staleVersion, token));
        await Assert.ThrowsAsync<ConflictException>(() => first.DeleteAsync(created.Id, staleVersion, token));
        await first.DeleteAsync(created.Id, updated.Version, token);
        Assert.Equal(0, (await first.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Reused", token));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => first.DeleteAsync(created.Id, updated.Version, token));
        Assert.Equal("MD.SUPPLIER_CATEGORY.NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task SupplierCategoryListSortsPagesAndValidatesInput()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(5000000, 6000000);
        await using var db = Context(companyId);
        var service = new SupplierCategoryService(db);
        var prefix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(prefix + "A", "Zeta", token);
        var second = await service.CreateAsync(prefix + "B", "Alpha", token);
        await service.UpdateAsync(second.Id, second.Name, false, second.Version, token);
        var page = await service.ListAsync(new PagedRequest { Page = 2, PageSize = 1, Sort = "code", Keyword = prefix }, token);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(prefix + "B", Assert.Single(page.Items).Code);
        var byName = await service.ListAsync(new PagedRequest { Sort = "-name", Keyword = prefix }, token);
        Assert.Equal(["Zeta", "Alpha"], byName.Items.Select(x => x.Name));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ListAsync(new PagedRequest { Sort = "createdAt" }, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ListAsync(new PagedRequest { Sort = "code,name" }, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync("bad code", "Name", token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(new string('A', 31), "Name", token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(prefix + "C", "  ", token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(prefix + "C", new string('N', 201), token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateAsync(second.Id, "", true, second.Version + 1, token));
    }

    [Fact]
    public async Task SupplierCategoryRejectsStaleWritersAndMissingCompany()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(6000000, 7000000);
        await using var setup = Context(companyId);
        var category = await new SupplierCategoryService(setup).CreateAsync(
            "S" + Guid.NewGuid().ToString("N")[..12], "Before", token);
        await using var firstDb = Context(companyId);
        await using var staleDb = Context(companyId);
        var first = await firstDb.Set<SupplierCategory>().SingleAsync(x => x.Id == category.Id, token);
        var stale = await staleDb.Set<SupplierCategory>().SingleAsync(x => x.Id == category.Id, token);
        await new SupplierCategoryService(firstDb).UpdateAsync(first.Id, "First", true, first.Version, token);
        await Assert.ThrowsAsync<ConflictException>(() => new SupplierCategoryService(staleDb)
            .UpdateAsync(stale.Id, "Stale", true, stale.Version, token));
        await using var noCompany = Context(null);
        var service = new SupplierCategoryService(noCompany);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(new PagedRequest(), token));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync("NOCOMPANY", "Name", token));
    }

    [Fact]
    public async Task SupplierCategoryHasCompanyScopedAlternateKeyForSupplierForeignKeys()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(1);
        var keys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('md_supplier_category')")
            .ToListAsync(token);
        Assert.Contains("pk_md_supplier_category", keys);
        Assert.Contains("ak_md_supplier_category_company_id_id", keys);
    }
}
