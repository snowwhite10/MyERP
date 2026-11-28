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
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-007c3（REQ_F4114.7.1～2、BR-PUR-030／032／034、ADR-224，PUR-RET-DRF-001～008）：退貨草稿以真實 SQL、真實 INV
/// 收貨異動證據驗收——只可引用同採購單 Posted 收貨行、可退量＝已收－已退－待審預留、保存原 PURCHASE_INBOUND movement、
/// 批序號須來自收貨擷取；任一行失敗整單零寫入；草稿不預留、不寫 INV；版本、狀態、範圍與軟刪。
/// </summary>
public sealed class PurchaseReturnDraftApiTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Today = new(2028, 3, 1, 9, 30, 0);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Today);

    [Fact]
    public async Task CreateAndEditKeepOriginalMovementWithoutReservingOrPosting()
    {
        var s = await SeedAsync();
        var order = await SeedOrderAsync(s, (s.ItemA, 5m), (s.ItemB, 3m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null), (1, 3m, null));

        await using (var db = NewDb(s))
        {
            var options = await Service(db, s).SourceLinesAsync(order.Id, Ct);
            Assert.Equal([(receipt.LineIds[0], 5m), (receipt.LineIds[1], 3m)],
                options.Select(x => (x.ReceiptLineId, x.ReturnableQuantity)).ToArray());
            Assert.Equal(4m, options[0].UnitCost);
        }

        PurchaseReturnReadDetail created;
        await using (var db = NewDb(s))
            created = await Service(db, s).CreateAsync(new PurchaseReturnSaveInput(order.Id, Day,
                [new(1, receipt.LineIds[0], 2m), new(2, receipt.LineIds[1], 3m, s.Warehouse2)]), Ct);
        Assert.StartsWith("RT-", created.DocumentNo);
        Assert.Equal("Draft", created.Status);
        Assert.Equal(receipt.MovementIds, created.Lines.Select(x => x.OriginalMovementId).ToArray());
        Assert.Equal([s.Warehouse, s.Warehouse2], created.Lines.Select(x => x.WarehouseId).ToArray());
        Assert.All(created.Lines, x => Assert.Null(x.OriginalUnitCost));
        Assert.Equal([5m, 3m], created.Lines.Select(x => x.ReturnableQuantity).ToArray());
        Assert.Equal(receipt.DocumentNo, created.Lines[0].ReceiptDocumentNo);

        PurchaseReturnReadDetail edited;
        await using (var db = NewDb(s))
            edited = await Service(db, s).UpdateAsync(created.Id, created.Version, new PurchaseReturnSaveInput(order.Id,
                Day.AddDays(1), [new(1, receipt.LineIds[0], 4.5m), new(3, receipt.LineIds[1], 1m)]), Ct);
        Assert.True(edited.Version > created.Version);
        Assert.Equal([1, 3], edited.Lines.Select(x => x.LineNumber).ToArray());
        Assert.Equal(created.Lines[0].Id, edited.Lines[0].Id); // kept line keeps its ID
        Assert.Equal((4.5m, Day.AddDays(1)), (edited.Lines[0].Quantity, edited.BusinessDate));

        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<ConflictException>(() => Service(db, s).UpdateAsync(created.Id, created.Version,
                new PurchaseReturnSaveInput(order.Id, Day, [new(1, receipt.LineIds[0], 1m)]), Ct));
            Assert.Equal(edited.Version, (await Service(db, s).GetAsync(created.Id, Ct)).Version);
        }
        await using var check = NewDb(s);
        Assert.Equal([(0m, 0m), (0m, 0m)], await CountersAsync(check, receipt.LineIds));
        Assert.Empty(await check.Set<StockMovement>().AsNoTracking()
            .Where(x => x.SourceType == ExternalInventorySourceTypes.PurchaseReturn).ToListAsync(Ct));
    }

    [Fact]
    public async Task ReturnableQuantityDeductsPostedAndPendingReturnsAndRefusesTheWholeDraft()
    {
        var s = await SeedAsync();
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null));
        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_receipt_line SET posted_returned=1, pending_return_reserved=1.5 WHERE id={receipt.LineIds[0]}", Ct);

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, s).CreateAsync(
                new PurchaseReturnSaveInput(order.Id, Day,
                    [new(1, receipt.LineIds[0], 2m), new(2, receipt.LineIds[0], 0.500001m)]), Ct));
            Assert.Equal(PurchaseReturnDraftService.QuantityExceeded, error.ErrorCode);
            Assert.Equal([1, 2], (int[])error.Extensions!["lineNos"]!);
            Assert.Contains("returnable 2.5", error.Message);
        }
        await using (var db = NewDb(s))
        {
            var ok = await Service(db, s).CreateAsync(new PurchaseReturnSaveInput(order.Id, Day,
                [new(1, receipt.LineIds[0], 2m), new(2, receipt.LineIds[0], 0.5m)]), Ct);
            Assert.Equal(2.5m, ok.Lines[0].ReturnableQuantity); // drafts reserve nothing
        }
        await using var check = NewDb(s);
        Assert.Equal(1, await check.Set<PurchaseReturn>().CountAsync(x => x.CompanyId == s.CompanyId, Ct));
        Assert.Equal([(1.5m, 1m)], await CountersAsync(check, receipt.LineIds));

        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_receipt_line SET posted_returned=5, pending_return_reserved=0 WHERE id={receipt.LineIds[0]}", Ct);
        await using var fresh = NewDb(s);
        Assert.Empty(await Service(fresh, s).SourceLinesAsync(order.Id, Ct)); // fully returned lines are not offered
    }

    [Fact]
    public async Task UnpostedForeignOrEvidenceLessSourcesAreRefusedWithLineNumbers()
    {
        var s = await SeedAsync();
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var otherOrder = await SeedOrderAsync(s, (s.ItemA, 5m));
        var posted = await SeedPostedReceiptAsync(s, order, (0, 2m, null));
        var draft = await SeedReceiptAsync(s, order, "Draft", (0, 1m, null));
        var foreign = await SeedPostedReceiptAsync(s, otherOrder, (0, 2m, null));
        var noEvidence = await SeedReceiptAsync(s, order, "Posted", (0, 1m, null)); // PUR says Posted, INV has nothing
        var other = await SeedAsync();
        var otherCompanyOrder = await SeedOrderAsync(other, (other.ItemA, 5m));
        var otherCompany = await SeedPostedReceiptAsync(other, otherCompanyOrder, (0, 1m, null));

        await using var db = NewDb(s);
        var service = Service(db, s);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseReturnSaveInput(order.Id, Day, [new(1, posted.LineIds[0], 1m), new(2, draft.LineIds[0], 1m),
                new(3, foreign.LineIds[0], 1m), new(4, otherCompany.LineIds[0], 1m)]), Ct));
        Assert.Equal(PurchaseReturnDraftService.InvalidSource, error.ErrorCode);
        Assert.Equal([2, 3, 4], (int[])error.Extensions!["lineNos"]!);
        error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseReturnSaveInput(order.Id, Day, [new(1, posted.LineIds[0], 1m), new(2, noEvidence.LineIds[0], 1m)]), Ct));
        Assert.Equal(PurchaseReturnDraftService.EvidenceMissing, error.ErrorCode);
        Assert.Equal([2], (int[])error.Extensions!["lineNos"]!);
        error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseReturnSaveInput(otherCompanyOrder.Id, Day, [new(1, posted.LineIds[0], 1m)]), Ct));
        Assert.Equal(PurchaseReturnDraftService.InvalidSource, error.ErrorCode);
        Assert.Equal("PUR.RETURN.INVALID_LINES", (await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseReturnSaveInput(order.Id, Day, [new(1, posted.LineIds[0], 1.0000001m)]), Ct))).ErrorCode);
        // Options come from PUR state only (advisory); the evidence-less line is offered but refused when saving.
        Assert.Equal([posted.LineIds[0], noEvidence.LineIds[0]], (await service.SourceLinesAsync(order.Id, Ct)).Select(x => x.ReceiptLineId).ToArray());
        Assert.Equal(0, await db.Set<PurchaseReturn>().CountAsync(x => x.CompanyId == s.CompanyId, Ct));
    }

    [Fact]
    public async Task LotsAndSerialsMustComeFromTheReceiptLineCapture()
    {
        var s = await SeedAsync();
        var order = await SeedOrderAsync(s, (s.LotItem, 5m), (s.ItemA, 2m));
        var receipt = await SeedPostedReceiptAsync(s, order,
            (0, 5m, [("L1", 3m), ("L2", 2m)]), (1, 2m, null));
        await using var db = NewDb(s);
        var service = Service(db, s);
        PurchaseReturnSaveInput Input(params PurchaseReturnSaveLine[] lines) => new(order.Id, Day, lines);
        static PurchaseReturnTrackingInput Lot(string lot, decimal qty) => new(lot, null, qty);

        foreach (var (input, lines) in new (PurchaseReturnSaveInput, int[])[]
                 {
                     (Input(new PurchaseReturnSaveLine(1, receipt.LineIds[0], 4m, null, [Lot("L1", 4m)])), [1]),
                     (Input(new PurchaseReturnSaveLine(1, receipt.LineIds[0], 1m, null, [Lot("L9", 1m)])), [1]),
                     (Input(new PurchaseReturnSaveLine(1, receipt.LineIds[0], 2m, null, [Lot("L1", 2m)]),
                         new PurchaseReturnSaveLine(2, receipt.LineIds[0], 2m, null, [Lot("l1 ", 2m)])), [1, 2]),
                     (Input(new PurchaseReturnSaveLine(1, receipt.LineIds[0], 1m, null, [new(null, "L1", 1m)])), [1]),
                     (Input(new PurchaseReturnSaveLine(1, receipt.LineIds[1], 1m, null, [Lot("X", 1m)])), [1]),
                 })
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(input, Ct));
            Assert.Equal(PurchaseReturnDraftService.TrackingNotFromReceipt, error.ErrorCode);
            Assert.Equal(lines, (int[])error.Extensions!["lineNos"]!);
        }
        var shape = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            Input(new PurchaseReturnSaveLine(1, receipt.LineIds[0], 3m, null, [new(null, "S1", 2m)])), Ct));
        Assert.Contains("lines[0].tracking[0].quantity", shape.Errors.Keys);

        var saved = await service.CreateAsync(Input(new PurchaseReturnSaveLine(1, receipt.LineIds[0], 4m, null, [Lot(" l1", 3m)]),
            new PurchaseReturnSaveLine(2, receipt.LineIds[0], 1m, null, [Lot("L2", 1m)])), Ct); // partial capture allowed on a Draft
        Assert.Equal(["l1", "L2"], saved.Lines.Select(x => Assert.Single(x.Tracking).LotNo).ToArray());
        var option = (await service.SourceLinesAsync(order.Id, Ct))[0];
        Assert.Equal(["L1", "L2"], option.Tracking.Select(x => x.LotNo).ToArray());
    }

    [Fact]
    public async Task StatusScopeVersionAndSoftDeleteRules()
    {
        var s = await SeedAsync();
        var order = await SeedOrderAsync(s, (s.ItemA, 5m));
        var otherOrder = await SeedOrderAsync(s, (s.ItemA, 5m));
        var receipt = await SeedPostedReceiptAsync(s, order, (0, 5m, null));
        PurchaseReturnSaveInput Input(decimal qty) => new(order.Id, Day, [new(1, receipt.LineIds[0], qty)]);
        PurchaseReturnReadDetail mine, theirs;
        await using (var db = NewDb(s))
        {
            mine = await Service(db, s).CreateAsync(Input(1m), Ct);
            theirs = await Service(db, s, s.UserId + 1).CreateAsync(Input(1m), Ct);
        }

        await using (var db = NewDb(s))
        {
            var self = Service(db, s, scope: DataScope.Self);
            await Assert.ThrowsAsync<NotFoundException>(() => self.GetAsync(theirs.Id, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => self.GetForEditAsync(theirs.Id, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => self.UpdateAsync(theirs.Id, theirs.Version, Input(2m), Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => self.DeleteAsync(theirs.Id, theirs.Version, Ct));
            Assert.Equal(mine.Id, (await self.GetAsync(mine.Id, Ct)).Id);
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Service(db, s, grants: [PurchasingPermissions.ReturnView]).CreateAsync(Input(1m), Ct));
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Service(db, s, grants: [PurchasingPermissions.ReturnView]).SourceLinesAsync(order.Id, Ct));
            var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, s).UpdateAsync(mine.Id,
                mine.Version, new PurchaseReturnSaveInput(otherOrder.Id, Day, [new(1, receipt.LineIds[0], 1m)]), Ct));
            Assert.Equal(PurchaseReturnDraftService.SourceLocked, locked.ErrorCode);
        }

        await SetStatusAsync(mine.Id, "PendingApproval");
        await using (var db = NewDb(s))
        {
            Assert.Equal("PUR.RETURN.STATUS_NOT_EDITABLE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).UpdateAsync(mine.Id, mine.Version, Input(2m), Ct))).ErrorCode);
        }
        await SetStatusAsync(mine.Id, "Rejected");
        await using (var db = NewDb(s))
        {
            Assert.Equal(PurchaseReturnDraftService.NotDeletable, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).DeleteAsync(mine.Id, mine.Version, Ct))).ErrorCode);
            var revised = await Service(db, s).UpdateAsync(mine.Id, mine.Version, Input(2m), Ct);
            Assert.Equal("Draft", revised.Status);
            mine = revised;
        }
        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<ConflictException>(() => Service(db, s).DeleteAsync(mine.Id, mine.Version - 1, Ct));
            await Service(db, s).DeleteAsync(mine.Id, mine.Version, Ct);
        }
        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => Service(db, s).GetAsync(mine.Id, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => Service(db, s).DeleteAsync(mine.Id, mine.Version + 1, Ct));
        }
        await using var raw = database.CreateRawContext();
        Assert.Equal(mine.DocumentNo, await raw.Database.SqlQuery<string>(
            $"SELECT document_no AS [Value] FROM dbo.pur_purchase_return WHERE id={mine.Id} AND is_deleted=1").SingleAsync(Ct));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Seeded(long CompanyId, long UserId, long ItemA, long ItemB, long LotItem, long Warehouse,
        long Warehouse2);

    private sealed record Order(long Id, long[] LineIds);

    private sealed record Receipt(long Id, string DocumentNo, long[] LineIds, long[] MovementIds);

    private async Task SetStatusAsync(long returnId, string status)
    {
        await using var raw = database.CreateRawContext();
        await raw.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.pur_purchase_return SET status={status} WHERE id={returnId}", Ct);
    }

    private static async Task<(decimal Pending, decimal Returned)[]> CountersAsync(MyErpDbContext db, long[] lineIds) =>
        (await db.Set<PurchaseReceiptSourceLine>().AsNoTracking().Where(x => lineIds.Contains(x.Id))
            .OrderBy(x => x.Id).Select(x => new { x.PendingReturnReserved, x.PostedReturned }).ToListAsync(Ct))
        .Select(x => (x.PendingReturnReserved, x.PostedReturned)).ToArray();

    private static PurchaseReturnDraftService Service(MyErpDbContext db, Seeded s, long? userId = null,
        DataScope scope = DataScope.Company, string[]? grants = null)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId ?? s.UserId, s.CompanyId, 10, 20, false,
                (grants ?? [PurchasingPermissions.ReturnView, PurchasingPermissions.ReturnCreate,
                    PurchasingPermissions.ReturnEdit, PurchasingPermissions.ReturnDelete])
                .ToDictionary(x => x, _ => scope)));
        var numbers = new Mock<INumberGenerator>();
        numbers.Setup(x => x.NextAsync("PUR.RETURN", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"RT-{Guid.NewGuid():N}"[..20]);
        return new PurchaseReturnDraftService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, numbers.Object, new InventoryReferenceGuard(db), new PurchaseInboundEvidenceQuery(db),
            new FixedClock(Today));
    }

    private async Task<Seeded> SeedAsync()
    {
        var name = "prt" + Guid.NewGuid().ToString("N")[..16];
        var user = new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
        var company = new SystemCompany { Code = "RT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Return draft test" };
        await using (var setup = NewDb(null, 7))
        {
            setup.AddRange(company, user);
            await setup.SaveChangesAsync(Ct);
            setup.Add(new SystemUserCompany { CompanyId = company.Id, UserId = user.Id });
            await setup.SaveChangesAsync(Ct);
        }
        await using var db = NewDb(company.Id, user.Id);
        var uom = await new UomService(db).CreateAsync("U" + company.Id, "Unit", 0, Ct);
        var items = new ItemService(db);
        var a = await items.CreateAsync("A" + company.Id, "Item A", null, uom.Id, null, null, Ct);
        var b = await items.CreateAsync("B" + company.Id, "Item B", null, uom.Id, null, null, Ct);
        var lot = await items.CreateAsync("L" + company.Id, "Lot item", null, uom.Id, null, null, ItemTrackingMode.Lot, Ct);
        var warehouses = new WarehouseService(db);
        var w1 = await warehouses.CreateAsync("W" + company.Id, "Warehouse", null, null, null, Ct);
        var w2 = await warehouses.CreateAsync("X" + company.Id, "Warehouse 2", null, null, null, Ct);
        return new Seeded(company.Id, user.Id, a.Id, b.Id, lot.Id, w1.Id, w2.Id);
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

    /// <summary>A Posted receipt whose lines were posted through the real INV purchase inbound contract.</summary>
    private async Task<Receipt> SeedPostedReceiptAsync(Seeded s, Order order,
        params (int OrderLine, decimal Quantity, (string Lot, decimal Quantity)[]? Lots)[] lines)
    {
        var receipt = await SeedReceiptAsync(s, order, "Posted", lines);
        await using var db = NewDb(s);
        var materials = await db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
            .Where(x => x.PurchaseReceiptId == receipt.Id).OrderBy(x => x.LineNo).Select(x => x.MaterialId).ToArrayAsync(Ct);
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var result = await Inventory(db).PostPurchaseInboundAsync(receipt.Id, Day, lines.Select((x, i) =>
            new PurchaseInboundLine(i + 1, materials[i], s.Warehouse, null, x.Quantity, 4m,
                x.Lots?.Select(l => InventoryTrackingLine.Lot(l.Lot, l.Quantity)).ToArray())).ToArray(), Ct);
        await tx.CommitAsync(Ct);
        return receipt with { MovementIds = result.MovementIds.ToArray() };
    }

    private async Task<Receipt> SeedReceiptAsync(Seeded s, Order order, string status,
        params (int OrderLine, decimal Quantity, (string Lot, decimal Quantity)[]? Lots)[] lines)
    {
        await using var db = NewDb(s);
        var sources = await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            .Where(x => x.PurchaseOrderId == order.Id).ToDictionaryAsync(x => x.Id, Ct);
        var receipt = new PurchaseReceiptSource
        {
            CompanyId = s.CompanyId, PurchaseOrderId = order.Id, SupplierId = 101, Currency = "TWD",
            DocumentNo = $"GR-{Guid.NewGuid():N}"[..20], BusinessDate = Day, Status = status,
            OwnerId = s.UserId, BranchId = 10, DepartmentId = 20
        };
        db.Add(receipt);
        await db.SaveChangesAsync(Ct);
        var rows = lines.Select((line, i) => new PurchaseReceiptSourceLine
        {
            CompanyId = s.CompanyId, PurchaseReceiptId = receipt.Id, LineNo = i + 1,
            PurchaseOrderLineId = order.LineIds[line.OrderLine],
            MaterialId = sources[order.LineIds[line.OrderLine]].MaterialId, Quantity = line.Quantity,
            WarehouseId = s.Warehouse, UnitCost = 4m, PostedReceived = status == "Posted" ? line.Quantity : 0m
        }).ToArray();
        db.AddRange(rows);
        await db.SaveChangesAsync(Ct);
        db.AddRange(rows.SelectMany((row, i) => (lines[i].Lots ?? []).Select((l, j) => new PurchaseReceiptLineTracking
        {
            CompanyId = s.CompanyId, LineId = row.Id, SeqNo = j + 1, LotNo = l.Lot, Quantity = l.Quantity
        })));
        await db.SaveChangesAsync(Ct);
        return new Receipt(receipt.Id, receipt.DocumentNo!, rows.Select(x => x.Id).ToArray(), []);
    }

    private static ExternalInventoryPostingService Inventory(MyErpDbContext db) =>
        new(db, new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
            new FixedClock(Today)));

    private MyErpDbContext NewDb(Seeded s) => NewDb(s.CompanyId, s.UserId);

    private MyErpDbContext NewDb(long? companyId, long userId)
    {
        var company = new FixedCompany(companyId);
        var audit = new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Today), new FixedUser(userId), company));
        return new(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString).AddInterceptors(audit).Options,
            [new PurchaseInvoiceSourceDbModel(), new PurchaseReturnDbModel(), new SystemOrganizationDbModel(),
                new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel()], company);
    }
}
