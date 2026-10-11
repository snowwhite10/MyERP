using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class UomTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel()], company);
    }

    private static string NewCode(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant();

    [Fact]
    public async Task UomLifecycleIsCompanyScopedWithDecimalPlacesAndReservedCodes()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(7000000, 8000000);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId + 1);
        var first = new UomService(firstDb);
        var second = new UomService(secondDb);
        var code = NewCode("U");
        var created = await first.CreateAsync(code.ToLowerInvariant(), " Kilogram ", 3, token);
        Assert.Equal(code, created.Code);
        Assert.Equal("Kilogram", created.Name);
        Assert.Equal(3, created.DecimalPlaces);
        Assert.True(created.IsActive);
        Assert.Equal(0, (await second.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => second.UpdateAsync(created.Id, "Other", true, 0, created.Version, token));
        Assert.NotEqual(created.Id, (await second.CreateAsync(code, "Separate company", 0, token)).Id);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Duplicate", 0, token));
        Assert.Equal("MD.UOM.DUPLICATE_CODE", duplicate.ErrorCode);
        var stale = created.Version;
        var updated = await first.UpdateAsync(created.Id, "Kg", false, 6, created.Version, token);
        Assert.Equal(("Kg", false, 6), (updated.Name, updated.IsActive, updated.DecimalPlaces));
        await Assert.ThrowsAsync<ConflictException>(() => first.UpdateAsync(created.Id, "Stale", true, 2, stale, token));
        Assert.Equal("decimalPlaces", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => first.CreateAsync(NewCode("U"), "Too precise", 7, token))).Errors).Key);
        await Assert.ThrowsAsync<RequestValidationException>(() => first.CreateAsync(NewCode("U"), "Negative", -1, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => first.UpdateAsync(created.Id, "Kg", true, 7, updated.Version, token));
        await first.DeleteAsync(created.Id, updated.Version, token);
        Assert.Equal(0, (await first.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Reused", 0, token));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => first.DeleteAsync(created.Id, updated.Version, token));
        Assert.Equal("MD.UOM.NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task ConversionsRejectSelfReverseAndIndirectPathsAndProtectReferencedUnits()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(8000000, 9000000);
        await using var db = Context(companyId);
        var uoms = new UomService(db);
        var conversions = new UomConversionService(db);
        var kg = await uoms.CreateAsync(NewCode("KG"), "Kilogram", 3, token);
        var g = await uoms.CreateAsync(NewCode("G"), "Gram", 0, token);
        var mg = await uoms.CreateAsync(NewCode("MG"), "Milligram", 0, token);
        var box = await uoms.CreateAsync(NewCode("BOX"), "Box", 0, token);

        var kgToG = await conversions.CreateAsync(kg.Id, g.Id, 1000m, token);
        Assert.Equal(1000m, kgToG.Factor);
        await conversions.CreateAsync(g.Id, mg.Id, 1000m, token);
        Assert.Equal("toUomId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => conversions.CreateAsync(kg.Id, kg.Id, 1m, token))).Errors).Key);
        foreach (var (from, to) in new[] { (kg.Id, g.Id), (g.Id, kg.Id), (mg.Id, kg.Id), (kg.Id, mg.Id) })
        {
            var path = await Assert.ThrowsAsync<ConflictException>(() => conversions.CreateAsync(from, to, 5m, token));
            Assert.Equal("MD.UOM_CONVERSION.PATH_EXISTS", path.ErrorCode);
        }
        foreach (var factor in new[] { 0m, -1m, 0.0000001m, 1_000_000_000m })
            Assert.Equal("factor", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => conversions.CreateAsync(box.Id, kg.Id, factor, token))).Errors).Key);

        var listed = await conversions.ListAsync(token);
        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, x => x.FromUomCode == kg.Code && x.ToUomCode == g.Code && x.Factor == 1000m);

        var inUse = await Assert.ThrowsAsync<ConflictException>(() => uoms.DeleteAsync(g.Id, g.Version, token));
        Assert.Equal("MD.UOM.IN_USE", inUse.ErrorCode);
        var stale = kgToG.Version;
        var edited = await conversions.UpdateAsync(kgToG.Id, 1000.5m, kgToG.Version, token);
        Assert.Equal(1000.5m, edited.Factor);
        await Assert.ThrowsAsync<ConflictException>(() => conversions.UpdateAsync(kgToG.Id, 2m, stale, token));
        await Assert.ThrowsAsync<RequestValidationException>(() => conversions.UpdateAsync(kgToG.Id, 0m, edited.Version, token));
        await conversions.DeleteAsync(kgToG.Id, edited.Version, token);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => conversions.DeleteAsync(kgToG.Id, edited.Version, token));
        Assert.Equal("MD.UOM_CONVERSION.NOT_FOUND", missing.ErrorCode);
        // 刪除後路徑中斷，可改以反方向重建；被刪除的換算不再阻止。
        var gToKg = await conversions.CreateAsync(g.Id, kg.Id, 0.001m, token);
        Assert.Equal(0.001m, gToKg.Factor);

        await uoms.UpdateAsync(box.Id, box.Name, false, 0, box.Version, token);
        Assert.Equal("fromUomId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => conversions.CreateAsync(box.Id, kg.Id, 12m, token))).Errors).Key);
    }

    [Fact]
    public async Task ConverterFollowsPathsInBothDirectionsAndRoundsToTargetPrecision()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(9000000, 10000000);
        await using var db = Context(companyId);
        var uoms = new UomService(db);
        var conversions = new UomConversionService(db);
        var kg = await uoms.CreateAsync(NewCode("KG"), "Kilogram", 3, token);
        var g = await uoms.CreateAsync(NewCode("G"), "Gram", 1, token);
        var lb = await uoms.CreateAsync(NewCode("LB"), "Pound", 2, token);
        var pcs = await uoms.CreateAsync(NewCode("PCS"), "Piece", 0, token);
        await conversions.CreateAsync(kg.Id, g.Id, 1000m, token);
        await conversions.CreateAsync(lb.Id, g.Id, 453.59237m, token);

        Assert.Equal(1250m, await conversions.ConvertAsync(kg.Id, g.Id, 1.25m, token));
        Assert.Equal(0.001m, await conversions.ConvertAsync(g.Id, kg.Id, 1.4m, token));
        Assert.Equal(0.002m, await conversions.ConvertAsync(g.Id, kg.Id, 1.5m, token));
        Assert.Equal(2.20m, await conversions.ConvertAsync(kg.Id, lb.Id, 1m, token));
        Assert.Equal(0.454m, await conversions.ConvertAsync(lb.Id, kg.Id, 1m, token));
        Assert.Equal(-0.454m, await conversions.ConvertAsync(lb.Id, kg.Id, -1m, token));
        Assert.Equal(3m, await conversions.ConvertAsync(pcs.Id, pcs.Id, 2.5m, token));
        var noPath = await Assert.ThrowsAsync<BusinessRuleException>(() => conversions.ConvertAsync(kg.Id, pcs.Id, 1m, token));
        Assert.Equal("MD.UOM_CONVERSION.NO_PATH", noPath.ErrorCode);
        await using var other = Context(companyId + 1);
        await Assert.ThrowsAsync<NotFoundException>(() => new UomConversionService(other).ConvertAsync(kg.Id, g.Id, 1m, token));
    }

    [Fact]
    public async Task ConcurrentReverseConversionsCannotBothBeCreated()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(10000000, 11000000);
        await using var setup = Context(companyId);
        var uoms = new UomService(setup);
        var a = await uoms.CreateAsync(NewCode("A"), "A", 0, token);
        var b = await uoms.CreateAsync(NewCode("B"), "B", 0, token);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId);
        var results = await Task.WhenAll(
            Attempt(new UomConversionService(firstDb), a.Id, b.Id),
            Attempt(new UomConversionService(secondDb), b.Id, a.Id));
        Assert.Equal(1, results.Count(ok => ok));
        Assert.Single(await new UomConversionService(setup).ListAsync(token));

        async Task<bool> Attempt(UomConversionService service, long from, long to)
        {
            try { await service.CreateAsync(from, to, 2m, token); return true; }
            catch (ConflictException ex) when (ex.ErrorCode == "MD.UOM_CONVERSION.PATH_EXISTS") { return false; }
        }
    }

    [Fact]
    public async Task UomTablesUseCompanyScopedKeysAndFactorPrecision()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(1);
        var keys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('md_uom')").ToListAsync(token);
        Assert.Contains("ak_md_uom_company_id_id", keys);
        var foreignKeys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('md_uom_conversion')").ToListAsync(token);
        Assert.Equal(2, foreignKeys.Count);
        var factorType = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(DATA_TYPE, '(', NUMERIC_PRECISION, ',', NUMERIC_SCALE, ')') AS [Value] FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_NAME = 'md_uom_conversion' AND COLUMN_NAME = 'factor'").SingleAsync(token);
        Assert.Equal("decimal(15,6)", factorType);
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new UomConversionService(noCompany).ListAsync(token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new UomService(noCompany).CreateAsync("X", "X", 0, token));
    }
}
