using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-ASM-006：成本快照與所有 INV 寫入共用物料鎖。</summary>
public sealed class AssemblyCostSnapshotTests(TestDatabase database)
{
    private static readonly DateTime Now = new(2026, 10, 15, 8, 0, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel()], company);
    }

    [Fact]
    public async Task LockedSnapshotSerializesAConcurrentPostingAndPreservesKnownZero()
    {
        var companyId = TestCompanies.Next();
        long itemId, warehouseId;
        await using (var setup = Context(companyId))
        {
            var uom = await new UomService(setup).CreateAsync("U" + companyId, "Unit", 0, Ct);
            itemId = (await new ItemService(setup).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct)).Id;
            warehouseId = (await new WarehouseService(setup).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct)).Id;
        }

        await using var first = Context(companyId);
        await using var firstTransaction = await first.Database.BeginTransactionAsync(Ct);
        var snapshot = new AssemblyCostSnapshot(first, new InventoryValuationQuery(first));
        var empty = await snapshot.GetLockedAsync(itemId, Ct);
        Assert.Null(empty.AverageUnitCost);

        await using var second = Context(companyId);
        var posting = new StockPostingService(second,
            new InventoryReferenceService(second, new InventoryReferenceGuard(second)), new FixedClock(Now));
        var pending = posting.PostAsync(new InventoryPostingRequest("TEST_SNAPSHOT", companyId,
            new DateOnly(2026, 10, 15),
            [new InventoryPostingLine(1, StockDirection.In, itemId, warehouseId, null, 2m, 0m)]), Ct);
        await Task.Delay(250, Ct);
        Assert.False(pending.IsCompleted);
        await firstTransaction.CommitAsync(Ct);
        await pending;

        await using var verify = Context(companyId);
        await using var verifyTransaction = await verify.Database.BeginTransactionAsync(Ct);
        var knownZero = await new AssemblyCostSnapshot(verify, new InventoryValuationQuery(verify))
            .GetLockedAsync(itemId, Ct);
        Assert.Equal(2m, knownZero.OnHand);
        Assert.Equal(0m, knownZero.AverageUnitCost);
    }
}
