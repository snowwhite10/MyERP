using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class ItemTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemBrandDbModel(), new ItemCategoryDbModel(), new ItemDbModel()], company);
    }

    [Fact]
    public async Task ItemRequiresActiveCompanyReferencesAndKeepsCodeReserved()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(19000000, 20000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var uoms = new UomService(db);
        var brands = new ItemBrandService(db);
        var categories = new ItemCategoryService(db);
        var items = new ItemService(db);
        var code = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var uom = await uoms.CreateAsync("U" + code, "Piece", 0, token);
        var brand = await brands.CreateAsync("B" + code, "Brand", token);
        var category = await categories.CreateAsync("C" + code, "Category", null, token);
        var item = await items.CreateAsync(code.ToLowerInvariant(), " Item ", " Large ", uom.Id, brand.Id, category.Id, token);
        Assert.Equal(code, item.Code);
        Assert.Equal("Item", item.Name);
        Assert.Equal("Large", item.Specification);
        Assert.Equal(0, (await new ItemService(otherDb).ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => new ItemService(otherDb).UpdateAsync(item.Id, "Other", null, uom.Id, null, null, false, item.Version, token));
        Assert.Equal("MD.ITEM_BRAND.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() => brands.DeleteAsync(brand.Id, brand.Version, token))).ErrorCode);
        Assert.Equal("MD.ITEM_CATEGORY.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() => categories.DeleteAsync(category.Id, category.Version, token))).ErrorCode);
        Assert.Equal("MD.UOM.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() => uoms.DeleteAsync(uom.Id, uom.Version, token))).ErrorCode);
        var staleVersion = item.Version;
        var updated = await items.UpdateAsync(item.Id, " Item 2 ", null, uom.Id, null, null, false, item.Version, token);
        Assert.Equal("Item 2", updated.Name);
        Assert.False(updated.IsActive);
        await Assert.ThrowsAsync<ConflictException>(() => items.UpdateAsync(item.Id, "Old", null, uom.Id, null, null, true, staleVersion, token));
        Assert.Equal(1, (await items.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        Assert.Equal("MD.ITEM.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(() => items.CreateAsync(code, "Duplicate", null, uom.Id, null, null, token))).ErrorCode);
    }

    [Fact]
    public async Task ItemRejectsInactiveAndForeignReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(20000000, 21000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var uom = await new UomService(db).CreateAsync("U" + Guid.NewGuid().ToString("N")[..12], "Unit", 0, token);
        var foreignBrand = await new ItemBrandService(otherDb).CreateAsync("B" + Guid.NewGuid().ToString("N")[..12], "Foreign", token);
        var items = new ItemService(db);
        await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync("A", "Name", null, uom.Id, foreignBrand.Id, null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync("A", "Name", null, 0, null, null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync("bad code", "Name", null, uom.Id, null, null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync("A", " ", null, uom.Id, null, null, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync("A", "Name", new string('S', 501), uom.Id, null, null, token));
        var brand = await new ItemBrandService(db).CreateAsync("B" + Guid.NewGuid().ToString("N")[..12], "Brand", token);
        await new ItemBrandService(db).UpdateAsync(brand.Id, brand.Name, false, brand.Version, token);
        await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync("A", "Name", null, uom.Id, brand.Id, null, token));
        var category = await new ItemCategoryService(db).CreateAsync("C" + Guid.NewGuid().ToString("N")[..12], "Category", null, token);
        await new ItemCategoryService(db).UpdateAsync(category.Id, category.Name, false, null, category.Version, token);
        await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync("A", "Name", null, uom.Id, null, category.Id, token));
        await new UomService(db).UpdateAsync(uom.Id, uom.Name, false, uom.DecimalPlaces, uom.Version, token);
        await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync("A", "Name", null, uom.Id, null, null, token));
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ItemService(noCompany).ListAsync(new PagedRequest(), token));
    }
}
