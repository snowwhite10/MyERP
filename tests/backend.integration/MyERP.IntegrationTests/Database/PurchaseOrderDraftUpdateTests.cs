using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-PUR-006a3（REQ_F4114.1.3）：Draft 整組修訂、版本、狀態、決標行鎖定與資料範圍。</summary>
public sealed class PurchaseOrderDraftUpdateTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    [Fact]
    public async Task ReplacesDraftLinesRecalculatesAmountsAndKeepsNumberAndRequisitionQuantity()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var sourceId = await SeedRequisitionAsync(db, companyId);
        var created = await Service(db, companyId).CreateAsync(new PurchaseOrderInput(101, Day, "old",
            [new(1, 11, sourceId, 2m, 10m, "TWD", 51), new(2, 12, null, 3m, 20m, "TWD", 51)]), Ct);
        db.ChangeTracker.Clear();

        var updated = await Service(db, companyId).UpdateAsync(created.Id, created.Version,
            new PurchaseOrderInput(102, Day.AddDays(1), "  new  ",
                [new(1, 11, sourceId, 4m, 10m, "TWD", 51), new(3, 13, null, 1m, 100m, "TWD", 51)]), Ct);

        await using var check = Context(companyId);
        var order = await check.Set<PurchaseOrderSource>().SingleAsync(x => x.Id == created.Id, Ct);
        var lines = await check.Set<PurchaseOrderSourceLine>().Where(x => x.PurchaseOrderId == order.Id)
            .OrderBy(x => x.LineNo).ToArrayAsync(Ct);
        Assert.Equal(created.DocumentNo, order.DocumentNo);
        Assert.Equal(created.Version + 1, order.Version);
        Assert.Equal(order.Version, updated.Version);
        Assert.Equal(102, order.SupplierId);
        Assert.Equal(Day.AddDays(1), order.BusinessDate);
        Assert.Equal("new", order.Remark);
        Assert.Equal("Draft", order.Status);
        Assert.Equal(140m, order.BaseNet);
        Assert.Equal(7m, order.BaseTax);
        Assert.Equal(147m, order.BaseGross);
        Assert.Equal([1, 3], lines.Select(x => x.LineNo));
        Assert.Equal(created.Lines[0].Id, lines[0].Id);
        Assert.Equal(4m, lines[0].Quantity);
        Assert.Equal(40m, lines[0].OriginalNet);
        Assert.Equal(13, lines[1].MaterialId);
        Assert.Equal(105m, lines[1].BaseGross);
        Assert.Equal(2m, await check.Set<RequisitionLine>().Where(x => x.Id == sourceId)
            .Select(x => x.OrderedQuantity).SingleAsync(Ct));
        Assert.Equal(2, updated.Lines.Count);
    }

    [Fact]
    public async Task StaleVersionConflictsAndNonDraftIsNotEditableWithoutChanges()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var created = await Service(db, companyId).CreateAsync(new PurchaseOrderInput(101, Day, null,
            [new(1, 11, null, 1m, 10m, "TWD", 51)]), Ct);
        db.ChangeTracker.Clear();
        var edit = new PurchaseOrderInput(101, Day, "edit", [new(1, 11, null, 9m, 10m, "TWD", 51)]);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(db, companyId).UpdateAsync(created.Id, created.Version + 1, edit, Ct));
        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(db, companyId).UpdateAsync(created.Id, 0, edit, Ct));

        foreach (var status in new[] { "PendingApproval", "Approved", "Cancelled" })
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_purchase_order SET status = {status} WHERE id = {created.Id}", Ct);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, companyId).UpdateAsync(created.Id, created.Version, edit, Ct));
            Assert.Equal("PUR.ORDER.NOT_EDITABLE", error.ErrorCode);
        }

        await using var check = Context(companyId);
        var line = await check.Set<PurchaseOrderSourceLine>().SingleAsync(x => x.PurchaseOrderId == created.Id, Ct);
        Assert.Equal(1m, line.Quantity);
        Assert.Null((await check.Set<PurchaseOrderSource>().SingleAsync(x => x.Id == created.Id, Ct)).Remark);
    }

    [Fact]
    public async Task RejectedOrderRevisionReturnsToDraft()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var created = await Service(db, companyId).CreateAsync(new PurchaseOrderInput(101, Day, null,
            [new(1, 11, null, 1m, 10m, "TWD", 51)]), Ct);
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE pur_purchase_order SET status = 'Rejected' WHERE id = {created.Id}", Ct);

        var updated = await Service(db, companyId).UpdateAsync(created.Id, created.Version,
            new PurchaseOrderInput(101, Day, "revised", [new(1, 11, null, 2m, 10m, "TWD", 51)]), Ct);

        Assert.Equal("Draft", updated.Status);
        await using var check = Context(companyId);
        var order = await check.Set<PurchaseOrderSource>().SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal("Draft", order.Status);
        Assert.Equal(21m, order.BaseGross);
        Assert.Equal(created.Version + 1, order.Version);
    }

    [Fact]
    public async Task EditScopeHidesOtherOwnerAndMissingEditIsForbidden()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var created = await Service(db, companyId).CreateAsync(new PurchaseOrderInput(101, Day, null,
            [new(1, 11, null, 1m, 10m, "TWD", 51)]), Ct);
        db.ChangeTracker.Clear();
        var edit = new PurchaseOrderInput(101, Day, "x", [new(1, 11, null, 2m, 10m, "TWD", 51)]);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(db, companyId, userId: 8).UpdateAsync(created.Id, created.Version, edit, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(db, companyId, editScope: null).UpdateAsync(created.Id, created.Version, edit, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(db, TestCompanies.Next()).UpdateAsync(created.Id, created.Version, edit, Ct));
        await using var check = Context(companyId);
        Assert.Equal(created.Version, (await check.Set<PurchaseOrderSource>()
            .SingleAsync(x => x.Id == created.Id, Ct)).Version);
    }

    [Fact]
    public async Task InvalidRequisitionSourceOnEditRollsBackWholeRevision()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var created = await Service(db, companyId).CreateAsync(new PurchaseOrderInput(101, Day, null,
            [new(1, 11, null, 1m, 10m, "TWD", 51)]), Ct);
        db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, companyId).UpdateAsync(
            created.Id, created.Version, new PurchaseOrderInput(101, Day, "x",
                [new(1, 11, null, 5m, 10m, "TWD", 51), new(2, 12, long.MaxValue, 1m, 1m, "TWD", 51)]), Ct));
        Assert.Equal("PUR.ORDER.INVALID_REQUISITION_SOURCE", error.ErrorCode);
        await using var check = Context(companyId);
        var lines = await check.Set<PurchaseOrderSourceLine>().Where(x => x.PurchaseOrderId == created.Id)
            .ToArrayAsync(Ct);
        Assert.Equal(1m, Assert.Single(lines).Quantity);
    }

    [Fact]
    public async Task AwardLineItemQuantityPriceSupplierAndCurrencyAreLockedButTaxAndOtherLinesEditable()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (orderId, version) = await SeedAwardOrderAsync(db, companyId);
        var service = Service(db, companyId);

        async Task Locked(PurchaseOrderInput input)
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.UpdateAsync(orderId, version, input, Ct));
            Assert.Equal("PUR.ORDER.AWARD_LINE_LOCKED", error.ErrorCode);
        }

        await Locked(new PurchaseOrderInput(101, Day, null, [new(1, 11, null, 6m, 10m, "TWD", 51)]));
        await Locked(new PurchaseOrderInput(101, Day, null, [new(1, 11, null, 5m, 11m, "TWD", 51)]));
        await Locked(new PurchaseOrderInput(101, Day, null, [new(1, 14, null, 5m, 10m, "TWD", 51)]));
        await Locked(new PurchaseOrderInput(101, Day, null, [new(2, 12, null, 1m, 1m, "TWD", 51)]));
        await Locked(new PurchaseOrderInput(102, Day, null, [new(1, 11, null, 5m, 10m, "TWD", 51)]));
        await Locked(new PurchaseOrderInput(101, Day, null, [new(1, 11, null, 5m, 10m, "TWD", 52)]));

        var updated = await service.UpdateAsync(orderId, version, new PurchaseOrderInput(101, Day, "ok",
            [new(1, 11, null, 5m, 10m, "TWD", 51), new(2, 12, null, 1m, 20m, "TWD", 52)]), Ct);

        await using var check = Context(companyId);
        var lines = await check.Set<PurchaseOrderSourceLine>().Where(x => x.PurchaseOrderId == orderId)
            .OrderBy(x => x.LineNo).ToArrayAsync(Ct);
        Assert.Equal(2, lines.Length);
        Assert.Equal(6m, lines[0].TaxRatePercent);
        Assert.Null(lines[0].OriginalNet);
        Assert.Equal(52, lines[1].TaxRateId);
        Assert.Equal(22m, lines[1].BaseGross);
        Assert.Equal(53m, lines[0].BaseGross);
        Assert.Equal(1, await check.Set<PurchaseOrderAwardSource>().CountAsync(x => x.PurchaseOrderLineId == lines[0].Id, Ct));
        Assert.Equal(version + 1, updated.Version);
        Assert.Equal(75m, updated.BaseGross);
        Assert.Equal("ok", updated.Remark);
    }

    [Fact]
    public async Task SavedAwardAmountsPlusManualLinesBeyondDecimalNineteenFourAreRejectedWithoutChanges()
    {
        // WU-PUR-006a4：決標行保存金額不重估，表頭合計須再檢查 DECIMAL(19,4) 上限，不可讓 SQL 溢位成 500。
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (orderId, version) = await SeedAwardOrderAsync(db, companyId, savedLineBaseNet: 900_000_000_000_000m);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, companyId).UpdateAsync(
            orderId, version, new PurchaseOrderInput(101, Day, "overflow",
                [new(1, 11, null, 5m, 10m, "TWD", 51), new(2, 12, null, 100m, 2_000_000_000_000m, "TWD", 51)]), Ct));

        Assert.Equal("PUR.ORDER.AMOUNT_OVERFLOW", error.ErrorCode);
        await using var check = Context(companyId);
        var order = await check.Set<PurchaseOrderSource>().SingleAsync(x => x.Id == orderId, Ct);
        Assert.Equal(version, order.Version);
        Assert.Null(order.Remark);
        Assert.Equal(900_000_000_000_000m, order.BaseGross);
        Assert.Equal(1, await check.Set<PurchaseOrderSourceLine>().CountAsync(x => x.PurchaseOrderId == orderId, Ct));
    }

    private static async Task<long> SeedRequisitionAsync(MyErpDbContext db, long companyId)
    {
        var requisition = new Requisition { CompanyId = companyId, DocumentNo = "REQ-U",
            BusinessDate = new DateOnly(2028, 2, 28), RequesterId = 7, Status = RequisitionStatus.Approved };
        requisition.Lines.Add(new RequisitionLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 1), Quantity = 10m, OrderedQuantity = 2m });
        db.Add(requisition);
        await db.SaveChangesAsync(Ct);
        return requisition.Lines[0].Id;
    }

    /// <summary>以最少資料建立一張由決標轉出的 Draft：行 1 物料 11、數量 5、單價 10，帶永久決標來源。</summary>
    private static async Task<(long OrderId, int Version)> SeedAwardOrderAsync(MyErpDbContext db, long companyId,
        decimal? savedLineBaseNet = null)
    {
        var rfq = new Rfq
        {
            CompanyId = companyId, DocumentNo = $"Q-UPD-{Guid.NewGuid():N}"[..20],
            BusinessDate = new DateOnly(2028, 2, 20), ReplyDeadline = new DateOnly(2028, 2, 28),
            OwnerId = 7, Status = RfqStatus.Sent, SentAt = new DateTime(2028, 2, 20)
        };
        rfq.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 31), Quantity = 5m });
        rfq.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = 101 });
        db.Add(rfq);
        await db.SaveChangesAsync(Ct);
        var rfqLine = rfq.Lines.Single();
        var revision = new QuotationRevision { CompanyId = companyId, RevisionNo = 1,
            QuoteDate = new DateOnly(2028, 2, 21), ValidUntil = new DateOnly(2028, 3, 31) };
        revision.Lines.Add(new QuotationLine { CompanyId = companyId, RfqLineId = rfqLine.Id,
            UnitPrice = 10m, CurrencyCode = "TWD", TaxRateId = 51 });
        var quote = new SupplierQuotation { CompanyId = companyId, RfqId = rfq.Id, SupplierId = 101,
            CurrentRevisionNo = 1, Status = SupplierQuotationStatus.Awarded };
        quote.Revisions.Add(revision);
        db.Add(quote);
        await db.SaveChangesAsync(Ct);
        var award = new RfqAward
        {
            CompanyId = companyId, RfqId = rfq.Id, RfqLineId = rfqLine.Id, QuotationId = quote.Id,
            QuotationLineId = revision.Lines.Single().Id, SupplierId = 101, Reason = "Award",
            Quantity = 5m, UnitPrice = 10m, CurrencyCode = "TWD", BaseCurrencyCode = "TWD", TaxRateId = 51,
            TaxRatePercent = 5m, TaxRateFrom = new DateOnly(2028, 1, 1), ExchangeRate = 1m,
            ExchangeRateId = 9, ExchangeRateFrom = new DateOnly(2028, 2, 1),
            ComparisonDate = new DateOnly(2028, 2, 22), BaseNet = 50m, BaseTax = 2.5m, BaseGross = 52.5m
        };
        db.Add(award);
        var order = new PurchaseOrderSource
        {
            CompanyId = companyId, SupplierId = 101, RfqId = rfq.Id, DocumentNo = "PO-AWD",
            BusinessDate = Day, Currency = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
            Status = "Draft", OwnerId = 7, BaseNet = 50m, BaseTax = 2.5m, BaseGross = 52.5m
        };
        if (savedLineBaseNet is { } large) (order.BaseNet, order.BaseTax, order.BaseGross) = (large, 0m, large);
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        var line = new PurchaseOrderSourceLine
        {
            CompanyId = companyId, PurchaseOrderId = order.Id, LineNo = 1, MaterialId = 11, Quantity = 5m,
            // Mirrors RfqConversionService: awarded rows carry no original amounts, and the saved
            // 6% tax snapshot differs from today's MD rate (5%) to prove it is not re-priced.
            UnitPrice = 10m, TaxRateId = 51, TaxRatePercent = 6m, ExchangeRate = 1m,
            BaseNet = 50m, BaseTax = 3m, BaseGross = 53m
        };
        if (savedLineBaseNet is { } big) (line.BaseNet, line.BaseTax, line.BaseGross) = (big, 0m, big);
        db.Add(line);
        await db.SaveChangesAsync(Ct);
        db.Add(new PurchaseOrderAwardSource { CompanyId = companyId, PurchaseOrderLineId = line.Id, AwardId = award.Id });
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        return (order.Id, order.Version);
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel(), new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel(),
                new PurchaseInvoiceSourceDbModel(), new SystemOrganizationDbModel()], company);
    }

    private static PurchaseOrderDraftService Service(MyErpDbContext db, long companyId, long userId = 7,
        DataScope? editScope = DataScope.Self)
    {
        var supplier = new Mock<ISupplierLookup>();
        supplier.Setup(x => x.RequireApprovedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) => new SupplierIdentity(id, "SUP", "Supplier"));
        var items = new Mock<IRequisitionItemGuard>();
        items.Setup(x => x.RunAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<Func<Task<PurchaseOrderDetail>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyCollection<long>, Func<Task<PurchaseOrderDetail>>, CancellationToken>((_, action, _) => action());
        var currency = new Mock<ICurrencyLookup>();
        currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Taiwan Dollar", "$", 0, true, true));
        currency.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Taiwan Dollar", "$", 0, true, true));
        currency.Setup(x => x.GetRateSnapshotAsync("TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(1m, new DateOnly(2028, 1, 1), null, null));
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(51, TaxDirection.Input, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(51, 5m, new DateOnly(2028, 1, 1), null));
        tax.Setup(x => x.GetRateSnapshotAsync(52, TaxDirection.Input, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(52, 10m, new DateOnly(2028, 1, 1), null));
        var numbers = new Mock<INumberGenerator>();
        numbers.Setup(x => x.NextAsync("PUR.ORDER", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"PO-{Guid.NewGuid():N}"[..20]);
        var grants = new Dictionary<string, DataScope> { ["PUR.ORDER.CREATE"] = DataScope.Self };
        if (editScope is { } scope) grants["PUR.ORDER.EDIT"] = scope;
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, 10, 20, false, grants));
        return new PurchaseOrderDraftService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            authorization.Object, numbers.Object, items.Object, supplier.Object, currency.Object, tax.Object);
    }
}
