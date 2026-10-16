using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests.Database;

public sealed class SafetyStockTests(TestDatabase database)
{
    [Fact]
    public async Task TenThousandCodeSortReportsThresholdAndMasterDataSqlCost()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        var captured = new SqlQueryCapture();
        await using var db = Context(company, captured);
        var uom = await new UomService(db).CreateAsync("SU" + company, "Unit", 0, token);
        var warehouse = await new WarehouseService(db).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var prefix = "SAF" + company + "-";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH numbers AS (SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO md_item (code, name, base_uom_id, company_id, created_at, created_by,
                version, is_active, is_deleted, tracking_mode)
            SELECT CONCAT({prefix}, RIGHT(CONCAT('00000', n), 5)), CONCAT('Item ', n), {uom.Id},
                {company}, SYSUTCDATETIME(), 7, 1, 1, 0, 'None' FROM numbers
            """, token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inv_safety_stock_threshold (item_id, warehouse_id, minimum_quantity,
                is_deleted, company_id, created_at, created_by, version)
            SELECT id, {warehouse.Id}, 5, 0, {company}, SYSUTCDATETIME(), 7, 1
            FROM md_item WHERE company_id = {company} AND code LIKE CONCAT({prefix}, '%')
            """, token);
        var references = new BoundedReferences(new InventorySelectionOptions(db));
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db), references,
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var page = await service.ListAsync(new SafetyStockQuery { Sort = "itemCode", PageSize = 2 }, token);
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        Assert.Equal(10000, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.True(references.MaximumItemsRequested <= 512);
        var io = await ReplaySqlIoAsync(captured.Commands, token);
        Assert.Contains(io, line => line.Contains("inv_safety_stock_threshold", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(io, line => line.Contains("md_item", StringComparison.OrdinalIgnoreCase));
        static long Reads(IEnumerable<string> lines, string table) => lines
            .Where(line => line.Contains(table, StringComparison.OrdinalIgnoreCase))
            .Sum(line => long.TryParse(Regex.Match(line, @"(?:逻辑读取次数|logical reads)\s*(\d+)",
                RegexOptions.IgnoreCase).Groups[1].Value, out var reads) ? reads : 0);
        Assert.True(Reads(io, "inv_safety_stock_threshold") > 0, "Threshold SQL IO was not parsed.");
        Assert.True(Reads(io, "md_item") > 0, "MD item SQL IO was not parsed.");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"10k itemCode first page: {watch.ElapsedMilliseconds} ms, process allocation {allocated} bytes, " +
            $"captured SQL {captured.Commands.Count}, threshold logical reads {Reads(io, "inv_safety_stock_threshold")}, " +
            $"MD item logical reads {Reads(io, "md_item")}");
    }

    private async Task<List<string>> ReplaySqlIoAsync(
        IReadOnlyCollection<(string Text, SqlParameter[] Parameters)> commands, CancellationToken token)
    {
        var io = new List<string>();
        await using var connection = new SqlConnection(database.ConnectionString);
        connection.InfoMessage += (_, args) => io.Add(args.Message);
        await connection.OpenAsync(token);
        await using (var enable = new SqlCommand("SET STATISTICS IO ON", connection))
            await enable.ExecuteNonQueryAsync(token);
        foreach (var query in commands)
        {
            await using var replay = new SqlCommand(query.Text, connection);
            foreach (var parameter in query.Parameters)
                replay.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
            await using var reader = await replay.ExecuteReaderAsync(token);
            do { while (await reader.ReadAsync(token)) { } }
            while (await reader.NextResultAsync(token));
        }
        return io;
    }

    [Fact]
    public async Task CompanyScopedIdPagingHasCoveringIndex()
    {
        await using var db = Context(TestCompanies.Next());
        var indexes = await db.Database.SqlQueryRaw<string>("""
            SELECT name AS Value FROM sys.indexes
            WHERE object_id = OBJECT_ID('dbo.inv_safety_stock_threshold')
              AND name = 'ix_inv_safety_stock_threshold_company_id_id'
            """).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(indexes);
    }

    [Fact]
    public async Task CodeSortPagesAcrossLargeCandidateSetWithoutLoadingAllReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        var captured = new SqlQueryCapture();
        await using var db = Context(company, captured);
        var uom = await new UomService(db).CreateAsync("SU" + company, "Unit", 0, token);
        var warehouse = await new WarehouseService(db).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var prefix = "SAF" + company + "-";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH numbers AS (SELECT TOP (520) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO md_item (code, name, base_uom_id, company_id, created_at, created_by,
                version, is_active, is_deleted, tracking_mode)
            SELECT CONCAT({prefix}, RIGHT(CONCAT('0000', n), 4)), CONCAT('Item ', n), {uom.Id},
                {company}, SYSUTCDATETIME(), 7, 1, 1, 0, 'None' FROM numbers
            """, token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inv_safety_stock_threshold (item_id, warehouse_id, minimum_quantity,
                is_deleted, company_id, created_at, created_by, version)
            SELECT id, {warehouse.Id}, 5, 0, {company}, SYSUTCDATETIME(), 7, 1
            FROM md_item WHERE company_id = {company} AND code LIKE CONCAT({prefix}, '%')
            """, token);
        var references = new BoundedReferences(new InventorySelectionOptions(db));
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db), references,
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        var page = await service.ListAsync(new SafetyStockQuery { Sort = "-itemCode", PageSize = 2 }, token);
        Assert.Equal(520, page.TotalCount);
        Assert.Equal(new[] { prefix + "0520", prefix + "0519" },
            (await new InventorySelectionOptions(db).SavedAsync(page.Items.Select(x => x.ItemId).ToArray(), [], [], token))
                .Items.OrderByDescending(x => x.Code).Select(x => x.Code).ToArray());
        var lastPage = await service.ListAsync(new SafetyStockQuery { Sort = "-itemCode", Page = 260, PageSize = 2 }, token);
        Assert.Equal(new[] { prefix + "0002", prefix + "0001" },
            (await new InventorySelectionOptions(db).SavedAsync(lastPage.Items.Select(x => x.ItemId).ToArray(), [], [], token))
                .Items.OrderByDescending(x => x.Code).Select(x => x.Code).ToArray());
        var firstPage = await service.ListAsync(new SafetyStockQuery { Sort = "itemCode", PageSize = 2, LowOnly = true }, token);
        Assert.Equal(new[] { prefix + "0001", prefix + "0002" },
            (await new InventorySelectionOptions(db).SavedAsync(firstPage.Items.Select(x => x.ItemId).ToArray(), [], [], token))
                .Items.OrderBy(x => x.Code).Select(x => x.Code).ToArray());
        Assert.True(references.MaximumItemsRequested <= 512, $"Requested {references.MaximumItemsRequested} item references in one call.");
        var keywordPage = await service.ListAsync(new SafetyStockQuery
            { Keyword = prefix + "05", PageSize = 2 }, token);
        Assert.Equal(21, keywordPage.TotalCount);
        Assert.True(references.MaximumItemsRequested <= 512,
            $"Keyword requested {references.MaximumItemsRequested} item references in one call.");
        var io = await ReplaySqlIoAsync(captured.Commands, token);
        Assert.Contains(io, line => line.Contains("inv_safety_stock_threshold", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(io, line => line.Contains("md_item", StringComparison.OrdinalIgnoreCase));
        TestContext.Current.TestOutputHelper?.WriteLine($"Captured {captured.Commands.Count} EF commands; SQL IO:\n{string.Join("\n", io)}");
    }

    private sealed class SqlQueryCapture : DbCommandInterceptor
    {
        public List<(string Text, SqlParameter[] Parameters)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("inv_safety_stock_threshold", StringComparison.OrdinalIgnoreCase)
                || command.CommandText.Contains("md_item", StringComparison.OrdinalIgnoreCase))
                Commands.Add((command.CommandText, command.Parameters.Cast<SqlParameter>()
                    .Select(x => (SqlParameter)((ICloneable)x).Clone()).ToArray()));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class BoundedReferences(IInventorySelectionOptions inner) : IInventorySelectionOptions
    {
        public int MaximumItemsRequested { get; private set; }
        public Task<MyERP.Shared.Paging.PagedResult<InventorySelectionOption>> ItemsAsync(
            MyERP.Shared.Paging.PagedRequest request, CancellationToken token = default) => inner.ItemsAsync(request, token);
        public Task<MyERP.Shared.Paging.PagedResult<InventorySelectionOption>> WarehousesAsync(
            MyERP.Shared.Paging.PagedRequest request, CancellationToken token = default) => inner.WarehousesAsync(request, token);
        public Task<MyERP.Shared.Paging.PagedResult<InventorySelectionOption>> LocationsAsync(long warehouseId,
            MyERP.Shared.Paging.PagedRequest request, CancellationToken token = default) => inner.LocationsAsync(warehouseId, request, token);
        public Task<InventorySavedReferences> SavedAsync(IReadOnlyCollection<long> itemIds,
            IReadOnlyCollection<long> warehouseIds, IReadOnlyCollection<long> locationIds, CancellationToken token = default)
        {
            MaximumItemsRequested = Math.Max(MaximumItemsRequested, itemIds.Count);
            return inner.SavedAsync(itemIds, warehouseIds, locationIds, token);
        }
    }
    private MyErpDbContext Context(long companyId, DbCommandInterceptor? commandInterceptor = null)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7), company);
        var options = new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper));
        if (commandInterceptor is not null) options.AddInterceptors(commandInterceptor);
        return new MyErpDbContext(options.Options,
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

    [Fact]
    public async Task DeletedThresholdCannotBeChangedAndRestorationIsVisibleOnlyInItsCompany()
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
        await service.DeleteAsync(created.Id, created.Version, token);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(created.Id, created.Version, 6m, token));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(created.Id, created.Version, token));
        Assert.Empty((await service.ListAsync(new SafetyStockQuery(), token)).Items);
        await using var foreign = Context(TestCompanies.Next());
        var foreignService = new SafetyStockService(foreign, new InventoryReferenceGuard(foreign),
            new InventorySelectionOptions(foreign), new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        Assert.Equal(0, (await foreignService.ListAsync(new SafetyStockQuery { LowOnly = true }, token)).TotalCount);
        var restored = await service.CreateWithStatusAsync(item.Id, warehouse.Id, 3m, token);
        Assert.True(restored.Restored);
        Assert.Equal(created.Id, restored.Entity.Id);
        Assert.Equal(3m, Assert.Single((await service.ListAsync(new SafetyStockQuery
            { WarehouseId = warehouse.Id, LowOnly = true }, token)).Items).Shortage);
        Assert.Equal(0, (await service.ListAsync(new SafetyStockQuery { WarehouseId = warehouse.Id + 1 }, token)).TotalCount);
        Assert.Equal(0, (await foreignService.ListAsync(new SafetyStockQuery(), token)).TotalCount);
        Assert.Empty(await db.Set<StockMovement>().ToListAsync(token));
    }

    [Fact]
    public async Task DisabledWarehouseKeepsHistoricalThresholdReadableButRejectsUpdate()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var uom = await new UomService(db).CreateAsync("SU" + company, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("SI" + company, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("SW" + company, "Warehouse", null, null, null, token);
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db), new InventorySelectionOptions(db),
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        var threshold = await service.CreateAsync(item.Id, warehouse.Id, 5m, token);
        warehouse.IsActive = false;
        await db.SaveChangesAsync(token);

        var row = Assert.Single((await service.ListAsync(new SafetyStockQuery { LowOnly = true }, token)).Items);
        Assert.Equal(threshold.Id, row.Id);
        Assert.Equal(5m, row.Shortage);
        var disabled = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.UpdateAsync(threshold.Id, threshold.Version, 6m, token));
        Assert.Equal("MD.WAREHOUSE.INACTIVE", disabled.ErrorCode);
        await service.DeleteAsync(threshold.Id, threshold.Version, token);
        Assert.Equal(0, (await service.ListAsync(new SafetyStockQuery(), token)).TotalCount);
        Assert.Empty(await db.Set<StockMovement>().ToListAsync(token));
    }

    [Fact]
    public async Task InvalidListFiltersAreRejectedBeforeDatabaseRead()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(TestCompanies.Next());
        var service = new SafetyStockService(db, new InventoryReferenceGuard(db), new InventorySelectionOptions(db),
            new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0)), new FixedUser(7));
        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.ListAsync(new SafetyStockQuery { WarehouseId = 0 }, token));
        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.ListAsync(new SafetyStockQuery { Sort = "unknown" }, token));
        Assert.Empty(await db.Set<StockMovement>().ToListAsync(token));
    }
}
