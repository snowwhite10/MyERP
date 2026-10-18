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
using MyERP.Shared.Numbering;
using MyERP.Shared.Approvals;

namespace MyERP.IntegrationTests.Database;

public sealed class DisassemblyDraftTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2028, 2, 29);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 18)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel(), new DisassemblyDbModel()], company);
    }

    [Fact]
    public async Task ApprovalTransfersWeightedValueAndRejectsUnknownCostAtomically()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var first = await items.CreateAsync("A" + companyId, "First", null, uom.Id, null, null, Ct);
        var second = await items.CreateAsync("B" + companyId, "Second", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = Posting(db);
        var service = Service(db, posting);
        var input = new DisassemblyInput(Day, null,
            new DisassemblyLineInput(1, finished.Id, warehouse.Id, null, 3m),
            [new DisassemblyLineInput(2, first.Id, warehouse.Id, null, 1m, 1m),
             new DisassemblyLineInput(3, second.Id, warehouse.Id, null, 2m, 2m)]);
        var missing = await service.CreateAsync(input, Ct);
        Assert.Equal("INV.DISASSEMBLY.COST_UNKNOWN", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(missing.Id, missing.Version, Ct))).ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(missing.Id, Ct)).Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == "DISASSEMBLY", Ct));
        await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, finished.Id, warehouse.Id, null, 3m, 1.234567m)]), Ct);
        var result = await service.SubmitAsync(missing.Id, missing.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Posted, result.Disassembly.Status);
        var rows = await db.Set<StockMovement>().Where(x => x.SourceType == "DISASSEMBLY")
            .OrderBy(x => x.SourceLineNo).ToListAsync(Ct);
        Assert.Equal(3, rows.Count);
        Assert.Null(rows[0].UnitCost);
        Assert.Equal(3.7037m, rows.Skip(1).Sum(x => decimal.Round(x.Quantity * x.UnitCost!.Value, 4, MidpointRounding.AwayFromZero)));
        Assert.Equal(1.234566m, result.Disassembly.Lines.Single(x => x.Role == DisassemblyLineRole.Finished).PostedUnitCost);
        Assert.Equal(0m, await new StockBalanceQuery(db).GetOnHandAsync(finished.Id, warehouse.Id, null, Ct));
        var replayLines = new[] {
            new InventoryPostingLine(1, StockDirection.Out, finished.Id, warehouse.Id, null, 3m),
            new InventoryPostingLine(2, StockDirection.In, first.Id, warehouse.Id, null, 1m, rows[1].UnitCost),
            new InventoryPostingLine(3, StockDirection.In, second.Id, warehouse.Id, null, 2m, rows[2].UnitCost) };
        Assert.True((await posting.PostAsync(new InventoryPostingRequest(DisassemblyService.SourceType,
            missing.Id, Day, replayLines), Ct)).Replayed);
        await Assert.ThrowsAsync<ConflictException>(() => posting.PostAsync(new InventoryPostingRequest(
            DisassemblyService.SourceType, missing.Id, Day,
            [replayLines[0] with { Quantity = 2m }, replayLines[1], replayLines[2]]), Ct));
        Assert.Equal(3, await db.Set<StockMovement>().CountAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
    }

    [Fact]
    public async Task PrecisionFailureAndShortageLeaveDraftAndMovementsUntouchedWhileKnownZeroPosts()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = Posting(db);
        await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, finished.Id, warehouse.Id, null, 1m, 0.0001m)]), Ct);
        var service = Service(db, posting);
        var large = new DisassemblyInput(Day, null, new DisassemblyLineInput(1, finished.Id, warehouse.Id, null, 1m),
            [new DisassemblyLineInput(2, component.Id, warehouse.Id, null, 1000m, 1m)]);
        var draft = await service.CreateAsync(large, Ct);
        Assert.Equal("INV.DISASSEMBLY.COST_PRECISION", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(draft.Id, Ct)).Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
        var updated = await service.UpdateAsync(draft.Id, large with { Components =
            [new DisassemblyLineInput(2, component.Id, warehouse.Id, null, 1m, 1m)] }, draft.Version, Ct);
        await service.SubmitAsync(draft.Id, updated.Version, Ct);
        var remote = await new WarehouseService(db).CreateAsync("R" + companyId, "Remote", null, null, null, Ct);
        await posting.PostAsync(new InventoryPostingRequest("REMOTE", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, finished.Id, remote.Id, null, 1m, 0m)]), Ct);
        var shortage = await service.CreateAsync(large with { Components =
            [new DisassemblyLineInput(2, component.Id, warehouse.Id, null, 1m, 1m)] }, Ct);
        Assert.Equal("INV.STOCK.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(shortage.Id, shortage.Version, Ct))).ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(shortage.Id, Ct)).Status);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));

        await posting.PostAsync(new InventoryPostingRequest("ZERO", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, finished.Id, warehouse.Id, null, 1m, 0m)]), Ct);
        var zero = await service.SubmitAsync(shortage.Id, (await service.GetAsync(shortage.Id, Ct)).Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Posted, zero.Disassembly.Status);
        Assert.Equal(0m, zero.Disassembly.Lines.Single(x => x.Role == DisassemblyLineRole.Component).PostedUnitCost);
    }

    [Fact]
    public async Task PendingDecisionRejectsWithoutPostingAndDuplicateDecisionConflicts()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = Posting(db);
        var workflow = new ImmediateWorkflow { Next = ApprovalStatus.Pending };
        var service = new DisassemblyService(db, new Numbers(), new InventoryReferenceGuard(db), workflow,
            posting, new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(new DateTime(2026, 10, 18)));
        var input = new DisassemblyInput(Day, null, new DisassemblyLineInput(1, finished.Id, warehouse.Id, null, 1m),
            [new DisassemblyLineInput(2, component.Id, warehouse.Id, null, 1m, 1m)]);
        var draft = await service.CreateAsync(input, Ct);
        var pending = await service.SubmitAsync(draft.Id, draft.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.PendingApproval, pending.Disassembly.Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            var handler = new DisassemblyApprovalResultHandler(db, posting,
                new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(new DateTime(2026, 10, 18)));
            await handler.HandleAsync(new ApprovalCompleted(1, DisassemblyService.DocumentType, draft.Id,
                ApprovalStatus.Rejected, 7, "reason"), Ct);
            await db.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
            await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new ApprovalCompleted(1,
                DisassemblyService.DocumentType, draft.Id, ApprovalStatus.Approved, 7, null), Ct));
        }
        Assert.Equal(InventoryDocumentStatus.Rejected, (await service.GetAsync(draft.Id, Ct)).Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
        var revised = await service.UpdateAsync(draft.Id, input, (await service.GetAsync(draft.Id, Ct)).Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, revised.Status);
    }

    [Fact]
    public async Task TwoConnectionsCompetingForOneSourceCannotOversell()
    {
        var companyId = TestCompanies.Next();
        long finishedId, componentId, warehouseId, firstId, secondId;
        await using (var setup = Context(companyId))
        {
            var uom = await new UomService(setup).CreateAsync("U" + companyId, "Unit", 0, Ct);
            var items = new ItemService(setup);
            finishedId = (await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct)).Id;
            componentId = (await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct)).Id;
            warehouseId = (await new WarehouseService(setup).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct)).Id;
            await Posting(setup).PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
                [new InventoryPostingLine(1, StockDirection.In, finishedId, warehouseId, null, 3m, 1m)]), Ct);
            var service = Service(setup, Posting(setup));
            var input = new DisassemblyInput(Day, null,
                new DisassemblyLineInput(1, finishedId, warehouseId, null, 2m),
                [new DisassemblyLineInput(2, componentId, warehouseId, null, 2m, 1m)]);
            firstId = (await service.CreateAsync(input, Ct)).Id;
            secondId = (await service.CreateAsync(input, Ct)).Id;
        }
        async Task<string> Submit(long id)
        {
            await using var db = Context(companyId);
            try { await Service(db, Posting(db)).SubmitAsync(id, 1, Ct); return "Posted"; }
            catch (BusinessRuleException ex) { return ex.ErrorCode; }
        }
        var results = await Task.WhenAll(Task.Run(() => Submit(firstId), Ct), Task.Run(() => Submit(secondId), Ct));
        Assert.Equal(1, results.Count(x => x == "Posted"));
        Assert.Equal(1, results.Count(x => x == "INV.STOCK.INSUFFICIENT"));
        await using var verify = Context(companyId);
        Assert.Equal(2, await verify.Set<StockMovement>().CountAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
        Assert.Equal(1m, await new StockBalanceQuery(verify).GetOnHandAsync(finishedId, warehouseId, null, Ct));
    }

    [Fact]
    public async Task ExtremeWeightProductReturnsCostOverflowWithoutWritingMovements()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var first = await items.CreateAsync("A" + companyId, "First", null, uom.Id, null, null, Ct);
        var second = await items.CreateAsync("B" + companyId, "Second", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        await Posting(db).PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, finished.Id, warehouse.Id, null,
                9_000_000_000_000m, 9_000_000_000_000m)]), Ct);
        var service = Service(db, Posting(db));
        var draft = await service.CreateAsync(new DisassemblyInput(Day, null,
            new DisassemblyLineInput(1, finished.Id, warehouse.Id, null, 9_000_000_000_000m),
            [new DisassemblyLineInput(2, first.Id, warehouse.Id, null, 1m, 9_000_000_000_000m),
             new DisassemblyLineInput(3, second.Id, warehouse.Id, null, 1m, 1m)]), Ct);
        Assert.Equal("INV.DISASSEMBLY.COST_OVERFLOW", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(draft.Id, Ct)).Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
    }

    [Fact]
    public async Task TrackingRequiresCompleteLotAndRejectsDuplicateOutputSerial()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null,
            ItemTrackingMode.Lot, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null,
            ItemTrackingMode.Serial, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = Posting(db);
        await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, finished.Id, warehouse.Id, null, 1m, 2m,
                [InventoryTrackingLine.Lot("LOT-A", 1m)]),
             new InventoryPostingLine(2, StockDirection.In, component.Id, warehouse.Id, null, 1m, 0m,
                [InventoryTrackingLine.Serial("USED")])]), Ct);
        var service = Service(db, posting);
        var input = new DisassemblyInput(Day, null,
            new DisassemblyLineInput(1, finished.Id, warehouse.Id, null, 1m),
            [new DisassemblyLineInput(2, component.Id, warehouse.Id, null, 1m, 1m,
                [InventoryTrackingLine.Serial("USED")])]);
        var draft = await service.CreateAsync(input, Ct);
        Assert.Equal("INV.TRACKING.REQUIRED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        var completed = await service.UpdateAsync(draft.Id, input with { Finished = input.Finished! with {
            Tracking = [InventoryTrackingLine.Lot("LOT-B", 1m)] } }, (await service.GetAsync(draft.Id, Ct)).Version, Ct);
        Assert.Equal("INV.LOT.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, completed.Version, Ct))).ErrorCode);
        completed = await service.UpdateAsync(draft.Id, input with { Finished = input.Finished! with {
            Tracking = [InventoryTrackingLine.Lot("LOT-A", 1m)] } }, (await service.GetAsync(draft.Id, Ct)).Version, Ct);
        Assert.Equal("INV.SERIAL.IN_STOCK", (await Assert.ThrowsAsync<ConflictException>(() =>
            service.SubmitAsync(draft.Id, completed.Version, Ct))).ErrorCode);
        completed = await service.UpdateAsync(draft.Id, input with {
            Finished = input.Finished! with { Tracking = [InventoryTrackingLine.Lot("LOT-A", 1m)] },
            Components = [input.Components![0] with { Tracking = [InventoryTrackingLine.Serial("NEW")] }]
        }, (await service.GetAsync(draft.Id, Ct)).Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Posted, (await service.SubmitAsync(draft.Id, completed.Version, Ct)).Disassembly.Status);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
    }

    [Fact]
    public async Task DraftPersistsWeightsAndTrackingWithoutPostingAndEnforcesVersionAndCompany()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null,
            ItemTrackingMode.Lot, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var service = Service(db, Posting(db));
        var input = new DisassemblyInput(Day, " draft ",
            new DisassemblyLineInput(1, finished.Id, warehouse.Id, null, 1m),
            [new DisassemblyLineInput(2, component.Id, warehouse.Id, null, 2m, 3m,
                [InventoryTrackingLine.Lot("LOT-D", 2m)])]);
        var draft = await service.CreateAsync(input, Ct);
        Assert.Equal("draft", draft.Remark);
        Assert.Equal(3m, Assert.Single((await service.GetAsync(draft.Id, Ct)).Lines, x => x.Role == DisassemblyLineRole.Component).AllocationWeight);
        Assert.Equal("LOT-D", Assert.Single((await service.GetAsync(draft.Id, Ct)).Lines
            .SelectMany(x => x.Tracking)).LotNo);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == "DISASSEMBLY", Ct));
        Assert.Equal([draft.Id], (await service.ListAsync(new DisassemblyQuery { PageSize = 1 }, Ct)).Items.Select(x => x.Id));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(draft.Id, input, draft.Version + 1, Ct));
        await using var other = Context(TestCompanies.Next());
        var otherService = Service(other, Posting(other));
        await Assert.ThrowsAsync<NotFoundException>(() => otherService.GetAsync(draft.Id, Ct));
        var updated = await service.UpdateAsync(draft.Id, input with { Remark = "revised",
            Components = [new DisassemblyLineInput(3, component.Id, warehouse.Id, null, 1m, 2m,
                [InventoryTrackingLine.Lot("LOT-NEW", 1m)])] }, draft.Version, Ct);
        Assert.Equal("revised", updated.Remark);
        Assert.Equal("LOT-NEW", Assert.Single((await service.GetAsync(draft.Id, Ct)).Lines
            .SelectMany(x => x.Tracking)).LotNo);
        updated.Status = InventoryDocumentStatus.PendingApproval;
        await db.SaveChangesAsync(Ct);
        var pendingVersion = updated.Version;
        Assert.Equal(DisassemblyService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(draft.Id, input, pendingVersion, Ct))).ErrorCode);
        Assert.Equal(DisassemblyService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, pendingVersion, Ct))).ErrorCode);
        updated = await db.Set<Disassembly>().SingleAsync(x => x.Id == draft.Id, Ct);
        updated.Status = InventoryDocumentStatus.Rejected;
        await db.SaveChangesAsync(Ct);
        Assert.Equal(DisassemblyService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, updated.Version, Ct))).ErrorCode);
        updated = await service.UpdateAsync(draft.Id, input, updated.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, updated.Status);
        await service.DeleteAsync(draft.Id, updated.Version, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(draft.Id, Ct));
        var max = await service.CreateAsync(input with { Components = Enumerable.Range(2, 999)
            .Select(i => new DisassemblyLineInput(i, component.Id, warehouse.Id, null, 0.000001m, 1m))
            .ToArray() }, Ct);
        Assert.Equal(1000, (await service.GetAsync(max.Id, Ct)).Lines.Count);
    }

    [Fact]
    public async Task NegativeValuesAreRejectedAndYearBoundaryDatesPersistWithoutPosting()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var service = Service(db, Posting(db));
        var input = new DisassemblyInput(new DateOnly(2027, 12, 31), null,
            new DisassemblyLineInput(1, finished.Id, warehouse.Id, null, 0.000001m),
            [new DisassemblyLineInput(2, component.Id, warehouse.Id, null, 0.000001m, 0.000001m)]);
        foreach (var invalid in new[] {
            input with { Finished = input.Finished! with { Quantity = -1m } },
            input with { Components = [input.Components![0] with { Quantity = -1m }] },
            input with { Components = [input.Components![0] with { AllocationWeight = -1m }] }
        })
            await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(invalid, Ct));
        Assert.False(await db.Set<Disassembly>().AnyAsync(Ct));
        var first = await service.CreateAsync(input, Ct);
        var second = await service.CreateAsync(input with { BusinessDate = new DateOnly(2028, 1, 1) }, Ct);
        Assert.Equal(new DateOnly(2027, 12, 31), (await service.GetAsync(first.Id, Ct)).BusinessDate);
        Assert.Equal(new DateOnly(2028, 1, 1), (await service.GetAsync(second.Id, Ct)).BusinessDate);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
    }

    [Fact]
    public async Task InvalidAndChangedMasterReferencesLeaveDraftAndMovementsUntouched()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var otherWarehouse = await new WarehouseService(db).CreateAsync("X" + companyId, "Other", null, null, null, Ct);
        var location = new MyERP.Modules.MasterData.Domain.StorageLocation {
            CompanyId = companyId, WarehouseId = otherWarehouse.Id, Code = "L" + companyId, Name = "Location" };
        db.Add(location);
        await db.SaveChangesAsync(Ct);
        var service = Service(db, Posting(db));
        var input = new DisassemblyInput(Day, null,
            new DisassemblyLineInput(1, finished.Id, warehouse.Id, null, 1m),
            [new DisassemblyLineInput(2, component.Id, warehouse.Id, null, 1m, 1m)]);
        Assert.Equal("MD.STORAGE_LOCATION.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(input with { Components = [input.Components![0] with { LocationId = location.Id }] }, Ct))).ErrorCode);
        Assert.False(await db.Set<Disassembly>().AnyAsync(Ct));
        var draft = await service.CreateAsync(input, Ct);
        var savedWarehouse = await db.Set<MyERP.Modules.MasterData.Domain.Warehouse>()
            .SingleAsync(x => x.Id == warehouse.Id, Ct);
        savedWarehouse.IsActive = false;
        await db.SaveChangesAsync(Ct);
        Assert.Equal("MD.WAREHOUSE.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        savedWarehouse = await db.Set<MyERP.Modules.MasterData.Domain.Warehouse>()
            .SingleAsync(x => x.Id == warehouse.Id, Ct);
        savedWarehouse.IsActive = true;
        await db.SaveChangesAsync(Ct);
        var item = await db.Set<MyERP.Modules.MasterData.Domain.Item>().SingleAsync(x => x.Id == component.Id, Ct);
        item.IsActive = false;
        await db.SaveChangesAsync(Ct);
        Assert.Equal("MD.ITEM.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(draft.Id, Ct)).Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
        item = await db.Set<MyERP.Modules.MasterData.Domain.Item>().SingleAsync(x => x.Id == component.Id, Ct);
        item.IsActive = true;
        item.IsDeleted = true;
        await db.SaveChangesAsync(Ct);
        Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(draft.Id, Ct)).Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == DisassemblyService.SourceType, Ct));
    }

    [Fact]
    public async Task InvalidDraftAndReferenceLeaveNoDocumentOrNumber()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var numbers = new Numbers();
        var service = new DisassemblyService(db, numbers, new InventoryReferenceGuard(db), new ImmediateWorkflow(),
            Posting(db), new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(new DateTime(2026, 10, 18)));
        var source = new DisassemblyLineInput(1, 1, 1, null, 1m);
        var component = new DisassemblyLineInput(2, 2, 1, null, 1m, 1m);
        var validShape = new DisassemblyInput(Day, null, source, [component]);
        var invalid = new[]
        {
            validShape with { Finished = null },
            validShape with { Components = [] },
            validShape with { Components = [component with { LineNo = 1 }] },
            validShape with { Components = [component with { ItemId = 1 }] },
            validShape with { Components = [component with { AllocationWeight = 0 }] },
            validShape with { Components = [component with { AllocationWeight = 1.0000001m }] },
            validShape with { Finished = source with { AllocationWeight = 1m } },
            validShape with { Finished = source with { Quantity = 0 } },
            validShape with { Remark = new string('x', 1001) },
            validShape with { Components = Enumerable.Range(2, 1000)
                .Select(i => component with { LineNo = i }).ToArray() },
        };
        foreach (var input in invalid)
            await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(input, Ct));
        Assert.Equal(0, numbers.Count);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(validShape, Ct));
        Assert.Equal("MD.ITEM.NOT_FOUND", missing.ErrorCode);
        Assert.False(await db.Set<Disassembly>().AnyAsync(Ct));
    }

    private sealed class Numbers : INumberGenerator
    {
        private int next;
        public int Count => next;
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default) =>
            Task.FromResult($"DIS-{Interlocked.Increment(ref next):0000}");
    }

    private static StockPostingService Posting(MyErpDbContext db) => new(db,
        new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(new DateTime(2026, 10, 18)));
    private static DisassemblyService Service(MyErpDbContext db, StockPostingService posting) => new(db,
        new Numbers(), new InventoryReferenceGuard(db), new ImmediateWorkflow(), posting,
        new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(new DateTime(2026, 10, 18)));
    private sealed class ImmediateWorkflow : IApprovalWorkflowService
    {
        public ApprovalStatus Next { get; init; } = ApprovalStatus.Approved;
        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalState(1, submission.DocumentType, submission.DocumentId, Next, null, 0));
        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) => Task.FromResult<ApprovalState?>(null);
    }
}
