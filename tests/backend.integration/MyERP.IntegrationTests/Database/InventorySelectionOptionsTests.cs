using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class InventorySelectionOptionsTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                    new FixedClock(new DateTime(2026, 10, 14)), new FixedUser(7), company))).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel()], company);
    }

    [Fact]
    public async Task OptionsAreActiveCompanyScopedSearchableAndStablyPaged()
    {
        var ct = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        var other = TestCompanies.Next();
        await using var db = Context(company);
        await using var foreign = Context(other);
        var uom = await new UomService(db).CreateAsync($"SU{company}", "Unit", 0, ct);
        var a = await new ItemService(db).CreateAsync($"SA{company}", "Needle one", null, uom.Id, null, null, ct);
        var b = await new ItemService(db).CreateAsync($"SB{company}", "Needle two", null, uom.Id, null, null, ct);
        var inactive = await new ItemService(db).CreateAsync($"SC{company}", "Needle inactive", null, uom.Id, null, null, ct);
        var deleted = await new ItemService(db).CreateAsync($"SD{company}", "Needle deleted", null, uom.Id, null, null, ct);
        var foreignUom = await new UomService(foreign).CreateAsync($"SU{other}", "Unit", 0, ct);
        await new ItemService(foreign).CreateAsync($"SZ{other}", "Needle foreign", null, foreignUom.Id, null, null, ct);
        inactive.IsActive = false;
        deleted.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        var options = new InventorySelectionOptions(db);
        var first = await options.ItemsAsync(new PagedRequest { Keyword = " needle ", PageSize = 1 }, ct);
        var second = await options.ItemsAsync(new PagedRequest { Keyword = "needle", Page = 2, PageSize = 1 }, ct);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(a.Id, Assert.Single(first.Items).Id);
        Assert.Equal(b.Id, Assert.Single(second.Items).Id);
        Assert.Equal(a.Id, Assert.Single((await options.ItemsAsync(new PagedRequest { Keyword = $"SA{company}" }, ct)).Items).Id);
        await Assert.ThrowsAsync<RequestValidationException>(() => options.ItemsAsync(new PagedRequest { PageSize = 201 }, ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => options.ItemsAsync(new PagedRequest { Sort = "name" }, ct));
    }

    [Fact]
    public async Task LocationsRequireAnActiveWarehouseInCurrentCompany()
    {
        var ct = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        var other = TestCompanies.Next();
        await using var db = Context(company);
        await using var foreign = Context(other);
        var warehouse = await new WarehouseService(db).CreateAsync($"SW{company}", "Warehouse", null, null, null, ct);
        var second = await new WarehouseService(db).CreateAsync($"SX{company}", "Other", null, null, null, ct);
        var inactive = await new WarehouseService(db).CreateAsync($"SY{company}", "Inactive", null, null, null, ct);
        var foreignWarehouse = await new WarehouseService(foreign).CreateAsync($"SW{other}", "Foreign", null, null, null, ct);
        var wanted = await new StorageLocationService(db).CreateAsync(warehouse.Id, $"SL{company}", "Needle", ct);
        await new StorageLocationService(db).CreateAsync(second.Id, $"SO{company}", "Needle elsewhere", ct);
        await new StorageLocationService(db).CreateAsync(inactive.Id, $"SI{company}", "Needle inactive parent", ct);
        var hidden = await new StorageLocationService(db).CreateAsync(warehouse.Id, $"SH{company}", "Needle hidden", ct);
        hidden.IsActive = false;
        inactive.IsActive = false;
        await db.SaveChangesAsync(ct);
        var options = new InventorySelectionOptions(db);
        Assert.Equal(wanted.Id, Assert.Single((await options.LocationsAsync(warehouse.Id,
            new PagedRequest { Keyword = "needle" }, ct)).Items).Id);
        Assert.DoesNotContain((await options.WarehousesAsync(new PagedRequest(), ct)).Items, x => x.Id == inactive.Id);
        foreach (var id in new[] { 0, inactive.Id, foreignWarehouse.Id, long.MaxValue })
            Assert.Equal("MD.WAREHOUSE.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
                options.LocationsAsync(id, new PagedRequest(), ct))).ErrorCode);
    }
}
