using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests.Database;

public sealed class SafetyStockTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new SafetyStockDbModel()], company);
    }

    [Fact]
    public async Task ZeroBalanceIsLowAndSoftDeletedKeyRestoresSameRow()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var uom = await new UomService(db).CreateAsync("SU" + company, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("SI" + company, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db),
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        var created = await service.CreateAsync(item.Id, warehouse.Id, 5m, token);
        var initialVersion = created.Version;
        var page = await service.ListAsync(new SafetyStockQuery { LowOnly = true }, token);
        Assert.Single(page.Items);
        Assert.Equal(0m, page.Items[0].OnHand);
        Assert.Equal(5m, page.Items[0].Shortage);
        await service.DeleteAsync(created.Id, created.Version, token);
        var restored = await service.CreateAsync(item.Id, warehouse.Id, 3m, token);
        Assert.Equal(created.Id, restored.Id);
        Assert.True(restored.Version > initialVersion);
        Assert.Equal(1, await db.Set<SafetyStockThreshold>().IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == company, token));
        Assert.Empty(await db.Set<StockMovement>().ToListAsync(token));
    }

    [Fact]
    public async Task OldVersionAndOtherCompanyCannotChangeThreshold()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var uom = await new UomService(db).CreateAsync("SU" + company, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("SI" + company, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db),
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        var created = await service.CreateAsync(item.Id, warehouse.Id, 5m, token);
        var oldVersion = created.Version;
        var updated = await service.UpdateAsync(created.Id, oldVersion, 6m, token);
        Assert.Equal(6m, updated.MinimumQuantity);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(created.Id, oldVersion, 7m, token));
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(item.Id, warehouse.Id, 8m, token));
        await using var other = Context(TestCompanies.Next());
        var otherService = new SafetyStockService(other, new InventoryReferenceGuard(other),
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        Assert.Empty((await otherService.ListAsync(new SafetyStockQuery(), token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => otherService.DeleteAsync(created.Id, updated.Version, token));
        Assert.Equal(6m, (await db.Set<SafetyStockThreshold>().SingleAsync(token)).MinimumQuantity);
    }
}
