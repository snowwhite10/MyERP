using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

/// <summary>倉庫與儲位（ADR-036）：公司隔離、儲位代碼倉庫內唯一、停用與刪除規則。</summary>
public sealed class WarehouseTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new WarehouseDbModel()], company);
    }

    private static string NewCode(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant();

    [Fact]
    public async Task WarehouseLifecycleIsCompanyScopedAndKeepsContactDetails()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(23000000, 24000000);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId + 1);
        var first = new WarehouseService(firstDb);
        var second = new WarehouseService(secondDb);
        var code = NewCode("WH");

        var created = await first.CreateAsync(code.ToLowerInvariant(), " Main ", " Taipei No.1 ", " Amy ", " 02-1234 ", token);
        Assert.Equal((code, "Main", "Taipei No.1", "Amy", "02-1234", true, 7L),
            (created.Code, created.Name, created.Address, created.ContactName, created.Phone, created.IsActive, created.CreatedBy));
        var blank = await first.CreateAsync(NewCode("WH"), "Blank", "  ", null, "", token);
        Assert.Equal((null, null, null), (blank.Address, blank.ContactName, blank.Phone));

        Assert.Equal(0, (await second.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(
            () => second.UpdateAsync(created.Id, "Other", null, null, null, false, created.Version, token));
        await Assert.ThrowsAsync<NotFoundException>(() => second.DeleteAsync(created.Id, created.Version, token));
        Assert.NotEqual(created.Id, (await second.CreateAsync(code, "Separate company", null, null, null, token)).Id);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Dup", null, null, null, token));
        Assert.Equal("MD.WAREHOUSE.DUPLICATE_CODE", duplicate.ErrorCode);

        // 地址也可搜尋。
        var byAddress = await first.ListAsync(new PagedRequest { Keyword = "Taipei No.1" }, token);
        Assert.Contains(byAddress.Items, x => x.Id == created.Id);

        var stale = created.Version;
        var updated = await first.UpdateAsync(created.Id, "Main 2", "Kaohsiung", null, "07-1", false, created.Version, token);
        Assert.Equal(("Main 2", "Kaohsiung", null, "07-1", false), (updated.Name, updated.Address, updated.ContactName, updated.Phone, updated.IsActive));
        await Assert.ThrowsAsync<ConflictException>(
            () => first.UpdateAsync(created.Id, "Stale", null, null, null, true, stale, token));
        await first.DeleteAsync(created.Id, updated.Version, token);
        Assert.Equal(0, (await first.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Reused", null, null, null, token));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => first.DeleteAsync(created.Id, updated.Version, token));
        Assert.Equal("MD.WAREHOUSE.NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task WarehouseValidatesInputAndSorts()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(24000000, 25000000);
        await using var db = Context(companyId);
        var service = new WarehouseService(db);
        var prefix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(prefix + "A", "Zeta", null, null, null, token);
        await service.CreateAsync(prefix + "B", "Alpha", null, null, null, token);
        var byName = await service.ListAsync(new PagedRequest { Sort = "-name", Keyword = prefix }, token);
        Assert.Equal(["Zeta", "Alpha"], byName.Items.Select(x => x.Name));
        var paged = await service.ListAsync(new PagedRequest { Page = 2, PageSize = 1, Keyword = prefix }, token);
        Assert.Equal(prefix + "B", Assert.Single(paged.Items).Code);

        Assert.Equal("code", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("bad code", "Name", null, null, null, token))).Errors).Key);
        Assert.Equal("name", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync(prefix + "C", " ", null, null, null, token))).Errors).Key);
        Assert.Equal("address", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync(prefix + "C", "Name", new string('A', 301), null, null, token))).Errors).Key);
        Assert.Equal("contactName", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync(prefix + "C", "Name", null, new string('C', 101), null, token))).Errors).Key);
        Assert.Equal("phone", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync(prefix + "C", "Name", null, null, new string('9', 51), token))).Errors).Key);
        // 剛好在上限內可保存。
        var max = await service.CreateAsync(prefix + "D", "Max", new string('A', 300), new string('C', 100), new string('9', 50), token);
        Assert.Equal(300, max.Address!.Length);

        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new WarehouseService(noCompany).ListAsync(new PagedRequest(), token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new StorageLocationService(noCompany).ListAsync(1, new PagedRequest(), token));
    }

    [Fact]
    public async Task StorageLocationsBelongToOneWarehouseAndBlockWarehouseDeletion()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(25000000, 26000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var warehouses = new WarehouseService(db);
        var locations = new StorageLocationService(db);
        var main = await warehouses.CreateAsync(NewCode("WH"), "Main", null, null, null, token);
        var spare = await warehouses.CreateAsync(NewCode("WH"), "Spare", null, null, null, token);

        var a1 = await locations.CreateAsync(main.Id, " a-01-01 ", " Shelf A1 ", token);
        Assert.Equal((main.Id, "A-01-01", "Shelf A1", true, 7L), (a1.WarehouseId, a1.Code, a1.Name, a1.IsActive, a1.CreatedBy));
        await locations.CreateAsync(main.Id, "A-01-02", "Shelf A2", token);
        // 同一倉庫內代碼唯一；不同倉庫可重複。
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => locations.CreateAsync(main.Id, "A-01-01", "Again", token));
        Assert.Equal("MD.STORAGE_LOCATION.DUPLICATE_CODE", duplicate.ErrorCode);
        Assert.Equal("A-01-01", (await locations.CreateAsync(spare.Id, "A-01-01", "Spare A1", token)).Code);

        var page = await locations.ListAsync(main.Id, new PagedRequest { Keyword = "Shelf" }, token);
        Assert.Equal(["A-01-01", "A-01-02"], page.Items.Select(x => x.Code));
        var desc = await locations.ListAsync(main.Id, new PagedRequest { Sort = "-code" }, token);
        Assert.Equal(["A-01-02", "A-01-01"], desc.Items.Select(x => x.Code));
        Assert.Equal("code", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => locations.CreateAsync(main.Id, "A 1", "Bad", token))).Errors).Key);
        Assert.Equal("name", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => locations.CreateAsync(main.Id, "A-9", new string('N', 201), token))).Errors).Key);

        // 別公司看不到倉庫，也不能在其下新增儲位。
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => new StorageLocationService(otherDb).CreateAsync(main.Id, "X", "X", token));
        Assert.Equal("MD.WAREHOUSE.NOT_FOUND", foreign.ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => new StorageLocationService(otherDb).ListAsync(main.Id, new PagedRequest(), token));

        // 修改：名稱與啟用；版本與所屬倉庫必須相符。
        var stale = a1.Version;
        var edited = await locations.UpdateAsync(main.Id, a1.Id, "Shelf A1 (cold)", false, a1.Version, token);
        Assert.Equal(("Shelf A1 (cold)", false), (edited.Name, edited.IsActive));
        await Assert.ThrowsAsync<ConflictException>(() => locations.UpdateAsync(main.Id, a1.Id, "Stale", true, stale, token));
        var wrongWarehouse = await Assert.ThrowsAsync<NotFoundException>(
            () => locations.UpdateAsync(spare.Id, a1.Id, "Moved", true, edited.Version, token));
        Assert.Equal("MD.STORAGE_LOCATION.NOT_FOUND", wrongWarehouse.ErrorCode);

        // 停用的倉庫不可新增儲位，但既有儲位仍可修改。
        var inactive = await warehouses.UpdateAsync(main.Id, main.Name, null, null, null, false, main.Version, token);
        var blocked = await Assert.ThrowsAsync<BusinessRuleException>(() => locations.CreateAsync(main.Id, "B-01", "B", token));
        Assert.Equal("MD.STORAGE_LOCATION.WAREHOUSE_INACTIVE", blocked.ErrorCode);
        edited = await locations.UpdateAsync(main.Id, a1.Id, "Shelf A1", true, edited.Version, token);

        // 有未刪除儲位的倉庫不可刪除；刪光後可刪，儲位代碼仍保留。
        var hasLocations = await Assert.ThrowsAsync<ConflictException>(() => warehouses.DeleteAsync(main.Id, inactive.Version, token));
        Assert.Equal("MD.WAREHOUSE.HAS_LOCATIONS", hasLocations.ErrorCode);
        foreach (var row in (await locations.ListAsync(main.Id, new PagedRequest(), token)).Items)
            await locations.DeleteAsync(main.Id, row.Id, row.Version, token);
        Assert.Equal(0, (await locations.ListAsync(main.Id, new PagedRequest(), token)).TotalCount);
        var gone = await Assert.ThrowsAsync<NotFoundException>(() => locations.DeleteAsync(main.Id, a1.Id, edited.Version, token));
        Assert.Equal("MD.STORAGE_LOCATION.NOT_FOUND", gone.ErrorCode);
        await warehouses.UpdateAsync(main.Id, main.Name, null, null, null, true, inactive.Version, token);
        await Assert.ThrowsAsync<ConflictException>(() => locations.CreateAsync(main.Id, "A-01-01", "Reuse", token));
        var reactivated = (await warehouses.ListAsync(new PagedRequest { Keyword = main.Code }, token)).Items.Single();
        await warehouses.DeleteAsync(main.Id, reactivated.Version, token);
        Assert.Equal(0, (await warehouses.ListAsync(new PagedRequest { Keyword = main.Code }, token)).TotalCount);
    }

    [Fact]
    public async Task WarehouseTablesUseCompanyScopedKeys()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(1);
        var keys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('md_warehouse')").ToListAsync(token);
        Assert.Contains("ak_md_warehouse_company_id_id", keys);
        var foreignKey = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('md_storage_location')").SingleAsync(token);
        Assert.Equal("fk_md_storage_location_md_warehouse_company_id_warehouse_id", foreignKey);
        var unique = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('md_storage_location') AND is_unique = 1 AND is_primary_key = 0")
            .ToListAsync(token);
        Assert.Contains("ux_md_storage_location_company_id_warehouse_id_code", unique);
    }
}
