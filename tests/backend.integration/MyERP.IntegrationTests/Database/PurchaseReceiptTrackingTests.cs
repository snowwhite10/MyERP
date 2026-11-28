using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
/// WU-PUR-007b2（REQ_F4114.2.2／2.5、BR-PUR-030、ADR-221，PUR-RECV-TRK-001～011）：收貨行批號／序號以真實 SQL、真實 MD 物料
/// 追蹤方式、真實 SYS 與 INV 驗收——草稿 CREATE／EDIT／READ 保存擷取（可不完整）、追蹤方式不符與跨行重複序號 422、
/// SUBMIT 不完整 422 整筆回滾、零步驟與最終核准把擷取傳給 INV 並在 inv_lot／inv_serial 可查、資料表 CHECK 與串聯刪除。
/// </summary>
public sealed class PurchaseReceiptTrackingTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Today = new(2028, 3, 1, 9, 30, 0);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Today);
    private static readonly DateOnly Made = new(2028, 2, 29);

    [Fact]
    public async Task DraftSavesTrimmedLotCaptureIncludingIncompleteLinesAndReadReturnsIt()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lines) = await SeedOrderAsync(s, (s.LotItem, 5m), (s.Plain, 2m));
        await using var db = NewDb(s);
        var created = await Drafts(db, s).CreateAsync(new(orderId, Day,
        [
            new(1, lines[0], 5m, s.Warehouse, [new(" LOT-A ", null, 2m, Made, Made.AddYears(1)), new("LOT-B", null, 1m)]),
            new(2, lines[1], 2m, s.Warehouse)
        ]), Ct);

        Assert.Equal([(1, "LOT-A", (string?)null, 2m, (DateOnly?)Made, (DateOnly?)Made.AddYears(1)),
                (2, "LOT-B", null, 1m, null, null)],
            created.Lines[0].Tracking.Select(x => (x.SeqNo, x.LotNo, x.SerialNo, x.Quantity, x.ManufactureDate,
                x.ExpiryDate)).ToArray());
        Assert.Empty(created.Lines[1].Tracking);
        await using var reread = NewDb(s);
        var read = await Drafts(reread, s).GetAsync(created.Id, Ct);
        Assert.Equal(created.Lines[0].Tracking, read.Lines[0].Tracking);
        Assert.Empty(read.Lines[1].Tracking);
    }

    [Fact]
    public async Task EditReplacesCaptureAsAWholeAndRemovedLinesLeaveNoRows()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lines) = await SeedOrderAsync(s, (s.LotItem, 5m), (s.SerialItem, 2m));
        PurchaseReceiptReadDetail created;
        await using (var db = NewDb(s))
            created = await Drafts(db, s).CreateAsync(new(orderId, Day,
            [
                new(1, lines[0], 3m, s.Warehouse, [new("OLD", null, 3m)]),
                new(2, lines[1], 2m, s.Warehouse, [new(null, "SN-1", 1m), new(null, "SN-2", 1m)])
            ]), Ct);

        await using (var db = NewDb(s))
        {
            var updated = await Drafts(db, s).UpdateAsync(created.Id, created.Version, new(orderId, Day,
                [new(1, lines[0], 3m, s.Warehouse, [new("NEW-1", null, 1m), new("NEW-2", null, 2m)])]), Ct);
            Assert.Equal(created.Version + 1, updated.Version);
            Assert.Equal(created.Lines[0].Id, Assert.Single(updated.Lines).Id);
            Assert.Equal(["NEW-1", "NEW-2"], updated.Lines[0].Tracking.Select(x => x.LotNo).ToArray());
        }
        await using var check = NewDb(s);
        var rows = await Captures(check, created.Id);
        Assert.Equal([("NEW-1", 1), ("NEW-2", 2)], rows.Select(x => (x.LotNo!, x.SeqNo)).ToArray());
    }

    [Theory]
    [InlineData("plain-lot")]
    [InlineData("lot-serial")]
    [InlineData("serial-lot")]
    [InlineData("serial-fraction")]
    public async Task CaptureThatDoesNotMatchTheItemModeIsRefusedAndNothingIsSaved(string scenario)
    {
        var s = await SeedAsync(withFlow: false);
        var (item, quantity, entry) = scenario switch
        {
            "plain-lot" => (s.Plain, 2m, new PurchaseReceiptTrackingInput("L", null, 1m)),
            "lot-serial" => (s.LotItem, 2m, new PurchaseReceiptTrackingInput(null, "S", 1m)),
            "serial-lot" => (s.SerialItem, 2m, new PurchaseReceiptTrackingInput("L", null, 1m)),
            _ => (s.SerialItem, 1.5m, new PurchaseReceiptTrackingInput(null, "S", 1m))
        };
        var (orderId, lines) = await SeedOrderAsync(s, (item, 5m));
        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Drafts(db, s).CreateAsync(
                new(orderId, Day, [new(1, lines[0], quantity, s.Warehouse, [entry])]), Ct));
            Assert.Equal(PurchaseReceiptTracking.ModeMismatch, error.ErrorCode);
            Assert.Equal(1, error.Extensions!["lineNo"]);
        }
        await using var check = NewDb(s);
        Assert.Empty(await check.Set<PurchaseReceiptSource>().AsNoTracking()
            .Where(x => x.PurchaseOrderId == orderId).ToListAsync(Ct));
    }

    [Fact]
    public async Task SameSerialOnTwoLinesOfOneItemIsRefused()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lines) = await SeedOrderAsync(s, (s.SerialItem, 5m));
        await using var db = NewDb(s);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Drafts(db, s).CreateAsync(new(orderId, Day,
        [
            new(1, lines[0], 1m, s.Warehouse, [new(null, "SN-9", 1m)]),
            new(2, lines[0], 1m, s.Warehouse, [new(null, " sn-9", 1m)])
        ]), Ct));
        Assert.Equal((PurchaseReceiptTracking.SerialDuplicate, (object?)2), (error.ErrorCode, error.Extensions!["lineNo"]));
    }

    [Fact]
    public async Task SubmitWithIncompleteCaptureIsRefusedAndRollsBack()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lines) = await SeedOrderAsync(s, (s.Plain, 2m), (s.LotItem, 5m));
        var receipt = await CreateAsync(s, orderId,
            new PurchaseReceiptSaveLine(1, lines[0], 2m, s.Warehouse),
            new PurchaseReceiptSaveLine(2, lines[1], 4m, s.Warehouse, [new("L1", null, 3m)]));

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Submission(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct));
            Assert.Equal((PurchaseReceiptTracking.Required, (object?)2), (error.ErrorCode, error.Extensions!["lineNo"]));
        }
        await AssertUntouchedAsync(s, receipt.Id, lines);
    }

    [Fact]
    public async Task ZeroStepSubmitPostsLotsWithDatesAndSerialsToInventory()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lines) = await SeedOrderAsync(s, (s.LotItem, 5m), (s.SerialItem, 2m), (s.Plain, 1m));
        var receipt = await CreateAsync(s, orderId,
            new PurchaseReceiptSaveLine(1, lines[0], 5m, s.Warehouse,
                [new("LOT-A", null, 2m, Made, Made.AddYears(1)), new("LOT-B", null, 3m)]),
            new PurchaseReceiptSaveLine(2, lines[1], 2m, s.Warehouse, [new(null, "SN-1", 1m), new(null, "SN-2", 1m)]),
            new PurchaseReceiptSaveLine(3, lines[2], 1m, s.Warehouse));

        await using (var db = NewDb(s))
            Assert.Equal("Posted", (await Submission(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct)).Status);

        await using var check = NewDb(s);
        await AssertPostedTrackingAsync(check, s, receipt.Id);
    }

    [Fact]
    public async Task FinalApprovalPassesSavedCaptureToInventory()
    {
        var s = await SeedAsync(withFlow: true);
        var (orderId, lines) = await SeedOrderAsync(s, (s.LotItem, 5m), (s.SerialItem, 2m), (s.Plain, 1m));
        var receipt = await CreateAsync(s, orderId,
            new PurchaseReceiptSaveLine(1, lines[0], 5m, s.Warehouse,
                [new("LOT-A", null, 2m, Made, Made.AddYears(1)), new("LOT-B", null, 3m)]),
            new PurchaseReceiptSaveLine(2, lines[1], 2m, s.Warehouse, [new(null, "SN-1", 1m), new(null, "SN-2", 1m)]),
            new PurchaseReceiptSaveLine(3, lines[2], 1m, s.Warehouse));
        await using (var db = NewDb(s))
            Assert.Equal("PendingApproval",
                (await Submission(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct)).Status);

        await using (var db = NewDb(s))
        {
            var instance = await db.Set<ApprovalInstance>().AsNoTracking()
                .SingleAsync(x => x.DocumentType == "PUR.RECEIPT" && x.DocumentId == receipt.Id, Ct);
            var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(s.ApproverId),
                new AuditTrail(), [new PurchaseReceiptApprovalResultHandler(db, () => Posting(db))]);
            Assert.Equal(ApprovalStatus.Approved,
                (await workflow.ApproveAsync(instance.Id, new ApprovalDecision(null, instance.Version), Ct)).Status);
        }
        await using var check = NewDb(s);
        await AssertPostedTrackingAsync(check, s, receipt.Id);
    }

    [Fact]
    public async Task SerialAlreadyInStockIsRefusedByInventoryAndTheWholeSubmitRollsBack()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lines) = await SeedOrderAsync(s, (s.SerialItem, 4m));
        var first = await CreateAsync(s, orderId,
            new PurchaseReceiptSaveLine(1, lines[0], 1m, s.Warehouse, [new(null, "SN-1", 1m)]));
        await using (var db = NewDb(s))
            await Submission(db, s).SubmitAsync(first.Id, first.Version, Ct);
        var second = await CreateAsync(s, orderId,
            new PurchaseReceiptSaveLine(1, lines[0], 1m, s.Warehouse, [new(null, "sn-1", 1m)]));

        await using (var db = NewDb(s))
            await Assert.ThrowsAnyAsync<ErpException>(() => Submission(db, s).SubmitAsync(second.Id, second.Version, Ct));
        await using var check = NewDb(s);
        Assert.Equal("Draft", await check.Set<PurchaseReceiptSource>().AsNoTracking()
            .Where(x => x.Id == second.Id).Select(x => x.Status).SingleAsync(Ct));
        var line = await check.Set<PurchaseOrderSourceLine>().AsNoTracking().SingleAsync(x => x.Id == lines[0], Ct);
        Assert.Equal((0m, 1m), (line.PendingReceiptReserved, line.PostedReceived));
        Assert.Empty(await Movements(check, second.Id));
    }

    [Fact]
    public async Task SchemaChecksRefuseInvalidRowsAndLineDeleteCascades()
    {
        var s = await SeedAsync(withFlow: false);
        var (orderId, lines) = await SeedOrderAsync(s, (s.LotItem, 5m));
        var receipt = await CreateAsync(s, orderId,
            new PurchaseReceiptSaveLine(1, lines[0], 2m, s.Warehouse, [new("L", null, 2m)]));
        var lineId = receipt.Lines[0].Id;
        await using var db = database.CreateRawContext();
        string Insert(string lot, string serial, string quantity, string seq, string made, string expiry) => $"""
            INSERT dbo.pur_purchase_receipt_line_tracking(company_id,line_id,seq_no,lot_no,serial_no,quantity,
              manufacture_date,expiry_date,created_at,created_by,version)
            VALUES ({s.CompanyId},{lineId},{seq},{lot},{serial},{quantity},{made},{expiry},'2028-03-01',1,1)
            """;
        foreach (var sql in new[]
                 {
                     Insert("NULL", "NULL", "1", "9", "NULL", "NULL"),          // neither number
                     Insert("N'L2'", "N'S2'", "1", "9", "NULL", "NULL"),        // both numbers
                     Insert("N''", "NULL", "1", "9", "NULL", "NULL"),           // blank lot
                     Insert("NULL", "N'S2'", "2", "9", "NULL", "NULL"),         // serial quantity 2
                     Insert("NULL", "N'S2'", "1", "9", "'2028-01-01'", "NULL"), // serial with date
                     Insert("N'L2'", "NULL", "0", "9", "NULL", "NULL"),         // zero quantity
                     Insert("N'L2'", "NULL", "1", "0", "NULL", "NULL"),         // seq 0
                     Insert("N'L2'", "NULL", "1", "9", "'2028-02-01'", "'2028-01-31'"), // expiry before manufacture
                     Insert("N'L2'", "NULL", "1", "1", "NULL", "NULL")          // duplicate seq on the line
                 })
#pragma warning disable EF1002 // fixed test literals and seeded IDs only
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, Ct));
#pragma warning restore EF1002

        await db.Database.ExecuteSqlAsync(
            $"DELETE dbo.pur_purchase_receipt_line WHERE company_id={s.CompanyId} AND id={lineId}", Ct);
        Assert.Equal(0, await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.pur_purchase_receipt_line_tracking WHERE line_id={lineId}").SingleAsync(Ct));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Seeded(long CompanyId, long UserId, long ApproverId, long Plain, long LotItem,
        long SerialItem, long Warehouse);

    private static async Task AssertPostedTrackingAsync(MyErpDbContext check, Seeded s, long receiptId)
    {
        Assert.Equal("Posted", await check.Set<PurchaseReceiptSource>().AsNoTracking()
            .Where(x => x.Id == receiptId).Select(x => x.Status).SingleAsync(Ct));
        var moves = await Movements(check, receiptId);
        Assert.Equal(3, moves.Count);
        var lots = await check.Set<StockLot>().AsNoTracking().Where(x => x.ItemId == s.LotItem)
            .OrderBy(x => x.LotNo).ToListAsync(Ct);
        Assert.Equal([("LOT-A", (DateOnly?)Made, (DateOnly?)Made.AddYears(1)), ("LOT-B", null, null)],
            lots.Select(x => (x.LotNo, x.ManufactureDate, x.ExpiryDate)).ToArray());
        Assert.Equal(["SN-1", "SN-2"], await check.Set<StockSerial>().AsNoTracking()
            .Where(x => x.ItemId == s.SerialItem).OrderBy(x => x.SerialNo).Select(x => x.SerialNo).ToArrayAsync(Ct));
        var details = await check.Set<StockMovementTracking>().AsNoTracking()
            .Where(x => moves.Select(m => m.Id).Contains(x.MovementId)).ToListAsync(Ct);
        Assert.Equal(5m, details.Where(x => x.MovementId == moves[0].Id).Sum(x => x.Quantity));
        Assert.Equal(2, details.Count(x => x.MovementId == moves[1].Id && x.SerialId != null));
        Assert.DoesNotContain(details, x => x.MovementId == moves[2].Id);
    }

    private async Task AssertUntouchedAsync(Seeded s, long receiptId, long[] lines)
    {
        await using var check = NewDb(s);
        Assert.Equal("Draft", await check.Set<PurchaseReceiptSource>().AsNoTracking()
            .Where(x => x.Id == receiptId).Select(x => x.Status).SingleAsync(Ct));
        Assert.All(await check.Set<PurchaseOrderSourceLine>().AsNoTracking().Where(x => lines.Contains(x.Id))
            .ToListAsync(Ct), x => Assert.Equal((0m, 0m), (x.PendingReceiptReserved, x.PostedReceived)));
        Assert.Empty(await check.Set<ApprovalInstance>().AsNoTracking()
            .Where(x => x.DocumentType == "PUR.RECEIPT" && x.DocumentId == receiptId).ToListAsync(Ct));
        Assert.Empty(await Movements(check, receiptId));
    }

    private static Task<List<StockMovement>> Movements(MyErpDbContext db, long receiptId) =>
        db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.SourceType == ExternalInventorySourceTypes.PurchaseInbound && x.SourceId == receiptId)
            .OrderBy(x => x.SourceLineNo).ToListAsync(Ct);

    private static Task<List<PurchaseReceiptLineTracking>> Captures(MyErpDbContext db, long receiptId) =>
        (from t in db.Set<PurchaseReceiptLineTracking>().AsNoTracking()
            join l in db.Set<PurchaseReceiptSourceLine>().AsNoTracking() on t.LineId equals l.Id
            where l.PurchaseReceiptId == receiptId
            orderby t.SeqNo
            select t).ToListAsync(Ct);

    private async Task<PurchaseReceiptReadDetail> CreateAsync(Seeded s, long orderId,
        params PurchaseReceiptSaveLine[] lines)
    {
        await using var db = NewDb(s);
        return await Drafts(db, s).CreateAsync(new(orderId, Day, lines), Ct);
    }

    private static PurchaseReceiptTracking Tracking(MyErpDbContext db) => new(db, new InventoryReferenceGuard(db));

    private static PurchaseReceiptPosting Posting(MyErpDbContext db) =>
        new(db, new ExternalInventoryPostingService(db, new StockPostingService(db,
            new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Today))), Tracking(db));

    private static UserCompanyAuthorization User(Seeded s) => new(s.UserId, s.CompanyId, 10, 20, false,
        new Dictionary<string, DataScope>
        {
            [PurchasingPermissions.ReceiptCreate] = DataScope.Company,
            [PurchasingPermissions.ReceiptEdit] = DataScope.Company,
            [PurchasingPermissions.ReceiptView] = DataScope.Company,
            [PurchasingPermissions.ReceiptSubmit] = DataScope.Company
        });

    private static PurchaseReceiptDraftService Drafts(MyErpDbContext db, Seeded s)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(User(s));
        var numbers = new Mock<INumberGenerator>();
        numbers.Setup(x => x.NextAsync("PUR.RECEIPT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"GR-{Guid.NewGuid():N}"[..20]);
        return new PurchaseReceiptDraftService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, numbers.Object, new InventoryReferenceGuard(db), Tracking(db));
    }

    private static PurchaseReceiptSubmissionService Submission(MyErpDbContext db, Seeded s)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(User(s));
        var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(s.UserId), new AuditTrail(), []);
        return new PurchaseReceiptSubmissionService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, workflow, Posting(db), Tracking(db));
    }

    private async Task<Seeded> SeedAsync(bool withFlow)
    {
        var name = "prt" + Guid.NewGuid().ToString("N")[..16];
        var user = new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
        var approverName = "pru" + Guid.NewGuid().ToString("N")[..16];
        var approver = new SystemUser { Username = approverName, NormalizedUsername = approverName.ToUpperInvariant(),
            PasswordHash = "x" };
        var company = new SystemCompany { Code = "RT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Receipt tracking test" };
        await using (var setup = NewDb(null, 7))
        {
            setup.AddRange(company, user, approver);
            await setup.SaveChangesAsync(Ct);
            setup.AddRange(new SystemUserCompany { CompanyId = company.Id, UserId = user.Id },
                new SystemUserCompany { CompanyId = company.Id, UserId = approver.Id });
            await setup.SaveChangesAsync(Ct);
        }
        await using var db = NewDb(company.Id, user.Id);
        var uom = await new UomService(db).CreateAsync("U" + company.Id, "Unit", 0, Ct);
        var items = new ItemService(db);
        var plain = await items.CreateAsync("P" + company.Id, "Plain item", null, uom.Id, null, null, Ct);
        var lot = await items.CreateAsync("L" + company.Id, "Lot item", null, uom.Id, null, null, ItemTrackingMode.Lot, Ct);
        var serial = await items.CreateAsync("S" + company.Id, "Serial item", null, uom.Id, null, null,
            ItemTrackingMode.Serial, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + company.Id, "Warehouse", null, null, null, Ct);
        if (withFlow)
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("PUR.RECEIPT", "Purchase receipt", false,
                [new ApprovalStepInput("Warehouse manager", ApproverType.User, null, approver.Id, null)]), Ct);
        return new Seeded(company.Id, user.Id, approver.Id, plain.Id, lot.Id, serial.Id, warehouse.Id);
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

    private MyErpDbContext NewDb(Seeded s) => NewDb(s.CompanyId, s.UserId);

    private MyErpDbContext NewDb(long? companyId, long userId)
    {
        var company = new FixedCompany(companyId);
        var audit = new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Today), new FixedUser(userId), company));
        return new(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString).AddInterceptors(audit).Options,
            [new RequisitionDbModel(), new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel(),
                new PurchaseInvoiceSourceDbModel(), new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(),
                new WarehouseDbModel(), new InventoryReferenceDbModel(), new StockMovementDbModel(),
                new StockTrackingDbModel()], company);
    }
}
