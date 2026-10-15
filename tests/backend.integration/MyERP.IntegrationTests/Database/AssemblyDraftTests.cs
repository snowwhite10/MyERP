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

/// <summary>INV-ASM-002/003/007–010：草稿保存、版本、公司隔離與引用。</summary>
public sealed class AssemblyDraftTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 12, 31);
    private static readonly DateTime Now = new(2026, 10, 15, 8, 0, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel(), new AssemblyDbModel()], company);
    }

    [Fact]
    public async Task ApprovalPostsKnownCostAndRollsBackUnknownCost()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var unknown = await items.CreateAsync("X" + companyId, "Unknown", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, component.Id, warehouse.Id, null, 10m, 1.234567m)]), Ct);
        var service = new AssemblyService(db, new FakeNumbers(), new InventoryReferenceGuard(db),
            new FakeWorkflow(), posting, new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(Now));
        var input = new AssemblyInput(Day, null,
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 2m),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 3m)]);
        var draft = await service.CreateAsync(input, Ct);
        var submitted = await service.SubmitAsync(draft.Id, draft.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Posted, submitted.Assembly.Status);
        var movements = await db.Set<StockMovement>().Where(x => x.SourceType == AssemblyService.SourceType).ToListAsync(Ct);
        Assert.Equal(2, movements.Count);
        var receipt = Assert.Single(movements, x => x.Direction == StockMovementDirection.In);
        Assert.Equal(1.85185m, receipt.UnitCost);
        Assert.Equal(3.7037m, decimal.Round(receipt.Quantity * receipt.UnitCost!.Value, 4, MidpointRounding.AwayFromZero));
        Assert.Equal(7m, await new StockBalanceQuery(db).GetOnHandAsync(component.Id, warehouse.Id, null, Ct));

        var invalid = await service.CreateAsync(input with { Components =
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 1m),
             new AssemblyLineInput(3, unknown.Id, warehouse.Id, null, 1m)] }, Ct);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(invalid.Id, invalid.Version, Ct));
        Assert.Equal("INV.ASSEMBLY.COST_UNKNOWN", error.ErrorCode);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(invalid.Id, Ct)).Status);
    }

    [Fact]
    public async Task SecondComponentShortageRollsBackEntireAssembly()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var first = await items.CreateAsync("A" + companyId, "First", null, uom.Id, null, null, Ct);
        var second = await items.CreateAsync("B" + companyId, "Second", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, first.Id, warehouse.Id, null, 3m, 2m),
             new InventoryPostingLine(2, StockDirection.In, second.Id, warehouse.Id, null, 1m, 3m)]), Ct);
        var service = Service(db, new FakeNumbers());
        var draft = await service.CreateAsync(new AssemblyInput(Day, null,
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1m),
            [new AssemblyLineInput(2, first.Id, warehouse.Id, null, 2m),
             new AssemblyLineInput(3, second.Id, warehouse.Id, null, 2m)]), Ct);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(draft.Id, draft.Version, Ct));
        Assert.Equal("INV.STOCK.INSUFFICIENT", error.ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(draft.Id, Ct)).Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
        Assert.Equal(3m, await new StockBalanceQuery(db).GetOnHandAsync(first.Id, warehouse.Id, null, Ct));
    }

    [Fact]
    public async Task UnrepresentableFinishedUnitCostRejectsWithoutPosting()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, component.Id, warehouse.Id, null, 1m, 0.0001m)]), Ct);
        var service = Service(db, new FakeNumbers());
        var draft = await service.CreateAsync(new AssemblyInput(Day, null,
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1000m),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 1m)]), Ct);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(draft.Id, draft.Version, Ct));
        Assert.Equal("INV.ASSEMBLY.COST_PRECISION", error.ErrorCode);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(draft.Id, Ct)).Status);
    }

    [Fact]
    public async Task LotShortageAndExistingFinishedSerialRollBackBeforeValidTrackingPosts()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null,
            ItemTrackingMode.Serial, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null,
            ItemTrackingMode.Lot, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, component.Id, warehouse.Id, null, 1m, 2m,
                [InventoryTrackingLine.Lot("LOT-A", 1m)]),
             new InventoryPostingLine(2, StockDirection.In, finished.Id, warehouse.Id, null, 1m, 0m,
                [InventoryTrackingLine.Serial("USED")])]), Ct);
        var service = Service(db, new FakeNumbers());
        var incomplete = await service.CreateAsync(new AssemblyInput(Day, null,
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1m),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 1m)]), Ct);
        Assert.Equal("INV.TRACKING.REQUIRED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(incomplete.Id, incomplete.Version, Ct))).ErrorCode);
        var input = new AssemblyInput(Day, null,
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1m,
                [InventoryTrackingLine.Serial("USED")]),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 1m,
                [InventoryTrackingLine.Lot("LOT-B", 1m)])]);
        var draft = await service.CreateAsync(input, Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(draft.Id, draft.Version, Ct));
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
        var original = await service.GetAsync(draft.Id, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, original.Status);
        var changed = await service.UpdateAsync(draft.Id, input with { Components =
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 1m,
                [InventoryTrackingLine.Lot("LOT-A", 1m)])] }, original.Version, Ct);
        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(draft.Id, changed.Version, Ct));
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
        var persisted = await service.GetAsync(draft.Id, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, persisted.Status);
        changed = await service.UpdateAsync(draft.Id, input with {
            Finished = input.Finished! with { Tracking = [InventoryTrackingLine.Serial("NEW")] },
            Components = [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 1m,
                [InventoryTrackingLine.Lot("LOT-A", 1m)])] }, persisted.Version, Ct);
        await service.SubmitAsync(draft.Id, changed.Version, Ct);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
    }

    [Fact]
    public async Task ConcurrentAssembliesShareComponentLockAndPostingReplayIsIdempotent()
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
            var posting = new StockPostingService(setup,
                new InventoryReferenceService(setup, new InventoryReferenceGuard(setup)), new FixedClock(Now));
            await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
                [new InventoryPostingLine(1, StockDirection.In, componentId, warehouseId, null, 3m, 1m)]), Ct);
            var service = Service(setup, new FakeNumbers());
            var input = new AssemblyInput(Day, null,
                new AssemblyLineInput(1, finishedId, warehouseId, null, 1m),
                [new AssemblyLineInput(2, componentId, warehouseId, null, 2m)]);
            firstId = (await service.CreateAsync(input, Ct)).Id;
            secondId = (await service.CreateAsync(input, Ct)).Id;
        }

        async Task<string> Submit(long id)
        {
            await using var db = Context(companyId);
            try { await Service(db, new FakeNumbers()).SubmitAsync(id, 1, Ct); return "Posted"; }
            catch (BusinessRuleException ex) { return ex.ErrorCode; }
        }
        var results = await Task.WhenAll(Task.Run(() => Submit(firstId), Ct), Task.Run(() => Submit(secondId), Ct));
        Assert.Equal(1, results.Count(x => x == "Posted"));
        Assert.Equal(1, results.Count(x => x == "INV.STOCK.INSUFFICIENT"));

        await using var verify = Context(companyId);
        var postedId = results[0] == "Posted" ? firstId : secondId;
        var original = await Service(verify, new FakeNumbers()).GetAsync(postedId, Ct);
        Assert.Equal(2, await verify.Set<StockMovement>().CountAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
        var postingVerify = new StockPostingService(verify,
            new InventoryReferenceService(verify, new InventoryReferenceGuard(verify)), new FixedClock(Now));
        var lines = new[] {
            new InventoryPostingLine(2, StockDirection.Out, componentId, warehouseId, null, 2m),
            new InventoryPostingLine(1, StockDirection.In, finishedId, warehouseId, null, 1m,
                original.Lines.Single(x => x.Role == AssemblyLineRole.Finished).PostedUnitCost) };
        Assert.True((await postingVerify.PostAsync(new InventoryPostingRequest(AssemblyService.SourceType,
            postedId, Day, lines), Ct)).Replayed);
        var changed = lines.ToArray();
        changed[0] = changed[0] with { Quantity = 1m };
        await Assert.ThrowsAsync<ConflictException>(() => postingVerify.PostAsync(new InventoryPostingRequest(
            AssemblyService.SourceType, postedId, Day, changed), Ct));
        Assert.Equal(2, await verify.Set<StockMovement>().CountAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
    }

    [Fact]
    public async Task PendingApprovalRejectsWithoutMovementAndRevisionCanBeResubmitted()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var posting = new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));
        var workflow = new FakeWorkflow { Next = ApprovalStatus.Pending };
        var service = new AssemblyService(db, new FakeNumbers(), new InventoryReferenceGuard(db), workflow,
            posting, new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(Now));
        var input = new AssemblyInput(Day, null, new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1m),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 1m)]);
        var draft = await service.CreateAsync(input, Ct);
        var pending = await service.SubmitAsync(draft.Id, draft.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.PendingApproval, pending.Assembly.Status);
        Assert.Equal(AssemblyService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, pending.Assembly.Version, Ct))).ErrorCode);
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            await new AssemblyApprovalResultHandler(db, posting,
                new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(Now))
                .HandleAsync(new ApprovalCompleted(1, AssemblyService.DocumentType, draft.Id, ApprovalStatus.Rejected, 7, null), Ct);
            await db.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == AssemblyService.SourceType, Ct));
        var rejected = await service.GetAsync(draft.Id, Ct);
        Assert.Equal(InventoryDocumentStatus.Rejected, rejected.Status);
        var revised = await service.UpdateAsync(draft.Id, input, rejected.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, revised.Status);
        await posting.PostAsync(new InventoryPostingRequest("SEED", companyId, Day,
            [new InventoryPostingLine(1, StockDirection.In, component.Id, warehouse.Id, null, 1m, 0m)]), Ct);
        Assert.Equal(InventoryDocumentStatus.PendingApproval,
            (await service.SubmitAsync(draft.Id, revised.Version, Ct)).Assembly.Status);
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            var handler = new AssemblyApprovalResultHandler(db, posting,
                new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(Now));
            await handler.HandleAsync(new ApprovalCompleted(2, AssemblyService.DocumentType, draft.Id,
                ApprovalStatus.Approved, 7, null), Ct);
            await db.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }
        Assert.Equal(InventoryDocumentStatus.Posted, (await service.GetAsync(draft.Id, Ct)).Status);
        Assert.Equal(0m, Assert.Single(await db.Set<StockMovement>().Where(x => x.SourceType == AssemblyService.SourceType &&
            x.Direction == StockMovementDirection.In).ToListAsync(Ct)).UnitCost);
        await Assert.ThrowsAsync<ConflictException>(() => new AssemblyApprovalResultHandler(db, posting,
            new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(Now))
            .HandleAsync(new ApprovalCompleted(2, AssemblyService.DocumentType, draft.Id,
                ApprovalStatus.Approved, 7, null), Ct));
    }

    [Fact]
    public async Task DraftRoundTripsAndVersionAndCompanyIsolation()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var finished = await new ItemService(db).CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await new ItemService(db).CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var service = Service(db, new FakeNumbers());
        var input = new AssemblyInput(Day, " draft ",
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1.123456m),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 2m)]);
        var draft = await service.CreateAsync(input, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, draft.Status);
        Assert.Equal("draft", draft.Remark);
        Assert.Equal(2, (await service.GetAsync(draft.Id, Ct)).Lines.Count);
        Assert.Equal([draft.Id], (await service.ListAsync(new AssemblyQuery { From = Day, To = Day,
            FinishedItemId = finished.Id, Status = InventoryDocumentStatus.Draft }, Ct)).Items.Select(x => x.Id));
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == "ASSEMBLY", Ct));
        await using var other = Context(TestCompanies.Next());
        var otherService = Service(other, new FakeNumbers());
        await Assert.ThrowsAsync<NotFoundException>(() => otherService.GetAsync(draft.Id, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(draft.Id, input, draft.Version + 1, Ct));
        var updated = await service.UpdateAsync(draft.Id, input with { Remark = "updated" }, draft.Version, Ct);
        Assert.Equal("updated", updated.Remark);
        var thousand = await service.CreateAsync(new AssemblyInput(new DateOnly(2028, 2, 29), null,
            input.Finished, Enumerable.Range(2, 999)
                .Select(i => new AssemblyLineInput(i, component.Id, warehouse.Id, null, 1m)).ToArray()), Ct);
        Assert.Equal(1000, (await service.GetAsync(thousand.Id, Ct)).Lines.Count);
        var firstPage = await service.ListAsync(new AssemblyQuery { Page = 1, PageSize = 1 }, Ct);
        var secondPage = await service.ListAsync(new AssemblyQuery { Page = 2, PageSize = 1 }, Ct);
        Assert.Equal([thousand.Id], firstPage.Items.Select(x => x.Id));
        Assert.Equal([draft.Id], secondPage.Items.Select(x => x.Id));
        await service.DeleteAsync(draft.Id, updated.Version, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(draft.Id, Ct));
    }

    [Fact]
    public async Task InvalidCompositionIsRejectedBeforeNumbering()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var numbers = new FakeNumbers();
        var service = Service(db, numbers);
        var line = new AssemblyLineInput(1, 1, 1, null, 1m);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line, []), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, null, [line]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line, [line with { LineNo = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line, [line with { ItemId = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line with { Quantity = 0m }, [line with { LineNo = 2, ItemId = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line with { Quantity = -1m }, [line with { LineNo = 2, ItemId = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, new string('x', 1001), line, [line with { LineNo = 2, ItemId = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line,
                Enumerable.Range(2, 1000).Select(i => line with { LineNo = i, ItemId = 2 }).ToArray()), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line with { Quantity = 1.0000001m }, [line with { LineNo = 2, ItemId = 2 }]), Ct));
        Assert.Equal(0, numbers.Count);
    }

    [Fact]
    public async Task LotAndSerialSelectionsSurviveDraftReplacementAndInvalidReferencesRollBack()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null,
            ItemTrackingMode.Serial, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null,
            ItemTrackingMode.Lot, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var numbers = new FakeNumbers();
        var service = Service(db, numbers);
        var input = new AssemblyInput(Day, null,
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1m,
                [InventoryTrackingLine.Serial("NEW-SERIAL")]),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 2m,
                [InventoryTrackingLine.Lot("LOT-A", 2m)])]);
        var draft = await service.CreateAsync(input, Ct);
        var saved = await service.GetAsync(draft.Id, Ct);
        Assert.Equal(2, saved.Lines.Sum(x => x.Tracking.Count));
        var replaced = await service.UpdateAsync(draft.Id, input with
        {
            Components = [new AssemblyLineInput(3, component.Id, warehouse.Id, null, 1m,
                [InventoryTrackingLine.Lot("LOT-B", 1m)])]
        }, draft.Version, Ct);
        Assert.Equal(["LOT-B", "NEW-SERIAL"], (await service.GetAsync(replaced.Id, Ct)).Lines
            .SelectMany(x => x.Tracking).Select(x => x.LotNo ?? x.SerialNo).OrderBy(x => x).ToArray());
        var invalid = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(input with
        {
            Components = [new AssemblyLineInput(2, component.Id, warehouse.Id + 999999, null, 2m)]
        }, Ct));
        Assert.Equal("MD.WAREHOUSE.NOT_FOUND", invalid.ErrorCode);
        Assert.Equal(1, await db.Set<Assembly>().CountAsync(Ct));
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == "ASSEMBLY", Ct));
    }

    private sealed class FakeNumbers : INumberGenerator
    {
        private int count;
        public int Count => count;
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default) =>
            Task.FromResult($"AS-{Interlocked.Increment(ref count):0000}");
    }

    private static AssemblyService Service(MyErpDbContext db, INumberGenerator numbers) =>
        new(db, numbers, new InventoryReferenceGuard(db), new FakeWorkflow(),
            new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now)),
            new AssemblyCostSnapshot(db, new InventoryValuationQuery(db)), new FixedClock(Now));

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
