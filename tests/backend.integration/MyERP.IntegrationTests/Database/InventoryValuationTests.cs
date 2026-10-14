using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class InventoryValuationTests(TestDatabase database)
{
    private static readonly DateTime Now = new(2026, 10, 14, 5, 40, 0);
    private static readonly DateOnly Day = new(2026, 10, 14);

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(
                    new AuditStamper(new FixedClock(Now), new FixedUser(7), company))).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(), new StockMovementDbModel()],
            company);
    }

    private static async Task<(long Item, long Warehouse, long A, long B)> Setup(MyErpDbContext db, long companyId, CancellationToken token)
    {
        var uom = await new UomService(db).CreateAsync($"VU{companyId}", "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync($"VI{companyId}", "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync($"VW{companyId}", "Warehouse", null, null, null, token);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, $"VA{companyId}", "A", token);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, $"VB{companyId}", "B", token);
        return (item.Id, warehouse.Id, a.Id, b.Id);
    }

    private static StockPostingService Posting(MyErpDbContext db) =>
        new(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));

    private static Task<InventoryPostingResult> Post(MyErpDbContext db, string source, long id, params InventoryPostingLine[] lines) =>
        Posting(db).PostAsync(new InventoryPostingRequest(source, id, Day, lines), TestContext.Current.CancellationToken);

    [Fact]
    public async Task WeightedAverageRoundsLinesAndDrainsResidualValue()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await Setup(db, companyId, token);
        await Post(db, "VALUATION_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 10m, 2.555555m));
        await Post(db, "VALUATION_IN", 2, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 5m, 4m));
        await Post(db, "VALUATION_OUT", 3, new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 3m));

        var query = new InventoryValuationQuery(db);
        var result = await query.GetItemAsync(item, null, token);
        Assert.Equal(12m, result.OnHand);
        Assert.Equal(36.4445m, result.Value);
        Assert.Equal("Valued", result.Status);
        Assert.Equal(3, result.MovementCount);
        await Post(db, "VALUATION_OUT", 4, new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 12m));
        Assert.Equal(0m, (await query.GetItemAsync(item, null, token)).Value);
    }

    [Fact]
    public async Task TransferIsValueNeutralAndUnknownCostIsDistinguishedFromZero()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await Setup(db, companyId, token);
        await Post(db, "VALUATION_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 8m, 2m));
        await Post(db, "TRANSFER", 2,
            new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 3m),
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, b, 3m));
        var query = new InventoryValuationQuery(db);
        Assert.Equal(16m, (await query.GetItemAsync(item, null, token)).Value);
        await Post(db, "STOCKTAKE", 3, new InventoryPostingLine(1, StockDirection.In, item, warehouse, b, 2m));
        Assert.Equal(20m, (await query.GetItemAsync(item, null, token)).Value);
        await Post(db, "VALUATION_OUT", 4, new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 5m));
        await Post(db, "VALUATION_OUT", 5, new InventoryPostingLine(1, StockDirection.Out, item, warehouse, b, 5m));
        await Post(db, "STOCKTAKE", 6, new InventoryPostingLine(1, StockDirection.In, item, warehouse, b, 1m));
        var unknown = await query.GetItemAsync(item, null, token);
        Assert.Equal("Unpriced", unknown.Status);
        Assert.Null(unknown.Value);
        Assert.Equal(1m, unknown.UnpricedQuantity);
        await Post(db, "VALUATION_OUT", 7, new InventoryPostingLine(1, StockDirection.Out, item, warehouse, b, 1m));
        await Post(db, "VALUATION_IN", 8, new InventoryPostingLine(1, StockDirection.In, item, warehouse, b, 1m, 0m));
        Assert.Equal("Valued", (await query.GetItemAsync(item, null, token)).Status);
        Assert.Equal(0m, (await query.GetItemAsync(item, null, token)).Value);
    }

    [Fact]
    public async Task CutoffAndCompanyScopeExcludeOtherMovements()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await Setup(db, companyId, token);
        await Post(db, "VALUATION_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 4m, 7m));
        var query = new InventoryValuationQuery(db);
        Assert.Equal(0, (await query.GetItemAsync(item, Now.AddTicks(-10), token)).MovementCount);
        Assert.Equal(28m, (await query.GetItemAsync(item, Now, token)).Value);
        await using var other = Context(TestCompanies.Next());
        var isolated = await new InventoryValuationQuery(other).GetItemAsync(item, null, token);
        Assert.Equal((0m, 0m, 0L), (isolated.OnHand, isolated.Value, isolated.MovementCount));
    }

    [Fact]
    public async Task UnpairedTransferReturnsBusinessError()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await Setup(db, companyId, token);
        await Post(db, "VALUATION_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 2m, 1m));
        await Post(db, "TRANSFER", 2, new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 1m));
        var error = await Assert.ThrowsAsync<MyERP.Shared.Errors.BusinessRuleException>(
            () => new InventoryValuationQuery(db).GetItemAsync(item, null, token));
        Assert.Contains("INV.VALUATION.TRANSFER_UNPAIRED", error.ErrorCode, StringComparison.Ordinal);
    }

    private async Task InsertRaw(long companyId, long item, long warehouse, long location, string direction,
        decimal quantity, decimal? unitCost, CancellationToken token)
    {
        // Simulates ledger rows the posting service would reject, to prove the SP refuses to value them.
        const string sql = """
            DECLARE @ref TABLE (id bigint);
            INSERT dbo.inv_reference (source_key, item_id, item_code, warehouse_id, warehouse_code,
                location_id, location_code, company_id, created_at, created_by, version)
            OUTPUT inserted.id INTO @ref
            VALUES (CONCAT('VALRAW:', NEWID()), @item, 'RAW', @warehouse, 'RAW', @location, 'RAW',
                @company, '2026-10-14T05:40:00', 7, 1);
            INSERT dbo.inv_stock_movement (source_type, source_id, source_line_no, direction,
                reference_id, item_id, warehouse_id, location_id, quantity, unit_cost, business_date,
                posted_at, company_id, created_at, created_by, version)
            SELECT 'VALRAW', id, 1, @direction, id, @item, @warehouse, @location, @quantity, @cost,
                '2026-10-14', '2026-10-14T05:40:00', @company, '2026-10-14T05:40:00', 7, 1 FROM @ref;
            """;
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@company", companyId);
        command.Parameters.AddWithValue("@item", item);
        command.Parameters.AddWithValue("@warehouse", warehouse);
        command.Parameters.AddWithValue("@location", location);
        command.Parameters.AddWithValue("@direction", direction);
        command.Parameters.Add(new SqlParameter("@quantity", System.Data.SqlDbType.Decimal) { Precision = 19, Scale = 6, Value = quantity });
        command.Parameters.Add(new SqlParameter("@cost", System.Data.SqlDbType.Decimal) { Precision = 19, Scale = 6, Value = (object?)unitCost ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(token);
    }

    [Fact]
    public async Task NegativePoolAndOverflowReturnBusinessErrors()
    {
        var token = TestContext.Current.CancellationToken;
        var negativeCompany = TestCompanies.Next();
        await using (var db = Context(negativeCompany))
        {
            var (item, warehouse, a, _) = await Setup(db, negativeCompany, token);
            await InsertRaw(negativeCompany, item, warehouse, a, "Out", 1m, null, token);
            var error = await Assert.ThrowsAsync<MyERP.Shared.Errors.BusinessRuleException>(
                () => new InventoryValuationQuery(db).GetItemAsync(item, null, token));
            Assert.Equal("INV.VALUATION.NEGATIVE_POOL", error.ErrorCode);
        }

        var overflowCompany = TestCompanies.Next();
        await using (var db = Context(overflowCompany))
        {
            var (item, warehouse, a, _) = await Setup(db, overflowCompany, token);
            await InsertRaw(overflowCompany, item, warehouse, a, "In", 9999999999999m, 9999999m, token);
            var error = await Assert.ThrowsAsync<MyERP.Shared.Errors.BusinessRuleException>(
                () => new InventoryValuationQuery(db).GetItemAsync(item, null, token));
            Assert.Equal("INV.VALUATION.OVERFLOW", error.ErrorCode);
        }
    }

    [Fact]
    public async Task HundredThousandMovementsUseValuationIndex()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await Setup(db, companyId, token);
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        const string seed = """
            CREATE TABLE #ids (id bigint NOT NULL PRIMARY KEY);
            WITH numbers AS (
                SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY x.object_id, y.object_id) AS n
                FROM sys.all_objects AS x CROSS JOIN sys.all_objects AS y
            )
            INSERT dbo.inv_reference (source_key, item_id, item_code, warehouse_id, warehouse_code,
                location_id, location_code, company_id, created_at, created_by, version)
            OUTPUT inserted.id INTO #ids
            SELECT CONCAT('VALPERF:', n), @item, 'PERF', @warehouse, 'PERF', @location, 'PERF',
                @company, '2026-10-14T05:40:00', 7, 1 FROM numbers;
            INSERT dbo.inv_stock_movement (source_type, source_id, source_line_no, direction,
                reference_id, item_id, warehouse_id, location_id, quantity, unit_cost, business_date,
                posted_at, company_id, created_at, created_by, version)
            SELECT 'VALPERF', id, 1, 'In', id, @item, @warehouse, @location, 1, 1,
                '2026-10-14', '2026-10-14T05:40:00', @company, '2026-10-14T05:40:00', 7, 1 FROM #ids;
            """;
        await using (var command = new SqlCommand(seed, connection, (SqlTransaction)transaction) { CommandTimeout = 180 })
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@item", item);
            command.Parameters.AddWithValue("@warehouse", warehouse);
            command.Parameters.AddWithValue("@location", a);
            await command.ExecuteNonQueryAsync(token);
        }

        // STATISTICS XML would emit one plan per cursor fetch and distort timing, so the run is
        // measured plainly and the plan and counters are read from the procedure's cached statistics.
        await using var measured = new SqlCommand("EXEC dbo.usp_inv_value_item @company, @item, NULL;",
            connection, (SqlTransaction)transaction) { CommandTimeout = 180 };
        measured.Parameters.AddWithValue("@company", companyId);
        measured.Parameters.AddWithValue("@item", item);
        var stopwatch = Stopwatch.StartNew();
        await using (var reader = await measured.ExecuteReaderAsync(token))
        {
            Assert.True(await reader.ReadAsync(token));
            Assert.Equal(100000m, reader.GetDecimal(0));
            Assert.Equal(100000m, reader.GetDecimal(1));
            Assert.Equal(100000L, reader.GetInt64(5));
        }
        stopwatch.Stop();
        await using var stats = new SqlCommand("""
            SELECT TOP (1) CONVERT(nvarchar(max), qp.query_plan), ps.last_worker_time, ps.last_logical_reads, ps.last_elapsed_time
            FROM sys.dm_exec_procedure_stats AS ps CROSS APPLY sys.dm_exec_query_plan(ps.plan_handle) AS qp
            WHERE ps.database_id = DB_ID() AND ps.object_id = OBJECT_ID('dbo.usp_inv_value_item')
            ORDER BY ps.last_execution_time DESC;
            """, connection, (SqlTransaction)transaction);
        await using (var reader = await stats.ExecuteReaderAsync(token))
        {
            Assert.True(await reader.ReadAsync(token));
            var plan = reader.GetString(0);
            Assert.Contains("ix_inv_stock_movement_valuation", plan, StringComparison.Ordinal);
            Assert.Contains("PhysicalOp=\"Index Seek\"", plan, StringComparison.Ordinal);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"100000 rows: elapsed={stopwatch.ElapsedMilliseconds}ms; cpu={reader.GetInt64(1) / 1000}ms; logicalReads={reader.GetInt64(2)}; spElapsed={reader.GetInt64(3) / 1000}ms");
        }
        await transaction.RollbackAsync(token);
    }
}
