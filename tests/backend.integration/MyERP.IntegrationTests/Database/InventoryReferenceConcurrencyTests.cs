using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class InventoryReferenceConcurrencyTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel()], company);
    }

    [Fact]
    public async Task WriteCommittedFirstBlocksMasterDeletesAndKeepsReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var writing = Context(companyId);
        var uom = await new UomService(writing).CreateAsync("U" + companyId, "Unit", 0, token);
        var item = await new ItemService(writing).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(writing).CreateAsync("W" + companyId, "Warehouse", null, null, null, token);
        var location = await new StorageLocationService(writing).CreateAsync(warehouse.Id, "L" + companyId, "Location", token);
        await using var deleting = Context(companyId);
        var usage = new InventoryReferenceService(deleting, new InventoryReferenceGuard(deleting));
        await using (var transaction = await writing.Database.BeginTransactionAsync(token))
        {
            await new InventoryReferenceService(writing, new InventoryReferenceGuard(writing))
                .RecordAsync("line:" + companyId, item.Id, warehouse.Id, location.Id, token);
            var itemDelete = Task.Run(() => new ItemService(deleting, [usage]).DeleteAsync(item.Id, item.Version, token), token);
            await Task.Delay(250, token);
            Assert.False(itemDelete.IsCompleted);
            await transaction.CommitAsync(token);
            Assert.Equal("MD.ITEM.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() => itemDelete)).ErrorCode);
        }
        Assert.Equal("MD.STORAGE_LOCATION.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() =>
            new StorageLocationService(deleting, [usage]).DeleteAsync(warehouse.Id, location.Id, location.Version, token))).ErrorCode);
        Assert.True(await deleting.Set<InventoryReference>().AnyAsync(x => x.ItemId == item.Id && x.LocationId == location.Id, token));
    }

    [Fact]
    public async Task MasterDeleteCommittedFirstMakesWriteFailWithoutOrphan()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var deleting = Context(companyId);
        var uom = await new UomService(deleting).CreateAsync("U" + companyId, "Unit", 0, token);
        var item = await new ItemService(deleting).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(deleting).CreateAsync("W" + companyId, "Warehouse", null, null, null, token);
        await using var writing = Context(companyId);
        await using (var transaction = await deleting.Database.BeginTransactionAsync(token))
        {
            await new ItemService(deleting).DeleteAsync(item.Id, item.Version, token);
            var write = Task.Run(() => new InventoryReferenceService(writing, new InventoryReferenceGuard(writing))
                .RecordAsync("line:" + companyId, item.Id, warehouse.Id, null, token), token);
            await Task.Delay(250, token);
            Assert.False(write.IsCompleted);
            await transaction.CommitAsync(token);
            Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => write)).ErrorCode);
        }
        Assert.False(await writing.Set<InventoryReference>().AnyAsync(x => x.ItemId == item.Id, token));
    }
}
