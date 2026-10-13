using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-TRF-001/003/004/007：調撥守恆、庫存不足回滾、批序號與重放。</summary>
public sealed class TransferTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 10, 14);
    private static readonly DateTime Now = new(2026, 10, 14, 0, 1, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel(), new TransferDbModel()], company);
    }

    [Fact]
    public async Task ApprovedTransferWritesBalancedPairAndReplayDoesNotDuplicate()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", 1, Day,
            [new InventoryPostingLine(1, StockDirection.In, item.Id, warehouse.Id, a.Id, 5m)]), Ct);

        var service = new TransferService(db, new FakeNumbers(), new InventoryReferenceGuard(db),
            new FakeWorkflow { Next = ApprovalStatus.Approved }, posting, new FixedClock(Now));
        var draft = await service.CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, item.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 3m)]), Ct);
        Assert.Single(await db.Set<StockMovement>().Where(x => x.SourceType == "SEED").ToListAsync(Ct));
        var result = await service.SubmitAsync(draft.Id, draft.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Posted, result.Transfer.Status);
        var movements = await db.Set<StockMovement>().Where(x => x.SourceType == TransferService.SourceType)
            .OrderBy(x => x.Direction).ToListAsync(Ct);
        Assert.Equal(2, movements.Count);
        Assert.Equal(0m, movements.Sum(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity));
        Assert.Equal(2m, await new StockBalanceQuery(db).GetOnHandAsync(item.Id, warehouse.Id, a.Id, Ct));
        Assert.Equal(3m, await new StockBalanceQuery(db).GetOnHandAsync(item.Id, warehouse.Id, b.Id, Ct));
        Assert.Equal(TransferService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        Assert.Equal(TransferService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, result.Transfer.Version, Ct))).ErrorCode);
        Assert.Equal(TransferService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(draft.Id, new TransferInput(Day, null,
                [new TransferLineInput(1, item.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 1m)]),
                result.Transfer.Version, Ct))).ErrorCode);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == TransferService.SourceType, Ct));
    }

    [Fact]
    public async Task CrossWarehouseTransferConservesCompanyQuantity()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var source = await new WarehouseService(db).CreateAsync("S" + companyId, "Source", null, null, null, Ct);
        var destination = await new WarehouseService(db).CreateAsync("D" + companyId, "Destination", null, null, null, Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", 6, Day,
            [new InventoryPostingLine(1, StockDirection.In, item.Id, source.Id, null, 7m)]), Ct);
        var service = new TransferService(db, new FakeNumbers(), new InventoryReferenceGuard(db), new FakeWorkflow(),
            posting, new FixedClock(Now));
        var draft = await service.CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, item.Id, source.Id, null, destination.Id, null, 3m)]), Ct);
        await service.SubmitAsync(draft.Id, draft.Version, Ct);
        Assert.Equal(4m, await new StockBalanceQuery(db).GetOnHandAsync(item.Id, source.Id, null, Ct));
        Assert.Equal(3m, await new StockBalanceQuery(db).GetOnHandAsync(item.Id, destination.Id, null, Ct));
        var pair = await db.Set<StockMovement>().Where(x => x.SourceType == TransferService.SourceType).ToListAsync(Ct);
        Assert.Equal(2, pair.Count);
        Assert.All(pair, x => Assert.Equal(3m, x.Quantity));
        Assert.Equal(0m, pair.Sum(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity));
    }

    [Fact]
    public async Task InsufficientSecondLineRollsBackBothDirectionsAndApproval()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        var numbers = new FakeNumbers();
        var emptyService = new TransferService(db, numbers, new InventoryReferenceGuard(db),
            new FakeWorkflow(), posting, new FixedClock(Now));
        var empty = await emptyService.CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, item.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 1m)]), Ct);
        Assert.Equal("INV.STOCK.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            emptyService.SubmitAsync(empty.Id, empty.Version, Ct))).ErrorCode);
        await posting.PostAsync(new InventoryPostingRequest("SEED", 2, Day,
            [new InventoryPostingLine(1, StockDirection.In, item.Id, warehouse.Id, a.Id, 5m)]), Ct);
        var service = new TransferService(db, numbers, new InventoryReferenceGuard(db),
            new FakeWorkflow(), posting, new FixedClock(Now));
        var draft = await service.CreateAsync(new TransferInput(Day, null,
        [
            new TransferLineInput(1, item.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 2m),
            new TransferLineInput(2, item.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 4m),
        ]), Ct);

        Assert.Equal("INV.STOCK.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(draft.Id, Ct)).Transfer.Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == TransferService.SourceType, Ct));
        Assert.Equal(5m, await new StockBalanceQuery(db).GetOnHandAsync(item.Id, warehouse.Id, a.Id, Ct));
        Assert.Equal(0m, await new StockBalanceQuery(db).GetOnHandAsync(item.Id, warehouse.Id, b.Id, Ct));
    }

    [Fact]
    public async Task LotAndSerialMoveUnchangedAndSameStockKeyIsRejectedBeforeNumbering()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var lot = await items.CreateAsync("L" + companyId, "Lot", null, uom.Id, null, null, ItemTrackingMode.Lot, Ct);
        var serial = await items.CreateAsync("S" + companyId, "Serial", null, uom.Id, null, null, ItemTrackingMode.Serial, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", 3, Day,
        [
            new InventoryPostingLine(1, StockDirection.In, lot.Id, warehouse.Id, a.Id, 4m, null,
                [InventoryTrackingLine.Lot("L1", 4m)]),
            new InventoryPostingLine(2, StockDirection.In, serial.Id, warehouse.Id, a.Id, 1m, null,
                [InventoryTrackingLine.Serial("S1")]),
        ]), Ct);
        var numbers = new FakeNumbers();
        var service = new TransferService(db, numbers, new InventoryReferenceGuard(db),
            new FakeWorkflow(), posting, new FixedClock(Now));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, lot.Id, warehouse.Id, a.Id, warehouse.Id, a.Id, 1m)]), Ct));
        Assert.Equal(0, numbers.Count);
        var incomplete = await service.CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, lot.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 1m)]), Ct);
        Assert.Equal("INV.TRACKING.REQUIRED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(incomplete.Id, incomplete.Version, Ct))).ErrorCode);
        var draft = await service.CreateAsync(new TransferInput(Day, null,
        [
            new TransferLineInput(1, lot.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 2m,
                [InventoryTrackingLine.Lot("L1", 2m)]),
            new TransferLineInput(2, serial.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 1m,
                [InventoryTrackingLine.Serial("S1")]),
        ]), Ct);
        await service.SubmitAsync(draft.Id, draft.Version, Ct);
        var movements = await db.Set<StockMovement>().Where(x => x.SourceType == TransferService.SourceType).ToListAsync(Ct);
        Assert.Equal(4, movements.Count);
        Assert.Equal(4, await db.Set<StockMovementTracking>()
            .CountAsync(x => x.Movement!.SourceType == TransferService.SourceType, Ct));
        Assert.Equal(2m, await new StockBalanceQuery(db).GetOnHandAsync(lot.Id, warehouse.Id, b.Id, Ct));
        Assert.Equal(1m, await new StockBalanceQuery(db).GetOnHandAsync(serial.Id, warehouse.Id, b.Id, Ct));
        var serialId = await db.Set<StockSerial>().Where(x => x.ItemId == serial.Id).Select(x => x.Id).SingleAsync(Ct);
        var trace = await new TrackingQueryService(db).SerialAsync(serialId, Ct);
        Assert.Equal([(warehouse.Id, (long?)b.Id, 1m)],
            trace.Locations.Select(x => (x.WarehouseId, x.LocationId, x.Quantity)));
        var missingLot = await service.CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, lot.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 1m,
                [InventoryTrackingLine.Lot("UNKNOWN", 1m)])]), Ct);
        Assert.Equal("INV.LOT.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(missingLot.Id, missingLot.Version, Ct))).ErrorCode);
        Assert.Equal(4, await db.Set<StockMovement>().CountAsync(x => x.SourceType == TransferService.SourceType, Ct));
    }

    [Fact]
    public async Task ConcurrentTransfersCannotConsumeSameSourceTwice()
    {
        var companyId = TestCompanies.Next();
        await using var setup = Context(companyId);
        var uom = await new UomService(setup).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(setup).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(setup).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var a = await new StorageLocationService(setup).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(setup).CreateAsync(warehouse.Id, "B" + companyId, "B", Ct);
        var c = await new StorageLocationService(setup).CreateAsync(warehouse.Id, "C" + companyId, "C", Ct);
        var posting = new StockPostingService(setup, new InventoryReferenceService(setup, new InventoryReferenceGuard(setup)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", 4, Day,
            [new InventoryPostingLine(1, StockDirection.In, item.Id, warehouse.Id, a.Id, 5m)]), Ct);
        var numbers = new FakeNumbers();
        var first = await new TransferService(setup, numbers, new InventoryReferenceGuard(setup), new FakeWorkflow(), posting,
            new FixedClock(Now)).CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, item.Id, warehouse.Id, a.Id, warehouse.Id, b.Id, 4m)]), Ct);
        var second = await new TransferService(setup, numbers, new InventoryReferenceGuard(setup), new FakeWorkflow(), posting,
            new FixedClock(Now)).CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, item.Id, warehouse.Id, a.Id, warehouse.Id, c.Id, 4m)]), Ct);
        await using var left = Context(companyId);
        await using var right = Context(companyId);
        var outcomes = await Task.WhenAll(Submit(left, first), Submit(right, second));
        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is BusinessRuleException { ErrorCode: "INV.STOCK.INSUFFICIENT" });
        await using var check = Context(companyId);
        Assert.Equal(1m, await new StockBalanceQuery(check).GetOnHandAsync(item.Id, warehouse.Id, a.Id, Ct));
        Assert.Equal(2, await check.Set<StockMovement>().CountAsync(x => x.SourceType == TransferService.SourceType, Ct));

        static async Task<Exception?> Submit(MyErpDbContext db, Transfer transfer)
        {
            try
            {
                var service = new TransferService(db, new FakeNumbers(), new InventoryReferenceGuard(db),
                    new FakeWorkflow(), new StockPostingService(db,
                        new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now)), new FixedClock(Now));
                await service.SubmitAsync(transfer.Id, transfer.Version, Ct);
                return null;
            }
            catch (Exception ex) { return ex; }
        }
    }

    [Fact]
    public async Task RejectedTransferMustBeRevisedBeforeResubmissionAndOnlyDraftCanBeDeleted()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        var numbers = new FakeNumbers();
        var pending = new TransferService(db, numbers, new InventoryReferenceGuard(db),
            new FakeWorkflow { Next = ApprovalStatus.Pending }, posting, new FixedClock(Now));
        var input = new TransferInput(Day, "Original", [new TransferLineInput(1, item.Id,
            warehouse.Id, a.Id, warehouse.Id, b.Id, 1m)]);
        var draft = await pending.CreateAsync(input, Ct);
        Assert.Equal(1, numbers.Count);
        Assert.Equal(InventoryDocumentStatus.PendingApproval,
            (await pending.SubmitAsync(draft.Id, draft.Version, Ct)).Transfer.Status);
        var pendingVersion = (await pending.GetAsync(draft.Id, Ct)).Transfer.Version;
        Assert.Equal(TransferService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            pending.DeleteAsync(draft.Id, pendingVersion, Ct))).ErrorCode);
        var handler = new TransferApprovalResultHandler(db, posting, new FixedClock(Now));
        await handler.HandleAsync(new ApprovalCompleted(1, TransferService.DocumentType, draft.Id,
            ApprovalStatus.Rejected, 8, "Wrong destination"), Ct);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(TransferService.ApprovalStateChanged, (await Assert.ThrowsAsync<ConflictException>(() =>
            handler.HandleAsync(new ApprovalCompleted(1, TransferService.DocumentType, draft.Id,
                ApprovalStatus.Rejected, 8, "Repeated"), Ct))).ErrorCode);
        db.ChangeTracker.Clear();
        var rejected = (await pending.GetAsync(draft.Id, Ct)).Transfer;
        Assert.Equal(InventoryDocumentStatus.Rejected, rejected.Status);
        Assert.Equal(TransferService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            pending.SubmitAsync(draft.Id, rejected.Version, Ct))).ErrorCode);
        var revised = await pending.UpdateAsync(draft.Id, input with { Remark = "Revised" }, rejected.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, revised.Status);
        Assert.Equal("Revised", revised.Remark);
        await Assert.ThrowsAsync<ConflictException>(() => pending.UpdateAsync(draft.Id, input, rejected.Version, Ct));
        Assert.Equal(InventoryDocumentStatus.PendingApproval,
            (await pending.SubmitAsync(draft.Id, revised.Version, Ct)).Transfer.Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == TransferService.SourceType, Ct));
        var disposable = await pending.CreateAsync(input, Ct);
        await pending.DeleteAsync(disposable.Id, disposable.Version, Ct);
        Assert.Equal(TransferService.NotFound, (await Assert.ThrowsAsync<NotFoundException>(() =>
            pending.GetAsync(disposable.Id, Ct))).ErrorCode);

    }

    [Fact]
    public async Task PostingReplayUsesTheExactPairedSnapshot()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", 5, Day,
            [new InventoryPostingLine(1, StockDirection.In, item.Id, warehouse.Id, a.Id, 5m)]), Ct);
        InventoryPostingRequest Request(decimal quantity) => new(TransferService.SourceType, 99, Day,
        [
            new InventoryPostingLine(1, StockDirection.Out, item.Id, warehouse.Id, a.Id, quantity),
            new InventoryPostingLine(1, StockDirection.In, item.Id, warehouse.Id, b.Id, quantity),
        ]);
        var first = await posting.PostAsync(Request(2m), Ct);
        var replay = await posting.PostAsync(Request(2m), Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(first.MovementIds, replay.MovementIds);
        Assert.Equal("INV.POSTING.CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            posting.PostAsync(Request(3m), Ct))).ErrorCode);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == TransferService.SourceType, Ct));
    }

    [Fact]
    public async Task ForeignWarehouseOnEitherSideIsRejectedBeforeNumbering()
    {
        var companyId = TestCompanies.Next();
        var foreignCompanyId = TestCompanies.Next();
        await using var db = Context(companyId);
        await using var foreign = Context(foreignCompanyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var otherLocal = await new WarehouseService(db).CreateAsync("Z" + companyId, "Second", null, null, null, Ct);
        var location = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var other = await new WarehouseService(foreign).CreateAsync("W" + foreignCompanyId, "Other", null, null, null, Ct);
        var numbers = new FakeNumbers();
        var service = new TransferService(db, numbers, new InventoryReferenceGuard(db), new FakeWorkflow(),
            new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now)),
            new FixedClock(Now));
        foreach (var (source, destination) in new[] { (warehouse.Id, other.Id), (other.Id, warehouse.Id) })
        {
            await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(new TransferInput(Day, null,
                [new TransferLineInput(1, item.Id, source, null, destination, null, 1m)]), Ct));
        }
        Assert.Equal("MD.STORAGE_LOCATION.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(new TransferInput(Day, null,
                [new TransferLineInput(1, item.Id, warehouse.Id, null, otherLocal.Id, location.Id, 1m)]), Ct))).ErrorCode);
        await new WarehouseService(db).UpdateAsync(otherLocal.Id, "Second", null, null, null, false, otherLocal.Version, Ct);
        Assert.Equal("MD.WAREHOUSE.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateAsync(new TransferInput(Day, null,
                [new TransferLineInput(1, item.Id, warehouse.Id, null, otherLocal.Id, null, 1m)]), Ct))).ErrorCode);
        Assert.Equal(0, numbers.Count);
        Assert.False(await db.Set<Transfer>().AnyAsync(Ct));
    }

    [Fact]
    public async Task QuantityLineAndTrackingLimitsAreCheckedBeforeNumbering()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var numbers = new FakeNumbers();
        var service = new TransferService(db, numbers, new InventoryReferenceGuard(db), new FakeWorkflow(),
            new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now)),
            new FixedClock(Now));
        TransferLineInput Line(int no, decimal quantity) => new(no, 1, 1, null, 2, null, quantity);
        var invalidPrecision = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new TransferInput(Day, null, [Line(1, 1.0000001m)]), Ct));
        Assert.Contains("lines[0].quantity", invalidPrecision.Errors.Keys);
        var tooManyLines = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new TransferInput(Day, null, Enumerable.Range(1, 1001).Select(i => Line(i, 1m)).ToArray()), Ct));
        Assert.Contains("lines", tooManyLines.Errors.Keys);
        var duplicateLine = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new TransferInput(Day, null, [Line(1, 1m), Line(1, 1m)]), Ct));
        Assert.Contains("lines", duplicateLine.Errors.Keys);
        var duplicateSerial = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new TransferInput(Day, null,
            [
                new TransferLineInput(1, 1, 1, null, 2, null, 1m, [InventoryTrackingLine.Serial("SN-1")]),
                new TransferLineInput(2, 1, 1, null, 2, null, 1m, [InventoryTrackingLine.Serial("sn-1")]),
            ]), Ct));
        Assert.Contains("lines", duplicateSerial.Errors.Keys);
        var manyTracking = Enumerable.Range(1, 1000).Select(i => new TransferLineInput(i, 1, 1, null, 2, null, 11m,
            Enumerable.Range(1, 11).Select(j => InventoryTrackingLine.Lot($"L{i}-{j}", 1m)).ToArray())).ToArray();
        var tooManyTracking = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new TransferInput(Day, null, manyTracking), Ct));
        Assert.Contains("lines", tooManyTracking.Errors.Keys);
        Assert.Equal(0, numbers.Count);
    }

    [Fact]
    public async Task DatabaseRejectsSameStockKeyWithBothLocationsNull()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var other = await new WarehouseService(db).CreateAsync("Z" + companyId, "Other", null, null, null, Ct);
        var service = new TransferService(db, new FakeNumbers(), new InventoryReferenceGuard(db), new FakeWorkflow(),
            new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now)),
            new FixedClock(Now));
        var draft = await service.CreateAsync(new TransferInput(Day, null,
            [new TransferLineInput(1, item.Id, warehouse.Id, null, other.Id, null, 1m)]), Ct);

        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO inv_transfer_line (transfer_id, line_no, item_id, source_warehouse_id, destination_warehouse_id, quantity, company_id, created_at, created_by, version) VALUES ({draft.Id}, {2}, {item.Id}, {warehouse.Id}, {warehouse.Id}, {1m}, {companyId}, {Now}, {7L}, {1})", Ct));
        Assert.Equal(547, error.Number);
    }

    private sealed class FakeNumbers : INumberGenerator
    {
        private int count;
        public int Count => count;
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default) =>
            Task.FromResult($"TR-{Interlocked.Increment(ref count):0000}");
    }

    private sealed class FakeWorkflow : IApprovalWorkflowService
    {
        public ApprovalStatus Next { get; init; } = ApprovalStatus.Approved;
        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalState(1, submission.DocumentType, submission.DocumentId, Next, null, 0));
        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalState?>(null);
    }
}
