using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>物料專屬換算（ADR-035）：以基本單位為中心，覆寫公司級換算，換不到基本單位時退回公司級路徑。</summary>
public sealed class ItemUomConversionTests(TestDatabase database)
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

    private static string NewCode(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant();

    [Fact]
    public async Task ItemConversionsOverrideCompanyRulesAndFallBackToCompanyPaths()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(21000000, 22000000);
        await using var db = Context(companyId);
        var uoms = new UomService(db);
        var conversions = new UomConversionService(db);
        var pcs = await uoms.CreateAsync(NewCode("PCS"), "Piece", 0, token);
        var box = await uoms.CreateAsync(NewCode("BOX"), "Box", 0, token);
        var ctn = await uoms.CreateAsync(NewCode("CTN"), "Carton", 0, token);
        var doz = await uoms.CreateAsync(NewCode("DOZ"), "Dozen", 2, token);
        var kg = await uoms.CreateAsync(NewCode("KG"), "Kilogram", 3, token);
        var g = await uoms.CreateAsync(NewCode("G"), "Gram", 0, token);
        await conversions.CreateAsync(box.Id, pcs.Id, 10m, token);
        await conversions.CreateAsync(doz.Id, pcs.Id, 12m, token);
        await conversions.CreateAsync(kg.Id, g.Id, 1000m, token);
        var item = await new ItemService(db).CreateAsync(NewCode("I"), "Item", null, pcs.Id, null, null, token);
        var itemConversions = new ItemUomConversionService(db);
        await itemConversions.CreateAsync(item.Id, box.Id, 12m, token);
        await itemConversions.CreateAsync(item.Id, ctn.Id, 144m, token);

        // 物料專屬係數覆寫公司級的 1 BOX = 10 PCS；公司級換算本身不受影響。
        Assert.Equal(24m, await conversions.ConvertForItemAsync(item.Id, box.Id, pcs.Id, 2m, token));
        Assert.Equal(20m, await conversions.ConvertAsync(box.Id, pcs.Id, 2m, token));
        Assert.Equal(12m, await conversions.ConvertForItemAsync(item.Id, ctn.Id, box.Id, 1m, token));
        Assert.Equal(0m, await conversions.ConvertForItemAsync(item.Id, pcs.Id, ctn.Id, 71m, token));
        Assert.Equal(1m, await conversions.ConvertForItemAsync(item.Id, pcs.Id, ctn.Id, 72m, token));
        // 沒有專屬換算的單位以公司級路徑補到基本單位：7 PCS = 0.5833 DOZ → 2 位小數。
        Assert.Equal(0.58m, await conversions.ConvertForItemAsync(item.Id, pcs.Id, doz.Id, 7m, token));
        Assert.Equal(5m, await conversions.ConvertForItemAsync(item.Id, doz.Id, box.Id, 5m, token));
        Assert.Equal(-5m, await conversions.ConvertForItemAsync(item.Id, doz.Id, box.Id, -5m, token));
        // 兩端都換不到基本單位時退回公司級直接路徑。
        Assert.Equal(1500m, await conversions.ConvertForItemAsync(item.Id, kg.Id, g.Id, 1.5m, token));
        Assert.Equal(3m, await conversions.ConvertForItemAsync(item.Id, box.Id, box.Id, 2.5m, token));
        var noPath = await Assert.ThrowsAsync<BusinessRuleException>(
            () => conversions.ConvertForItemAsync(item.Id, kg.Id, pcs.Id, 1m, token));
        Assert.Equal("MD.UOM_CONVERSION.NO_PATH", noPath.ErrorCode);
        var missingUom = await Assert.ThrowsAsync<NotFoundException>(
            () => conversions.ConvertForItemAsync(item.Id, box.Id, long.MaxValue, 1m, token));
        Assert.Equal("MD.UOM.NOT_FOUND", missingUom.ErrorCode);

        // 停用物料仍可換算（歷史資料）；別公司看不到物料。
        await new ItemService(db).UpdateAsync(item.Id, item.Name, null, pcs.Id, null, null, false, item.Version, token);
        Assert.Equal(24m, await conversions.ConvertForItemAsync(item.Id, box.Id, pcs.Id, 2m, token));
        await using var other = Context(companyId + 1);
        var foreign = await Assert.ThrowsAsync<NotFoundException>(
            () => new UomConversionService(other).ConvertForItemAsync(item.Id, box.Id, pcs.Id, 1m, token));
        Assert.Equal("MD.ITEM.NOT_FOUND", foreign.ErrorCode);
    }

    [Fact]
    public async Task ItemConversionsValidateUnitsFactorsAndVersionsAndLockTheBaseUnit()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(22000000, 23000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var uoms = new UomService(db);
        var pcs = await uoms.CreateAsync(NewCode("PCS"), "Piece", 0, token);
        var box = await uoms.CreateAsync(NewCode("BOX"), "Box", 0, token);
        var pack = await uoms.CreateAsync(NewCode("PK"), "Pack", 0, token);
        var retired = await uoms.CreateAsync(NewCode("OLD"), "Retired", 0, token);
        await uoms.UpdateAsync(retired.Id, retired.Name, false, 0, retired.Version, token);
        var foreignUom = await new UomService(otherDb).CreateAsync(NewCode("F"), "Foreign", 0, token);
        var items = new ItemService(db);
        var item = await items.CreateAsync(NewCode("I"), "Item", null, pcs.Id, null, null, token);
        var service = new ItemUomConversionService(db);

        var created = await service.CreateAsync(item.Id, box.Id, 12m, token);
        Assert.Equal((item.Id, box.Id, box.Code, 12m), (created.ItemId, created.UomId, created.UomCode, created.Factor));
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(item.Id, box.Id, 6m, token));
        Assert.Equal("MD.ITEM_UOM_CONVERSION.DUPLICATE", duplicate.ErrorCode);
        foreach (var uomId in new[] { pcs.Id, retired.Id, foreignUom.Id, 0L })
            Assert.Equal("uomId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => service.CreateAsync(item.Id, uomId, 2m, token))).Errors).Key);
        foreach (var factor in new[] { 0m, -1m, 0.0000001m, 1_000_000_000m })
            Assert.Equal("factor", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => service.CreateAsync(item.Id, pack.Id, factor, token))).Errors).Key);
        var foreignItem = await Assert.ThrowsAsync<NotFoundException>(
            () => new ItemUomConversionService(otherDb).CreateAsync(item.Id, foreignUom.Id, 2m, token));
        Assert.Equal("MD.ITEM.NOT_FOUND", foreignItem.ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => new ItemUomConversionService(otherDb).ListAsync(item.Id, token));

        await service.CreateAsync(item.Id, pack.Id, 0.5m, token);
        var listed = await service.ListAsync(item.Id, token);
        Assert.Equal(2, listed.Count);
        Assert.Equal(listed.OrderBy(x => x.UomCode, StringComparer.Ordinal).Select(x => x.Id), listed.Select(x => x.Id));
        Assert.Equal("MD.UOM.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => uoms.DeleteAsync(pack.Id, pack.Version, token))).ErrorCode);

        var stale = created.Version;
        var edited = await service.UpdateAsync(item.Id, created.Id, 24.5m, created.Version, token);
        Assert.Equal(24.5m, edited.Factor);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(item.Id, created.Id, 2m, stale, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateAsync(item.Id, created.Id, 0m, edited.Version, token));
        var otherItem = await items.CreateAsync(NewCode("I"), "Other item", null, pcs.Id, null, null, token);
        var wrongItem = await Assert.ThrowsAsync<NotFoundException>(
            () => service.UpdateAsync(otherItem.Id, created.Id, 2m, edited.Version, token));
        Assert.Equal("MD.ITEM_UOM_CONVERSION.NOT_FOUND", wrongItem.ErrorCode);

        // 有專屬換算時基本單位鎖定；其他欄位仍可修改。
        var locked = await Assert.ThrowsAsync<ConflictException>(
            () => items.UpdateAsync(item.Id, item.Name, null, box.Id, null, null, true, item.Version, token));
        Assert.Equal("MD.ITEM.BASE_UOM_LOCKED", locked.ErrorCode);
        var renamed = await items.UpdateAsync(item.Id, "Renamed", null, pcs.Id, null, null, true, item.Version, token);
        foreach (var row in await service.ListAsync(item.Id, token))
            await service.DeleteAsync(item.Id, row.Id, row.Version, token);
        Assert.Empty(await service.ListAsync(item.Id, token));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(item.Id, created.Id, edited.Version, token));
        Assert.Equal("MD.ITEM_UOM_CONVERSION.NOT_FOUND", missing.ErrorCode);
        var rebased = await items.UpdateAsync(item.Id, renamed.Name, null, box.Id, null, null, true, renamed.Version, token);
        Assert.Equal(box.Id, rebased.BaseUomId);
        // 刪除後可重建：新基本單位為 BOX，1 PCS = 0.083333 BOX。
        Assert.Equal(0.083333m, (await service.CreateAsync(item.Id, pcs.Id, 0.083333m, token)).Factor);
    }

    [Fact]
    public async Task ItemConversionTableUsesCompanyScopedKeys()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(1);
        var itemKeys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('md_item')").ToListAsync(token);
        Assert.Contains("ak_md_item_company_id_id", itemKeys);
        var foreignKeys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('md_item_uom_conversion')").ToListAsync(token);
        Assert.Equal(2, foreignKeys.Count);
        var factorType = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(DATA_TYPE, '(', NUMERIC_PRECISION, ',', NUMERIC_SCALE, ')') AS [Value] FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_NAME = 'md_item_uom_conversion' AND COLUMN_NAME = 'factor'").SingleAsync(token);
        Assert.Equal("decimal(15,6)", factorType);
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ItemUomConversionService(noCompany).ListAsync(1, token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new UomConversionService(noCompany).ConvertForItemAsync(1, 1, 2, 1m, token));
        Assert.Equal(UomConversion.FactorScale, ItemUomConversion.FactorScale);
    }
}
