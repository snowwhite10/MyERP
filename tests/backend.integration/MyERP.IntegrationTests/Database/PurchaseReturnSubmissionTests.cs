using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-007c4（REQ_F4114.7.3、BR-PUR-030～034／044、ADR-225，PUR-RET-SUB-001～009）：退貨 SUBMIT 與 SYS 決定以真實 SQL、
/// 真實 SYS 審批引擎與真實 INV 驗收——SUBMIT 在「採購單 → 退貨 → 收貨行升冪」鎖內重驗可退量與已開票限制、追蹤物料擷取須完整，
/// 把本單量加到收貨行 pending_return_reserved；最終核准（或零步驟）同交易經 INV PostPurchaseReturnAsync 出庫、保存原入庫成本，
/// 預留轉 posted_returned；駁回只釋回本單；INV 失敗或重放整筆回滾；重複決定 409；雙連線不超退。
/// </summary>
public sealed partial class PurchaseReturnSubmissionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Today = new(2028, 3, 1, 9, 30, 0);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Today);

    [Fact]
    public async Task SubmitReservesAndFinalApprovalPostsOutboundAtOriginalCost()
    {
        var s = await SeedAsync(withFlow: true);
        var order = await SeedOrderAsync(s, (s.ItemA, 5m), (s.ItemB, 3m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null), (1, 3m, null));
        var draft = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 2m), L(2, receipt.LineIds[1], 3m));

        await using (var db = NewDb(s))
        {
            var submitted = await Submission(db, s).SubmitAsync(draft.Id, draft.Version, Ct);
            Assert.Equal(("PendingApproval", ApprovalStatus.Pending), (submitted.Status, submitted.Approval.Status));
            Assert.True(submitted.Version > draft.Version);
        }
        await using (var check = NewDb(s))
        {
            Assert.Equal([(2m, 0m), (3m, 0m)], await CountersAsync(check, receipt.LineIds));
            Assert.Empty(await ReturnMovementsAsync(check, draft.Id));
            Assert.Equal(ApprovalStatus.Pending, (await Instances(check, draft.Id).SingleAsync(Ct)).Status);
        }

        Assert.Equal(ApprovalStatus.Approved, (await DecideAsync(s, draft.Id, approve: true)).Status);
        await using (var check = NewDb(s))
        {
            Assert.Equal("Posted", await StatusAsync(check, draft.Id));
            Assert.Equal([(0m, 2m), (0m, 3m)], await CountersAsync(check, receipt.LineIds));
            var moves = await ReturnMovementsAsync(check, draft.Id);
            Assert.Equal([(1, s.ItemA, s.Warehouse, 2m), (2, s.ItemB, s.Warehouse, 3m)],
                moves.Select(x => (x.SourceLineNo, x.ItemId, x.WarehouseId, x.Quantity)).ToArray());
            Assert.All(moves, x => Assert.Equal((StockMovementDirection.Out, (decimal?)null, Day),
                (x.Direction, x.UnitCost, x.BusinessDate)));
            var read = await Drafts(check, s).GetAsync(draft.Id, Ct);
            Assert.Equal([4m, 4m], read.Lines.Select(x => x.OriginalUnitCost!.Value).ToArray());
            Assert.Equal([3m, 0m], read.Lines.Select(x => x.ReturnableQuantity).ToArray());
        }
    }

    [Fact]
    public async Task ZeroStepSubmitPostsTheReturnInTheSameTransaction()
    {
        var s = await SeedAsync(withFlow: false);
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null));
        var draft = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 1.5m, s.Warehouse));

        await using (var db = NewDb(s))
        {
            var result = await Submission(db, s).SubmitAsync(draft.Id, draft.Version, Ct);
            Assert.Equal(("Posted", ApprovalStatus.Approved), (result.Status, result.Approval.Status));
        }
        await using var check = NewDb(s);
        Assert.Equal([(0m, 1.5m)], await CountersAsync(check, receipt.LineIds));
        var move = Assert.Single(await ReturnMovementsAsync(check, draft.Id));
        Assert.Equal((s.ItemA, 1.5m, StockMovementDirection.Out), (move.ItemId, move.Quantity, move.Direction));
        Assert.Equal(4m, Assert.Single((await Drafts(check, s).GetAsync(draft.Id, Ct)).Lines).OriginalUnitCost);
    }

    [Fact]
    public async Task RejectionReleasesOnlyThisReturnAndRepeatedDecisionsAreConflicts()
    {
        var s = await SeedAsync(withFlow: true);
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null));
        var mine = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 2m));
        var other = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 1m));
        await SubmitAsync(s, mine, "PendingApproval");
        await SubmitAsync(s, other, "PendingApproval");

        Assert.Equal(ApprovalStatus.Rejected, (await DecideAsync(s, mine.Id, approve: false)).Status);
        await using (var check = NewDb(s))
        {
            Assert.Equal("Rejected", await StatusAsync(check, mine.Id));
            Assert.Equal([(1m, 0m)], await CountersAsync(check, receipt.LineIds));
        }
        foreach (var status in new[] { ApprovalStatus.Approved, ApprovalStatus.Rejected })
        {
            await using var db = NewDb(s);
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            var error = await Assert.ThrowsAsync<ConflictException>(() => Handler(db).HandleAsync(
                new ApprovalCompleted(1, "PUR.RETURN", mine.Id, status, s.ApproverId, null, null), Ct));
            Assert.Equal(PurchaseReturnApprovalResultHandler.StateChanged, error.ErrorCode);
        }

        // A rejected return is revised back to Draft by EDIT and can reserve again.
        PurchaseReturnReadDetail revised;
        await using (var db = NewDb(s))
            revised = await Drafts(db, s).UpdateAsync(mine.Id, (await Drafts(db, s).GetAsync(mine.Id, Ct)).Version,
                new PurchaseReturnSaveInput(order.Id, Day, [new(1, receipt.LineIds[0], 4m)]), Ct);
        await SubmitAsync(s, revised, "PendingApproval");
        await using var final = NewDb(s);
        Assert.Equal([(5m, 0m)], await CountersAsync(final, receipt.LineIds));
        Assert.Empty(await ReturnMovementsAsync(final, mine.Id));
    }

    [Fact]
    public async Task SubmitRefusalsLeaveZeroChanges()
    {
        var s = await SeedAsync(withFlow: true);
        var order = await SeedOrderAsync(s, (s.ItemA, 5m), (s.ItemB, 3m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null), (1, 3m, null));
        var first = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 3m));
        var second = await CreateDraftAsync(s, order, L(1, receipt.LineIds[1], 1m), L(2, receipt.LineIds[0], 3m));
        var invoiced = await CreateDraftAsync(s, order, L(1, receipt.LineIds[1], 2m));
        var foreign = await CreateDraftForAsync(s, order, s.UserId + 1, new PurchaseReturnSaveLine(1, receipt.LineIds[1], 1m));

        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Submission(db, s, grants: [PurchasingPermissions.ReturnEdit]).SubmitAsync(first.Id, first.Version, Ct));
            await Assert.ThrowsAsync<ConflictException>(() => Submission(db, s).SubmitAsync(first.Id, first.Version + 1, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Submission(db, s, scope: DataScope.Self).SubmitAsync(foreign.Id, foreign.Version, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => Submission(db, s).SubmitAsync(first.Id + 100000, 1, Ct));
        }
        await SubmitAsync(s, first, "PendingApproval");
        await using (var db = NewDb(s))
        {
            var again = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Submission(db, s).SubmitAsync(first.Id, first.Version + 1, Ct));
            Assert.Equal(PurchaseReturnSubmissionService.NotSubmittable, again.ErrorCode);

            // The second draft was valid when saved; the first return's reservation now leaves 2 on receipt line 1.
            var over = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Submission(db, s).SubmitAsync(second.Id, second.Version, Ct));
            Assert.Equal(PurchaseReturnDraftService.QuantityExceeded, over.ErrorCode);
            Assert.Equal([2], (int[])over.Extensions!["lineNos"]!);
        }

        // Quantity already invoiced can only leave through an FI credit note (BR-PUR-044).
        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_receipt_line SET posted_invoiced=2 WHERE id={receipt.LineIds[1]}", Ct);
        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Submission(db, s).SubmitAsync(invoiced.Id, invoiced.Version, Ct));
            Assert.Equal(PurchaseReturnInvoicedLimit.ExceedsUninvoiced, error.ErrorCode);
            Assert.Equal([1], (int[])error.Extensions!["lineNos"]!);
        }

        await using var check = NewDb(s);
        Assert.Equal([(3m, 0m), (0m, 0m)], await CountersAsync(check, receipt.LineIds));
        Assert.Equal(["PendingApproval", "Draft", "Draft", "Draft"],
            await check.Set<PurchaseReturn>().AsNoTracking().Where(x => new[] { first.Id, second.Id, invoiced.Id, foreign.Id }.Contains(x.Id)).OrderBy(x => x.Id).Select(x => x.Status).ToArrayAsync(Ct));
        Assert.Equal(1, await check.Set<ApprovalInstance>().CountAsync(x => x.DocumentType == "PUR.RETURN"
            && new[] { first.Id, second.Id, invoiced.Id, foreign.Id }.Contains(x.DocumentId), Ct));
    }

    [Fact]
    public async Task TrackedLinesMustCarryCompleteCaptureAndPostTheirLots()
    {
        var s = await SeedAsync(withFlow: false);
        var order = await SeedOrderAsync(s, (s.LotItem, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, [("L1", 3m), ("L2", 2m)]));
        var draft = await CreateDraftAsync(s, order,
            new PurchaseReturnSaveLine(1, receipt.LineIds[0], 4m, null, [new("L1", null, 3m)]));

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Submission(db, s).SubmitAsync(draft.Id, draft.Version, Ct));
            Assert.Equal(PurchaseReturnSubmissionService.TrackingRequired, error.ErrorCode);
            Assert.Equal([1], (int[])error.Extensions!["lineNos"]!);
        }
        await using (var check = NewDb(s)) Assert.Equal([(0m, 0m)], await CountersAsync(check, receipt.LineIds));

        PurchaseReturnReadDetail complete;
        await using (var db = NewDb(s))
            complete = await Drafts(db, s).UpdateAsync(draft.Id, draft.Version, new PurchaseReturnSaveInput(order.Id, Day,
                [new(1, receipt.LineIds[0], 4m, null, [new("L1", null, 3m), new("L2", null, 1m)])]), Ct);
        await SubmitAsync(s, complete, "Posted");
        await using var final = NewDb(s);
        Assert.Equal([(0m, 4m)], await CountersAsync(final, receipt.LineIds));
        var move = Assert.Single(await ReturnMovementsAsync(final, draft.Id));
        Assert.Equal(4m, move.Quantity);
        Assert.Equal([("L1", 3m), ("L2", 1m)], (await final.Set<StockMovementTracking>().AsNoTracking()
                .Where(x => x.MovementId == move.Id).Join(final.Set<StockLot>().AsNoTracking(), m => m.LotId, l => (long?)l.Id,
                    (m, l) => new { l.LotNo, m.Quantity }).OrderBy(x => x.LotNo).ToListAsync(Ct))
            .Select(x => (x.LotNo, x.Quantity)).ToArray());
    }

    [Fact]
    public async Task InventoryFailureDuringFinalApprovalRollsBackEverythingAndRetryPostsOnce()
    {
        var s = await SeedAsync(withFlow: true);
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null));
        var draft = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 3m));
        await SubmitAsync(s, draft, "PendingApproval");

        var fault = new FailCommand("INSERT INTO [inv_stock_movement]");
        await Assert.ThrowsAnyAsync<Exception>(() => DecideAsync(s, draft.Id, approve: true, fault));
        Assert.True(fault.Fired);
        await using (var check = NewDb(s))
        {
            Assert.Equal(ApprovalStatus.Pending, (await Instances(check, draft.Id).SingleAsync(Ct)).Status);
            Assert.Equal("PendingApproval", await StatusAsync(check, draft.Id));
            Assert.Equal([(3m, 0m)], await CountersAsync(check, receipt.LineIds));
            Assert.Empty(await ReturnMovementsAsync(check, draft.Id));
            Assert.Null(Assert.Single((await Drafts(check, s).GetAsync(draft.Id, Ct)).Lines).OriginalUnitCost);
        }

        Assert.Equal(ApprovalStatus.Approved, (await DecideAsync(s, draft.Id, approve: true)).Status);
        await using var final = NewDb(s);
        Assert.Equal([(0m, 3m)], await CountersAsync(final, receipt.LineIds));
        Assert.Single(await ReturnMovementsAsync(final, draft.Id));
    }

    [Fact]
    public async Task ExistingInventoryReturnPostingIsAConflictNotASecondIssue()
    {
        var s = await SeedAsync(withFlow: true);
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null));
        var draft = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 2m));
        await SubmitAsync(s, draft, "PendingApproval");
        await using (var db = NewDb(s))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await Inventory(db).PostPurchaseReturnAsync(draft.Id, Day,
                [new PurchaseReturnOutboundLine(1, receipt.MovementIds[0], s.ItemA, s.Warehouse, null, 2m)], Ct);
            await tx.CommitAsync(Ct);
        }

        var error = await Assert.ThrowsAsync<ConflictException>(() => DecideAsync(s, draft.Id, approve: true));
        Assert.Equal(PurchaseReturnPosting.PostingConflict, error.ErrorCode);
        await using var check = NewDb(s);
        Assert.Equal("PendingApproval", await StatusAsync(check, draft.Id));
        Assert.Equal([(2m, 0m)], await CountersAsync(check, receipt.LineIds));
        Assert.Single(await ReturnMovementsAsync(check, draft.Id));
    }

    [Fact]
    public async Task ConcurrentSubmitsOnTwoConnectionsNeverOverReturn()
    {
        var s = await SeedAsync(withFlow: true);
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null));
        var a = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 4m));
        var b = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 4m));

        async Task<Exception?> Run(PurchaseReturnReadDetail draft)
        {
            await using var db = NewDb(s);
            try { await Submission(db, s).SubmitAsync(draft.Id, draft.Version, Ct); return null; }
            catch (Exception ex) { return ex; }
        }
        var results = await Task.WhenAll(Run(a), Run(b));
        Assert.Single(results, x => x is null);
        Assert.Equal(PurchaseReturnDraftService.QuantityExceeded,
            Assert.IsType<BusinessRuleException>(Assert.Single(results, x => x is not null)).ErrorCode);
        await using var check = NewDb(s);
        Assert.Equal([(4m, 0m)], await CountersAsync(check, receipt.LineIds));
    }

    [Fact]
    public async Task ApprovalRechecksTheInvoicedLimitExcludingItsOwnReservation()
    {
        var s = await SeedAsync(withFlow: true);
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null));
        var draft = await CreateDraftAsync(s, order, L(1, receipt.LineIds[0], 3m));
        await SubmitAsync(s, draft, "PendingApproval");
        // An invoice matched after SUBMIT uses the remaining 2: counting the return's own 3 again would refuse
        // (5 - 3 - 3 - 2 < 0); excluding it leaves exactly 3 (BR-PUR-044, CurrentReturnReserved), so approval posts.
        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_receipt_line SET matched_reserved=2 WHERE id={receipt.LineIds[0]}", Ct);
        Assert.Equal(ApprovalStatus.Approved, (await DecideAsync(s, draft.Id, approve: true)).Status);
        await using var check = NewDb(s);
        Assert.Equal([(0m, 3m)], await CountersAsync(check, receipt.LineIds));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Seeded(long CompanyId, long UserId, long ApproverId, long ItemA, long ItemB, long LotItem,
        long Warehouse);

    private sealed record Order(long Id, long[] LineIds);

    private sealed record Receipt(long Id, long[] LineIds, long[] MovementIds);

    private static IQueryable<ApprovalInstance> Instances(MyErpDbContext db, long returnId) =>
        db.Set<ApprovalInstance>().AsNoTracking().Where(x => x.DocumentType == "PUR.RETURN" && x.DocumentId == returnId);

    private static Task<List<StockMovement>> ReturnMovementsAsync(MyErpDbContext db, long returnId) =>
        db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.SourceType == ExternalInventorySourceTypes.PurchaseReturn && x.SourceId == returnId)
            .OrderBy(x => x.SourceLineNo).ToListAsync(Ct);

    private static async Task<(decimal Pending, decimal Returned)[]> CountersAsync(MyErpDbContext db, long[] lineIds) =>
        (await db.Set<PurchaseReceiptSourceLine>().AsNoTracking().Where(x => lineIds.Contains(x.Id))
            .OrderBy(x => x.Id).Select(x => new { x.PendingReturnReserved, x.PostedReturned }).ToListAsync(Ct))
        .Select(x => (x.PendingReturnReserved, x.PostedReturned)).ToArray();

    private static async Task<string> StatusAsync(MyErpDbContext db, long returnId)
    {
        return await db.Set<PurchaseReturn>().AsNoTracking().IgnoreQueryFilters().Where(x => x.Id == returnId)
            .Select(x => x.Status).SingleAsync(Ct);
    }

    private static PurchaseReturnSaveLine L(int lineNo, long receiptLineId, decimal quantity, long? warehouseId = null) =>
        new(lineNo, receiptLineId, quantity, warehouseId);

    private Task<PurchaseReturnReadDetail> CreateDraftAsync(Seeded s, Order order, params PurchaseReturnSaveLine[] lines) =>
        CreateDraftForAsync(s, order, s.UserId, lines);

    private async Task<PurchaseReturnReadDetail> CreateDraftForAsync(Seeded s, Order order, long ownerId,
        params PurchaseReturnSaveLine[] lines)
    {
        await using var db = NewDb(s);
        return await Drafts(db, s, ownerId).CreateAsync(new PurchaseReturnSaveInput(order.Id, Day, lines), Ct);
    }

    private async Task SubmitAsync(Seeded s, PurchaseReturnReadDetail draft, string expected)
    {
        await using var db = NewDb(s);
        Assert.Equal(expected, (await Submission(db, s).SubmitAsync(draft.Id, draft.Version, Ct)).Status);
    }

    private async Task<ApprovalInstanceDetail> DecideAsync(Seeded s, long returnId, bool approve,
        DbCommandInterceptor? fault = null)
    {
        await using var db = NewDb(s, fault);
        var instance = await Instances(db, returnId).SingleAsync(x => x.Status == ApprovalStatus.Pending, Ct);
        var decision = new ApprovalDecision(approve ? null : "Wrong goods", instance.Version);
        var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(s.ApproverId),
            new AuditTrail(), [Handler(db)]);
        return approve
            ? await workflow.ApproveAsync(instance.Id, decision, Ct)
            : await workflow.RejectAsync(instance.Id, decision, Ct);
    }

    private static PurchaseReturnApprovalResultHandler Handler(MyErpDbContext db) =>
        new(db, () => new PurchaseReturnPosting(db, Inventory(db)));

    private static ExternalInventoryPostingService Inventory(MyErpDbContext db) =>
        new(db, new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
            new FixedClock(Today)));

    private static UserCompanyAuthorization User(Seeded s, long userId, DataScope scope, string[] grants) =>
        new(userId, s.CompanyId, 10, 20, false, grants.ToDictionary(x => x, _ => scope));

    private static PurchaseReturnSubmissionService Submission(MyErpDbContext db, Seeded s,
        DataScope scope = DataScope.Company, string[]? grants = null)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(s, s.UserId, scope, grants ?? [PurchasingPermissions.ReturnSubmit]));
        var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(s.UserId), new AuditTrail(), []);
        return new PurchaseReturnSubmissionService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, workflow, new PurchaseReturnPosting(db, Inventory(db)), new InventoryReferenceGuard(db));
    }

    private static PurchaseReturnDraftService Drafts(MyErpDbContext db, Seeded s, long? userId = null)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(User(s, userId ?? s.UserId,
            DataScope.Company, [PurchasingPermissions.ReturnView, PurchasingPermissions.ReturnCreate,
                PurchasingPermissions.ReturnEdit, PurchasingPermissions.ReturnDelete]));
        var numbers = new Mock<INumberGenerator>();
        numbers.Setup(x => x.NextAsync("PUR.RETURN", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"RT-{Guid.NewGuid():N}"[..20]);
        return new PurchaseReturnDraftService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, numbers.Object, new InventoryReferenceGuard(db), new PurchaseInboundEvidenceQuery(db),
            new FixedClock(Today));
    }

    private async Task<Seeded> SeedAsync(bool withFlow)
    {
        var name = "prs" + Guid.NewGuid().ToString("N")[..16];
        var user = new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
        var approverName = "prv" + Guid.NewGuid().ToString("N")[..16];
        var approver = new SystemUser { Username = approverName, NormalizedUsername = approverName.ToUpperInvariant(),
            PasswordHash = "x" };
        var company = new SystemCompany { Code = "RS" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Return submission test" };
        await using (var setup = NewDb(null, null, 7))
        {
            setup.AddRange(company, user, approver);
            await setup.SaveChangesAsync(Ct);
            setup.AddRange(new SystemUserCompany { CompanyId = company.Id, UserId = user.Id },
                new SystemUserCompany { CompanyId = company.Id, UserId = approver.Id });
            await setup.SaveChangesAsync(Ct);
        }
        await using var db = NewDb(company.Id, null, user.Id);
        var uom = await new UomService(db).CreateAsync("U" + company.Id, "Unit", 0, Ct);
        var items = new ItemService(db);
        var a = await items.CreateAsync("A" + company.Id, "Item A", null, uom.Id, null, null, Ct);
        var b = await items.CreateAsync("B" + company.Id, "Item B", null, uom.Id, null, null, Ct);
        var lot = await items.CreateAsync("L" + company.Id, "Lot item", null, uom.Id, null, null, ItemTrackingMode.Lot, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + company.Id, "Warehouse", null, null, null, Ct);
        if (withFlow)
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("PUR.RETURN", "Purchase return", false,
                [new ApprovalStepInput("Purchasing manager", ApproverType.User, null, approver.Id, null)]), Ct);
        return new Seeded(company.Id, user.Id, approver.Id, a.Id, b.Id, lot.Id, warehouse.Id);
    }

    /// <summary>Approved order; each line costs 4 base currency per unit.</summary>
    private async Task<Order> SeedOrderAsync(Seeded s, params (long Item, decimal Quantity)[] lines)
    {
        await using var db = NewDb(s);
        var total = lines.Sum(x => x.Quantity) * 4m;
        var order = new PurchaseOrderSource
        {
            CompanyId = s.CompanyId, SupplierId = 101, DocumentNo = $"PO-{Guid.NewGuid():N}"[..20], BusinessDate = Day,
            Currency = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m, Status = "Approved", OwnerId = s.UserId,
            BranchId = 10, DepartmentId = 20, BaseNet = total, BaseTax = 0m, BaseGross = total
        };
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        var rows = lines.Select((x, i) => new PurchaseOrderSourceLine
        {
            CompanyId = s.CompanyId, PurchaseOrderId = order.Id, LineNo = i + 1, MaterialId = x.Item, Quantity = x.Quantity,
            UnitPrice = 4m, TaxRatePercent = 0m, ExchangeRate = 1m, BaseNet = x.Quantity * 4m, BaseTax = 0m,
            BaseGross = x.Quantity * 4m, PostedReceived = x.Quantity
        }).ToArray();
        db.AddRange(rows);
        await db.SaveChangesAsync(Ct);
        return new Order(order.Id, rows.Select(x => x.Id).ToArray());
    }

    /// <summary>A Posted receipt whose lines were posted through the real INV purchase inbound contract at cost 4.</summary>
    private async Task<Receipt> SeedPostedReceiptAsync(Seeded s, Order order,
        params (int OrderLine, decimal Quantity, (string Lot, decimal Quantity)[]? Lots)[] lines)
    {
        long receiptId;
        long[] lineIds;
        long[] materials;
        await using (var db = NewDb(s))
        {
            var receipt = new PurchaseReceiptSource
            {
                CompanyId = s.CompanyId, PurchaseOrderId = order.Id, SupplierId = 101, Currency = "TWD",
                DocumentNo = $"GR-{Guid.NewGuid():N}"[..20], BusinessDate = Day, Status = "Posted",
                OwnerId = s.UserId, BranchId = 10, DepartmentId = 20
            };
            db.Add(receipt);
            await db.SaveChangesAsync(Ct);
            var sources = await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
                .Where(x => x.PurchaseOrderId == order.Id).ToDictionaryAsync(x => x.Id, Ct);
            var rows = lines.Select((line, i) => new PurchaseReceiptSourceLine
            {
                CompanyId = s.CompanyId, PurchaseReceiptId = receipt.Id, LineNo = i + 1,
                PurchaseOrderLineId = order.LineIds[line.OrderLine],
                MaterialId = sources[order.LineIds[line.OrderLine]].MaterialId, Quantity = line.Quantity,
                WarehouseId = s.Warehouse, UnitCost = 4m, PostedReceived = line.Quantity
            }).ToArray();
            db.AddRange(rows);
            await db.SaveChangesAsync(Ct);
            db.AddRange(rows.SelectMany((row, i) => (lines[i].Lots ?? []).Select((l, j) => new PurchaseReceiptLineTracking
            {
                CompanyId = s.CompanyId, LineId = row.Id, SeqNo = j + 1, LotNo = l.Lot, Quantity = l.Quantity
            })));
            await db.SaveChangesAsync(Ct);
            (receiptId, lineIds, materials) = (receipt.Id, rows.Select(x => x.Id).ToArray(),
                rows.Select(x => x.MaterialId).ToArray());
        }
        await using var post = NewDb(s);
        await using var tx = await post.Database.BeginTransactionAsync(Ct);
        var result = await Inventory(post).PostPurchaseInboundAsync(receiptId, Day, lines.Select((x, i) =>
            new PurchaseInboundLine(i + 1, materials[i], s.Warehouse, null, x.Quantity, 4m,
                x.Lots?.Select(l => InventoryTrackingLine.Lot(l.Lot, l.Quantity)).ToArray())).ToArray(), Ct);
        await tx.CommitAsync(Ct);
        return new Receipt(receiptId, lineIds, result.MovementIds.ToArray());
    }

    private MyErpDbContext NewDb(Seeded s, DbCommandInterceptor? fault = null) => NewDb(s.CompanyId, fault, s.UserId);

    private MyErpDbContext NewDb(long? companyId, DbCommandInterceptor? fault, long userId)
    {
        var company = new FixedCompany(companyId);
        var audit = new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Today), new FixedUser(userId), company));
        return new(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(fault is null ? [audit] : [audit, fault]).Options,
            [new PurchaseInvoiceSourceDbModel(), new PurchaseReturnDbModel(), new SystemOrganizationDbModel(),
                new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel()], company);
    }

    private sealed class FailCommand(string statement) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Check(DbCommand command)
        {
            if (command.CommandText.Contains(statement, StringComparison.OrdinalIgnoreCase))
            {
                Fired = true;
                throw new InvalidOperationException($"Injected {statement} failure.");
            }
        }
    }
}
