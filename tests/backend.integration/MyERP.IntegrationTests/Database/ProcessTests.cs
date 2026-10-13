using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>工序類別與工序（ADR-042）：排序、類別與單位參照、刪除保護、公司隔離與 PP 查詢契約。</summary>
public sealed class ProcessTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemBrandDbModel(), new ItemCategoryDbModel(), new ItemDbModel(), new ProcessDbModel()], company);
    }

    private static ProcessService Processes(MyErpDbContext db, params string[] usedCodes) =>
        new(db, usedCodes.Length == 0 ? [] : [new FixedUsage(usedCodes)]);

    private static ProcessInput Input(long? categoryId = null, long? uomId = null, decimal rate = 0m, int sortOrder = 0) =>
        new(categoryId, uomId, rate, sortOrder);

    [Fact]
    public async Task ProcessCategoriesSortBySortOrderAndKeepTheirProcesses()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(50000000, 51000000);
        await using var db = Context(companyId);
        var service = new ProcessCategoryService(db);

        var sewing = await service.CreateAsync(" sew ", " Sewing ", 20, token);
        Assert.Equal(("SEW", "Sewing", 20, true, 7L), (sewing.Code, sewing.Name, sewing.SortOrder, sewing.IsActive, sewing.CreatedBy));
        var cutting = await service.CreateAsync("CUT", "Cutting", 10, token);
        var packing = await service.CreateAsync("PACK", "Packing", 10, token);
        Assert.Equal("MD.PROCESS_CATEGORY.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("sew", "Again", 0, token))).ErrorCode);
        foreach (var bad in new[] { -1, ProcessCategory.MaxSortOrder + 1 })
            Assert.Equal("sortOrder", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => service.CreateAsync("BAD", "Bad", bad, token))).Errors).Key);

        // 預設按排序號，同號按代碼；也可按名稱或倒序排序號。
        Assert.Equal(["CUT", "PACK", "SEW"], (await service.ListAsync(new(), token)).Items.Select(x => x.Code));
        Assert.Equal(["SEW", "CUT", "PACK"], (await service.ListAsync(new() { Sort = "-sortOrder" }, token)).Items.Select(x => x.Code));
        Assert.Equal(["CUT", "PACK", "SEW"], (await service.ListAsync(new() { Sort = "name" }, token)).Items.Select(x => x.Code));
        Assert.Equal("sort", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.ListAsync(new() { Sort = "standardRate" }, token))).Errors).Key);

        packing = await service.UpdateAsync(packing.Id, "Packing line", 30, false, packing.Version, token);
        Assert.Equal(("Packing line", 30, false), (packing.Name, packing.SortOrder, packing.IsActive));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(packing.Id, "Stale", 0, true, packing.Version - 1, token));

        var processes = Processes(db);
        await processes.CreateAsync("SEW-1", "Sew collar", Input(sewing.Id), token);
        await processes.CreateAsync("SEW-2", "Sew sleeve", Input(sewing.Id), token);
        Assert.Equal(new Dictionary<long, int> { [sewing.Id] = 2 },
            await service.ProcessCountsAsync([sewing.Id, cutting.Id, packing.Id], token));

        // 有工序的類別不可刪除；沒有的可刪，且代碼永久保留。
        Assert.Equal("MD.PROCESS_CATEGORY.HAS_PROCESSES", (await Assert.ThrowsAsync<ConflictException>(
            () => service.DeleteAsync(sewing.Id, sewing.Version, token))).ErrorCode);
        await service.DeleteAsync(cutting.Id, cutting.Version, token);
        Assert.Equal(["SEW", "PACK"], (await service.ListAsync(new(), token)).Items.Select(x => x.Code));
        Assert.Equal("MD.PROCESS_CATEGORY.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("CUT", "Cutting again", 0, token))).ErrorCode);

        await using var otherDb = Context(companyId + 1);
        Assert.Empty((await new ProcessCategoryService(otherDb).ListAsync(new(), token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(
            () => new ProcessCategoryService(otherDb).UpdateAsync(sewing.Id, "X", 0, true, sewing.Version, token));
        Assert.Empty(await new ProcessCategoryService(otherDb).ProcessCountsAsync([sewing.Id], token));
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ProcessCategoryService(noCompany).ListAsync(new(), token));
    }

    [Fact]
    public async Task ProcessesValidateCategoryUnitAndRate()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(51000000, 52000000);
        await using var db = Context(companyId);
        var categories = new ProcessCategoryService(db);
        var sewing = await categories.CreateAsync("SEW", "Sewing", 10, token);
        var retired = await categories.CreateAsync("OLD", "Retired", 0, token);
        var uoms = new UomService(db);
        var piece = await uoms.CreateAsync("PCS", "Piece", 0, token);
        var dozen = await uoms.CreateAsync("DOZ", "Dozen", 0, token);
        var service = Processes(db);

        var collar = await service.CreateAsync(" sew-collar ", " Sew collar ", Input(sewing.Id, piece.Id, 1.234567m, 5), token);
        Assert.Equal(("SEW-COLLAR", "Sew collar", sewing.Id, piece.Id, 1.234567m, 5),
            (collar.Code, collar.Name, collar.CategoryId, collar.UomId, collar.StandardRate, collar.SortOrder));
        var loose = await service.CreateAsync("INSPECT", "Inspect", Input(), token);
        Assert.Equal(((long?)null, (long?)null, 0m), (loose.CategoryId, loose.UomId, loose.StandardRate));
        Assert.Equal("MD.PROCESS.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("inspect", "Again", Input(), token))).ErrorCode);
        foreach (var rate in new[] { -0.01m, 1.0000001m, 10_000_000_000_000m })
            Assert.Equal("standardRate", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => service.CreateAsync("BAD", "Bad", Input(rate: rate), token))).Errors).Key);
        Assert.Equal("sortOrder", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("BAD", "Bad", Input(sortOrder: -1), token))).Errors).Key);

        // 停用或不存在的類別與單位不可新設定；已設定的不重新檢查。
        retired = await categories.UpdateAsync(retired.Id, retired.Name, 0, false, retired.Version, token);
        dozen = await uoms.UpdateAsync(dozen.Id, dozen.Name, false, 0, dozen.Version, token);
        Assert.Equal("MD.PROCESS.INVALID_CATEGORY", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("X1", "X", Input(retired.Id), token))).ErrorCode);
        Assert.Equal("MD.PROCESS.INVALID_CATEGORY", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("X2", "X", Input(long.MaxValue), token))).ErrorCode);
        Assert.Equal("MD.PROCESS.INVALID_UOM", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("X3", "X", Input(uomId: dozen.Id), token))).ErrorCode);
        sewing = await categories.UpdateAsync(sewing.Id, sewing.Name, 10, false, sewing.Version, token);
        collar = await service.UpdateAsync(collar.Id, "Sew collar", Input(sewing.Id, piece.Id, 2m, 1), true, collar.Version, token);
        Assert.Equal((2m, 1), (collar.StandardRate, collar.SortOrder));
        Assert.Equal("MD.PROCESS.INVALID_UOM", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.UpdateAsync(collar.Id, "Sew collar", Input(sewing.Id, dozen.Id), true, collar.Version, token))).ErrorCode);
        loose = await service.UpdateAsync(loose.Id, "Final inspect", Input(null, piece.Id, 0.5m, 9), false, loose.Version, token);
        Assert.Equal(("Final inspect", piece.Id, false), (loose.Name, loose.UomId, loose.IsActive));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(loose.Id, "Stale", Input(), true, loose.Version - 1, token));

        Assert.Equal(["SEW-COLLAR", "INSPECT"], (await service.ListAsync(new(), null, token)).Items.Select(x => x.Code));
        Assert.Equal(["SEW-COLLAR"], (await service.ListAsync(new(), sewing.Id, token)).Items.Select(x => x.Code));
        Assert.Equal(["INSPECT"], (await service.ListAsync(new() { Keyword = "final" }, null, token)).Items.Select(x => x.Code));

        // 被工序使用的單位不可刪除；別公司看不到也不可引用本公司類別或單位。
        piece = await db.Set<UnitOfMeasure>().AsNoTracking().SingleAsync(x => x.Id == piece.Id, token);
        Assert.Equal("MD.UOM.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => uoms.DeleteAsync(piece.Id, piece.Version, token))).ErrorCode);
        await using var otherDb = Context(companyId + 1);
        Assert.Empty((await Processes(otherDb).ListAsync(new(), null, token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => Processes(otherDb).UpdateAsync(loose.Id, "X", Input(), true, loose.Version, token));
        var otherCategory = await new ProcessCategoryService(otherDb).CreateAsync("SEW", "Sewing", 0, token);
        Assert.Equal("MD.PROCESS.INVALID_UOM", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => Processes(otherDb).CreateAsync("P", "P", Input(otherCategory.Id, piece.Id), token))).ErrorCode);
        Assert.Equal("MD.PROCESS.INVALID_CATEGORY", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => Processes(otherDb).CreateAsync("P", "P", Input(sewing.Id), token))).ErrorCode);

        // 被 PP 引用時只可停用。
        Assert.Equal("MD.PROCESS.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => Processes(db, "INSPECT").DeleteAsync(loose.Id, loose.Version, token))).ErrorCode);
        await service.DeleteAsync(loose.Id, loose.Version, token);
        Assert.Equal(["SEW-COLLAR"], (await service.ListAsync(new(), null, token)).Items.Select(x => x.Code));
        Assert.Equal("MD.PROCESS.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("INSPECT", "Again", Input(), token))).ErrorCode);
    }

    [Fact]
    public async Task ProcessLookupServesPlanningInCategoryOrder()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(52000000, 53000000);
        await using var db = Context(companyId);
        var categories = new ProcessCategoryService(db);
        var sewing = await categories.CreateAsync("SEW", "Sewing", 20, token);
        var cutting = await categories.CreateAsync("CUT", "Cutting", 10, token);
        var piece = await new UomService(db).CreateAsync("PCS", "Piece", 0, token);
        var service = Processes(db);
        await service.CreateAsync("SEW-B", "Sew B", Input(sewing.Id, piece.Id, 1.5m, 1), token);
        await service.CreateAsync("SEW-A", "Sew A", Input(sewing.Id, null, 2m, 2), token);
        await service.CreateAsync("CUT-A", "Cut A", Input(cutting.Id, null, 0.8m, 5), token);
        await service.CreateAsync("QC", "Inspect", Input(), token);
        var retired = await service.CreateAsync("CUT-OLD", "Old cut", Input(cutting.Id), token);
        await service.UpdateAsync(retired.Id, retired.Name, Input(cutting.Id), false, retired.Version, token);

        var lookup = new ProcessLookup(db);
        Assert.Equal(new ProcessInfo("SEW-B", "Sew B", "SEW", "Sewing", "PCS", 1.5m, 1, true), await lookup.FindAsync(" sew-b ", token));
        Assert.Equal(new ProcessInfo("QC", "Inspect", null, null, null, 0m, 0, true), await lookup.RequireActiveAsync("qc", token));
        Assert.False((await lookup.FindAsync("CUT-OLD", token))!.IsActive);
        Assert.Null(await lookup.FindAsync("NONE", token));
        Assert.Equal("MD.PROCESS.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => lookup.RequireActiveAsync("CUT-OLD", token))).ErrorCode);
        Assert.Equal("MD.PROCESS.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => lookup.RequireActiveAsync("NONE", token))).ErrorCode);
        // 代碼為 null 與查無資料相同，不是 500（MD-B10）。
        Assert.Null(await lookup.FindAsync(null!, token));
        Assert.Equal("MD.PROCESS.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => lookup.RequireActiveAsync(null!, token))).ErrorCode);

        // 類別排序號 → 工序排序號；沒有類別的排最後；停用不列出。
        Assert.Equal(["CUT-A", "SEW-B", "SEW-A", "QC"], (await lookup.ListActiveAsync(token: token)).Select(x => x.Code));
        Assert.Equal(["SEW-B", "SEW-A"], (await lookup.ListActiveAsync(" sew ", token)).Select(x => x.Code));
        Assert.Empty(await lookup.ListActiveAsync("NONE", token));

        await using var otherDb = Context(companyId + 1);
        Assert.Null(await new ProcessLookup(otherDb).FindAsync("SEW-B", token));
        Assert.Empty(await new ProcessLookup(otherDb).ListActiveAsync(token: token));
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ProcessLookup(noCompany).FindAsync("SEW-B", token));
    }

    [Fact]
    public async Task ProcessTablesHaveChecksAndForeignKeyIndexes()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(53000000, 54000000);
        await using var db = Context(companyId);
        db.Add(new Process { CompanyId = companyId, Code = "BAD", Name = "Bad", StandardRate = -1m });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        db.ChangeTracker.Clear();
        db.Add(new ProcessCategory { CompanyId = companyId, Code = "BAD", Name = "Bad", SortOrder = 10000 });
        error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);

        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN " +
            "(OBJECT_ID('md_process_category'), OBJECT_ID('md_process'))").ToListAsync(token);
        Assert.Contains("ck_md_process_category_sort_order", checks);
        Assert.Contains("ck_md_process_sort_order", checks);
        Assert.Contains("ck_md_process_standard_rate", checks);
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN " +
            "(OBJECT_ID('md_process_category'), OBJECT_ID('md_process'))").ToListAsync(token);
        Assert.Contains("ux_md_process_category_company_id_code", indexes);
        Assert.Contains("ux_md_process_company_id_code", indexes);
        Assert.Contains("ix_md_process_company_id_category_id", indexes);
        Assert.Contains("ix_md_process_company_id_uom_id", indexes);
        var rateType = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(TYPE_NAME(system_type_id), '(', precision, ',', scale, ')') AS [Value] FROM sys.columns " +
            "WHERE object_id = OBJECT_ID('md_process') AND name = 'standard_rate'").SingleAsync(token);
        Assert.Equal("decimal(19,6)", rateType);
    }

    private sealed class FixedUsage(IEnumerable<string> codes) : IProcessUsage
    {
        private readonly HashSet<string> used = [.. codes];

        public Task<bool> IsInUseAsync(string code, CancellationToken token = default) => Task.FromResult(used.Contains(code));
    }
}
