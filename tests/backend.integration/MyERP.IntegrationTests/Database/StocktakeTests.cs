using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-STK-001～004、007（010a1 範圍）：快照、範圍互斥、實盤保存、重新快照與刪除。</summary>
public sealed class StocktakeTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 10, 14);
    private static readonly DateTime Now = new(2026, 10, 14, 1, 10, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemCategoryDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel(), new StocktakeDbModel()], company);
    }

    private static StocktakeService Service(MyErpDbContext db, FakeNumbers? numbers = null, StocktakeLimits? limits = null) =>
        new(db, numbers ?? new FakeNumbers(), new InventoryReferenceGuard(db), new ItemCategoryScope(db), new FakeWorkflow(),
            new FixedClock(Now), limits);

    private static StockPostingService Posting(MyErpDbContext db) =>
        new(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));

    private static Task<InventoryPostingResult> Post(MyErpDbContext db, long sourceId, params InventoryPostingLine[] lines) =>
        Posting(db).PostAsync(new InventoryPostingRequest("SEED", sourceId, Day, lines), Ct);

    private static InventoryPostingLine In(int no, long item, long warehouse, long? location, decimal quantity,
        params InventoryTrackingLine[] tracking) =>
        new(no, StockDirection.In, item, warehouse, location, quantity, null, tracking.Length == 0 ? null : tracking);

    private static InventoryPostingLine Out(int no, long item, long warehouse, long? location, decimal quantity,
        params InventoryTrackingLine[] tracking) =>
        new(no, StockDirection.Out, item, warehouse, location, quantity, null, tracking.Length == 0 ? null : tracking);

    private sealed record Master(long Uom, long Warehouse, long Location, long OtherWarehouse);

    private static async Task<Master> SetupAsync(MyErpDbContext db, long companyId)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var other = await new WarehouseService(db).CreateAsync("X" + companyId, "Other", null, null, null, Ct);
        var location = await new StorageLocationService(db).CreateAsync(warehouse.Id, "L" + companyId, "L", Ct);
        return new Master(uom.Id, warehouse.Id, location.Id, other.Id);
    }

    private static async Task<long> ItemAsync(MyErpDbContext db, string code, long uom, long? category = null,
        ItemTrackingMode mode = ItemTrackingMode.None) =>
        (await new ItemService(db).CreateAsync(code, code, null, uom, null, category, mode, Ct)).Id;

    [Fact]
    public async Task SnapshotCapturesPositiveKeysWithinScopeIncludingSubcategoriesAndTracking()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var categories = new ItemCategoryService(db);
        var root = await categories.CreateAsync("R" + companyId, "Root", null, Ct);
        var child = await categories.CreateAsync("C" + companyId, "Child", root.Id, Ct);
        var outside = await categories.CreateAsync("O" + companyId, "Outside", null, Ct);
        var a = await ItemAsync(db, "A" + companyId, m.Uom, child.Id);
        var b = await ItemAsync(db, "B" + companyId, m.Uom, outside.Id);
        var lot = await ItemAsync(db, "LOT" + companyId, m.Uom, child.Id, ItemTrackingMode.Lot);
        var serial = await ItemAsync(db, "SN" + companyId, m.Uom, root.Id, ItemTrackingMode.Serial);
        var zero = await ItemAsync(db, "Z" + companyId, m.Uom, child.Id);
        var elsewhere = await ItemAsync(db, "E" + companyId, m.Uom, child.Id);
        await Post(db, 1, In(1, a, m.Warehouse, null, 5m), In(2, a, m.Warehouse, m.Location, 3m), In(3, b, m.Warehouse, null, 4m),
            In(4, lot, m.Warehouse, m.Location, 3m, InventoryTrackingLine.Lot("L-A", 2m), InventoryTrackingLine.Lot("L-B", 1m)),
            In(5, serial, m.Warehouse, null, 2m, InventoryTrackingLine.Serial("S1"), InventoryTrackingLine.Serial("S2")),
            In(6, zero, m.Warehouse, null, 2m), In(7, elsewhere, m.OtherWarehouse, null, 6m));
        await Post(db, 2, Out(1, zero, m.Warehouse, null, 2m));
        var maxId = await db.Set<StockMovement>().MaxAsync(x => x.Id, Ct);
        var movementCount = await db.Set<StockMovement>().CountAsync(Ct);

        var service = Service(db);
        var created = await service.CreateAsync(new StocktakeInput(Day, " Year end ",
            new StocktakeScope([m.Warehouse, m.Warehouse], [root.Id])), Ct);

        var st = created.Stocktake;
        Assert.Equal(InventoryDocumentStatus.Draft, st.Status);
        Assert.Equal("Year end", st.Remark);
        Assert.Equal(maxId, st.SnapshotMovementId);
        Assert.Equal(Now, st.SnapshotAt);
        Assert.Equal([m.Warehouse], created.Scope.WarehouseIds);
        Assert.Equal(movementCount, await db.Set<StockMovement>().CountAsync(Ct));
        db.ChangeTracker.Clear();
        var detail = (await service.GetAsync(st.Id, Ct)).Stocktake;
        var lines = detail.Lines.OrderBy(x => x.LineNo).Select(x => (x.LineNo, x.ItemId, x.LocationId, x.BookQuantity,
            x.CountedQuantity, x.TrackingMode)).ToList();
        Assert.Equal(
        [
            (1, a, (long?)null, 5m, (decimal?)null, "None"), (2, a, m.Location, 3m, null, "None"),
            (3, lot, m.Location, 3m, null, "Lot"), (4, serial, null, 2m, null, "Serial"),
        ], lines);
        Assert.Equal([("L-A", 2m), ("L-B", 1m)],
            detail.Lines.Single(x => x.ItemId == lot).Tracking.OrderBy(x => x.SeqNo).Select(x => (x.LotNo!, x.BookQuantity)));
        Assert.Equal(["S1", "S2"], detail.Lines.Single(x => x.ItemId == serial).Tracking.Select(x => x.SerialNo!).Order());

        await service.DeleteAsync(st.Id, detail.Version, Ct);
        var intersect = await service.CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse], [root.Id], [a, b])), Ct);
        Assert.Equal([a, a], intersect.Stocktake.Lines.Select(x => x.ItemId));
        var itemsOnly = await service.CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse], null, [b])), Ct);
        Assert.Equal([(b, 4m)], itemsOnly.Stocktake.Lines.Select(x => (x.ItemId, x.BookQuantity)));
        var page = await service.ListAsync(new StocktakeQuery { Sort = "documentNo" }, Ct);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, x => Assert.Equal(1, x.WarehouseCount));
        Assert.Equal([1, 2], page.Items.Select(x => x.LineCount).Order());
    }

    [Fact]
    public async Task ScopeIsValidatedAgainstMasterDataAndLimitsBeforeNumbering()
    {
        var companyId = TestCompanies.Next();
        var foreignCompanyId = TestCompanies.Next();
        await using var db = Context(companyId);
        await using var foreign = Context(foreignCompanyId);
        var m = await SetupAsync(db, companyId);
        var foreignWarehouse = await new WarehouseService(foreign).CreateAsync("W" + foreignCompanyId, "Foreign", null, null, null, Ct);
        var foreignCategory = await new ItemCategoryService(foreign).CreateAsync("C" + foreignCompanyId, "Foreign", null, Ct);
        var category = await new ItemCategoryService(db).CreateAsync("C" + companyId, "Category", null, Ct);
        await new ItemCategoryService(db).UpdateAsync(category.Id, "Category", false, null, category.Version, Ct);
        var inactive = (await new WarehouseService(db).CreateAsync("I" + companyId, "Inactive", null, null, null, Ct));
        await new WarehouseService(db).UpdateAsync(inactive.Id, "Inactive", null, null, null, false, inactive.Version, Ct);
        var a = await ItemAsync(db, "A" + companyId, m.Uom);
        var b = await ItemAsync(db, "B" + companyId, m.Uom);
        var off = await new ItemService(db).CreateAsync("OFF" + companyId, "Off", null, m.Uom, null, null, Ct);
        await new ItemService(db).UpdateAsync(off.Id, "Off", null, m.Uom, null, null, false, off.Version, Ct);
        var lot = await ItemAsync(db, "LOT" + companyId, m.Uom, null, ItemTrackingMode.Lot);
        await Post(db, 1, In(1, a, m.Warehouse, null, 1m), In(2, b, m.Warehouse, null, 1m),
            In(3, lot, m.OtherWarehouse, null, 2m, InventoryTrackingLine.Lot("L1", 1m), InventoryTrackingLine.Lot("L2", 1m)));
        var numbers = new FakeNumbers();
        var service = Service(db, numbers);
        Task<StocktakeDetail> Create(StocktakeScope scope, StocktakeService? s = null) =>
            (s ?? service).CreateAsync(new StocktakeInput(Day, null, scope), Ct);

        Assert.Equal("MD.WAREHOUSE.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            Create(new StocktakeScope([foreignWarehouse.Id])))).ErrorCode);
        Assert.Equal("MD.WAREHOUSE.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(new StocktakeScope([inactive.Id])))).ErrorCode);
        Assert.Equal("MD.ITEM_CATEGORY.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            Create(new StocktakeScope([m.Warehouse], [foreignCategory.Id])))).ErrorCode);
        Assert.Equal("MD.ITEM_CATEGORY.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(new StocktakeScope([m.Warehouse], [category.Id])))).ErrorCode);
        Assert.Equal("MD.ITEM.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(new StocktakeScope([m.Warehouse], null, [off.Id])))).ErrorCode);
        Assert.Contains("scope.warehouseIds", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            Create(new StocktakeScope([])))).Errors.Keys);
        Assert.Contains("scope.warehouseIds", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            Create(new StocktakeScope([.. Enumerable.Range(1, 21).Select(x => (long)x)])))).Errors.Keys);
        Assert.Contains("scope.categoryIds", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            Create(new StocktakeScope([m.Warehouse], [.. Enumerable.Range(1, 51).Select(x => (long)x)])))).Errors.Keys);
        Assert.Contains("scope.itemIds", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            Create(new StocktakeScope([m.Warehouse], null, [.. Enumerable.Range(1, 501).Select(x => (long)x)])))).Errors.Keys);
        Assert.Equal(StocktakeService.TooManyLines, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(new StocktakeScope([m.Warehouse]), Service(db, numbers, new StocktakeLimits(MaxLines: 1))))).ErrorCode);
        Assert.Equal(StocktakeService.TooManyLines, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(new StocktakeScope([m.OtherWarehouse]), Service(db, numbers, new StocktakeLimits(MaxTrackingEntries: 1))))).ErrorCode);
        Assert.Equal(0, numbers.Count);
        Assert.False(await db.Set<Stocktake>().AnyAsync(Ct));

        var first = await Create(new StocktakeScope([m.Warehouse], null, [a]));
        Assert.Equal(StocktakeService.ScopeInUse, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(new StocktakeScope([m.Warehouse])))).ErrorCode);
        Assert.Equal(1, numbers.Count);
        var second = await Create(new StocktakeScope([m.Warehouse], null, [b]));
        Assert.Equal([b], second.Stocktake.Lines.Select(x => x.ItemId));
        await service.DeleteAsync(first.Stocktake.Id, first.Stocktake.Version, Ct);
        Assert.Equal([a], (await Create(new StocktakeScope([m.Warehouse], null, [a]))).Stocktake.Lines.Select(x => x.ItemId));
    }

    [Fact]
    public async Task ConcurrentCreationForSameScopeLetsOnlyOneSucceed()
    {
        var companyId = TestCompanies.Next();
        await using (var setup = Context(companyId))
        {
            var m = await SetupAsync(setup, companyId);
            var a = await ItemAsync(setup, "A" + companyId, m.Uom);
            await Post(setup, 1, In(1, a, m.Warehouse, null, 1m));
            await using var left = Context(companyId);
            await using var right = Context(companyId);
            var outcomes = await Task.WhenAll(Create(left, m.Warehouse), Create(right, m.Warehouse));
            Assert.Single(outcomes, x => x is null);
            Assert.Single(outcomes, x => x is BusinessRuleException { ErrorCode: StocktakeService.ScopeInUse });
        }
        await using var check = Context(companyId);
        Assert.Equal(1, await check.Set<Stocktake>().CountAsync(Ct));

        static async Task<Exception?> Create(MyErpDbContext db, long warehouse)
        {
            try
            {
                await Service(db).CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([warehouse])), Ct);
                return null;
            }
            catch (Exception ex) { return ex; }
        }
    }

    [Fact]
    public async Task CountsAreSavedAsAWholeWithAddedLinesTrackingAndVersionChecks()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var a = await ItemAsync(db, "A" + companyId, m.Uom);
        var found = await ItemAsync(db, "F" + companyId, m.Uom);
        var other = await ItemAsync(db, "O" + companyId, m.Uom);
        var lot = await ItemAsync(db, "LOT" + companyId, m.Uom, null, ItemTrackingMode.Lot);
        var serial = await ItemAsync(db, "SN" + companyId, m.Uom, null, ItemTrackingMode.Serial);
        await Post(db, 1, In(1, a, m.Warehouse, null, 5m), In(2, lot, m.Warehouse, m.Location, 2m, InventoryTrackingLine.Lot("L-A", 2m)),
            In(3, serial, m.Warehouse, null, 1m, InventoryTrackingLine.Serial("S1")), In(4, other, m.Warehouse, null, 1m));
        var service = Service(db);
        var draft = (await service.CreateAsync(new StocktakeInput(Day, null,
            new StocktakeScope([m.Warehouse], null, [a, lot, serial])), Ct)).Stocktake;
        await service.CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse], null, [other])), Ct);
        long LineOf(long item) => draft.Lines.Single(x => x.ItemId == item).Id;
        StocktakeCountInput Counts(params StocktakeCountLine[] lines) => new(Day.AddDays(1), "Counted", lines);
        var full = Counts(
            new StocktakeCountLine(LineOf(a), 4.123456m),
            new StocktakeCountLine(LineOf(lot), 3m, [new("l-a", null, 2m), new("L-NEW", null, 1m)]),
            new StocktakeCountLine(LineOf(serial), 0m, [new(null, "S1", 0m)]),
            new StocktakeCountLine(null, 1m, null, found, m.Warehouse, m.Location));

        async Task<RequestValidationException> Invalid(StocktakeCountInput input) =>
            await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateAsync(draft.Id, input, draft.Version, Ct));
        Assert.Contains("lines[0].countedQuantity", (await Invalid(Counts(new StocktakeCountLine(LineOf(a), 1.0000001m)))).Errors.Keys);
        Assert.Contains("lines[0].countedQuantity", (await Invalid(Counts(new StocktakeCountLine(LineOf(a), -1m)))).Errors.Keys);
        Assert.Contains("lines[0].tracking[0].countedQuantity", (await Invalid(Counts(
            new StocktakeCountLine(LineOf(serial), 1m, [new(null, "S1", 2m)])))).Errors.Keys);
        Assert.Contains("lines[0].countedQuantity", (await Invalid(Counts(
            new StocktakeCountLine(LineOf(lot), 5m, [new("L-A", null, 2m)])))).Errors.Keys);
        Assert.Contains("lines[0].countedQuantity", (await Invalid(Counts(new StocktakeCountLine(LineOf(lot), 0m)))).Errors.Keys);
        Assert.Contains("lines[0].tracking", (await Invalid(Counts(new StocktakeCountLine(LineOf(a), 1m, [new("X", null, 1m)])))).Errors.Keys);
        Assert.Contains("lines[0].lineId", (await Invalid(Counts(new StocktakeCountLine(999_999_999, 1m)))).Errors.Keys);
        Assert.Contains("lines", (await Invalid(Counts(new StocktakeCountLine(LineOf(a), 1m), new StocktakeCountLine(LineOf(a), 2m)))).Errors.Keys);
        Assert.Contains("lines[0].itemId", (await Invalid(Counts(new StocktakeCountLine(null, 1m, null, a, m.Warehouse)))).Errors.Keys);
        Assert.Equal(StocktakeService.LineOutOfScope, (await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(draft.Id,
            Counts(new StocktakeCountLine(null, 1m, null, found, m.OtherWarehouse)), draft.Version, Ct))).ErrorCode);
        Assert.Equal(StocktakeService.ScopeInUse, (await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(draft.Id,
            Counts(new StocktakeCountLine(null, 1m, null, other, m.Warehouse, m.Location)), draft.Version, Ct))).ErrorCode);

        var firstVersion = draft.Version;
        var saved = (await service.UpdateAsync(draft.Id, full, firstVersion, Ct)).Stocktake;
        Assert.Equal(Day.AddDays(1), saved.BusinessDate);
        db.ChangeTracker.Clear();
        var reread = (await service.GetAsync(draft.Id, Ct)).Stocktake;
        Assert.Equal(4.123456m, reread.Lines.Single(x => x.ItemId == a).CountedQuantity);
        var lotLine = reread.Lines.Single(x => x.ItemId == lot);
        Assert.Equal([("L-A", 2m, (decimal?)2m), ("L-NEW", 0m, 1m)],
            lotLine.Tracking.OrderBy(x => x.SeqNo).Select(x => (x.LotNo!, x.BookQuantity, x.CountedQuantity)));
        Assert.Equal(0m, reread.Lines.Single(x => x.ItemId == serial).Tracking.Single().CountedQuantity);
        var added = reread.Lines.Single(x => x.ItemId == found);
        Assert.True(added.IsAdded);
        Assert.Equal((0m, (decimal?)1m, 4, (long?)m.Location), (added.BookQuantity, added.CountedQuantity, added.LineNo, added.LocationId));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(draft.Id, full, firstVersion, Ct));

        var partial = (await service.UpdateAsync(draft.Id, Counts(new StocktakeCountLine(LineOf(lot), null, [new("L-A", null, 1m)])),
            reread.Version, Ct)).Stocktake;
        db.ChangeTracker.Clear();
        var after = (await service.GetAsync(draft.Id, Ct)).Stocktake;
        Assert.Equal(3, after.Lines.Count);
        Assert.DoesNotContain(after.Lines, x => x.ItemId == found);
        Assert.Null(after.Lines.Single(x => x.ItemId == a).CountedQuantity);
        Assert.Equal([("L-A", (decimal?)1m)], after.Lines.Single(x => x.ItemId == lot).Tracking.Select(x => (x.LotNo!, x.CountedQuantity)));
        Assert.Equal(partial.Version, after.Version);
        var summary = Assert.Single((await service.ListAsync(new StocktakeQuery { Keyword = draft.DocumentNo }, Ct)).Items);
        Assert.Equal((3, 0), (summary.LineCount, summary.CountedLineCount));
    }

    [Fact]
    public async Task RefreshSnapshotUpdatesBookKeepsCountsFlagsRecountAndAddsOrRemovesKeys()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var a = await ItemAsync(db, "A" + companyId, m.Uom);
        var gone = await ItemAsync(db, "G" + companyId, m.Uom);
        var kept = await ItemAsync(db, "K" + companyId, m.Uom);
        var same = await ItemAsync(db, "S" + companyId, m.Uom);
        var fresh = await ItemAsync(db, "N" + companyId, m.Uom);
        var lot = await ItemAsync(db, "LOT" + companyId, m.Uom, null, ItemTrackingMode.Lot);
        await Post(db, 1, In(1, a, m.Warehouse, null, 5m), In(2, gone, m.Warehouse, null, 2m), In(3, kept, m.Warehouse, null, 1m),
            In(4, same, m.Warehouse, null, 3m), In(5, lot, m.Warehouse, null, 2m, InventoryTrackingLine.Lot("L-A", 2m)));
        var service = Service(db);
        var draft = (await service.CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse])), Ct)).Stocktake;
        var staleVersion = draft.Version;
        long LineOf(long item) => draft.Lines.Single(x => x.ItemId == item).Id;
        var counted = (await service.UpdateAsync(draft.Id, new StocktakeCountInput(Day, null,
        [
            new StocktakeCountLine(LineOf(a), 5m), new StocktakeCountLine(LineOf(kept), 1m), new StocktakeCountLine(LineOf(same), 3m),
            new StocktakeCountLine(LineOf(lot), 2m, [new("L-A", null, 2m)]),
        ]), draft.Version, Ct)).Stocktake;
        await Post(db, 2, In(1, a, m.Warehouse, null, 1m), Out(2, gone, m.Warehouse, null, 2m), Out(3, kept, m.Warehouse, null, 1m),
            In(4, fresh, m.Warehouse, null, 3m), In(5, lot, m.Warehouse, null, 1m, InventoryTrackingLine.Lot("L-B", 1m)));
        var maxId = await db.Set<StockMovement>().MaxAsync(x => x.Id, Ct);

        await Assert.ThrowsAsync<ConflictException>(() => service.RefreshSnapshotAsync(draft.Id, staleVersion, Ct));
        await service.RefreshSnapshotAsync(draft.Id, counted.Version, Ct);
        db.ChangeTracker.Clear();
        var refreshed = (await service.GetAsync(draft.Id, Ct)).Stocktake;
        Assert.Equal(maxId, refreshed.SnapshotMovementId);
        var rows = refreshed.Lines.OrderBy(x => x.LineNo)
            .Select(x => (x.ItemId, x.BookQuantity, x.CountedQuantity, x.RecountRequired)).ToList();
        Assert.Equal(
        [
            (a, 6m, (decimal?)5m, true), (kept, 0m, 1m, true), (same, 3m, 3m, false), (lot, 3m, 2m, true), (fresh, 3m, null, false),
        ], rows);
        Assert.Equal([("L-A", 2m, (decimal?)2m), ("L-B", 1m, null)], refreshed.Lines.Single(x => x.ItemId == lot).Tracking
            .OrderBy(x => x.SeqNo).Select(x => (x.LotNo!, x.BookQuantity, x.CountedQuantity)));
        Assert.Equal(6, refreshed.Lines.Single(x => x.ItemId == fresh).LineNo);
    }

    [Fact]
    public async Task OnlyDraftCanBeRefreshedOrDeletedAndRejectedRevisionReturnsToDraft()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var a = await ItemAsync(db, "A" + companyId, m.Uom);
        await Post(db, 1, In(1, a, m.Warehouse, null, 2m));
        var service = Service(db);
        var draft = (await service.CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse])), Ct)).Stocktake;
        var counts = new StocktakeCountInput(Day, null, [new StocktakeCountLine(draft.Lines[0].Id, 2m)]);

        await SetStatusAsync(db, draft.Id, "PendingApproval");
        var pending = (await service.GetAsync(draft.Id, Ct)).Stocktake;
        Assert.Equal(StocktakeService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(draft.Id, counts, pending.Version, Ct))).ErrorCode);
        Assert.Equal(StocktakeService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.RefreshSnapshotAsync(draft.Id, pending.Version, Ct))).ErrorCode);
        Assert.Equal(StocktakeService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, pending.Version, Ct))).ErrorCode);

        await SetStatusAsync(db, draft.Id, "Rejected");
        var rejected = (await service.GetAsync(draft.Id, Ct)).Stocktake;
        Assert.Equal(StocktakeService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.RefreshSnapshotAsync(draft.Id, rejected.Version, Ct))).ErrorCode);
        Assert.Equal(StocktakeService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, rejected.Version, Ct))).ErrorCode);
        Assert.Equal(StocktakeService.ScopeInUse, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse])), Ct))).ErrorCode);
        var revised = (await service.UpdateAsync(draft.Id, counts, rejected.Version, Ct)).Stocktake;
        Assert.Equal(InventoryDocumentStatus.Draft, revised.Status);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(draft.Id, rejected.Version, Ct));
        await service.DeleteAsync(draft.Id, revised.Version, Ct);
        Assert.Equal(StocktakeService.NotFound, (await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(draft.Id, Ct))).ErrorCode);
        Assert.Equal(StocktakeService.NotFound, (await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RefreshSnapshotAsync(draft.Id, revised.Version + 1, Ct))).ErrorCode);
        Assert.True(await db.Set<Stocktake>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .AnyAsync(x => x.Id == draft.Id && x.DocumentNo == draft.DocumentNo && x.IsDeleted, Ct));
        Assert.Single((await service.CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse])), Ct)).Stocktake.Lines);
        await SetStatusAsync(db, (await db.Set<Stocktake>().MaxAsync(x => x.Id, Ct)), "Posted", posted: true);
        Assert.Single((await service.CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse])), Ct)).Stocktake.Lines);
    }

    [Fact]
    public async Task DatabaseRejectsAddedLineWithBookQuantityAndOverCountedSerial()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var serial = await ItemAsync(db, "SN" + companyId, m.Uom, null, ItemTrackingMode.Serial);
        await Post(db, 1, In(1, serial, m.Warehouse, null, 1m, InventoryTrackingLine.Serial("S1")));
        var draft = (await Service(db).CreateAsync(new StocktakeInput(Day, null, new StocktakeScope([m.Warehouse])), Ct)).Stocktake;
        var line = draft.Lines.Single();

        var added = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO inv_stocktake_line (stocktake_id, line_no, item_id, warehouse_id, location_id, tracking_mode, book_quantity, is_added, recount_required, company_id, created_at, created_by, version) VALUES ({draft.Id}, {2}, {serial}, {m.Warehouse}, {m.Location}, {"None"},{1m}, {true}, {false}, {companyId}, {Now}, {7L}, {1})", Ct));
        Assert.Equal(547, added.Number);
        var overCounted = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inv_stocktake_line_tracking SET counted_quantity = {2m} WHERE line_id = {line.Id}", Ct));
        Assert.Equal(547, overCounted.Number);
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO inv_stocktake_line_tracking (line_id, seq_no, serial_no, book_quantity, company_id, created_at, created_by, version) VALUES ({line.Id}, {2}, {"s1"}, {0m}, {companyId}, {Now}, {7L}, {1})", Ct));
        Assert.Equal(2601, duplicate.Number);
    }

    private static Task<int> SetStatusAsync(MyErpDbContext db, long id, string status, bool posted = false)
    {
        db.ChangeTracker.Clear();
        return posted
            ? db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inv_stocktake SET status = {status}, posted_at = {Now}, version = version + 1 WHERE id = {id}", Ct)
            : db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inv_stocktake SET status = {status}, version = version + 1 WHERE id = {id}", Ct);
    }

    private sealed class FakeNumbers : INumberGenerator
    {
        private int count;
        public int Count => count;
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default) =>
            Task.FromResult($"ST-{Guid.NewGuid():N}"[..20] + Interlocked.Increment(ref count));
    }

    private sealed class FakeWorkflow : IApprovalWorkflowService
    {
        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalState?>(null);
    }
}
