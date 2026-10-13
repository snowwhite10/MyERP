using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-REC-001～004：其他入庫草稿、明細驗證、送審、SYS 結果處理與同交易過帳（myerp_test）。</summary>
public sealed class OtherReceiptTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 10, 13);
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
                new StockMovementDbModel(), new StockTrackingDbModel(), new OtherReceiptDbModel()],
            company);
    }

    private static StockPostingService Posting(MyErpDbContext db) =>
        new(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));

    private static OtherReceiptService Service(MyErpDbContext db, FakeWorkflow? workflow = null, FakeNumbers? numbers = null) =>
        new(db, numbers ?? new FakeNumbers(), new InventoryReferenceGuard(db), workflow ?? new FakeWorkflow(), Posting(db), new FixedClock(Now));

    private static OtherReceiptApprovalResultHandler Handler(MyErpDbContext db) => new(db, Posting(db), new FixedClock(Now));

    private static async Task<Master> SetupAsync(MyErpDbContext db, long companyId)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var other = await new ItemService(db).CreateAsync("J" + companyId, "Other item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var second = await new WarehouseService(db).CreateAsync("X" + companyId, "Second", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(second.Id, "B" + companyId, "B", Ct);
        return new Master(item.Id, other.Id, warehouse.Id, second.Id, a.Id, b.Id);
    }

    private static OtherReceiptInput Input(Master m, string? remark = "Found in audit") => new(Day, remark,
    [
        new OtherReceiptLineInput(1, m.Item, m.Warehouse, m.LocationA, 10.123456m, 12.5m),
        new OtherReceiptLineInput(2, m.Other, m.Second, null, 3m, 0m),
    ]);

    private static Task<decimal> OnHandAsync(MyErpDbContext db, long item, long warehouse, long? location) =>
        new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, location, Ct);

    [Fact]
    public async Task DraftsAreNumberedEditedAndSoftDeletedWithoutStock()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var numbers = new FakeNumbers();
        var service = Service(db, numbers: numbers);

        var created = await service.CreateAsync(Input(m), Ct);
        Assert.Equal(("OR-0001", InventoryDocumentStatus.Draft, 1, Day), (created.DocumentNo, created.Status, created.Version, created.BusinessDate));
        Assert.Equal([1, 2], created.Lines.OrderBy(x => x.LineNo).Select(x => x.LineNo));
        Assert.Equal((OtherReceiptService.DocumentType, Day), numbers.Calls.Single());
        Assert.False(await db.Set<StockMovement>().AnyAsync(Ct));

        var updated = await service.UpdateAsync(created.Id, new OtherReceiptInput(Day.AddDays(1), "  Corrected  ",
            [new OtherReceiptLineInput(5, m.Other, m.Warehouse, m.LocationA, 2.5m, 4m)]), created.Version, Ct);
        Assert.Equal((Day.AddDays(1), "Corrected", 2), (updated.BusinessDate, updated.Remark, updated.Version));
        db.ChangeTracker.Clear();
        var detail = await service.GetAsync(created.Id, Ct);
        var line = Assert.Single(detail.Receipt.Lines);
        Assert.Equal((5, m.Other, 2.5m, 4m), (line.LineNo, line.ItemId, line.Quantity, line.UnitCost));
        Assert.Null(detail.Approval);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(created.Id, Input(m), 1, Ct));

        var page = await service.ListAsync(new OtherReceiptQuery { Keyword = "OR-0001", Status = InventoryDocumentStatus.Draft }, Ct);
        var summary = Assert.Single(page.Items);
        Assert.Equal((1, 2.5m, 10m), (summary.LineCount, summary.TotalQuantity, summary.TotalAmount));
        Assert.Equal(0, (await service.ListAsync(new OtherReceiptQuery { From = Day.AddDays(2) }, Ct)).TotalCount);
        Assert.Equal(1, (await service.ListAsync(new OtherReceiptQuery { From = Day, To = Day.AddDays(1) }, Ct)).TotalCount);
        Assert.Equal(0, (await service.ListAsync(new OtherReceiptQuery { Status = InventoryDocumentStatus.Posted }, Ct)).TotalCount);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.ListAsync(new OtherReceiptQuery { From = Day.AddDays(1), To = Day }, Ct));

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(created.Id, 1, Ct));
        await service.DeleteAsync(created.Id, 2, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(created.Id, Ct));
        Assert.Equal(0, (await service.ListAsync(new OtherReceiptQuery(), Ct)).TotalCount);
        var deleted = await db.Set<OtherReceipt>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AsNoTracking().SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal((true, Now), (deleted.IsDeleted, deleted.DeletedAt));

        // 單號不重用：下一張取新號
        Assert.Equal("OR-0002", (await service.CreateAsync(Input(m), Ct)).DocumentNo);
    }

    public static TheoryData<string, OtherReceiptLineInput[]> InvalidLines => new()
    {
        { "lines", [] },
        { "lines", [new(1, 1, 1, null, 1m, 0m), new(1, 1, 1, null, 2m, 0m)] },
        { "lines[0].lineNo", [new(0, 1, 1, null, 1m, 0m)] },
        { "lines[0].itemId", [new(1, 0, 1, null, 1m, 0m)] },
        { "lines[0].warehouseId", [new(1, 1, 0, null, 1m, 0m)] },
        { "lines[0].locationId", [new(1, 1, 1, 0, 1m, 0m)] },
        { "lines[0].quantity", [new(1, 1, 1, null, 0m, 0m)] },
        { "lines[0].quantity", [new(1, 1, 1, null, -1m, 0m)] },
        { "lines[0].quantity", [new(1, 1, 1, null, 0.0000001m, 0m)] },
        { "lines[0].quantity", [new(1, 1, 1, null, 10_000_000_000_000m, 0m)] },
        { "lines[0].unitCost", [new(1, 1, 1, null, 1m, -0.01m)] },
        { "lines[0].unitCost", [new(1, 1, 1, null, 1m, 1.0000001m)] },
        { "lines", [new(1, 1, 1, null, 9_999_999_999_999m, 9_999_999_999_999m)] },
    };

    [Theory]
    [MemberData(nameof(InvalidLines))]
    public async Task InvalidLinesAreRejectedBeforeNumberingOrSaving(string field, OtherReceiptLineInput[] lines)
    {
        await using var db = Context(TestCompanies.Next());
        var numbers = new FakeNumbers();

        var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
            Service(db, numbers: numbers).CreateAsync(new OtherReceiptInput(Day, null, lines), Ct));

        Assert.Contains(field, error.Errors.Keys);
        Assert.Empty(numbers.Calls);
        Assert.False(await db.Set<OtherReceipt>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AnyAsync(Ct));
    }

    [Fact]
    public async Task HeaderFieldsAndLineCountAreValidated()
    {
        await using var db = Context(TestCompanies.Next());
        var line = new OtherReceiptLineInput(1, 1, 1, null, 1m, 0m);
        var service = Service(db);

        Assert.Contains("businessDate", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(new OtherReceiptInput(default, null, [line]), Ct))).Errors.Keys);
        Assert.Contains("remark", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(new OtherReceiptInput(Day, new string('x', 501), [line]), Ct))).Errors.Keys);
        Assert.Contains("lines", (await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new OtherReceiptInput(Day, null, [.. Enumerable.Range(1, OtherReceiptService.MaxLines + 1).Select(i => line with { LineNo = i })]), Ct))).Errors.Keys);
    }

    [Fact]
    public async Task ForeignOrMismatchedReferencesAreRejectedWithoutPartialSave()
    {
        var companyA = TestCompanies.Next();
        var companyB = TestCompanies.Next();
        await using var dbA = Context(companyA);
        var m = await SetupAsync(dbA, companyA);
        await using var dbB = Context(companyB);
        var mine = await SetupAsync(dbB, companyB);
        var service = Service(dbB);

        var foreignItem = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(new OtherReceiptInput(Day, null,
            [new OtherReceiptLineInput(1, mine.Item, mine.Warehouse, null, 1m, 0m), new OtherReceiptLineInput(2, m.Item, mine.Warehouse, null, 1m, 0m)]), Ct));
        Assert.Equal("MD.ITEM.NOT_FOUND", foreignItem.ErrorCode);
        var wrongLocation = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(new OtherReceiptInput(Day, null,
            [new OtherReceiptLineInput(1, mine.Item, mine.Warehouse, mine.LocationB, 1m, 0m)]), Ct));
        Assert.Equal("MD.STORAGE_LOCATION.NOT_FOUND", wrongLocation.ErrorCode);
        Assert.False(await dbB.Set<OtherReceipt>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AnyAsync(Ct));

        var draft = await service.CreateAsync(Input(mine), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(draft.Id, new OtherReceiptInput(Day, null,
            [new OtherReceiptLineInput(1, mine.Item, m.Warehouse, null, 1m, 0m)]), draft.Version, Ct));
        dbB.ChangeTracker.Clear();
        Assert.Equal(2, (await service.GetAsync(draft.Id, Ct)).Receipt.Lines.Count);
    }

    [Fact]
    public async Task ImmediateApprovalPostsStockInTheSubmitTransactionAndFreezesTheReceipt()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var workflow = new FakeWorkflow { Next = ApprovalStatus.Approved };
        var service = Service(db, workflow);
        var draft = await service.CreateAsync(Input(m), Ct);

        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(draft.Id, draft.Version + 1, Ct));
        var submitted = await service.SubmitAsync(draft.Id, draft.Version, Ct);

        Assert.Equal(InventoryDocumentStatus.Posted, submitted.Receipt.Status);
        Assert.Equal(Now, submitted.Receipt.PostedAt);
        Assert.Equal(ApprovalStatus.Approved, submitted.Approval!.Status);
        var submission = Assert.Single(workflow.Submissions);
        Assert.Equal(new ApprovalSubmission(OtherReceiptService.DocumentType, draft.Id, "OR-0001", 126.5432m, "Found in audit"), submission);
        Assert.Equal(10.123456m, await OnHandAsync(db, m.Item, m.Warehouse, m.LocationA));
        Assert.Equal(3m, await OnHandAsync(db, m.Other, m.Second, null));
        var movements = await db.Set<StockMovement>().AsNoTracking().Where(x => x.SourceId == draft.Id).OrderBy(x => x.SourceLineNo).ToListAsync(Ct);
        Assert.All(movements, x => Assert.Equal((OtherReceiptService.SourceType, StockMovementDirection.In, Day), (x.SourceType, x.Direction, x.BusinessDate)));
        Assert.Equal([12.5m, 0m], movements.Select(x => x.UnitCost!.Value));

        var version = submitted.Receipt.Version;
        Assert.Equal(OtherReceiptService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, version, Ct))).ErrorCode);
        Assert.Equal(OtherReceiptService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(draft.Id, Input(m), version, Ct))).ErrorCode);
        Assert.Equal(OtherReceiptService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, version, Ct))).ErrorCode);
        Assert.Single(workflow.Submissions);
    }

    [Fact]
    public async Task PendingReceiptIsPostedOnceByTheFinalApprovalHandler()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var workflow = new FakeWorkflow { Next = ApprovalStatus.Pending };
        var service = Service(db, workflow);
        var draft = await service.CreateAsync(Input(m), Ct);
        var pending = (await service.SubmitAsync(draft.Id, draft.Version, Ct)).Receipt;

        Assert.Equal(InventoryDocumentStatus.PendingApproval, pending.Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(Ct));
        Assert.Equal(OtherReceiptService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(draft.Id, Input(m), pending.Version, Ct))).ErrorCode);
        Assert.Equal(OtherReceiptService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, pending.Version, Ct))).ErrorCode);

        var handler = Handler(db);
        Assert.Equal(OtherReceiptService.DocumentType, handler.DocumentType);
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            await handler.HandleAsync(new ApprovalCompleted(1, OtherReceiptService.DocumentType, draft.Id, ApprovalStatus.Approved, 8, null), Ct);
            await db.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }

        db.ChangeTracker.Clear();
        Assert.Equal(InventoryDocumentStatus.Posted, (await service.GetAsync(draft.Id, Ct)).Receipt.Status);
        Assert.Equal(10.123456m, await OnHandAsync(db, m.Item, m.Warehouse, m.LocationA));
        var replay = await Assert.ThrowsAsync<ConflictException>(() => Handler(db).HandleAsync(
            new ApprovalCompleted(1, OtherReceiptService.DocumentType, draft.Id, ApprovalStatus.Approved, 8, null), Ct));
        Assert.Equal(OtherReceiptService.ApprovalStateChanged, replay.ErrorCode);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(Ct));
    }

    [Fact]
    public async Task RejectedReceiptKeepsStockUnchangedAndCanBeRevisedAndResubmitted()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var workflow = new FakeWorkflow { Next = ApprovalStatus.Pending };
        var service = Service(db, workflow);
        var draft = await service.CreateAsync(Input(m), Ct);
        await service.SubmitAsync(draft.Id, draft.Version, Ct);

        await Handler(db).HandleAsync(new ApprovalCompleted(1, OtherReceiptService.DocumentType, draft.Id, ApprovalStatus.Rejected, 8, "Wrong qty"), Ct);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var rejected = (await service.GetAsync(draft.Id, Ct)).Receipt;
        Assert.Equal(InventoryDocumentStatus.Rejected, rejected.Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(Ct));
        Assert.Equal(OtherReceiptService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, rejected.Version, Ct))).ErrorCode);
        Assert.Equal(OtherReceiptService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DeleteAsync(draft.Id, rejected.Version, Ct))).ErrorCode);

        var revised = await service.UpdateAsync(draft.Id, new OtherReceiptInput(Day, "Recounted",
            [new OtherReceiptLineInput(1, m.Item, m.Warehouse, m.LocationA, 4m, 1m)]), rejected.Version, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, revised.Status);
        workflow.Next = ApprovalStatus.Approved;
        var posted = await service.SubmitAsync(draft.Id, revised.Version, Ct);

        Assert.Equal(InventoryDocumentStatus.Posted, posted.Receipt.Status);
        Assert.Equal(2, workflow.Submissions.Count);
        Assert.Equal(4m, workflow.Submissions[1].Amount);
        Assert.Equal(4m, await OnHandAsync(db, m.Item, m.Warehouse, m.LocationA));
        Assert.Equal(0m, await OnHandAsync(db, m.Other, m.Second, null));
    }

    [Fact]
    public async Task PostingFailureRollsBackTheApprovalAndTheReceiptState()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var workflow = new FakeWorkflow { Next = ApprovalStatus.Pending };
        var service = Service(db, workflow);
        var pendingDraft = await service.CreateAsync(Input(m), Ct);
        await service.SubmitAsync(pendingDraft.Id, pendingDraft.Version, Ct);
        var immediateDraft = await service.CreateAsync(Input(m), Ct);
        var items = new ItemService(db);
        var item = await db.Set<Item>().AsNoTracking().SingleAsync(x => x.Id == m.Item, Ct);
        await items.UpdateAsync(item.Id, item.Name, item.Specification, item.BaseUomId, item.BrandId, item.CategoryId, false, item.Version, Ct);

        // 模擬 SYS 審批交易：處理器過帳失敗時整個交易回滾
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Handler(db).HandleAsync(
                new ApprovalCompleted(1, OtherReceiptService.DocumentType, pendingDraft.Id, ApprovalStatus.Approved, 8, null), Ct));
            Assert.Equal("MD.ITEM.INACTIVE", error.ErrorCode);
            await transaction.RollbackAsync(Ct);
        }

        db.ChangeTracker.Clear();
        Assert.Equal(InventoryDocumentStatus.PendingApproval, (await service.GetAsync(pendingDraft.Id, Ct)).Receipt.Status);

        workflow.Next = ApprovalStatus.Approved;
        Assert.Equal("MD.ITEM.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(immediateDraft.Id, immediateDraft.Version, Ct))).ErrorCode);
        db.ChangeTracker.Clear();
        var unchanged = (await service.GetAsync(immediateDraft.Id, Ct)).Receipt;
        Assert.Equal((InventoryDocumentStatus.Draft, immediateDraft.Version), (unchanged.Status, unchanged.Version));
        Assert.False(await db.Set<StockMovement>().AnyAsync(Ct));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentEditAndSubmitOfTheSameVersionLetOnlyOneWin()
    {
        var companyId = TestCompanies.Next();
        await using var setup = Context(companyId);
        var m = await SetupAsync(setup, companyId);
        var draft = await Service(setup).CreateAsync(Input(m), Ct);
        await using var editing = Context(companyId);
        await using var submitting = Context(companyId);
        var revised = new OtherReceiptInput(Day, "Edited", [new OtherReceiptLineInput(1, m.Item, m.Warehouse, m.LocationA, 1m, 1m)]);

        var edit = Task.Run(() => Service(editing).UpdateAsync(draft.Id, revised, draft.Version, Ct), Ct);
        var submit = Task.Run(() => Service(submitting, new FakeWorkflow { Next = ApprovalStatus.Approved }).SubmitAsync(draft.Id, draft.Version, Ct), Ct);
        var outcomes = await Task.WhenAll(Capture(edit), Capture(submit));

        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is ConflictException);
        await using var check = Context(companyId);
        var final = (await Service(check).GetAsync(draft.Id, Ct)).Receipt;
        var onHand = await OnHandAsync(check, m.Item, m.Warehouse, m.LocationA);
        if (outcomes[0] is null)
            Assert.Equal((InventoryDocumentStatus.Draft, "Edited", 0m), (final.Status, final.Remark, onHand));
        else
            Assert.Equal((InventoryDocumentStatus.Posted, "Found in audit", 10.123456m), (final.Status, final.Remark, onHand));

        static async Task<Exception?> Capture(Task task)
        {
            try { await task; return null; }
            catch (Exception ex) { return ex; }
        }
    }

    [Fact]
    public async Task ReceiptsAreInvisibleAndImmutableFromAnotherCompany()
    {
        var companyA = TestCompanies.Next();
        await using var dbA = Context(companyA);
        var m = await SetupAsync(dbA, companyA);
        var draft = await Service(dbA).CreateAsync(Input(m), Ct);

        await using var dbB = Context(TestCompanies.Next());
        var serviceB = Service(dbB, new FakeWorkflow { Next = ApprovalStatus.Approved });
        Assert.Equal(0, (await serviceB.ListAsync(new OtherReceiptQuery(), Ct)).TotalCount);
        Assert.Equal(OtherReceiptService.NotFound, (await Assert.ThrowsAsync<NotFoundException>(() => serviceB.GetAsync(draft.Id, Ct))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => serviceB.UpdateAsync(draft.Id, Input(m), draft.Version, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => serviceB.DeleteAsync(draft.Id, draft.Version, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => serviceB.SubmitAsync(draft.Id, draft.Version, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Handler(dbB).HandleAsync(
            new ApprovalCompleted(1, OtherReceiptService.DocumentType, draft.Id, ApprovalStatus.Approved, 8, null), Ct));
        await using var none = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(none).ListAsync(new OtherReceiptQuery(), Ct));
    }

    /// <summary>INV-LOT-011：草稿可暫缺批序號、送審檢查完整、整組替換、核准過帳建立批號／序號（ADR-060 第 8 點）。</summary>
    [Fact]
    public async Task LotAndSerialNumbersAreCapturedOnDraftsCheckedOnSubmitAndPosted()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var m = await SetupAsync(db, companyId);
        var uom = await db.Set<Item>().Where(x => x.Id == m.Item).Select(x => x.BaseUomId).SingleAsync(Ct);
        var lotItem = (await new ItemService(db).CreateAsync("LT" + companyId, "Lot", null, uom, null, null, ItemTrackingMode.Lot, Ct)).Id;
        var serialItem = (await new ItemService(db).CreateAsync("SN" + companyId, "Serial", null, uom, null, null, ItemTrackingMode.Serial, Ct)).Id;
        var workflow = new FakeWorkflow { Next = ApprovalStatus.Pending };
        var service = Service(db, workflow);
        var made = new DateOnly(2026, 9, 1);

        Assert.Contains("lines[0].tracking", (await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new OtherReceiptInput(Day, null, [new OtherReceiptLineInput(1, m.Item, m.Warehouse, null, 1m, 1m, [InventoryTrackingLine.Lot("L", 1m)])]), Ct))).Errors.Keys);
        Assert.Contains("lines[0].tracking[0].quantity", (await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new OtherReceiptInput(Day, null, [new OtherReceiptLineInput(1, serialItem, m.Warehouse, null, 2m, 1m, [new InventoryTrackingLine(null, "S", 2m)])]), Ct))).Errors.Keys);

        var draft = await service.CreateAsync(new OtherReceiptInput(Day, null,
        [
            new OtherReceiptLineInput(1, lotItem, m.Warehouse, m.LocationA, 5m, 2m, [InventoryTrackingLine.Lot(" L1 ", 3m, made)]),
            new OtherReceiptLineInput(2, serialItem, m.Warehouse, m.LocationA, 2m, 9m),
        ]), Ct);
        Assert.Equal("INV.TRACKING.REQUIRED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        Assert.Empty(workflow.Submissions);

        var revised = await service.UpdateAsync(draft.Id, new OtherReceiptInput(Day, null,
        [
            new OtherReceiptLineInput(1, lotItem, m.Warehouse, m.LocationA, 5m, 2m,
                [InventoryTrackingLine.Lot("L1", 3m, made), InventoryTrackingLine.Lot("L2", 2m)]),
            new OtherReceiptLineInput(2, serialItem, m.Warehouse, m.LocationA, 2m, 9m,
                [InventoryTrackingLine.Serial("S-2"), InventoryTrackingLine.Serial("S-1")]),
        ]), draft.Version, Ct);
        Assert.Equal(4, await db.Set<OtherReceiptLineTracking>().CountAsync(Ct));
        await service.SubmitAsync(draft.Id, revised.Version, Ct);
        Assert.False(await db.Set<StockLot>().AnyAsync(Ct));

        await Handler(db).HandleAsync(new ApprovalCompleted(1, OtherReceiptService.DocumentType, draft.Id, ApprovalStatus.Approved, 8, null), Ct);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();

        var posted = (await service.GetAsync(draft.Id, Ct)).Receipt;
        Assert.Equal(InventoryDocumentStatus.Posted, posted.Status);
        var lines = posted.Lines.OrderBy(x => x.LineNo).ToList();
        Assert.Equal([("L1", 3m, (DateOnly?)made), ("L2", 2m, null)], lines[0].Tracking.Select(x => (x.LotNo!, x.Quantity, x.ManufactureDate)));
        Assert.Equal(["S-2", "S-1"], lines[1].Tracking.Select(x => x.SerialNo!));
        Assert.Equal(["L1", "L2"], await db.Set<StockLot>().OrderBy(x => x.LotNo).Select(x => x.LotNo).ToListAsync(Ct));
        Assert.Equal(made, await db.Set<StockLot>().Where(x => x.LotNo == "L1").Select(x => x.ManufactureDate).SingleAsync(Ct));
        Assert.Equal(2, await db.Set<StockSerial>().CountAsync(x => x.ItemId == serialItem, Ct));
        Assert.Equal(4, await db.Set<StockMovementTracking>().CountAsync(Ct));
        Assert.Equal(5m, await OnHandAsync(db, lotItem, m.Warehouse, m.LocationA));

        // 同序號再入庫：送審即立即核准並過帳時回 409，單據與審批一起回滾
        workflow.Next = ApprovalStatus.Approved;
        var duplicate = await service.CreateAsync(new OtherReceiptInput(Day, null,
            [new OtherReceiptLineInput(1, serialItem, m.Second, null, 1m, 9m, [InventoryTrackingLine.Serial("s-1")])]), Ct);
        Assert.Equal("INV.SERIAL.IN_STOCK", (await Assert.ThrowsAsync<ConflictException>(() =>
            service.SubmitAsync(duplicate.Id, duplicate.Version, Ct))).ErrorCode);
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(duplicate.Id, Ct)).Receipt.Status);
    }

    private sealed record Master(long Item, long Other, long Warehouse, long Second, long LocationA, long LocationB);

    private sealed class FakeNumbers : INumberGenerator
    {
        public List<(string DocumentType, DateOnly Date)> Calls { get; } = [];

        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default)
        {
            Calls.Add((documentType, documentDate));
            return Task.FromResult($"OR-{Calls.Count:0000}");
        }
    }

    private sealed class FakeWorkflow : IApprovalWorkflowService
    {
        public ApprovalStatus Next { get; set; } = ApprovalStatus.Pending;
        public List<ApprovalSubmission> Submissions { get; } = [];
        private ApprovalState? last;

        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default)
        {
            Submissions.Add(submission);
            last = new ApprovalState(Submissions.Count, submission.DocumentType, submission.DocumentId, Next,
                Next == ApprovalStatus.Pending ? 1 : null, Next == ApprovalStatus.Pending ? 1 : 0);
            return Task.FromResult(last);
        }

        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(last?.DocumentId == documentId ? last : null);
    }
}
