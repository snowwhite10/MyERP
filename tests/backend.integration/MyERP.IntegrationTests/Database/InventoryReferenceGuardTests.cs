using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class InventoryReferenceGuardTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel()], company);
    }

    [Fact]
    public async Task GuardValidatesReferencesAndKeepsSharedLocksUntilTransactionEnds()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var writer = Context(companyId);
        await using var probe = database.CreateRawContext();
        var uom = await new UomService(writer).CreateAsync("U" + companyId, "Unit", 0, token);
        var item = await new ItemService(writer).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(writer).CreateAsync("W" + companyId, "Warehouse", null, null, null, token);
        var location = await new StorageLocationService(writer).CreateAsync(warehouse.Id, "L" + companyId, "Location", token);
        var guard = new InventoryReferenceGuard(writer);

        await using (var transaction = await writer.Database.BeginTransactionAsync(token))
        {
            Assert.True(await guard.RunAsync(item.Id, warehouse.Id, location.Id, () => Task.FromResult(true), token));
            Assert.Equal(0, await GrantableAsync(probe, $"md_item:{companyId}", token));
            Assert.Equal(0, await GrantableAsync(probe, $"md_warehouse:{companyId}", token));
            await transaction.CommitAsync(token);
        }
        Assert.Equal(1, await GrantableAsync(probe, $"md_item:{companyId}", token));
        Assert.Equal(1, await GrantableAsync(probe, $"md_warehouse:{companyId}", token));
        Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            guard.RunAsync(long.MaxValue, warehouse.Id, location.Id, () => Task.FromResult(true), token))).ErrorCode);
        Assert.Equal("MD.STORAGE_LOCATION.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            guard.RunAsync(item.Id, warehouse.Id, long.MaxValue, () => Task.FromResult(true), token))).ErrorCode);
    }

    [Fact]
    public async Task LocationDeleteHoldsExclusiveWarehouseLockUntilCommit()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var deleting = Context(companyId);
        await using var probe = database.CreateRawContext();
        var warehouse = await new WarehouseService(deleting).CreateAsync("W" + companyId, "Warehouse", null, null, null, token);
        var location = await new StorageLocationService(deleting).CreateAsync(warehouse.Id, "L" + companyId, "Location", token);
        deleting.ChangeTracker.Clear();
        location = await deleting.Set<MyERP.Modules.MasterData.Domain.StorageLocation>()
            .SingleAsync(x => x.Id == location.Id, token);
        await using (var transaction = await deleting.Database.BeginTransactionAsync(token))
        {
            await new StorageLocationService(deleting).DeleteAsync(warehouse.Id, location.Id, location.Version, token);
            Assert.Equal(0, await GrantableAsync(probe, $"md_warehouse:{companyId}", token));
            await transaction.CommitAsync(token);
        }
        Assert.Equal(1, await GrantableAsync(probe, $"md_warehouse:{companyId}", token));
    }

    private static async Task<int> GrantableAsync(MyErpDbContext db, string resource, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        return await db.Database.SqlQuery<int>(
            $"SELECT APPLOCK_TEST('public', {resource}, 'Exclusive', 'Transaction') AS Value").SingleAsync(token);
    }
}
