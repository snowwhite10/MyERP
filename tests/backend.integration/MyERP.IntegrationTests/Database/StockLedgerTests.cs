using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-LED-001～008、INV-BASE-003：異動帳過帳、防重放、負庫存、並發與不可改寫。</summary>
public sealed class StockLedgerTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 10, 13);
    private static readonly DateTime Now = new(2026, 10, 13, 17, 30, 0);

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(), new StockMovementDbModel(), new StockTrackingDbModel()],
            company);
    }

    private static StockPostingService Posting(MyErpDbContext db) =>
        new(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));

    private static ExternalInventoryPostingService External(MyErpDbContext db) =>
        new ExternalInventoryPostingService(db, Posting(db));

    private static async Task<(long Item, long Warehouse, long LocationA, long LocationB)> SetupAsync(
        MyErpDbContext db, long companyId, CancellationToken token)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, token);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", token);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", token);
        return (item.Id, warehouse.Id, a.Id, b.Id);
    }

    private static InventoryPostingRequest Request(string type, long id, params InventoryPostingLine[] lines) =>
        new(type, id, Day, lines);

    [Fact]
    public async Task ExternalSourcesRequireCallerTransactionAndKeepSeparateReplayScopes()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var (item, warehouse, location, _) = await SetupAsync(db, company, token);
        var line = new PurchaseInboundLine(1, item, warehouse, location, 2m, 3m);
        await Assert.ThrowsAsync<BusinessRuleException>(() => External(db).PostPurchaseInboundAsync(7, Day, [line], token));
        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            var first = await External(db).PostPurchaseInboundAsync(7, Day, [line], token);
            Assert.False(first.Replayed);
            await tx.CommitAsync(token);
        }
        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            var replay = await External(db).PostPurchaseInboundAsync(7, Day, [line], token);
            Assert.True(replay.Replayed);
            Assert.Equal("INV.POSTING.CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
                External(db).PostPurchaseInboundAsync(7, Day, [line with { Quantity = 3m }], token))).ErrorCode);
            await tx.RollbackAsync(token);
        }
        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            var issue = await External(db).PostSalesOutboundAsync(7, Day,
                [new SalesOutboundLine(1, item, warehouse, location, 1m)], token);
            Assert.False(issue.Replayed);
            await tx.CommitAsync(token);
        }
        var movements = await db.Set<StockMovement>().OrderBy(x => x.Id).ToListAsync(token);
        Assert.Contains(movements, x => x.SourceType == ExternalInventorySourceTypes.PurchaseInbound
            && x.Direction == StockMovementDirection.In && x.UnitCost == 3m);
        Assert.Contains(movements, x => x.SourceType == ExternalInventorySourceTypes.SalesOutbound
            && x.Direction == StockMovementDirection.Out && x.UnitCost == null);
        Assert.Equal(1m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, location, token));
    }

    [Fact]
    public async Task ExternalPostingRollsBackWithCallerAndSeparatesCompanies()
    {
        var token = TestContext.Current.CancellationToken;
        var companyA = TestCompanies.Next();
        var companyB = TestCompanies.Next();
        await using var a = Context(companyA);
        await using var b = Context(companyB);
        var ma = await SetupAsync(a, companyA, token);
        var mb = await SetupAsync(b, companyB, token);
        await using (var tx = await a.Database.BeginTransactionAsync(token))
        {
            await External(a).PostPurchaseInboundAsync(9, Day,
                [new PurchaseInboundLine(1, ma.Item, ma.Warehouse, ma.LocationA, 1m, 2m)], token);
            await tx.RollbackAsync(token);
        }
        a.ChangeTracker.Clear();
        Assert.False(await a.Set<StockMovement>().AnyAsync(token));
        await using (var tx = await a.Database.BeginTransactionAsync(token))
        {
            Assert.False((await External(a).PostPurchaseInboundAsync(9, Day,
                [new PurchaseInboundLine(1, ma.Item, ma.Warehouse, ma.LocationA, 1m, 2m)], token)).Replayed);
            await tx.CommitAsync(token);
        }
        await using (var tx = await b.Database.BeginTransactionAsync(token))
        {
            Assert.False((await External(b).PostPurchaseInboundAsync(9, Day,
                [new PurchaseInboundLine(1, mb.Item, mb.Warehouse, mb.LocationA, 3m, 4m)], token)).Replayed);
            await tx.CommitAsync(token);
        }
        Assert.Single(await a.Set<StockMovement>().ToListAsync(token));
        Assert.Single(await b.Set<StockMovement>().ToListAsync(token));
    }

    [Fact]
    public async Task ConcurrentExternalSalesIssuesCannotOversell()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var setup = Context(company);
        var (item, warehouse, location, _) = await SetupAsync(setup, company, token);
        await using (var tx = await setup.Database.BeginTransactionAsync(token))
        {
            await External(setup).PostPurchaseInboundAsync(1, Day,
                [new PurchaseInboundLine(1, item, warehouse, location, 10m, 2m)], token);
            await tx.CommitAsync(token);
        }
        await using var first = Context(company);
        await using var second = Context(company);
        await using var firstTx = await first.Database.BeginTransactionAsync(token);
        await External(first).PostSalesOutboundAsync(1, Day,
            [new SalesOutboundLine(1, item, warehouse, location, 6m)], token);
        var waiting = Task.Run(async () =>
        {
            await using var secondTx = await second.Database.BeginTransactionAsync(token);
            return await External(second).PostSalesOutboundAsync(2, Day,
                [new SalesOutboundLine(1, item, warehouse, location, 6m)], token);
        }, token);
        await Task.Delay(250, token);
        Assert.False(waiting.IsCompleted);
        await firstTx.CommitAsync(token);
        Assert.Equal("INV.STOCK.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() => waiting)).ErrorCode);
        Assert.Equal(4m, await new StockBalanceQuery(setup).GetOnHandAsync(item, warehouse, location, token));
    }

    [Fact]
    public async Task StockFlowReportUsesBusinessDatesAndCompanyScopedKeys()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var (item, warehouse, a, b) = await SetupAsync(db, company, token);
        var posting = Posting(db);
        await posting.PostAsync(new InventoryPostingRequest("RPT_OPEN", 1, Day.AddDays(-1),
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 10.123456m)]), token);
        await posting.PostAsync(new InventoryPostingRequest("RPT_IN", 2, Day,
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 3m),
             new InventoryPostingLine(2, StockDirection.In, item, warehouse, b, 1m)]), token);
        await posting.PostAsync(new InventoryPostingRequest("RPT_OUT", 3, Day.AddDays(1),
            [new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 4m)]), token);

        var result = await new StockFlowReportService(db, new FixedClock(Now)).QueryAsync(
            new StockFlowReportQuery { From = Day, To = Day.AddDays(1), ItemId = item, PageSize = 1 }, token);
        Assert.Equal(2, result.TotalCount);
        var row = Assert.Single(result.Items);
        Assert.Equal((10.123456m, 3m, 4m, 9.123456m),
            (row.OpeningQuantity, row.InboundQuantity, row.OutboundQuantity, row.ClosingQuantity));
        Assert.False(row.NegativeClosing);
        Assert.Equal(Now, result.QueriedAt.DateTime);
        var next = await new StockFlowReportService(db, new FixedClock(Now)).QueryAsync(
            new StockFlowReportQuery { From = Day, To = Day.AddDays(1), ItemId = item, Page = 2, PageSize = 1 }, token);
        Assert.Single(next.Items);
        Assert.Equal(1m, next.Items[0].ClosingQuantity);
        Assert.Equal(4, await db.Set<StockMovement>().CountAsync(token));
        await using var other = Context(TestCompanies.Next());
        Assert.Equal(0, (await new StockFlowReportService(other, new FixedClock(Now)).QueryAsync(
            new StockFlowReportQuery { From = Day, To = Day }, token)).TotalCount);
    }

    [Fact]
    public async Task StockFlowReportRejectsInvalidDatesAndIds()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(TestCompanies.Next());
        var report = new StockFlowReportService(db, new FixedClock(Now));
        Assert.Contains("from", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            report.QueryAsync(new StockFlowReportQuery { To = Day }, token))).Errors.Keys);
        Assert.Contains("to", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            report.QueryAsync(new StockFlowReportQuery { From = Day.AddDays(1), To = Day }, token))).Errors.Keys);
        Assert.Contains("itemId", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            report.QueryAsync(new StockFlowReportQuery { From = Day, To = Day, ItemId = 0 }, token))).Errors.Keys);
    }

    [Fact]
    public async Task StockFlowExportIncludesEveryKeyAndRejectsAnExcessiveResultBeforeWriting()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(TestCompanies.Next());
        var (item, warehouse, a, b) = await SetupAsync(db, db.CurrentCompanyId!.Value, token);
        await Posting(db).PostAsync(new InventoryPostingRequest("RPT_CSV", 1, Day,
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1.123456m),
             new InventoryPostingLine(2, StockDirection.In, item, warehouse, b, 2m)]), token);
        var service = new StockFlowReportService(db, new FixedClock(Now));
        await using var output = new MemoryStream();
        var count = await service.ExportAsync(new StockFlowReportQuery { From = Day, To = Day, PageSize = 1 }, output, maxRows: 2, token: token);
        Assert.Equal(2, count);
        var csv = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("1.123456", csv);
        Assert.Equal(4, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        await using var rejected = new MemoryStream();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ExportAsync(
            new StockFlowReportQuery { From = Day, To = Day }, rejected, maxRows: 1, token: token));
        Assert.Equal("INV.REPORT.EXPORT_LIMIT", error.ErrorCode);
        Assert.Equal(0, rejected.Length);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await using var cancelledOutput = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(
            new StockFlowReportQuery { From = Day, To = Day }, cancelledOutput, token: cancelled.Token));
        Assert.Equal(0, cancelledOutput.Length);
    }

    [Fact]
    public async Task StockFlowExportKeepsTheCountAndRowsInOneSnapshot()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var first = Context(company);
        var (item, warehouse, location, _) = await SetupAsync(first, company, token);
        await Posting(first).PostAsync(new InventoryPostingRequest("RPT_EXPORT_SNAP", 1, Day,
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, location, 1m)]), token);
        await using var second = Context(company);
        await using var output = new MemoryStream();
        var count = await new StockFlowReportService(first, new FixedClock(Now)).ExportAsync(
            new StockFlowReportQuery { From = Day, To = Day }, output, onReady: async (_, _) =>
            {
                await Posting(second).PostAsync(new InventoryPostingRequest("RPT_EXPORT_SNAP", 2, Day,
                    [new InventoryPostingLine(1, StockDirection.In, item, warehouse, location, 2m)]), token);
            }, token: token);
        Assert.Equal(1, count);
        var csv = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("1.000000,0.000000,1.000000,false", csv);
        Assert.DoesNotContain("3.000000", csv);
        Assert.Equal(3m, (await new StockFlowReportService(second, new FixedClock(Now)).QueryAsync(
            new StockFlowReportQuery { From = Day, To = Day }, token)).Items.Single().ClosingQuantity);
    }

    [Fact]
    public async Task StockFlowSnapshotDoesNotIncludeMovementCommittedAfterFirstRead()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var first = Context(company);
        var (item, warehouse, a, _) = await SetupAsync(first, company, token);
        await Posting(first).PostAsync(new InventoryPostingRequest("RPT_SNAP", 1, Day,
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)]), token);
        await using var second = Context(company);
        await using (var snapshot = await first.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token))
        {
            Assert.Equal(1, await first.Set<StockMovement>().CountAsync(token));
            await Posting(second).PostAsync(new InventoryPostingRequest("RPT_SNAP", 2, Day,
                [new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 2m)]), token);
            Assert.Equal(1, await first.Set<StockMovement>().CountAsync(token));
            await snapshot.CommitAsync(token);
        }
        var report = await new StockFlowReportService(first, new FixedClock(Now)).QueryAsync(
            new StockFlowReportQuery { From = Day, To = Day }, token);
        Assert.Equal(3m, Assert.Single(report.Items).InboundQuantity);
    }

    [Fact]
    public async Task StockFlowReportPreservesLeapDayAndNegativeHistoricalClosing()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(TestCompanies.Next());
        var (item, warehouse, a, _) = await SetupAsync(db, db.CurrentCompanyId!.Value, token);
        var posting = Posting(db);
        await posting.PostAsync(new InventoryPostingRequest("RPT_LEAP", 1, new DateOnly(2024, 2, 28),
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 5m)]), token);
        await posting.PostAsync(new InventoryPostingRequest("RPT_LEAP", 2, new DateOnly(2024, 3, 1),
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 2m)]), token);
        await posting.PostAsync(new InventoryPostingRequest("RPT_LEAP", 3, new DateOnly(2024, 2, 29),
            [new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 6m)]), token);
        var service = new StockFlowReportService(db, new FixedClock(Now));
        var leap = Assert.Single((await service.QueryAsync(new StockFlowReportQuery
            { From = new DateOnly(2024, 2, 29), To = new DateOnly(2024, 2, 29) }, token)).Items);
        Assert.Equal((5m, 0m, 6m, -1m, true),
            (leap.OpeningQuantity, leap.InboundQuantity, leap.OutboundQuantity, leap.ClosingQuantity, leap.NegativeClosing));
        await posting.PostAsync(new InventoryPostingRequest("RPT_YEAR", 4, new DateOnly(2024, 12, 31),
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 3m)]), token);
        await posting.PostAsync(new InventoryPostingRequest("RPT_YEAR", 5, new DateOnly(2025, 1, 1),
            [new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 1m)]), token);
        var year = Assert.Single((await service.QueryAsync(new StockFlowReportQuery
            { From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 1, 1) }, token)).Items);
        Assert.Equal((4m, 0m, 1m, 3m),
            (year.OpeningQuantity, year.InboundQuantity, year.OutboundQuantity, year.ClosingQuantity));
        Assert.Contains("to", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.QueryAsync(new StockFlowReportQuery { From = Day }, token))).Errors.Keys);
    }

    [Fact]
    public async Task StockFlowReportIncludesMonthEndWithoutCarryingNextMonthIntoThePeriod()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(TestCompanies.Next());
        var (item, warehouse, location, _) = await SetupAsync(db, db.CurrentCompanyId!.Value, token);
        await Posting(db).PostAsync(new InventoryPostingRequest("RPT_MONTH", 1, new DateOnly(2026, 1, 31),
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, location, 3m)]), token);
        await Posting(db).PostAsync(new InventoryPostingRequest("RPT_MONTH", 2, new DateOnly(2026, 2, 1),
            [new InventoryPostingLine(1, StockDirection.Out, item, warehouse, location, 1m)]), token);
        var report = new StockFlowReportService(db, new FixedClock(Now));
        var january = Assert.Single((await report.QueryAsync(new StockFlowReportQuery
            { From = new DateOnly(2026, 1, 31), To = new DateOnly(2026, 1, 31) }, token)).Items);
        Assert.Equal((0m, 3m, 0m, 3m), (january.OpeningQuantity, january.InboundQuantity,
            january.OutboundQuantity, january.ClosingQuantity));
        var february = Assert.Single((await report.QueryAsync(new StockFlowReportQuery
            { From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 2, 1) }, token)).Items);
        Assert.Equal((3m, 0m, 1m, 2m), (february.OpeningQuantity, february.InboundQuantity,
            february.OutboundQuantity, february.ClosingQuantity));
    }

    [Fact]
    public async Task StockFlowReportRemainsReadOnlyWhenThePostedItemsMasterIsInactive()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var (item, warehouse, location, _) = await SetupAsync(db, company, token);
        var report = new StockFlowReportService(db, new FixedClock(Now));
        Assert.Equal(0, (await report.QueryAsync(new StockFlowReportQuery { From = Day, To = Day }, token)).TotalCount);
        await Posting(db).PostAsync(new InventoryPostingRequest("RPT_INACTIVE", 1, Day,
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, location, 2m)]), token);
        var masterService = new ItemService(db);
        var master = Assert.Single((await masterService.ListAsync(new MyERP.Shared.Paging.PagedRequest
            { Keyword = "I" + company }, token)).Items);
        await masterService.UpdateAsync(item, master.Name, master.Specification, master.BaseUomId,
            master.BrandId, master.CategoryId, false, master.Version, token);
        var countBefore = await db.Set<StockMovement>().CountAsync(token);
        var row = Assert.Single((await report.QueryAsync(new StockFlowReportQuery { From = Day, To = Day }, token)).Items);
        Assert.Equal(2m, row.ClosingQuantity);
        Assert.Equal(countBefore, await db.Set<StockMovement>().CountAsync(token));
    }

    [Fact]
    public async Task InventoryQueryPagesNonZeroBalancesWithinCompanyAndLocation()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await SetupAsync(db, companyId, token);
        var posting = Posting(db);
        await posting.PostAsync(Request("QUERY_IN", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 3.123456m),
            new InventoryPostingLine(2, StockDirection.In, item, warehouse, b, 2m),
            new InventoryPostingLine(3, StockDirection.In, item, warehouse, null, 1m)), token);
        await posting.PostAsync(Request("QUERY_OUT", 2,
            new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 1m),
            new InventoryPostingLine(2, StockDirection.Out, item, warehouse, b, 2m)), token);
        await using var other = Context(TestCompanies.Next());
        var query = new StockQueryService(db);

        var first = await query.BalancesAsync(new StockBalancePageQuery { ItemId = item, PageSize = 1 }, token);
        var second = await query.BalancesAsync(new StockBalancePageQuery { ItemId = item, Page = 2, PageSize = 1 }, token);
        Assert.Equal(2, first.TotalCount);
        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.Contains(new[] { first.Items[0], second.Items[0] }, x => x.LocationId == a && x.Quantity == 2.123456m);
        Assert.DoesNotContain(new[] { first.Items[0], second.Items[0] }, x => x.LocationId == b);
        Assert.Equal(2.123456m, Assert.Single((await query.BalancesAsync(new StockBalancePageQuery { LocationId = a }, token)).Items).Quantity);
        Assert.Empty((await new StockQueryService(other).BalancesAsync(new StockBalancePageQuery(), token)).Items);
    }

    [Fact]
    public async Task MovementQueryFiltersDatesDirectionAndLocationWithStablePaging()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await SetupAsync(db, companyId, token);
        var posting = Posting(db);
        await posting.PostAsync(Request("QUERY_IN", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 5m),
            new InventoryPostingLine(2, StockDirection.In, item, warehouse, b, 1m)), token);
        await posting.PostAsync(new InventoryPostingRequest("QUERY_OUT", 2, Day.AddDays(1),
            [new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 2m)]), token);
        var query = new StockQueryService(db);

        var page1 = await query.MovementsAsync(new StockMovementPageQuery { ItemId = item, PageSize = 1 }, token);
        var page2 = await query.MovementsAsync(new StockMovementPageQuery { ItemId = item, Page = 2, PageSize = 1 }, token);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal("QUERY_OUT", Assert.Single(page1.Items).SourceType);
        Assert.NotEqual(page1.Items[0].Id, Assert.Single(page2.Items).Id);
        var filtered = await query.MovementsAsync(new StockMovementPageQuery
        { WarehouseId = warehouse, LocationId = a, Direction = StockMovementDirection.In, From = Day, To = Day }, token);
        var movement = Assert.Single(filtered.Items);
        Assert.Equal(("QUERY_IN", 1L, 5m), (movement.SourceType, movement.SourceId, movement.Quantity));
        Assert.Equal("I" + companyId, movement.ItemCode);
        Assert.Equal("A" + companyId, movement.LocationCode);
        Assert.Contains("to", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            query.MovementsAsync(new StockMovementPageQuery { From = Day.AddDays(1), To = Day }, token))).Errors.Keys);
    }

    [Fact]
    public async Task PostsReceiptAndIssueAndDerivesBalanceFromLedger()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await SetupAsync(db, companyId, token);
        var posting = Posting(db);

        var receipt = await posting.PostAsync(Request("INV_OTHER_IN", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 10.123456m, 12.5m)), token);
        var issue = await posting.PostAsync(Request("INV_OTHER_OUT", 1,
            new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 3m)), token);

        Assert.False(receipt.Replayed);
        Assert.Single(receipt.MovementIds);
        Assert.Single(issue.MovementIds);
        var query = new StockBalanceQuery(db);
        Assert.Equal(7.123456m, await query.GetOnHandAsync(item, warehouse, a, token));
        Assert.Equal(0m, await query.GetOnHandAsync(item, warehouse, null, token));
        var movement = await db.Set<StockMovement>().AsNoTracking().SingleAsync(x => x.Id == receipt.MovementIds[0], token);
        Assert.Equal(("INV_OTHER_IN", 1L, 1, StockMovementDirection.In), (movement.SourceType, movement.SourceId, movement.SourceLineNo, movement.Direction));
        Assert.Equal((10.123456m, 12.5m, Day, Now), (movement.Quantity, movement.UnitCost, movement.BusinessDate, movement.PostedAt));
        Assert.Equal(("I" + companyId, "A" + companyId), await db.Set<InventoryReference>()
            .Where(x => x.Id == movement.ReferenceId).Select(x => ValueTuple.Create(x.ItemCode, x.LocationCode!)).SingleAsync(token));
        var balance = Assert.Single(await query.ListAsync(item, null, token));
        Assert.Equal(new StockBalance(item, warehouse, a, 7.123456m), balance);
    }

    [Fact]
    public async Task ReplayReturnsExistingMovementsAndConflictingReplayIsRejected()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await SetupAsync(db, companyId, token);
        var request = Request("INV_OTHER_IN", 9,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 5m, 2m),
            new InventoryPostingLine(2, StockDirection.In, item, warehouse, b, 1m));

        var first = await Posting(db).PostAsync(request, token);
        await using var other = Context(companyId);
        var replay = await Posting(other).PostAsync(request with { Lines = [.. request.Lines.Reverse()] }, token);

        Assert.True(replay.Replayed);
        Assert.Equal(first.MovementIds, replay.MovementIds);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == "INV_OTHER_IN" && x.SourceId == 9, token));
        var changed = request with { Lines = [request.Lines[0] with { Quantity = 6m }, request.Lines[1]] };
        Assert.Equal("INV.POSTING.CONFLICT",
            (await Assert.ThrowsAsync<ConflictException>(() => Posting(other).PostAsync(changed, token))).ErrorCode);
        var missingLine = request with { Lines = [request.Lines[0]] };
        Assert.Equal("INV.POSTING.CONFLICT",
            (await Assert.ThrowsAsync<ConflictException>(() => Posting(other).PostAsync(missingLine, token))).ErrorCode);
        Assert.Equal(6m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, a, token) +
            await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, b, token));
    }

    [Fact]
    public async Task InsufficientStockRejectsWholePostingWithoutPartialWrites()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await SetupAsync(db, companyId, token);
        await Posting(db).PostAsync(Request("INV_OTHER_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 2m)), token);

        await using var failing = Context(companyId);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(failing).PostAsync(Request("INV_TRANSFER", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, b, 5m),
            new InventoryPostingLine(2, StockDirection.Out, item, warehouse, a, 2.000001m)), token));

        Assert.Equal("INV.STOCK.INSUFFICIENT", error.ErrorCode);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == "INV_TRANSFER", token));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(x => x.SourceKey.StartsWith("STOCK:INV_TRANSFER:"), token));
        Assert.Equal(2m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, a, token));
    }

    [Fact]
    public async Task TransferMovesStockAtomicallyAndConservesTotal()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await SetupAsync(db, companyId, token);
        var posting = Posting(db);
        await posting.PostAsync(Request("INV_OTHER_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 8m)), token);

        await posting.PostAsync(Request("INV_TRANSFER", 1,
            new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 8m),
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, b, 8m)), token);

        var balances = await new StockBalanceQuery(db).ListAsync(null, warehouse, token);
        Assert.Equal([new StockBalance(item, warehouse, b, 8m)], balances);
    }

    [Fact]
    public async Task ConcurrentIssuesForSameStockCannotOversell()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using (var setup = Context(companyId))
        {
            var (item, warehouse, a, _) = await SetupAsync(setup, companyId, token);
            await Posting(setup).PostAsync(Request("INV_OTHER_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 10m)), token);

            await using var first = Context(companyId);
            await using var second = Context(companyId);
            InventoryPostingRequest Issue(long id) => Request("INV_OTHER_OUT", id, new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 6m));
            Task<InventoryPostingResult> held;
            Task<InventoryPostingResult> waiting;
            await using (var transaction = await first.Database.BeginTransactionAsync(token))
            {
                held = Posting(first).PostAsync(Issue(1), token);
                await held;
                waiting = Task.Run(() => Posting(second).PostAsync(Issue(2), token), token);
                await Task.Delay(250, token);
                Assert.False(waiting.IsCompleted);
                await transaction.CommitAsync(token);
            }

            Assert.Equal("INV.STOCK.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() => waiting)).ErrorCode);
            Assert.Equal(4m, await new StockBalanceQuery(setup).GetOnHandAsync(item, warehouse, a, token));
        }
    }

    [Fact]
    public async Task CallerRollbackDiscardsPostingAndReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await SetupAsync(db, companyId, token);

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await Posting(db).PostAsync(Request("INV_OTHER_IN", 3, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)), token);
            await transaction.RollbackAsync(token);
        }

        db.ChangeTracker.Clear();
        Assert.False(await db.Set<StockMovement>().AnyAsync(token));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(token));
        var retried = await Posting(db).PostAsync(Request("INV_OTHER_IN", 3, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)), token);
        Assert.False(retried.Replayed);
    }

    [Fact]
    public async Task LedgerRowsCannotBeUpdatedOrDeleted()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await SetupAsync(db, companyId, token);
        var id = (await Posting(db).PostAsync(Request("INV_OTHER_IN", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)), token)).MovementIds[0];
        await using var raw = database.CreateRawContext();

        await Assert.ThrowsAsync<SqlException>(() =>
            raw.Database.ExecuteSqlAsync($"UPDATE inv_stock_movement SET quantity = 99 WHERE id = {id}", token));
        await Assert.ThrowsAsync<SqlException>(() =>
            raw.Database.ExecuteSqlAsync($"DELETE FROM inv_stock_movement WHERE id = {id}", token));
        Assert.Equal(1m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, a, token));
    }

    [Fact]
    public async Task BalancesAndMovementsAreCompanyIsolated()
    {
        var token = TestContext.Current.CancellationToken;
        var companyA = TestCompanies.Next();
        var companyB = TestCompanies.Next();
        await using var dbA = Context(companyA);
        var (item, warehouse, a, _) = await SetupAsync(dbA, companyA, token);
        await Posting(dbA).PostAsync(Request("INV_OTHER_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 4m)), token);

        await using var dbB = Context(companyB);
        var queryB = new StockBalanceQuery(dbB);
        Assert.Equal(0m, await queryB.GetOnHandAsync(item, warehouse, a, token));
        Assert.Empty(await queryB.ListAsync(null, null, token));
        Assert.False(await dbB.Set<StockMovement>().AnyAsync(token));
        await Assert.ThrowsAsync<NotFoundException>(() => Posting(dbB).PostAsync(Request("INV_OTHER_IN", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)), token));
        Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => Posting(dbB).PostAsync(Request("INV_OTHER_OUT", 1,
            new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 1m)), token))).ErrorCode);
        Assert.False(await dbB.Set<InventoryReference>().AnyAsync(token));
    }

    public static TheoryData<string, InventoryPostingLine[]> InvalidRequests => new()
    {
        { "", [new(1, StockDirection.In, 1, 1, null, 1m)] },
        { "inv_lower", [new(1, StockDirection.In, 1, 1, null, 1m)] },
        { "INV_OTHER_IN", [] },
        { "INV_OTHER_IN", [new(0, StockDirection.In, 1, 1, null, 1m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 0m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, -1m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, decimal.MinValue)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 0.0000001m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 10_000_000_000_000m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 1m, -0.01m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 1m, 1.0000001m)] },
        { "INV_OTHER_OUT", [new(1, StockDirection.Out, 1, 1, null, 1m, 1m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 1m), new(1, StockDirection.In, 1, 1, null, 2m)] },
        { "INV_OTHER_IN", [new(1, (StockDirection)9, 1, 1, null, 1m)] },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequestsAreRejectedBeforeWriting(string sourceType, InventoryPostingLine[] lines)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(TestCompanies.Next());

        await Assert.ThrowsAsync<RequestValidationException>(() => Posting(db).PostAsync(new(sourceType, 1, Day, lines), token));
        await Assert.ThrowsAsync<RequestValidationException>(() => Posting(db).PostAsync(new("INV_OTHER_IN", 0, Day,
            [new(1, StockDirection.In, 1, 1, null, 1m)]), token));
        Assert.False(await db.Set<StockMovement>().AnyAsync(token));
    }
}
