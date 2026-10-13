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

/// <summary>INV-OPN-001～005：物料期初庫存的預覽、逐行錯誤、冪等提交、日期規則與並發（myerp_test，ADR-059）。</summary>
public sealed class OpeningStockTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 10, 1);
    private static readonly DateTime Now = new(2026, 10, 13, 18, 0, 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel(), new OpeningStockDbModel()],
            company);
    }

    private static StockPostingService Posting(MyErpDbContext db) =>
        new(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));

    private static OpeningStockService Service(MyErpDbContext db) =>
        new(db, new InventoryReferenceGuard(db), Posting(db), new FixedClock(Now));

    private static async Task<Master> SetupAsync(MyErpDbContext db, long companyId)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var other = await new ItemService(db).CreateAsync("J" + companyId, "Other item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var second = await new WarehouseService(db).CreateAsync("X" + companyId, "Second", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(second.Id, "B" + companyId, "B", Ct);
        return new Master(item.Id, other.Id, uom.Id, warehouse.Id, second.Id, a.Id, b.Id);
    }

    private static OpeningStockInput Input(Master m, string key = "batch-1", DateOnly? date = null) => new(key, date ?? Day, "Go-live",
    [
        new OpeningStockLineInput(1, m.Item, m.Warehouse, m.LocationA, 10.123456m, 12.5m),
        new OpeningStockLineInput(2, m.Other, m.Second, null, 3m, 0m),
    ]);

    private static Task<decimal> OnHandAsync(MyErpDbContext db, long item, long warehouse, long? location) =>
        new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, location, Ct);

    /// <summary>INV-LOT-014：期初批序號逐行檢查、提交建立批號／序號、重送比較包含批序號。</summary>
    [Fact]
    public async Task OpeningStockCapturesLotAndSerialNumbers()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var lot = (await new ItemService(db).CreateAsync("LT" + companyId, "Lot", null, m.Uom, null, null, ItemTrackingMode.Lot, Ct)).Id;
        var serial = (await new ItemService(db).CreateAsync("SN" + companyId, "Serial", null, m.Uom, null, null, ItemTrackingMode.Serial, Ct)).Id;
        var made = new DateOnly(2026, 1, 1);

        var preview = await Service(db).PreviewAsync(new OpeningStockInput("preview", Day, null,
        [
            new OpeningStockLineInput(1, lot, m.Warehouse, null, 2m, 1m),
            new OpeningStockLineInput(2, m.Item, m.Warehouse, null, 1m, 1m, [InventoryTrackingLine.Lot("L", 1m)]),
            new OpeningStockLineInput(3, serial, m.Warehouse, null, 1m, 1m, [InventoryTrackingLine.Serial("S")]),
        ]), Ct);
        var byLine = preview.Lines.ToDictionary(x => x.LineNo);
        Assert.Equal(new OpeningStockLineError("tracking", "INV.TRACKING.REQUIRED"), Assert.Single(byLine[1].Errors));
        Assert.Equal(new OpeningStockLineError("tracking", OpeningStockService.TrackingMismatch), Assert.Single(byLine[2].Errors));
        Assert.Empty(byLine[3].Errors);

        var input = new OpeningStockInput("tracked", Day, null,
        [
            new OpeningStockLineInput(1, lot, m.Warehouse, m.LocationA, 3m, 1m,
                [InventoryTrackingLine.Lot("L-1", 1m, made), InventoryTrackingLine.Lot("L-2", 2m)]),
            new OpeningStockLineInput(2, serial, m.Warehouse, m.LocationA, 2m, 5m,
                [InventoryTrackingLine.Serial("S-1"), InventoryTrackingLine.Serial("S-2")]),
        ]);
        var submitted = await Service(db).SubmitAsync(input, Ct);
        Assert.False(submitted.Replayed);
        db.ChangeTracker.Clear();
        var batch = await Service(db).GetAsync(submitted.OpeningStock.Id, Ct);
        Assert.Equal([("L-1", 1m, (DateOnly?)made), ("L-2", 2m, null)], batch.Lines[0].Tracking.Select(x => (x.LotNo!, x.Quantity, x.ManufactureDate)));
        Assert.Equal(["S-1", "S-2"], batch.Lines[1].Tracking.Select(x => x.SerialNo!));
        Assert.Equal(4, await db.Set<StockMovementTracking>().CountAsync(Ct));

        await using var other = Context(companyId);
        Assert.True((await Service(other).SubmitAsync(input, Ct)).Replayed);
        var changed = input with { Lines = [input.Lines![0], input.Lines[1] with { Tracking = [InventoryTrackingLine.Serial("S-1"), InventoryTrackingLine.Serial("S-3")] }] };
        Assert.Equal(OpeningStockService.RequestConflict, (await Assert.ThrowsAsync<ConflictException>(() => Service(other).SubmitAsync(changed, Ct))).ErrorCode);
    }

    [Fact]
    public async Task PreviewReportsEveryLineErrorWithoutWriting()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var inactive = await new ItemService(db).CreateAsync("K" + companyId, "Inactive", null, m.Uom, null, null, Ct);
        await new ItemService(db).UpdateAsync(inactive.Id, "Inactive", null, m.Uom, null, null, false, inactive.Version, Ct);
        db.ChangeTracker.Clear();

        var preview = await Service(db).PreviewAsync(new OpeningStockInput("preview", Day, null,
        [
            new OpeningStockLineInput(1, m.Item, m.Warehouse, m.LocationA, 2m, 1.25m),
            new OpeningStockLineInput(2, 999_999_999, m.Warehouse, null, 1m, 0m),
            new OpeningStockLineInput(3, m.Other, m.Warehouse, m.LocationB, 1m, 0m),
            new OpeningStockLineInput(4, m.Item, m.Warehouse, m.LocationA, 5m, 1m),
            new OpeningStockLineInput(5, inactive.Id, m.Second, null, 1m, 0m),
            new OpeningStockLineInput(6, m.Other, 999_999_999, null, 1m, 0m),
        ]), Ct);

        Assert.Equal(5, preview.ErrorCount);
        Assert.Equal((11m, 7.5m), (preview.TotalQuantity, preview.TotalAmount));
        var byLine = preview.Lines.ToDictionary(x => x.LineNo);
        Assert.Empty(byLine[1].Errors);
        Assert.Equal(2.5m, byLine[1].Amount);
        Assert.Equal(new OpeningStockLineError("itemId", "MD.ITEM.NOT_FOUND"), Assert.Single(byLine[2].Errors));
        Assert.Equal(new OpeningStockLineError("locationId", "MD.STORAGE_LOCATION.NOT_FOUND"), Assert.Single(byLine[3].Errors));
        Assert.Equal(new OpeningStockLineError("line", OpeningStockService.DuplicateInBatch), Assert.Single(byLine[4].Errors));
        Assert.Equal(new OpeningStockLineError("itemId", "MD.ITEM.INACTIVE"), Assert.Single(byLine[5].Errors));
        Assert.Equal(new OpeningStockLineError("warehouseId", "MD.WAREHOUSE.NOT_FOUND"), Assert.Single(byLine[6].Errors));
        Assert.False(await db.Set<OpeningStock>().AnyAsync(Ct));
        Assert.False(await db.Set<StockMovement>().AnyAsync(Ct));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(Ct));
    }

    [Fact]
    public async Task SubmitPostsOnceAndReplaysTheSameRequest()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var service = Service(db);

        var first = await service.SubmitAsync(Input(m), Ct);
        Assert.False(first.Replayed);
        var batch = first.OpeningStock;
        Assert.Equal(("batch-1", Day, "Go-live", Now, 2), (batch.RequestKey, batch.OpeningDate, batch.Remark, batch.PostedAt, batch.Lines.Count));
        Assert.Equal(126.5432m, batch.TotalAmount);
        var movements = await db.Set<StockMovement>().AsNoTracking().OrderBy(x => x.SourceLineNo).ToListAsync(Ct);
        Assert.All(movements, x => Assert.Equal((OpeningStockService.SourceType, batch.Id, StockMovementDirection.In, Day),
            (x.SourceType, x.SourceId, x.Direction, x.BusinessDate)));
        Assert.Equal([12.5m, 0m], movements.Select(x => x.UnitCost!.Value));
        Assert.Equal(10.123456m, await OnHandAsync(db, m.Item, m.Warehouse, m.LocationA));
        Assert.Equal(3m, await OnHandAsync(db, m.Other, m.Second, null));

        // 網絡重送：同一請求鍵與內容回原批次，不再入庫
        db.ChangeTracker.Clear();
        var replay = await service.SubmitAsync(Input(m), Ct);
        Assert.Equal((true, batch.Id), (replay.Replayed, replay.OpeningStock.Id));
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(Ct));

        var changed = Input(m) with { Remark = "Different" };
        Assert.Equal(OpeningStockService.RequestConflict,
            (await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(changed, Ct))).ErrorCode);

        // 新請求鍵重複登記同一結存鍵：整批拒絕且不留資料
        var again = new OpeningStockInput("batch-2", Day, null,
            [new OpeningStockLineInput(1, m.Item, m.Second, m.LocationB, 1m, 1m), new OpeningStockLineInput(2, m.Other, m.Second, null, 1m, 1m)]);
        Assert.Equal(OpeningStockService.HasErrors,
            (await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(again, Ct))).ErrorCode);
        var preview = await service.PreviewAsync(again, Ct);
        Assert.Empty(preview.Lines[0].Errors);
        Assert.Equal(new OpeningStockLineError("line", OpeningStockService.Duplicate), Assert.Single(preview.Lines[1].Errors));
        Assert.Equal(1, await db.Set<OpeningStock>().CountAsync(Ct));
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(Ct));

        var page = await service.ListAsync(new OpeningStockQuery(), Ct);
        var summary = Assert.Single(page.Items);
        Assert.Equal((batch.Id, 2, 13.123456m, 126.5432m), (summary.Id, summary.LineCount, summary.TotalQuantity, summary.TotalAmount));
        Assert.Equal(1, (await service.ListAsync(new OpeningStockQuery { ItemId = m.Other }, Ct)).TotalCount);
        Assert.Equal(0, (await service.ListAsync(new OpeningStockQuery { WarehouseId = m.Second, ItemId = m.Item }, Ct)).TotalCount);
        Assert.Equal(0, (await service.ListAsync(new OpeningStockQuery { From = Day.AddDays(1) }, Ct)).TotalCount);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.ListAsync(new OpeningStockQuery { From = Day.AddDays(1), To = Day }, Ct));
        var detail = await service.GetAsync(batch.Id, Ct);
        Assert.Equal([1, 2], detail.Lines.Select(x => x.LineNo));
        Assert.Equal(126.5432m, detail.Lines[0].Amount);
    }

    [Fact]
    public async Task OpeningDateMustPrecedeExistingMovementsAndNotBeInTheFuture()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        await Posting(db).PostAsync(new InventoryPostingRequest("EARLY", 1, Day.AddDays(-1),
            [new InventoryPostingLine(1, StockDirection.In, m.Item, m.Warehouse, m.LocationA, 1m, 1m)]), Ct);
        db.ChangeTracker.Clear();
        var service = Service(db);

        var future = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.PreviewAsync(Input(m, date: DateOnly.FromDateTime(Now).AddDays(1)), Ct));
        Assert.Contains("openingDate", future.Errors.Keys);

        var preview = await service.PreviewAsync(Input(m), Ct);
        Assert.Equal(new OpeningStockLineError("openingDate", OpeningStockService.AfterMovement), Assert.Single(preview.Lines[0].Errors));
        Assert.Empty(preview.Lines[1].Errors);

        var posted = await service.SubmitAsync(Input(m, date: Day.AddDays(-1)), Ct);
        Assert.False(posted.Replayed);
        Assert.Equal(11.123456m, await OnHandAsync(db, m.Item, m.Warehouse, m.LocationA));
    }

    public static TheoryData<string, OpeningStockInput> InvalidInputs => new()
    {
        { "requestKey", new("", Day, null, [new(1, 1, 1, null, 1m, 0m)]) },
        { "requestKey", new("bad key!", Day, null, [new(1, 1, 1, null, 1m, 0m)]) },
        { "requestKey", new(new string('k', 65), Day, null, [new(1, 1, 1, null, 1m, 0m)]) },
        { "requestKey", new("key\n", Day, null, [new(1, 1, 1, null, 1m, 0m)]) },
        { "openingDate", new("k", default, null, [new(1, 1, 1, null, 1m, 0m)]) },
        { "remark", new("k", Day, new string('r', 501), [new(1, 1, 1, null, 1m, 0m)]) },
        { "lines", new("k", Day, null, []) },
        { "lines", new("k", Day, null, [new(1, 1, 1, null, 1m, 0m), new(1, 2, 1, null, 1m, 0m)]) },
        { "lines[0].lineNo", new("k", Day, null, [new(0, 1, 1, null, 1m, 0m)]) },
        { "lines[0].itemId", new("k", Day, null, [new(1, 0, 1, null, 1m, 0m)]) },
        { "lines[0].warehouseId", new("k", Day, null, [new(1, 1, 0, null, 1m, 0m)]) },
        { "lines[0].locationId", new("k", Day, null, [new(1, 1, 1, 0, 1m, 0m)]) },
        { "lines[0].quantity", new("k", Day, null, [new(1, 1, 1, null, 0m, 0m)]) },
        { "lines[0].quantity", new("k", Day, null, [new(1, 1, 1, null, 0.0000001m, 0m)]) },
        { "lines[0].unitCost", new("k", Day, null, [new(1, 1, 1, null, 1m, -1m)]) },
        { "lines[0].unitCost", new("k", Day, null, [new(1, 1, 1, null, 1m, 10_000_000_000_000m)]) },
        { "lines", new("k", Day, null, [new(1, 1, 1, null, 9_999_999_999_999m, 9_999_999_999_999m)]) },
    };

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task MalformedRequestsAreRejectedBeforeAnyWrite(string field, OpeningStockInput input)
    {
        await using var db = Context(TestCompanies.Next());
        var service = Service(db);

        Assert.Contains(field, (await Assert.ThrowsAsync<RequestValidationException>(() => service.PreviewAsync(input, Ct))).Errors.Keys);
        Assert.Contains(field, (await Assert.ThrowsAsync<RequestValidationException>(() => service.SubmitAsync(input, Ct))).Errors.Keys);
        Assert.False(await db.Set<OpeningStock>().AnyAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentSubmissionsForTheSameKeyPostOnlyOnce()
    {
        var companyId = TestCompanies.Next();
        await using (var setup = Context(companyId))
        {
            var m = await SetupAsync(setup, companyId);
            await using var a = Context(companyId);
            await using var b = Context(companyId);
            var first = Task.Run(() => Capture(Service(a).SubmitAsync(Input(m, "concurrent-a"), Ct)), Ct);
            var second = Task.Run(() => Capture(Service(b).SubmitAsync(Input(m, "concurrent-b"), Ct)), Ct);
            var outcomes = await Task.WhenAll(first, second);

            Assert.Single(outcomes, x => x is null);
            Assert.Single(outcomes, x => x is BusinessRuleException { ErrorCode: OpeningStockService.HasErrors });
            await using var check = Context(companyId);
            Assert.Equal(1, await check.Set<OpeningStock>().CountAsync(Ct));
            Assert.Equal(10.123456m, await OnHandAsync(check, m.Item, m.Warehouse, m.LocationA));
        }

        // 公司隔離：另一公司看不到批次
        await using var other = Context(TestCompanies.Next());
        Assert.Equal(0, (await Service(other).ListAsync(new OpeningStockQuery(), Ct)).TotalCount);
        await using var none = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(none).ListAsync(new OpeningStockQuery(), Ct));

        static async Task<Exception?> Capture(Task task)
        {
            try { await task; return null; }
            catch (Exception ex) { return ex; }
        }
    }

    [Fact]
    public async Task DatabaseRejectsASecondOpeningLineForTheSameBalanceKey()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var posted = (await Service(db).SubmitAsync(Input(m), Ct)).OpeningStock;
        db.ChangeTracker.Clear();

        // 無儲位的鍵（location_id NULL）同樣受唯一索引保護
        db.Add(new OpeningStockLine { CompanyId = companyId, OpeningStockId = posted.Id, LineNo = 9, ItemId = m.Other,
            WarehouseId = m.Second, LocationId = null, Quantity = 1m, UnitCost = 0m, Amount = 0m });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("ux_inv_opening_stock_line_balance_key", error.InnerException!.Message, StringComparison.Ordinal);
    }

    private sealed record Master(long Item, long Other, long Uom, long Warehouse, long Second, long LocationA, long LocationB);
}
