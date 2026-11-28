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
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-007b1（REQ_F4114.2.5、BR-PUR-030、ADR-220，PUR-RECV-POST-001～006）：收貨最終核准與零步驟 SUBMIT 以真實 SQL、
/// 真實 SYS 審批引擎與真實 INV 過帳驗收——同交易把本單待審量轉已收量、逐行寫 PURCHASE_INBOUND（保存的本位幣單位成本與倉庫）、
/// 收貨 Posted；INV 失敗或重放整筆回滾；重複決定 409 不二次過帳；未擷取批序號的追蹤物料由 SUBMIT 拒絕（007b2）。
/// </summary>
public sealed class PurchaseReceiptPostingTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Today = new(2028, 3, 1, 9, 30, 0);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Today);

    [Fact]
    public async Task FinalApprovalPostsSavedLinesAtSavedCostAndMovesPendingToReceived()
    {
        var s = await SeedAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, (s.ItemA, 5m), (s.ItemB, 2m));
        var receipt = await SeedReceiptAsync(s, orderId,
            (lineIds[0], 1m, 4m), (lineIds[0], 2m, 4.333333m), (lineIds[1], 1.5m, 7.25m));
        await SubmitAsync(s, receipt, expected: "PendingApproval");

        var decided = await DecideAsync(s, receipt.Id, approve: true);
        Assert.Equal(ApprovalStatus.Approved, decided.Status);

        await using var check = NewDb(s);
        Assert.Equal("Posted", await StatusAsync(check, receipt.Id));
        Assert.Equal((0m, 3m), await CountersAsync(check, lineIds[0]));
        Assert.Equal((0m, 1.5m), await CountersAsync(check, lineIds[1]));
        Assert.Equal([1m, 2m, 1.5m], await ReceivedAsync(check, receipt.Id));
        var moves = await MovementsAsync(check, receipt.Id);
        Assert.Equal(
            [(1, s.ItemA, s.Warehouse, 1m, (decimal?)4m), (2, s.ItemA, s.Warehouse, 2m, 4.333333m),
                (3, s.ItemB, s.Warehouse, 1.5m, 7.25m)],
            moves.Select(x => (x.SourceLineNo, x.ItemId, x.WarehouseId, x.Quantity, x.UnitCost)).ToArray());
        Assert.All(moves, x => Assert.Equal((StockMovementDirection.In, Day), (x.Direction, x.BusinessDate)));
    }

    [Fact]
    public async Task ZeroStepSubmitPostsInboundInTheSameTransaction()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lineIds) = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 2m, 4m));

        await using (var db = NewDb(s))
        {
            var result = await Submission(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct);
            Assert.Equal("Posted", result.Status);
            Assert.Equal(ApprovalStatus.Approved, result.Approval.Status);
        }
        await using var check = NewDb(s);
        Assert.Equal((0m, 2m), await CountersAsync(check, lineIds[0]));
        Assert.Equal([2m], await ReceivedAsync(check, receipt.Id));
        var move = Assert.Single(await MovementsAsync(check, receipt.Id));
        Assert.Equal((s.ItemA, 2m, (decimal?)4m), (move.ItemId, move.Quantity, move.UnitCost));
    }

    [Fact]
    public async Task InventoryFailureDuringFinalApprovalRollsBackEverythingAndRetryPostsOnce()
    {
        var s = await SeedAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 3m, 4m));
        await SubmitAsync(s, receipt, expected: "PendingApproval");

        var fault = new FailCommand("INSERT INTO [inv_stock_movement]");
        await Assert.ThrowsAsync<DbUpdateException>(() => DecideAsync(s, receipt.Id, approve: true, fault));
        Assert.True(fault.Fired);
        await using (var check = NewDb(s))
        {
            Assert.Equal(ApprovalStatus.Pending, (await Instances(check, receipt.Id).SingleAsync(Ct)).Status);
            Assert.Equal("PendingApproval", await StatusAsync(check, receipt.Id));
            Assert.Equal((3m, 0m), await CountersAsync(check, lineIds[0]));
            Assert.Equal([0m], await ReceivedAsync(check, receipt.Id));
            Assert.Empty(await MovementsAsync(check, receipt.Id));
        }

        Assert.Equal(ApprovalStatus.Approved, (await DecideAsync(s, receipt.Id, approve: true)).Status);
        await using (var check = NewDb(s))
        {
            Assert.Equal((0m, 3m), await CountersAsync(check, lineIds[0]));
            Assert.Single(await MovementsAsync(check, receipt.Id));
        }
    }

    [Fact]
    public async Task RepeatedDecisionAfterPostingIsConflictAndNeverPostsTwice()
    {
        var s = await SeedAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 2m, 4m));
        await SubmitAsync(s, receipt, expected: "PendingApproval");
        await DecideAsync(s, receipt.Id, approve: true);

        foreach (var status in new[] { ApprovalStatus.Approved, ApprovalStatus.Rejected })
        {
            await using var db = NewDb(s);
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            var error = await Assert.ThrowsAsync<ConflictException>(() => Handler(db).HandleAsync(
                new ApprovalCompleted(1, "PUR.RECEIPT", receipt.Id, status, s.ApproverId, null, null), Ct));
            Assert.Equal(PurchaseReceiptApprovalResultHandler.StateChanged, error.ErrorCode);
        }
        await using var check = NewDb(s);
        Assert.Equal((0m, 2m), await CountersAsync(check, lineIds[0]));
        Assert.Single(await MovementsAsync(check, receipt.Id));
    }

    [Fact]
    public async Task ExistingInventoryPostingForTheReceiptIsAConflictNotASecondCount()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lineIds) = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 2m, 4m));
        // A stray INV posting with identical content makes INV answer with a replay instead of new movements.
        await using (var stray = NewDb(s))
        {
            await using var tx = await stray.Database.BeginTransactionAsync(Ct);
            await RealInventory(stray).PostPurchaseInboundAsync(receipt.Id, Day,
                [new PurchaseInboundLine(1, s.ItemA, s.Warehouse, null, 2m, 4m)], Ct);
            await tx.CommitAsync(Ct);
        }

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() =>
                Submission(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct));
            Assert.Equal(PurchaseReceiptPosting.PostingConflict, error.ErrorCode);
        }
        await using var check = NewDb(s);
        Assert.Equal("Draft", await StatusAsync(check, receipt.Id));
        Assert.Equal((0m, 0m), await CountersAsync(check, lineIds[0]));
        Assert.Empty(await Instances(check, receipt.Id).ToListAsync(Ct));
        Assert.Single(await MovementsAsync(check, receipt.Id));
    }

    [Fact]
    public async Task LotTrackedItemWithoutCaptureIsRefusedAtSubmitBeforeInventory()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lineIds) = await SeedOrderAsync(s, (s.LotItem, 5m));
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 2m, 4m));

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Submission(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct));
            Assert.Equal(PurchaseReceiptTracking.Required, error.ErrorCode); // 007b2: PUR refuses before SYS/INV
        }
        await using var check = NewDb(s);
        Assert.Equal("Draft", await StatusAsync(check, receipt.Id));
        Assert.Equal((0m, 0m), await CountersAsync(check, lineIds[0]));
        Assert.Empty(await Instances(check, receipt.Id).ToListAsync(Ct));
        Assert.Empty(await MovementsAsync(check, receipt.Id));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Seeded(long CompanyId, long UserId, long ApproverId, long ItemA, long ItemB, long LotItem,
        long Warehouse);

    private static IQueryable<ApprovalInstance> Instances(MyErpDbContext db, long receiptId) =>
        db.Set<ApprovalInstance>().AsNoTracking().Where(x => x.DocumentType == "PUR.RECEIPT" && x.DocumentId == receiptId);

    private static Task<List<StockMovement>> MovementsAsync(MyErpDbContext db, long receiptId) =>
        db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.SourceType == ExternalInventorySourceTypes.PurchaseInbound && x.SourceId == receiptId)
            .OrderBy(x => x.SourceLineNo).ToListAsync(Ct);

    private static async Task<(decimal Pending, decimal Posted)> CountersAsync(MyErpDbContext db, long lineId)
    {
        var line = await db.Set<PurchaseOrderSourceLine>().AsNoTracking().SingleAsync(x => x.Id == lineId, Ct);
        return (line.PendingReceiptReserved, line.PostedReceived);
    }

    private static Task<decimal[]> ReceivedAsync(MyErpDbContext db, long receiptId) =>
        db.Set<PurchaseReceiptSourceLine>().AsNoTracking().Where(x => x.PurchaseReceiptId == receiptId)
            .OrderBy(x => x.LineNo).Select(x => x.PostedReceived).ToArrayAsync(Ct);

    private static Task<string> StatusAsync(MyErpDbContext db, long receiptId) =>
        db.Set<PurchaseReceiptSource>().AsNoTracking().Where(x => x.Id == receiptId).Select(x => x.Status).SingleAsync(Ct);

    private async Task SubmitAsync(Seeded s, PurchaseReceiptSource receipt, string expected)
    {
        await using var db = NewDb(s);
        Assert.Equal(expected, (await Submission(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct)).Status);
    }

    private async Task<ApprovalInstanceDetail> DecideAsync(Seeded s, long receiptId, bool approve,
        DbCommandInterceptor? fault = null)
    {
        await using var db = NewDb(s, fault);
        var instance = await Instances(db, receiptId).SingleAsync(Ct);
        var decision = new ApprovalDecision(approve ? null : "Quantity mismatch", instance.Version);
        var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(s.ApproverId),
            new AuditTrail(), [Handler(db)]);
        return approve
            ? await workflow.ApproveAsync(instance.Id, decision, Ct)
            : await workflow.RejectAsync(instance.Id, decision, Ct);
    }

    private static PurchaseReceiptApprovalResultHandler Handler(MyErpDbContext db) =>
        new(db, () => new PurchaseReceiptPosting(db, RealInventory(db), Tracking(db)));

    private static PurchaseReceiptTracking Tracking(MyErpDbContext db) => new(db, new InventoryReferenceGuard(db));

    /// <summary>Real INV posting through its public contract, as the composed API would build it.</summary>
    private static ExternalInventoryPostingService RealInventory(MyErpDbContext db) =>
        new(db, new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
            new FixedClock(Today)));

    private static PurchaseReceiptSubmissionService Submission(MyErpDbContext db, Seeded s)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(s.UserId, s.CompanyId, 10, 20, false,
                new Dictionary<string, DataScope> { [PurchasingPermissions.ReceiptSubmit] = DataScope.Company }));
        var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(s.UserId), new AuditTrail(), []);
        return new PurchaseReceiptSubmissionService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, workflow, new PurchaseReceiptPosting(db, RealInventory(db), Tracking(db)), Tracking(db));
    }

    private async Task<Seeded> SeedAsync(bool withFlow)
    {
        var name = "prp" + Guid.NewGuid().ToString("N")[..16];
        var user = new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
        var approverName = "prq" + Guid.NewGuid().ToString("N")[..16];
        var approver = new SystemUser { Username = approverName, NormalizedUsername = approverName.ToUpperInvariant(),
            PasswordHash = "x" };
        var company = new SystemCompany { Code = "RP" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Receipt posting test" };
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
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("PUR.RECEIPT", "Purchase receipt", false,
                [new ApprovalStepInput("Warehouse manager", ApproverType.User, null, approver.Id, null)]), Ct);
        return new Seeded(company.Id, user.Id, approver.Id, a.Id, b.Id, lot.Id, warehouse.Id);
    }

    /// <summary>Approved order; each line costs 4 base currency per unit.</summary>
    private async Task<(long OrderId, long[] LineIds)> SeedOrderAsync(Seeded s, params (long Item, decimal Quantity)[] lines)
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
            BaseGross = x.Quantity * 4m
        }).ToArray();
        db.AddRange(rows);
        await db.SaveChangesAsync(Ct);
        return (order.Id, rows.Select(x => x.Id).ToArray());
    }

    private async Task<PurchaseReceiptSource> SeedReceiptAsync(Seeded s, long orderId,
        params (long LineId, decimal Quantity, decimal UnitCost)[] lines)
    {
        await using var db = NewDb(s);
        var sources = await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            .Where(x => x.PurchaseOrderId == orderId).ToDictionaryAsync(x => x.Id, Ct);
        var receipt = new PurchaseReceiptSource
        {
            CompanyId = s.CompanyId, PurchaseOrderId = orderId, SupplierId = 101, Currency = "TWD",
            DocumentNo = $"GR-{Guid.NewGuid():N}"[..20], BusinessDate = Day, Status = "Draft",
            OwnerId = s.UserId, BranchId = 10, DepartmentId = 20
        };
        db.Add(receipt);
        await db.SaveChangesAsync(Ct);
        db.AddRange(lines.Select((line, i) => new PurchaseReceiptSourceLine
        {
            CompanyId = s.CompanyId, PurchaseReceiptId = receipt.Id, LineNo = i + 1, PurchaseOrderLineId = line.LineId,
            MaterialId = sources[line.LineId].MaterialId, Quantity = line.Quantity, WarehouseId = s.Warehouse,
            UnitCost = line.UnitCost
        }));
        await db.SaveChangesAsync(Ct);
        return receipt;
    }

    private MyErpDbContext NewDb(Seeded s, DbCommandInterceptor? fault = null) => NewDb(s.CompanyId, fault, s.UserId);

    private MyErpDbContext NewDb(long? companyId, DbCommandInterceptor? fault, long userId)
    {
        var company = new FixedCompany(companyId);
        var audit = new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Today), new FixedUser(userId), company));
        return new(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(fault is null ? [audit] : [audit, fault]).Options,
            [new RequisitionDbModel(), new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel(),
                new PurchaseInvoiceSourceDbModel(), new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(),
                new WarehouseDbModel(), new InventoryReferenceDbModel(), new StockMovementDbModel(),
                new StockTrackingDbModel()], company);
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
