using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
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
    public async Task ConcurrentCreatesKeepPermanentUniqueKey()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var setup = Context(company);
        var uom = await new UomService(setup).CreateAsync("SU" + company, "Unit", 0, token);
        var item = await new ItemService(setup).CreateAsync("SI" + company, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(setup).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        await using var first = Context(company);
        await using var second = Context(company);
        var clock = new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0));
        var a = new SafetyStockService(first, new InventoryReferenceGuard(first), new InventorySelectionOptions(first), clock, new FixedUser(7));
        var b = new SafetyStockService(second, new InventoryReferenceGuard(second), new InventorySelectionOptions(second), clock, new FixedUser(7));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Attempt(SafetyStockService service, decimal quantity)
        {
            await gate.Task;
            try { await service.CreateAsync(item.Id, warehouse.Id, quantity, token); return null; }
            catch (Exception error) { return error; }
        }
        var attempts = new[] { Task.Run(() => Attempt(a, 5m), token), Task.Run(() => Attempt(b, 6m), token) };
        gate.SetResult();
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(x => x is null));
        Assert.Equal(1, results.Count(x => x is ConflictException));
        Assert.Equal(1, await setup.Set<SafetyStockThreshold>().IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == company, token));
    }

    [Fact]
    public async Task QuantityBoundariesAndDeletedMasterReferenceDoNotWriteMovements()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var uom = await new UomService(db).CreateAsync("SU" + company, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("SI" + company, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db),
            new InventorySelectionOptions(db), new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        foreach (var invalid in new[] { 0m, -1m, 0.0000001m, 10_000_000_000_000m })
            await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(item.Id, warehouse.Id, invalid, token));
        var smallest = await service.CreateAsync(item.Id, warehouse.Id, 0.000001m, token);
        var largest = await service.UpdateAsync(smallest.Id, smallest.Version, 9_999_999_999_999.999999m, token);
        Assert.Equal(9_999_999_999_999.999999m, largest.MinimumQuantity);
        await service.DeleteAsync(largest.Id, largest.Version, token);
        await new ItemService(db).DeleteAsync(item.Id, item.Version, token);
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(item.Id, warehouse.Id, 1m, token));
        Assert.Empty(await db.Set<StockMovement>().ToListAsync(token));
    }

    [Fact]
    public async Task PostedMovementsAcrossLocationsEqualThresholdThenFallBelowIt()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var uom = await new UomService(db).CreateAsync("SU" + company, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("SI" + company, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "SA" + company, "A", token);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "SB" + company, "B", token);
        var clock = new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0));
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db),
            new InventorySelectionOptions(db), clock, new FixedUser(7));
        await service.CreateAsync(item.Id, warehouse.Id, 5m, token);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), clock);
        async Task Post(long source, StockDirection direction, long location, decimal quantity) =>
            await posting.PostAsync(new InventoryPostingRequest("SAFETY_STOCK_TEST", source,
                new DateOnly(2026, 10, 16), [new InventoryPostingLine(1, direction, item.Id,
                    warehouse.Id, location, quantity, direction == StockDirection.In ? 1m : null)]), token);
        await Post(1, StockDirection.In, a.Id, 2m);
        await Post(2, StockDirection.In, b.Id, 3m);
        var equal = Assert.Single((await service.ListAsync(new SafetyStockQuery(), token)).Items);
        Assert.Equal(5m, equal.OnHand);
        Assert.False(equal.IsLow);
        Assert.Equal(0m, equal.Shortage);
        Assert.Equal(0, (await service.ListAsync(new SafetyStockQuery { LowOnly = true }, token)).TotalCount);
        await Post(3, StockDirection.Out, b.Id, 1m);
        var movementsBefore = await db.Set<StockMovement>().CountAsync(token);
        var low = Assert.Single((await service.ListAsync(new SafetyStockQuery { LowOnly = true }, token)).Items);
        Assert.Equal(4m, low.OnHand);
        Assert.Equal(1m, low.Shortage);
        Assert.Equal(movementsBefore, await db.Set<StockMovement>().CountAsync(token));
    }

    [Fact]
    public async Task TwoConnectionsCannotCreateDuplicateOrOverwriteNewerVersion()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var first = Context(company);
        await using var second = Context(company);
        var uom = await new UomService(first).CreateAsync("SU" + company, "Unit", 0, token);
        var item = await new ItemService(first).CreateAsync("SI" + company, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(first).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var clock = new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0));
        var firstService = new SafetyStockService(first, new InventoryReferenceGuard(first),
            new InventorySelectionOptions(first), clock, new FixedUser(7));
        var secondService = new SafetyStockService(second, new InventoryReferenceGuard(second),
            new InventorySelectionOptions(second), clock, new FixedUser(7));
        var created = await firstService.CreateAsync(item.Id, warehouse.Id, 5m, token);
        await Assert.ThrowsAsync<ConflictException>(() => secondService.CreateAsync(item.Id, warehouse.Id, 6m, token));
        var newer = await secondService.UpdateAsync(created.Id, created.Version, 7m, token);
        await Assert.ThrowsAsync<ConflictException>(() => firstService.UpdateAsync(created.Id, created.Version, 8m, token));
        Assert.Equal(7m, (await first.Set<SafetyStockThreshold>().AsNoTracking().SingleAsync(token)).MinimumQuantity);
        Assert.Equal(1, await second.Set<SafetyStockThreshold>().IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == company, token));
    }

    [Fact]
    public async Task KeywordAndCodeAndShortageSortRunBeforePaging()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var uom = await new UomService(db).CreateAsync("SU" + company, "Unit", 0, token);
        var a = await new ItemService(db).CreateAsync("ZA" + company, "Alpha needle", null, uom.Id, null, null, token);
        var b = await new ItemService(db).CreateAsync("AB" + company, "Beta needle", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db), new InventorySelectionOptions(db),
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        await service.CreateAsync(a.Id, warehouse.Id, 2m, token);
        await service.CreateAsync(b.Id, warehouse.Id, 5m, token);
        var page = await service.ListAsync(new SafetyStockQuery { Keyword = "needle", Sort = "-shortage", PageSize = 1 }, token);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(b.Id, Assert.Single(page.Items).ItemId);
        var codePage = await service.ListAsync(new SafetyStockQuery { Sort = "-itemCode", PageSize = 1 }, token);
        Assert.Equal(a.Id, Assert.Single(codePage.Items).ItemId);
        var secondPage = await service.ListAsync(new SafetyStockQuery { Sort = "-itemCode", PageSize = 1, Page = 2 }, token);
        Assert.Equal(2, secondPage.TotalCount);
        Assert.Equal(b.Id, Assert.Single(secondPage.Items).ItemId);
        var ascendingShortage = await service.ListAsync(new SafetyStockQuery { Keyword = " needle ", Sort = "shortage", PageSize = 1 }, token);
        Assert.Equal(a.Id, Assert.Single(ascendingShortage.Items).ItemId);
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
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db), new InventorySelectionOptions(db),
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
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db), new InventorySelectionOptions(db),
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        var created = await service.CreateAsync(item.Id, warehouse.Id, 5m, token);
        var oldVersion = created.Version;
        var updated = await service.UpdateAsync(created.Id, oldVersion, 6m, token);
        Assert.Equal(6m, updated.MinimumQuantity);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(created.Id, oldVersion, 7m, token));
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(item.Id, warehouse.Id, 8m, token));
        await using var other = Context(TestCompanies.Next());
        var otherService = new SafetyStockService(other, new InventoryReferenceGuard(other), new InventorySelectionOptions(other),
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        Assert.Empty((await otherService.ListAsync(new SafetyStockQuery(), token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => otherService.DeleteAsync(created.Id, updated.Version, token));
        Assert.Equal(6m, (await db.Set<SafetyStockThreshold>().SingleAsync(token)).MinimumQuantity);
    }
}
