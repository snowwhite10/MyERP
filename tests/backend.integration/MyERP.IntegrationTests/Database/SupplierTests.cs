using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class SupplierTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new SupplierCategoryDbModel(), new SupplierDbModel()], company);
    }

    [Fact]
    public async Task SupplierRequiresSameCompanyCategoryAndPreventsCategoryDeletion()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(7000000, 8000000);
        await using var db = Context(companyId);
        var categories = new SupplierCategoryService(db);
        var category = await categories.CreateAsync("R" + Guid.NewGuid().ToString("N")[..12], "Raw", token);
        var suppliers = new SupplierService(db);
        var code = "S" + Guid.NewGuid().ToString("N")[..12];
        var created = await suppliers.CreateAsync(code, " First supplier ", category.Id, token);
        Assert.Equal(category.Id, created.CategoryId);
        Assert.Equal("First supplier", created.Name);
        Assert.Equal(7, created.CreatedBy);
        var inUse = await Assert.ThrowsAsync<ConflictException>(() => categories.DeleteAsync(category.Id, category.Version, token));
        Assert.Equal("MD.SUPPLIER_CATEGORY.IN_USE", inUse.ErrorCode);
        await using var other = Context(companyId + 1);
        await Assert.ThrowsAsync<RequestValidationException>(() => new SupplierService(other)
            .CreateAsync(code, "Cross company", category.Id, token));
        await Assert.ThrowsAsync<NotFoundException>(() => new SupplierService(other)
            .UpdateAsync(created.Id, "Other", true, null, created.Version, token));
        await suppliers.UpdateAsync(created.Id, "Renamed", false, null, created.Version, token);
        var page = await suppliers.ListAsync(new PagedRequest { Keyword = code }, token);
        Assert.Equal("Renamed", Assert.Single(page.Items).Name);
        Assert.False(page.Items[0].IsActive);
        Assert.Null(page.Items[0].CategoryId);
        Assert.Equal(0, (await new SupplierService(other).ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await categories.DeleteAsync(category.Id, category.Version, token);
        await using var direct = Context(companyId + 1);
        var foreign = await new SupplierCategoryService(db).CreateAsync("F" + Guid.NewGuid().ToString("N")[..12], "Foreign", token);
        direct.Add(new Supplier { CompanyId = companyId + 1, Code = code + "X", Name = "Invalid FK", CategoryId = foreign.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync(token));
    }

    [Fact]
    public async Task SupplierCodeVersionAndInactiveCategoryRulesAreEnforced()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(8000000, 9000000);
        await using var db = Context(companyId);
        var categories = new SupplierCategoryService(db);
        var category = await categories.CreateAsync("K" + Guid.NewGuid().ToString("N")[..12], "Key", token);
        var suppliers = new SupplierService(db);
        var code = "S" + Guid.NewGuid().ToString("N")[..12];
        var created = await suppliers.CreateAsync(code.ToLowerInvariant(), " Original ", category.Id, token);
        Assert.Equal(code.ToUpperInvariant(), created.Code);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => suppliers.CreateAsync(code, "Duplicate", null, token));
        Assert.Equal("MD.SUPPLIER.DUPLICATE_CODE", duplicate.ErrorCode);
        await Assert.ThrowsAsync<RequestValidationException>(() => suppliers.CreateAsync("bad code", "Name", null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => suppliers.CreateAsync(new string('A', 31), "Name", null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => suppliers.CreateAsync(code + "2", "  ", null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => suppliers.CreateAsync(code + "2", new string('N', 201), null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => suppliers.CreateAsync(code + "2", "Name", 0, token));
        await categories.UpdateAsync(category.Id, category.Name, false, category.Version, token);
        await Assert.ThrowsAsync<RequestValidationException>(() => suppliers.CreateAsync(code + "2", "New", category.Id, token));
        var staleVersion = created.Version;
        var updated = await suppliers.UpdateAsync(created.Id, " Kept ", true, category.Id, created.Version, token);
        Assert.Equal("Kept", updated.Name);
        Assert.Equal(category.Id, updated.CategoryId);
        await Assert.ThrowsAsync<ConflictException>(() => suppliers.UpdateAsync(created.Id, "Stale", true, null, staleVersion, token));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => suppliers.UpdateAsync(-1, "Missing", true, null, 1, token));
        Assert.Equal("MD.SUPPLIER.NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task SupplierListSortsPagesAndRequiresCompany()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(9000000, 10000000);
        await using var db = Context(companyId);
        var service = new SupplierService(db);
        var prefix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(prefix + "A", "Zeta", null, token);
        var second = await service.CreateAsync(prefix + "B", "Alpha", null, token);
        await service.UpdateAsync(second.Id, second.Name, false, null, second.Version, token);
        var page = await service.ListAsync(new PagedRequest { Page = 2, PageSize = 1, Sort = "code", Keyword = prefix }, token);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(prefix + "B", Assert.Single(page.Items).Code);
        var byName = await service.ListAsync(new PagedRequest { Sort = "-name", Keyword = prefix }, token);
        Assert.Equal(["Zeta", "Alpha"], byName.Items.Select(x => x.Name));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ListAsync(new PagedRequest { Sort = "createdAt" }, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ListAsync(new PagedRequest { Sort = "code,name" }, token));
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new SupplierService(noCompany).ListAsync(new PagedRequest(), token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new SupplierService(noCompany).CreateAsync("NOCOMPANY", "Name", null, token));
    }
}
