using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests.Database;

public sealed class PurchaseOrderDraftCreateTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavesManualDraftAndDoesNotConsumeRequisition()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var requisition = new Requisition { CompanyId = companyId, DocumentNo = "REQ-1",
            BusinessDate = new DateOnly(2028, 2, 28), RequesterId = 7, Status = RequisitionStatus.Approved };
        requisition.Lines.Add(new RequisitionLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 1), Quantity = 10m, OrderedQuantity = 2m });
        db.Add(requisition);
        await db.SaveChangesAsync(Ct);
        var sourceId = requisition.Lines[0].Id;
        var service = Service(db, companyId);
        var result = await service.CreateAsync(new PurchaseOrderInput(101, new DateOnly(2028, 2, 29), " draft ",
            [new(1, 11, sourceId, 2m, 10m, "TWD", 51),
             new(2, 12, null, 3m, 20m, "TWD", 51)]), Ct);

        await using var check = Context(companyId);
        var order = await check.Set<PurchaseOrderSource>().SingleAsync(x => x.Id == result.Id, Ct);
        var lines = await check.Set<PurchaseOrderSourceLine>().Where(x => x.PurchaseOrderId == order.Id)
            .OrderBy(x => x.LineNo).ToArrayAsync(Ct);
        Assert.Equal("PO-001", order.DocumentNo);
        Assert.Equal("draft", order.Remark);
        Assert.Equal("Draft", order.Status);
        Assert.Equal(80m, order.BaseNet);
        Assert.Equal(4m, order.BaseTax);
        Assert.Equal(84m, order.BaseGross);
        Assert.Equal(2, lines.Length);
        Assert.Equal(20m, lines[0].OriginalNet);
        Assert.Equal(1m, lines[0].OriginalTax);
        Assert.Equal(sourceId, lines[0].RequisitionLineId);
        Assert.Equal(2m, await check.Set<RequisitionLine>().Where(x => x.Id == sourceId)
            .Select(x => x.OrderedQuantity).SingleAsync(Ct));
        var detail = await Query(check, companyId).GetAsync(order.Id, Ct);
        Assert.Equal(order.DocumentNo, detail.DocumentNo);
        Assert.Equal(2, detail.Lines.Count);
        Assert.Equal(sourceId, detail.Lines[0].RequisitionLineId);
    }

    [Fact]
    public async Task InvalidSourceRollsBackOrderAndNumber()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseOrderInput(101, new DateOnly(2028, 2, 29), null,
                [new(1, 11, long.MaxValue, 1m, 1m, "TWD", 51)]), Ct));
        Assert.Empty(await db.Set<PurchaseOrderSource>().ToListAsync(Ct));
    }

    [Fact]
    public async Task UnapprovedSourceAndMixedCurrencyRejectWholeDraft()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var requisition = new Requisition { CompanyId = companyId, DocumentNo = "REQ-D",
            BusinessDate = new DateOnly(2028, 2, 28), RequesterId = 7, Status = RequisitionStatus.Draft };
        requisition.Lines.Add(new RequisitionLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 1), Quantity = 5m });
        db.Add(requisition);
        await db.SaveChangesAsync(Ct);
        var service = Service(db, companyId);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseOrderInput(101, new DateOnly(2028, 2, 29), null,
                [new(1, 12, null, 1m, 1m, "TWD", 51),
                 new(2, 11, requisition.Lines[0].Id, 1m, 1m, "TWD", 51)]), Ct));
        Assert.Equal("PUR.ORDER.INVALID_REQUISITION_SOURCE", error.ErrorCode);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new PurchaseOrderInput(101, new DateOnly(2028, 2, 29), null,
                [new(1, 11, null, 1m, 1m, "TWD", 51), new(2, 12, null, 1m, 1m, "USD", 51)]), Ct));
        Assert.Empty(await db.Set<PurchaseOrderSource>().ToListAsync(Ct));
        Assert.Empty(await db.Set<PurchaseOrderSourceLine>().ToListAsync(Ct));
    }

    [Fact]
    public async Task SelfScopeHidesOtherOwnersDraftAndViewDoesNotGrantEdit()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var created = await Service(db, companyId).CreateAsync(new PurchaseOrderInput(101,
            new DateOnly(2028, 2, 29), null, [new(1, 11, null, 1m, 1m, "TWD", 51)]), Ct);
        await using var check = Context(companyId);
        await Assert.ThrowsAsync<NotFoundException>(() => Query(check, companyId, 8).GetAsync(created.Id, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Query(check, companyId).GetForEditAsync(created.Id, Ct));
        Assert.Equal(created.Id, (await Query(check, companyId).GetAsync(created.Id, Ct)).Id);
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel(), new PurchaseInvoiceSourceDbModel()], company);
    }

    private static PurchaseOrderDraftService Service(MyErpDbContext db, long companyId)
    {
        var supplier = new Mock<ISupplierLookup>();
        supplier.Setup(x => x.RequireApprovedAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierIdentity(101, "SUP", "Supplier"));
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
        var numbers = new Mock<INumberGenerator>();
        numbers.Setup(x => x.NextAsync("PUR.ORDER", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("PO-001");
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, 10, 20, false,
                new Dictionary<string, DataScope> { ["PUR.ORDER.CREATE"] = DataScope.Self }));
        return new PurchaseOrderDraftService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            authorization.Object, numbers.Object, items.Object, supplier.Object, currency.Object, tax.Object);
    }

    private static PurchaseOrderQueryService Query(MyErpDbContext db, long companyId, long userId = 7)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, 10, 20, false,
                new Dictionary<string, DataScope> { ["PUR.ORDER.VIEW"] = DataScope.Self }));
        return new PurchaseOrderQueryService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            authorization.Object);
    }
}
