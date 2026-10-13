using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>MD-ITEM-TRK-001～004：物料批號／序號追蹤方式（REQ_F4118.3.7、ADR-060）。</summary>
public sealed class ItemTrackingModeTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemBrandDbModel(), new ItemCategoryDbModel(), new ItemDbModel(), new WarehouseDbModel()], company);
    }

    private sealed class FakeItemUsage : IItemUsage
    {
        public bool InUse { get; set; }
        public List<string> Calls { get; } = [];

        public Task<bool> IsInUseAsync(string itemCode, CancellationToken token = default)
        {
            Calls.Add(itemCode);
            return Task.FromResult(InUse);
        }
    }

    [Fact]
    public async Task CreateDefaultsToNoneAndStoresChosenMode()
    {
        // MD-ITEM-TRK-001
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, token);
        var items = new ItemService(db);

        var plain = await items.CreateAsync("P" + companyId, "Plain", null, uom.Id, null, null, token);
        var lot = await items.CreateAsync("L" + companyId, "Lot", null, uom.Id, null, null, ItemTrackingMode.Lot, token);
        var serial = await items.CreateAsync("S" + companyId, "Serial", null, uom.Id, null, null, ItemTrackingMode.Serial, token);

        Assert.Equal("None", plain.TrackingMode);
        db.ChangeTracker.Clear();
        var stored = await db.Set<Modules.MasterData.Domain.Item>().AsNoTracking()
            .Where(x => x.Id == lot.Id || x.Id == serial.Id).OrderBy(x => x.Id).Select(x => x.TrackingMode).ToListAsync(token);
        Assert.Equal(["Lot", "Serial"], stored);
        var raw = await db.Database.SqlQuery<string>(
            $"SELECT tracking_mode AS Value FROM md_item WHERE id = {serial.Id}").SingleAsync(token);
        Assert.Equal("Serial", raw);

        Assert.Equal("trackingMode", (await Assert.ThrowsAsync<RequestValidationException>(() => items.CreateAsync(
            "X" + companyId, "Bad", null, uom.Id, null, null, (ItemTrackingMode)99, token))).Errors.Keys.Single());
        Assert.False(await db.Set<Modules.MasterData.Domain.Item>().AnyAsync(x => x.Code == "X" + companyId, token));
    }

    [Fact]
    public async Task TrackingModeCanChangeOnlyBeforeAnyUsage()
    {
        // MD-ITEM-TRK-002
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var usage = new FakeItemUsage();
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, token);
        var items = new ItemService(db, [usage]);
        var item = await items.CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, token);

        // 未傳追蹤方式（舊用戶端）或傳入相同方式時保持不變，也不詢問使用方。
        item = await items.UpdateAsync(item.Id, "Renamed", null, uom.Id, null, null, true, item.Version, token);
        item = await items.UpdateAsync(item.Id, "Renamed", null, uom.Id, null, null, true, item.Version, ItemTrackingMode.None, token);
        Assert.Equal("None", item.TrackingMode);
        Assert.Empty(usage.Calls);

        item = await items.UpdateAsync(item.Id, "Renamed", null, uom.Id, null, null, true, item.Version, ItemTrackingMode.Lot, token);
        Assert.Equal("Lot", item.TrackingMode);
        Assert.Equal([item.Code], usage.Calls);

        usage.InUse = true;
        var version = item.Version;
        Assert.Equal("MD.ITEM.TRACKING_MODE_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() => items.UpdateAsync(
            item.Id, "Changed", null, uom.Id, null, null, true, version, ItemTrackingMode.Serial, token))).ErrorCode);
        db.ChangeTracker.Clear();
        var unchanged = await db.Set<Modules.MasterData.Domain.Item>().AsNoTracking().SingleAsync(x => x.Id == item.Id, token);
        Assert.Equal(("Lot", "Renamed", version), (unchanged.TrackingMode, unchanged.Name, unchanged.Version));

        // 使用中仍可修改其他欄位，追蹤方式保持不變。
        var renamed = await new ItemService(db, [usage]).UpdateAsync(
            item.Id, "Still editable", null, uom.Id, null, null, false, version, ItemTrackingMode.Lot, token);
        Assert.Equal(("Lot", "Still editable", false), (renamed.TrackingMode, renamed.Name, renamed.IsActive));
        Assert.Equal("trackingMode", (await Assert.ThrowsAsync<RequestValidationException>(() => new ItemService(db, [usage]).UpdateAsync(
            item.Id, "Bad", null, uom.Id, null, null, true, renamed.Version, (ItemTrackingMode)(-1), token))).Errors.Keys.Single());
    }

    [Fact]
    public async Task TrackingChangeHoldsExclusiveItemLockUntilCommit()
    {
        // MD-ITEM-TRK-003：與 INV 引用端（共享 md_item 鎖）互斥，不會在檢查使用後才出現第一筆異動。
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        await using var probe = database.CreateRawContext();
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, token);
        var items = new ItemService(db, [new FakeItemUsage()]);
        var item = await items.CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, token);

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await items.UpdateAsync(item.Id, "Item", null, uom.Id, null, null, true, item.Version, ItemTrackingMode.Serial, token);
            Assert.Equal(0, await SharedGrantableAsync(probe, $"md_item:{companyId}", token));
            await transaction.CommitAsync(token);
        }
        Assert.Equal(1, await SharedGrantableAsync(probe, $"md_item:{companyId}", token));
    }

    [Fact]
    public async Task InventoryGuardReturnsTrackingMode()
    {
        // MD-ITEM-TRK-004：INV 經 MD 契約取得追蹤方式，不讀 md_item。
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, ItemTrackingMode.Lot, token);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, token);

        var codes = await new InventoryReferenceGuard(db).RunWithCodesAsync(item.Id, warehouse.Id, null, Task.FromResult, token);
        Assert.Equal(new InventoryReferenceCodes(item.Code, warehouse.Code, null, ItemTrackingMode.Lot), codes);
    }

    private static async Task<int> SharedGrantableAsync(MyErpDbContext db, string resource, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        return await db.Database.SqlQuery<int>(
            $"SELECT APPLOCK_TEST('public', {resource}, 'Shared', 'Transaction') AS Value").SingleAsync(token);
    }
}
