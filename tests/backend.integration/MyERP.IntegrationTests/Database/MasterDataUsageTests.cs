using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>MD-USAGE-001～004：物料、倉庫、儲位、稅別／稅率的使用方引用契約（ADR-049）。</summary>
public sealed class MasterDataUsageTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemBrandDbModel(), new ItemCategoryDbModel(), new ItemDbModel(),
             new WarehouseDbModel(), new TaxDbModel(), new CurrencyDbModel()], company);
    }

    private static long NewCompany() => Random.Shared.NextInt64(90000000, 91000000);

    private static string NewCode(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant();

    /// <summary>可設定的假使用方；記錄 MD 詢問的參數。</summary>
    private sealed class FakeUsage : IItemUsage, IWarehouseUsage, IStorageLocationUsage, ITaxCodeUsage
    {
        public bool InUse { get; set; }
        public Func<DateOnly, DateOnly?, bool>? Period { get; set; }
        public List<string> Calls { get; } = [];

        public Task<bool> IsInUseAsync(string code, CancellationToken token = default)
        {
            Calls.Add(code);
            return Task.FromResult(InUse);
        }

        public Task<bool> IsInUseAsync(string warehouseCode, string locationCode, CancellationToken token = default)
        {
            Calls.Add(warehouseCode + "/" + locationCode);
            return Task.FromResult(InUse);
        }

        public Task<bool> IsInUseAsync(string taxCode, DateOnly from, DateOnly? to, CancellationToken token = default)
        {
            Calls.Add($"{taxCode}:{from:yyyy-MM-dd}~{to:yyyy-MM-dd}");
            return Task.FromResult(Period?.Invoke(from, to) ?? InUse);
        }
    }

    [Fact]
    public async Task ItemInUseCannotChangeBaseUnitButOtherFieldsStayEditable()
    {
        // MD-USAGE-001
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany());
        var usage = new FakeUsage { InUse = true };
        var uoms = new UomService(db);
        var piece = await uoms.CreateAsync(NewCode("U"), "Piece", 0, token);
        var box = await uoms.CreateAsync(NewCode("U"), "Box", 0, token);
        var items = new ItemService(db, [usage]);
        var item = await items.CreateAsync(NewCode("I"), "Item", null, piece.Id, null, null, token);

        var renamed = await items.UpdateAsync(item.Id, "Renamed", null, piece.Id, null, null, true, item.Version, token);
        Assert.Empty(usage.Calls);
        Assert.Equal("MD.ITEM.BASE_UOM_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() =>
            items.UpdateAsync(item.Id, "Renamed", null, box.Id, null, null, true, renamed.Version, token))).ErrorCode);
        Assert.Equal([item.Code], usage.Calls);

        usage.InUse = false;
        var changed = await items.UpdateAsync(item.Id, "Renamed", null, box.Id, null, null, true, renamed.Version, token);
        Assert.Equal(box.Id, changed.BaseUomId);
    }

    [Fact]
    public async Task WarehouseInUseCannotBeDeleted()
    {
        // MD-USAGE-002
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany());
        var usage = new FakeUsage { InUse = true };
        var warehouses = new WarehouseService(db, [usage]);
        var warehouse = await warehouses.CreateAsync(NewCode("W"), "Main", null, null, null, token);

        Assert.Equal("MD.WAREHOUSE.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() =>
            warehouses.DeleteAsync(warehouse.Id, warehouse.Version, token))).ErrorCode);
        Assert.Equal([warehouse.Code], usage.Calls);
        usage.InUse = false;
        await warehouses.DeleteAsync(warehouse.Id, warehouse.Version, token);
        Assert.False(await db.Set<Modules.MasterData.Domain.Warehouse>().AnyAsync(x => x.Id == warehouse.Id, token));
    }

    [Fact]
    public async Task StorageLocationInUseCannotBeDeleted()
    {
        // MD-USAGE-003
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany());
        var usage = new FakeUsage { InUse = true };
        var warehouse = await new WarehouseService(db).CreateAsync(NewCode("W"), "Main", null, null, null, token);
        var locations = new StorageLocationService(db, [usage]);
        var location = await locations.CreateAsync(warehouse.Id, NewCode("L"), "Shelf", token);

        Assert.Equal("MD.STORAGE_LOCATION.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() =>
            locations.DeleteAsync(warehouse.Id, location.Id, location.Version, token))).ErrorCode);
        Assert.Equal([warehouse.Code + "/" + location.Code], usage.Calls);
        usage.InUse = false;
        await locations.DeleteAsync(warehouse.Id, location.Id, location.Version, token);
    }

    [Fact]
    public async Task TaxRatesUsedByDocumentsCannotBeChangedRetroactively()
    {
        // MD-USAGE-004：單據日期 2026-03-15 使用此稅別。
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany());
        var documentDate = new DateOnly(2026, 3, 15);
        var usage = new FakeUsage { Period = (from, to) => from <= documentDate && (to is null || to >= documentDate) };
        var codes = new TaxCodeService(db, [usage]);
        var rates = new TaxRateService(db, [usage]);
        var code = await codes.CreateAsync(NewCode("T"), "VAT", "Both", token);
        var rate = await rates.CreateAsync(code.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), 5m, token);

        // 改稅率：檢查原期間。
        Assert.Equal("MD.TAX_RATE.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() => rates.UpdateAsync(
            code.Id, rate.Id, rate.EffectiveFrom, rate.EffectiveTo, 6m, rate.Version, token))).ErrorCode);
        Assert.Equal($"{code.Code}:2026-01-01~2026-06-30", usage.Calls[^1]);
        // 只延長結束日：原期間仍被涵蓋，不需詢問。
        usage.Calls.Clear();
        rate = await rates.UpdateAsync(code.Id, rate.Id, rate.EffectiveFrom, null, 5m, rate.Version, token);
        Assert.Empty(usage.Calls);
        // 縮短到單據日之後：不再涵蓋的部分沒有單據，可以修改。
        rate = await rates.UpdateAsync(code.Id, rate.Id, rate.EffectiveFrom, new DateOnly(2026, 4, 30), 5m, rate.Version, token);
        Assert.Equal($"{code.Code}:2026-05-01~", usage.Calls[^1]);
        // 起始日移到單據日之後：失去涵蓋的部分有單據。
        Assert.Equal("MD.TAX_RATE.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() => rates.UpdateAsync(
            code.Id, rate.Id, new DateOnly(2026, 4, 1), rate.EffectiveTo, 5m, rate.Version, token))).ErrorCode);
        Assert.Equal($"{code.Code}:2026-01-01~2026-03-31", usage.Calls[^1]);
        Assert.Equal("MD.TAX_RATE.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() =>
            rates.DeleteAsync(code.Id, rate.Id, rate.Version, token))).ErrorCode);

        // 不涵蓋單據日的稅率可以自由修改與刪除。
        var later = await rates.CreateAsync(code.Id, new DateOnly(2026, 7, 1), null, 5m, token);
        later = await rates.UpdateAsync(code.Id, later.Id, later.EffectiveFrom, null, 6m, later.Version, token);
        await rates.DeleteAsync(code.Id, later.Id, later.Version, token);

        // 稅別刪除檢查全部期間（先以「無單據」刪掉稅率，再恢復有單據）。
        usage.Period = null;
        usage.InUse = false;
        await rates.DeleteAsync(code.Id, rate.Id, rate.Version, token);
        usage.InUse = true;
        Assert.Equal("MD.TAX_CODE.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() =>
            codes.DeleteAsync(code.Id, code.Version, token))).ErrorCode);
        Assert.Equal($"{code.Code}:0001-01-01~", usage.Calls[^1]);
    }
}
