using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-LOT-001～009、INV-LOT-012：批號／序號過帳、結存核對、追蹤方式檢查、重放、並發與不可改寫（myerp_test，ADR-060）。</summary>
public sealed class LotSerialTrackingTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 10, 13);
    private static readonly DateTime Now = new(2026, 10, 13, 23, 30, 0);
    private static readonly DateOnly Made = new(2026, 9, 1);
    private static readonly DateOnly Expiry = new(2027, 9, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel()],
            company);
    }

    private static StockPostingService Posting(MyErpDbContext db) =>
        new(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));

    private sealed record Master(long Plain, long Lot, long Serial, long Warehouse, long A, long B);

    private static async Task<Master> SetupAsync(MyErpDbContext db, long companyId)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var plain = await items.CreateAsync("P" + companyId, "Plain", null, uom.Id, null, null, ItemTrackingMode.None, Ct);
        var lot = await items.CreateAsync("L" + companyId, "Lot", null, uom.Id, null, null, ItemTrackingMode.Lot, Ct);
        var serial = await items.CreateAsync("S" + companyId, "Serial", null, uom.Id, null, null, ItemTrackingMode.Serial, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", Ct);
        return new(plain.Id, lot.Id, serial.Id, warehouse.Id, a.Id, b.Id);
    }

    private static InventoryPostingRequest Request(string type, long id, params InventoryPostingLine[] lines) => new(type, id, Day, lines);

    private static InventoryPostingLine In(int no, long item, long warehouse, long? location, decimal quantity,
        params InventoryTrackingLine[] tracking) => new(no, StockDirection.In, item, warehouse, location, quantity, 1m, tracking);

    private static InventoryPostingLine Out(int no, long item, long warehouse, long? location, decimal quantity,
        params InventoryTrackingLine[] tracking) => new(no, StockDirection.Out, item, warehouse, location, quantity, null, tracking);

    private static InventoryTrackingLine Lot(string no, decimal quantity, DateOnly? made = null, DateOnly? expiry = null) =>
        InventoryTrackingLine.Lot(no, quantity, made, expiry);

    private static InventoryTrackingLine Serial(string no) => InventoryTrackingLine.Serial(no);

    private static async Task<decimal> LotBalanceAsync(MyErpDbContext db, string lotNo, long locationId) =>
        await db.Set<StockMovementTracking>()
            .Where(x => x.Lot!.LotNo == lotNo && x.Movement!.LocationId == locationId)
            .SumAsync(x => x.Movement!.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity, Ct);

    [Fact]
    public async Task LotReceiptCreatesLotsAndIssueConsumesPerLocation()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var posting = Posting(db);

        await posting.PostAsync(Request("INV_OTHER_IN", 1,
            In(1, m.Lot, m.Warehouse, m.A, 8m, Lot(" L-001 ", 5m, Made, Expiry), Lot("L-002", 3m))), Ct);
        await posting.PostAsync(Request("INV_OTHER_IN", 2, In(1, m.Lot, m.Warehouse, m.B, 1m, Lot("l-001", 1m))), Ct);
        await posting.PostAsync(Request("INV_OTHER_OUT", 1, Out(1, m.Lot, m.Warehouse, m.A, 2m, Lot("L-001", 2m))), Ct);

        var lots = await db.Set<StockLot>().AsNoTracking().OrderBy(x => x.LotNo).ToListAsync(Ct);
        Assert.Equal(["L-001", "L-002"], lots.Select(x => x.LotNo));
        Assert.Equal((Made, Expiry), (lots[0].ManufactureDate, lots[0].ExpiryDate));
        Assert.Equal((m.Lot, (DateOnly?)null), (lots[1].ItemId, lots[1].ExpiryDate));
        Assert.Equal(3m, await LotBalanceAsync(db, "L-001", m.A));
        Assert.Equal(1m, await LotBalanceAsync(db, "L-001", m.B));
        Assert.Equal(3m, await LotBalanceAsync(db, "L-002", m.A));
        Assert.Equal(4, await db.Set<StockMovementTracking>().CountAsync(Ct));

        // B 有 1 個（L-001），整體結存足夠，但 L-002 不在 B
        Assert.Equal("INV.LOT.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(db).PostAsync(
            Request("INV_OTHER_OUT", 2, Out(1, m.Lot, m.Warehouse, m.B, 1m, Lot("L-002", 1m))), Ct))).ErrorCode);
        Assert.Equal("INV.LOT.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(db).PostAsync(
            Request("INV_OTHER_OUT", 3, Out(1, m.Lot, m.Warehouse, m.A, 1m, Lot("L-404", 1m))), Ct))).ErrorCode);
        Assert.Equal("INV.LOT.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(db).PostAsync(
            Request("INV_OTHER_OUT", 4, Out(1, m.Lot, m.Warehouse, m.A, 4m, Lot("L-001", 4m))), Ct))).ErrorCode);
        Assert.Equal(3, await db.Set<StockMovement>().CountAsync(Ct));
    }

    [Fact]
    public async Task ExistingLotMustKeepItsDates()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        await Posting(db).PostAsync(Request("INV_OTHER_IN", 1, In(1, m.Lot, m.Warehouse, m.A, 1m, Lot("L1", 1m, Made, Expiry))), Ct);

        await using var other = Context(companyId);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(other).PostAsync(Request("INV_OTHER_IN", 2,
            In(1, m.Lot, m.Warehouse, m.A, 2m, Lot("L1", 2m, Made, Expiry.AddDays(1)))), Ct));
        Assert.Equal("INV.LOT.ATTRIBUTE_MISMATCH", error.ErrorCode);
        var sameBatch = await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(other).PostAsync(Request("INV_OTHER_IN", 3,
            In(1, m.Lot, m.Warehouse, m.A, 1m, Lot("NEW", 1m, Made)), In(2, m.Lot, m.Warehouse, m.B, 1m, Lot("new", 1m, Made.AddDays(1)))), Ct));
        Assert.Equal("INV.LOT.ATTRIBUTE_MISMATCH", sameBatch.ErrorCode);

        await Posting(other).PostAsync(Request("INV_OTHER_IN", 4, In(1, m.Lot, m.Warehouse, m.A, 2m, Lot("L1", 2m, Made))), Ct);
        Assert.Equal(3m, await LotBalanceAsync(db, "L1", m.A));
        Assert.Equal(1, await db.Set<StockLot>().CountAsync(Ct));
    }

    [Fact]
    public async Task SerialsAreUniqueInStockAndCanBeReceivedAgainAfterIssue()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var posting = Posting(db);
        await posting.PostAsync(Request("INV_OTHER_IN", 1, In(1, m.Serial, m.Warehouse, m.A, 2m, Serial("SN-1"), Serial("SN-2"))), Ct);

        await using var other = Context(companyId);
        Assert.Equal("INV.SERIAL.IN_STOCK", (await Assert.ThrowsAsync<ConflictException>(() => Posting(other).PostAsync(
            Request("INV_OTHER_IN", 2, In(1, m.Serial, m.Warehouse, m.B, 1m, Serial("sn-1"))), Ct))).ErrorCode);
        Assert.Equal("INV.SERIAL.NOT_AVAILABLE", (await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(other).PostAsync(
            Request("INV_OTHER_OUT", 2, Out(1, m.Serial, m.Warehouse, m.A, 1m, Serial("SN-9"))), Ct))).ErrorCode);

        await Posting(other).PostAsync(Request("INV_TRANSFER", 1,
            Out(1, m.Serial, m.Warehouse, m.A, 1m, Serial("SN-2")), new InventoryPostingLine(1, StockDirection.In, m.Serial, m.Warehouse, m.B, 1m,
                Tracking: [Serial("SN-2")])), Ct);
        // B 現有 SN-2，整體結存足夠，但 SN-1 仍在 A
        Assert.Equal("INV.SERIAL.NOT_AVAILABLE", (await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(other).PostAsync(
            Request("INV_OTHER_OUT", 1, Out(1, m.Serial, m.Warehouse, m.B, 1m, Serial("SN-1"))), Ct))).ErrorCode);
        await Posting(other).PostAsync(Request("INV_OTHER_OUT", 3, Out(1, m.Serial, m.Warehouse, m.A, 1m, Serial("SN-1"))), Ct);
        await Posting(other).PostAsync(Request("INV_OTHER_IN", 3, In(1, m.Serial, m.Warehouse, m.B, 1m, Serial("SN-1"))), Ct);

        Assert.Equal(2, await db.Set<StockSerial>().CountAsync(Ct));
        var onHand = await db.Set<StockMovementTracking>().Where(x => x.Serial != null)
            .GroupBy(x => new { x.Serial!.SerialNo, x.Movement!.LocationId })
            .Select(g => new { g.Key.SerialNo, g.Key.LocationId, Quantity = g.Sum(x => x.Movement!.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity) })
            .Where(x => x.Quantity != 0).OrderBy(x => x.SerialNo).ToListAsync(Ct);
        Assert.Equal([("SN-1", (long?)m.B), ("SN-2", m.B)], onHand.Select(x => (x.SerialNo, x.LocationId)));
        Assert.All(onHand, x => Assert.Equal(1m, x.Quantity));
    }

    [Fact]
    public async Task TrackingMustMatchItemTrackingMode()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);

        async Task<T> Fails<T>(params InventoryPostingLine[] lines) where T : Exception
        {
            await using var fresh = Context(companyId);
            return await Assert.ThrowsAsync<T>(() => Posting(fresh).PostAsync(Request("INV_OTHER_IN", 1, lines), Ct));
        }

        Assert.Contains("lines[0].tracking", (await Fails<RequestValidationException>(In(1, m.Plain, m.Warehouse, m.A, 1m, Lot("L", 1m)))).Errors.Keys);
        Assert.Contains("lines[0].tracking", (await Fails<RequestValidationException>(In(1, m.Lot, m.Warehouse, m.A, 1m, Serial("S")))).Errors.Keys);
        Assert.Contains("lines[0].tracking", (await Fails<RequestValidationException>(In(1, m.Serial, m.Warehouse, m.A, 1m, Lot("L", 1m)))).Errors.Keys);
        Assert.Contains("lines[0].quantity", (await Fails<RequestValidationException>(In(1, m.Serial, m.Warehouse, m.A, 1.5m))).Errors.Keys);
        Assert.Equal("INV.TRACKING.REQUIRED", (await Fails<BusinessRuleException>(In(1, m.Lot, m.Warehouse, m.A, 1m))).ErrorCode);
        Assert.Equal("INV.TRACKING.REQUIRED", (await Fails<BusinessRuleException>(In(1, m.Lot, m.Warehouse, m.A, 3m, Lot("L", 2m)))).ErrorCode);
        Assert.Equal("INV.TRACKING.REQUIRED", (await Fails<BusinessRuleException>(In(1, m.Serial, m.Warehouse, m.A, 2m, Serial("S")))).ErrorCode);

        Assert.False(await db.Set<StockMovement>().AnyAsync(Ct));
        Assert.False(await db.Set<StockLot>().AnyAsync(Ct));
        Assert.False(await db.Set<StockSerial>().AnyAsync(Ct));
        await Posting(db).PostAsync(Request("INV_OTHER_IN", 1, In(1, m.Plain, m.Warehouse, m.A, 1.5m)), Ct);
    }

    public static TheoryData<InventoryPostingLine[]> InvalidTracking => new()
    {
        { [In(1, 1, 1, null, 1m, new InventoryTrackingLine(null, null, 1m))] },
        { [In(1, 1, 1, null, 1m, new InventoryTrackingLine("L", "S", 1m))] },
        { [In(1, 1, 1, null, 1m, Lot("  ", 1m))] },
        { [In(1, 1, 1, null, 1m, Lot(new string('L', 51), 1m))] },
        { [In(1, 1, 1, null, 1m, InventoryTrackingLine.Serial(new string('S', 101)))] },
        { [In(1, 1, 1, null, 1m, Lot("L", 0m))] },
        { [In(1, 1, 1, null, 1m, Lot("L", 0.0000001m))] },
        { [In(2, 1, 1, null, 2m, new InventoryTrackingLine(null, "S", 2m))] },
        { [In(1, 1, 1, null, 2m, Lot("L", 1m), Lot(" l ", 1m))] },
        { [In(1, 1, 1, null, 2m, Lot("L", 1m), Serial("S"))] },
        { [In(1, 1, 1, null, 1m, Lot("L", 1m, Expiry, Made))] },
        { [Out(1, 1, 1, null, 1m, Lot("L", 1m, Made))] },
        { [In(1, 1, 1, null, 1m, new InventoryTrackingLine(null, "S", 1m, Made))] },
        { [In(1, 1, 1, null, 1m, Serial("S")), In(2, 1, 2, null, 1m, Serial("s"))] },
    };

    [Theory]
    [MemberData(nameof(InvalidTracking))]
    public async Task InvalidTrackingShapesAreRejectedBeforeWriting(InventoryPostingLine[] lines)
    {
        await using var db = Context(TestCompanies.Next());

        await Assert.ThrowsAsync<RequestValidationException>(() => Posting(db).PostAsync(Request("INV_OTHER_IN", 1, lines), Ct));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(Ct));
    }

    [Fact]
    public async Task ReplaySnapshotIncludesLotAndSerialNumbers()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var request = Request("INV_OTHER_IN", 7,
            In(1, m.Lot, m.Warehouse, m.A, 3m, Lot("L1", 1m, Made), Lot("L2", 2m)),
            In(2, m.Serial, m.Warehouse, m.A, 2m, Serial("S1"), Serial("S2")));
        var first = await Posting(db).PostAsync(request, Ct);

        await using var other = Context(companyId);
        var replay = await Posting(other).PostAsync(Request("INV_OTHER_IN", 7,
            In(2, m.Serial, m.Warehouse, m.A, 2m, Serial("s2"), Serial("S1")),
            In(1, m.Lot, m.Warehouse, m.A, 3m, Lot("L2", 2.000m), Lot("l1", 1m, Made))), Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(first.MovementIds, replay.MovementIds);

        var changedLot = Request("INV_OTHER_IN", 7, In(1, m.Lot, m.Warehouse, m.A, 3m, Lot("L1", 2m), Lot("L2", 1m)), request.Lines[1]);
        Assert.Equal("INV.POSTING.CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() => Posting(other).PostAsync(changedLot, Ct))).ErrorCode);
        var changedSerial = Request("INV_OTHER_IN", 7, request.Lines[0], In(2, m.Serial, m.Warehouse, m.A, 2m, Serial("S1"), Serial("S3")));
        Assert.Equal("INV.POSTING.CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() => Posting(other).PostAsync(changedSerial, Ct))).ErrorCode);
        Assert.Equal(4, await db.Set<StockMovementTracking>().CountAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentReceiptsOfSameSerialAdmitOnlyOne()
    {
        var companyId = TestCompanies.Next();
        await using var setup = Context(companyId);
        var m = await SetupAsync(setup, companyId);

        await using var first = Context(companyId);
        await using var second = Context(companyId);
        Task<InventoryPostingResult> waiting;
        await using (var transaction = await first.Database.BeginTransactionAsync(Ct))
        {
            await Posting(first).PostAsync(Request("INV_OTHER_IN", 1, In(1, m.Serial, m.Warehouse, m.A, 1m, Serial("SN-X"))), Ct);
            waiting = Task.Run(() => Posting(second).PostAsync(Request("INV_OTHER_IN", 2, In(1, m.Serial, m.Warehouse, m.B, 1m, Serial("SN-X"))), Ct), Ct);
            await Task.Delay(250, Ct);
            Assert.False(waiting.IsCompleted);
            await transaction.CommitAsync(Ct);
        }

        Assert.Equal("INV.SERIAL.IN_STOCK", (await Assert.ThrowsAsync<ConflictException>(() => waiting)).ErrorCode);
        Assert.Equal(1, await setup.Set<StockSerial>().CountAsync(Ct));
        Assert.Equal(1, await setup.Set<StockMovementTracking>().CountAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentFirstReceiptsOfSameLotShareOneLot()
    {
        var companyId = TestCompanies.Next();
        await using var setup = Context(companyId);
        var m = await SetupAsync(setup, companyId);

        await using var first = Context(companyId);
        await using var second = Context(companyId);
        Task<InventoryPostingResult> waiting;
        await using (var transaction = await first.Database.BeginTransactionAsync(Ct))
        {
            await Posting(first).PostAsync(Request("INV_OTHER_IN", 1, In(1, m.Lot, m.Warehouse, m.A, 1m, Lot("LOT-X", 1m))), Ct);
            waiting = Task.Run(() => Posting(second).PostAsync(Request("INV_OTHER_IN", 2, In(1, m.Lot, m.Warehouse, m.B, 2m, Lot("LOT-X", 2m))), Ct), Ct);
            await Task.Delay(250, Ct);
            Assert.False(waiting.IsCompleted);
            await transaction.CommitAsync(Ct);
        }

        Assert.False((await waiting).Replayed);
        Assert.Equal(1, await setup.Set<StockLot>().CountAsync(Ct));
        Assert.Equal(2m, await LotBalanceAsync(setup, "LOT-X", m.B));
    }

    /// <summary>INV-LOT-012：追溯查詢的批號／序號列表、在庫篩選、現存位置與歷史。</summary>
    [Fact]
    public async Task TraceQueriesListLotsAndSerialsWithLocationsAndHistory()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var posting = Posting(db);
        await posting.PostAsync(Request("INV_OTHER_IN", 1,
            In(1, m.Lot, m.Warehouse, m.A, 5m, Lot("LOT-A", 5m, Made, Expiry)),
            In(2, m.Lot, m.Warehouse, m.B, 1m, Lot("LOT-B", 1m)),
            In(3, m.Serial, m.Warehouse, m.A, 2m, Serial("SN-1"), Serial("SN-2"))), Ct);
        await posting.PostAsync(new InventoryPostingRequest("INV_OTHER_OUT", 1, Day.AddDays(1),
            [Out(1, m.Lot, m.Warehouse, m.B, 1m, Lot("LOT-B", 1m)), Out(2, m.Serial, m.Warehouse, m.A, 1m, Serial("SN-1"))]), Ct);
        await posting.PostAsync(new InventoryPostingRequest("INV_TRANSFER", 1, Day.AddDays(2),
            [Out(1, m.Lot, m.Warehouse, m.A, 2m, Lot("LOT-A", 2m)), new InventoryPostingLine(1, StockDirection.In, m.Lot, m.Warehouse, m.B, 2m,
                Tracking: [Lot("LOT-A", 2m)])]), Ct);
        var query = new TrackingQueryService(db);

        var lots = await query.LotsAsync(new TrackingPageQuery { ItemId = m.Lot }, Ct);
        Assert.Equal([("LOT-A", 5m), ("LOT-B", 0m)], lots.Items.Select(x => (x.LotNo, x.OnHand)));
        var inStock = Assert.Single((await query.LotsAsync(new TrackingPageQuery { InStockOnly = true }, Ct)).Items);
        Assert.Equal(("LOT-A", (DateOnly?)Expiry), (inStock.LotNo, inStock.ExpiryDate));
        Assert.Equal("LOT-B", Assert.Single((await query.LotsAsync(new TrackingPageQuery { Keyword = "b" }, Ct)).Items).LotNo);

        var trace = await query.LotAsync(inStock.Id, Ct);
        Assert.Equal([new TrackedLocation(m.Warehouse, m.A, 3m), new TrackedLocation(m.Warehouse, m.B, 2m)], trace.Locations);
        Assert.Equal([("INV_TRANSFER", StockMovementDirection.In), ("INV_TRANSFER", StockMovementDirection.Out), ("INV_OTHER_IN", StockMovementDirection.In)],
            trace.History.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Direction == StockMovementDirection.In)
                .Select(x => (x.SourceType, x.Direction)));
        Assert.False(trace.HistoryTruncated);

        var serials = await query.SerialsAsync(new TrackingPageQuery { ItemId = m.Serial }, Ct);
        Assert.Equal([("SN-1", false), ("SN-2", true)], serials.Items.Select(x => (x.SerialNo, x.InStock)));
        Assert.Equal("SN-2", Assert.Single((await query.SerialsAsync(new TrackingPageQuery { InStockOnly = true }, Ct)).Items).SerialNo);
        var issued = await query.SerialAsync(serials.Items[0].Id, Ct);
        Assert.Empty(issued.Locations);
        Assert.Equal([StockMovementDirection.Out, StockMovementDirection.In], issued.History.Select(x => x.Direction));
        Assert.Equal(("INV_OTHER_OUT", 1L, 2), (issued.History[0].SourceType, issued.History[0].SourceId, issued.History[0].SourceLineNo));

        await using var other = Context(TestCompanies.Next());
        Assert.Equal(TrackingQueryService.LotNotFound, (await Assert.ThrowsAsync<NotFoundException>(() =>
            new TrackingQueryService(other).LotAsync(inStock.Id, Ct))).ErrorCode);
        Assert.Empty((await new TrackingQueryService(other).SerialsAsync(new TrackingPageQuery(), Ct)).Items);
        await Assert.ThrowsAsync<RequestValidationException>(() => query.LotsAsync(new TrackingPageQuery { ItemId = 0 }, Ct));
    }

    [Fact]
    public async Task TrackingRowsCannotBeUpdatedOrDeletedAndAreCompanyIsolated()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        await Posting(db).PostAsync(Request("INV_OTHER_IN", 1, In(1, m.Lot, m.Warehouse, m.A, 1m, Lot("L1", 1m))), Ct);
        var id = await db.Set<StockMovementTracking>().Select(x => x.Id).SingleAsync(Ct);
        await using var raw = database.CreateRawContext();

        await Assert.ThrowsAsync<SqlException>(() =>
            raw.Database.ExecuteSqlAsync($"UPDATE inv_stock_movement_tracking SET quantity = 99 WHERE id = {id}", Ct));
        await Assert.ThrowsAsync<SqlException>(() =>
            raw.Database.ExecuteSqlAsync($"DELETE FROM inv_stock_movement_tracking WHERE id = {id}", Ct));
        Assert.Equal(1m, await LotBalanceAsync(db, "L1", m.A));

        await using var otherCompany = Context(TestCompanies.Next());
        Assert.False(await otherCompany.Set<StockLot>().AnyAsync(Ct));
        Assert.False(await otherCompany.Set<StockMovementTracking>().AnyAsync(Ct));
    }
}
