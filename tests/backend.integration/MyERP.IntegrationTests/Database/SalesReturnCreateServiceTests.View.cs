using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-RETURN-VIEW-001～004 (WU-SD-008a2b2, ADR-187): scoped return detail and list on real SQL — saved snapshot,
/// source references and allocations; filters, sorting and paging; SAL.RETURN.VIEW scope applied to the source order
/// before counting; soft-deleted returns hidden.
/// </summary>
public sealed partial class SalesReturnCreateServiceTests
{
    [Fact]
    public async Task DetailShowsTheSavedSnapshotSourceReferencesAndPostingIdentities()
    {
        // SD-RETURN-VIEW-001
        var s = await SeedAsync("VA");
        var created = await CreateAsync(s, new SalesReturnDraftInput("V-1", Day.AddDays(1), " note ",
        [
            new SalesReturnLineInput(1, s.DeliveryA, 51, null, 0.5m, [new(s.InvoiceLineA, 1m)]),
            new SalesReturnLineInput(2, s.DeliveryB, 52, 9, 1m, null)
        ]));
        await using var verify = Context(s.CompanyId);
        var saved = await verify.Set<SalesReturn>().AsNoTracking().Include(x => x.Lines)
            .ThenInclude(x => x.InvoiceAllocations).SingleAsync(x => x.Id == created.Return.Id, Ct);

        var detail = await GetReturnAsync(s, saved.Id);

        Assert.Equal((saved.Id, "VA001", "V-1", s.Order.Id, s.Order.DocumentNo, 21L, "C21", "Customer",
                Day.AddDays(1), "USD", "TWD", 31m), (detail.Id, detail.DocumentNo, detail.RequestKey, detail.OrderId,
            detail.OrderDocumentNo, detail.CustomerId, detail.CustomerCode, detail.CustomerName, detail.BusinessDate,
            detail.CurrencyCode, detail.BaseCurrencyCode, detail.ExchangeRate));
        Assert.Equal((SalesReturnStatus.Draft, "note", (long?)null, (DateTime?)null, (long?)null, 1),
            (detail.Status, detail.Remark, detail.ApprovalInstanceId, detail.PostedAt, detail.FiCreditNoteId,
                detail.Version));
        Assert.Equal((saved.OriginalNet, saved.OriginalTax, saved.OriginalGross, saved.BaseNet, saved.BaseTax,
                saved.BaseGross), (detail.OriginalNet, detail.OriginalTax, detail.OriginalGross, detail.BaseNet,
            detail.BaseTax, detail.BaseGross));
        Assert.Equal([1, 2], detail.Lines.Select(x => x.LineNo));

        var a = detail.Lines[0];
        var savedA = saved.Lines.Single(x => x.LineNo == 1);
        Assert.Equal((savedA.Id, s.Delivery.Id, s.Delivery.DocumentNo, s.DeliveryA, s.Order.Lines[0].Id, 1, 31L,
                "I31", "Item", 51L, (long?)null), (a.Id, a.DeliveryId, a.DeliveryDocumentNo, a.DeliveryLineId,
            a.OrderLineId, a.OrderLineNo, a.ItemId, a.ItemCode, a.ItemName, a.WarehouseId, a.LocationId));
        Assert.Equal((1.5m, 0.5m, 1m, 501L, (long?)null), (a.Quantity, a.UnbilledQuantity, a.BilledQuantity,
            a.OriginalOutboundMovementId, a.InvMovementId));
        Assert.Equal((savedA.OriginalNet, savedA.OriginalTax, savedA.OriginalGross, savedA.BaseNet, savedA.BaseTax,
                savedA.BaseGross), (a.OriginalNet, a.OriginalTax, a.OriginalGross, a.BaseNet, a.BaseTax, a.BaseGross));
        var allocation = Assert.Single(a.Allocations);
        var savedAllocation = Assert.Single(savedA.InvoiceAllocations);
        Assert.Equal((savedAllocation.Id, s.Invoice.Id, s.Invoice.DocumentNo, s.InvoiceLineA, 1m, 33.33m, 1.67m,
                35m, 1033m, 52m, 1085m), (allocation.Id, allocation.InvoiceId, allocation.InvoiceDocumentNo,
            allocation.InvoiceLineId, allocation.Quantity, allocation.OriginalNet, allocation.OriginalTax,
            allocation.OriginalGross, allocation.BaseNet, allocation.BaseTax, allocation.BaseGross));

        var b = detail.Lines[1];
        Assert.Equal((2, s.Order.Lines[1].Id, 32L, "I32", 52L, (long?)9, 1m, 1m, 0m, 502L),
            (b.OrderLineNo, b.OrderLineId, b.ItemId, b.ItemCode, b.WarehouseId, b.LocationId, b.Quantity,
                b.UnbilledQuantity, b.BilledQuantity, b.OriginalOutboundMovementId));
        Assert.Empty(b.Allocations);

        // Posted: posted time, INV inbound movements and the FI credit note are shown as stored.
        await PostAsync(s.CompanyId, saved.Id, 9001);
        var posted = await GetReturnAsync(s, saved.Id);
        Assert.Equal((SalesReturnStatus.Posted, (DateTime?)new DateTime(2028, 3, 1, 10, 0, 0), (long?)9001),
            (posted.Status, posted.PostedAt, posted.FiCreditNoteId));
        Assert.Equal(posted.Lines.Select(x => (long?)(x.Id + 800000)), posted.Lines.Select(x => x.InvMovementId));
    }

    [Fact]
    public async Task ListFiltersSortsAndPagesReturnsOfTheCompany()
    {
        // SD-RETURN-VIEW-002
        var s = await SeedAsync("VB");
        var sibling = await SeedAsync("VC", sameCompany: s.CompanyId, customerName: "Zephyr Trading");
        var r1 = (await CreateAsync(s, Unbilled("L-1", 1, s.DeliveryB, 0.5m))).Return.Id;
        var r2 = (await CreateAsync(s, new SalesReturnDraftInput("L-2", Day.AddDays(3), null,
        [
            new SalesReturnLineInput(1, s.DeliveryA, 51, null, 0.25m, null),
            new SalesReturnLineInput(2, s.DeliveryB, 51, null, 0.25m, null)
        ]))).Return.Id;
        var r3 = (await CreateAsync(s, Unbilled("L-3", 2, sibling.DeliveryB, 0.5m))).Return.Id;
        var r4 = (await CreateAsync(s, Unbilled("L-4", 3, s.DeliveryB, 0.1m))).Return.Id;
        await SetStatusAsync(s.CompanyId, r3, "Pending");

        var all = await ListReturnsAsync(s, new SalesReturnQuery());
        Assert.Equal([r4, r2, r3, r1], all.Items.Select(x => x.Id));
        Assert.Equal(4L, all.TotalCount);
        var two = all.Items[1];
        Assert.Equal((r2, "VB002", s.Order.Id, s.Order.DocumentNo, 21L, "Customer", Day.AddDays(3), "USD", "TWD",
                SalesReturnStatus.Draft, (DateTime?)null, 2, 1), (two.Id, two.DocumentNo, two.OrderId,
            two.OrderDocumentNo, two.CustomerId, two.CustomerName, two.BusinessDate, two.CurrencyCode,
            two.BaseCurrencyCode, two.Status, two.PostedAt, two.LineCount, two.Version));
        await using (var verify = Context(s.CompanyId))
        {
            var saved = await verify.Set<SalesReturn>().AsNoTracking().SingleAsync(x => x.Id == r2, Ct);
            Assert.Equal((saved.OriginalGross, saved.BaseGross), (two.OriginalGross, two.BaseGross));
        }
        Assert.Equal("Zephyr Trading", all.Items[2].CustomerName);

        Assert.Equal([r1, r3, r2, r4], await IdsAsync(s, new SalesReturnQuery { Sort = "businessDate" }));
        Assert.Equal([r1, r2, r3, r4], await IdsAsync(s, new SalesReturnQuery { Sort = "documentNo" }));
        Assert.Equal([r4, r3, r2, r1], await IdsAsync(s, new SalesReturnQuery { Sort = "-documentNo" }));
        var page2 = await ListReturnsAsync(s, new SalesReturnQuery { Sort = "documentNo", Page = 2, PageSize = 2 });
        Assert.Equal([r3, r4], page2.Items.Select(x => x.Id));
        Assert.Equal(4L, page2.TotalCount);

        Assert.Equal([r2], await IdsAsync(s, new SalesReturnQuery { Keyword = " VB002 " }));
        Assert.Equal([r3], await IdsAsync(s, new SalesReturnQuery { Keyword = sibling.Order.DocumentNo }));
        Assert.Equal([r3], await IdsAsync(s, new SalesReturnQuery { Keyword = "zephyr" }));
        Assert.Equal([r3], await IdsAsync(s, new SalesReturnQuery { Status = SalesReturnStatus.Pending }));
        Assert.Equal([r4, r2, r1], await IdsAsync(s, new SalesReturnQuery { Status = SalesReturnStatus.Draft }));
        Assert.Equal([r3], await IdsAsync(s, new SalesReturnQuery { From = Day.AddDays(2), To = Day.AddDays(2) }));
        Assert.Equal([r4, r2], await IdsAsync(s, new SalesReturnQuery { From = Day.AddDays(3) }));
        Assert.Equal([r1], await IdsAsync(s, new SalesReturnQuery { To = Day.AddDays(1) }));
        Assert.Equal([r3], await IdsAsync(s, new SalesReturnQuery { OrderId = sibling.Order.Id }));
        Assert.Equal(4, (await IdsAsync(s, new SalesReturnQuery { CustomerId = 21 })).Length);
        Assert.Empty(await IdsAsync(s, new SalesReturnQuery { CustomerId = 22 }));

        Assert.Equal("to", await InvalidFieldAsync(s, new SalesReturnQuery { From = Day.AddDays(2), To = Day }));
        Assert.Equal("orderId", await InvalidFieldAsync(s, new SalesReturnQuery { OrderId = 0 }));
        Assert.Equal("customerId", await InvalidFieldAsync(s, new SalesReturnQuery { CustomerId = -1 }));
        Assert.Equal("sort", await InvalidFieldAsync(s, new SalesReturnQuery { Sort = "documentNo,businessDate" }));
        Assert.Equal("sort", await InvalidFieldAsync(s, new SalesReturnQuery { Sort = "remark" }));
    }

    [Fact]
    public async Task ViewScopeCompanyAndPermissionHideReturnsBeforeCounting()
    {
        // SD-RETURN-VIEW-003
        var s = await SeedAsync("VD");
        var sibling = await SeedAsync("VE", sameCompany: s.CompanyId);
        var foreign = await SeedAsync("VF");
        await using (var db = Context(s.CompanyId))
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET owner_user_id = 8 WHERE id = {0}", [sibling.Order.Id], Ct);
        var own = (await CreateAsync(s, Unbilled("S-1", 1, s.DeliveryB, 0.5m))).Return.Id;
        var others = (await CreateAsync(s, Unbilled("S-2", 1, sibling.DeliveryB, 0.5m))).Return.Id;
        var alien = (await CreateAsync(foreign, Unbilled("S-3", 1, foreign.DeliveryB, 0.5m))).Return.Id;

        var self7 = await ListReturnsAsync(s, new SalesReturnQuery(), userId: 7, scope: DataScope.Self);
        Assert.Equal([own], self7.Items.Select(x => x.Id));
        Assert.Equal(1L, self7.TotalCount);
        var self8 = await ListReturnsAsync(s, new SalesReturnQuery(), userId: 8, scope: DataScope.Self);
        Assert.Equal([others], self8.Items.Select(x => x.Id));
        Assert.Equal(1L, self8.TotalCount);
        Assert.Equal(others, (await GetReturnAsync(s, others, userId: 8, scope: DataScope.Self)).Id);
        Assert.Equal("SAL.RETURN.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            GetReturnAsync(s, others, userId: 7, scope: DataScope.Self))).ErrorCode);

        Assert.Equal([others, own], await IdsAsync(s, new SalesReturnQuery()));
        Assert.Equal("SAL.RETURN.NOT_FOUND",
            (await Assert.ThrowsAsync<NotFoundException>(() => GetReturnAsync(s, alien))).ErrorCode);
        Assert.Equal(alien, (await GetReturnAsync(foreign, alien)).Id);
        Assert.Equal("SAL.RETURN.NOT_FOUND",
            (await Assert.ThrowsAsync<NotFoundException>(() => GetReturnAsync(s, long.MaxValue))).ErrorCode);

        await Assert.ThrowsAsync<ForbiddenException>(() => ListReturnsAsync(s, new SalesReturnQuery(), allow: false));
        await Assert.ThrowsAsync<ForbiddenException>(() => GetReturnAsync(s, own, allow: false));
    }

    [Fact]
    public async Task EveryStoredStateIsListedButSoftDeletedDraftsAreHidden()
    {
        // SD-RETURN-VIEW-004
        var s = await SeedAsync("VG");
        var draft = (await CreateAsync(s, Unbilled("D-1", 1, s.DeliveryB, 0.1m))).Return.Id;
        var pending = (await CreateAsync(s, Unbilled("D-2", 1, s.DeliveryB, 0.1m))).Return.Id;
        var rejected = (await CreateAsync(s, Unbilled("D-3", 1, s.DeliveryB, 0.1m))).Return.Id;
        var posted = (await CreateAsync(s, Unbilled("D-4", 1, s.DeliveryB, 0.1m))).Return.Id;
        var deleted = (await CreateAsync(s, Unbilled("D-5", 1, s.DeliveryB, 0.1m))).Return.Id;
        await SetStatusAsync(s.CompanyId, pending, "Pending");
        await SetStatusAsync(s.CompanyId, rejected, "Pending");
        await SetStatusAsync(s.CompanyId, rejected, "Rejected");
        await PostAsync(s.CompanyId, posted, null);
        await DeleteAsync(s, deleted, 1);

        var page = await ListReturnsAsync(s, new SalesReturnQuery { Sort = "documentNo" });
        Assert.Equal(4L, page.TotalCount);
        Assert.Equal([(draft, SalesReturnStatus.Draft), (pending, SalesReturnStatus.Pending),
                (rejected, SalesReturnStatus.Rejected), (posted, SalesReturnStatus.Posted)],
            page.Items.Select(x => (x.Id, x.Status)));
        Assert.Equal(SalesReturnStatus.Rejected, (await GetReturnAsync(s, rejected)).Status);
        Assert.Equal("SAL.RETURN.NOT_FOUND",
            (await Assert.ThrowsAsync<NotFoundException>(() => GetReturnAsync(s, deleted))).ErrorCode);
        Assert.Empty(await IdsAsync(s, new SalesReturnQuery { Keyword = "VG005" }));
    }

    private static SalesReturnDraftInput Unbilled(string key, int dayOffset, long deliveryLineId, decimal quantity) =>
        new(key, Day.AddDays(dayOffset), null, [new SalesReturnLineInput(1, deliveryLineId, 51, null, quantity, null)]);

    private async Task<SalesReturnDetail> GetReturnAsync(Seeded s, long id, bool allow = true, long userId = 7,
        DataScope? scope = null)
    {
        await using var db = Context(s.CompanyId);
        return await QueryService(db, s.CompanyId, allow, userId, scope).GetAsync(id, Ct);
    }

    private async Task<MyERP.Shared.Paging.PagedResult<SalesReturnSummary>> ListReturnsAsync(Seeded s,
        SalesReturnQuery query, bool allow = true, long userId = 7, DataScope? scope = null)
    {
        await using var db = Context(s.CompanyId);
        return await QueryService(db, s.CompanyId, allow, userId, scope).ListAsync(query, Ct);
    }

    private async Task<long[]> IdsAsync(Seeded s, SalesReturnQuery query) =>
        [.. (await ListReturnsAsync(s, query)).Items.Select(x => x.Id)];

    private async Task<string> InvalidFieldAsync(Seeded s, SalesReturnQuery query) =>
        Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(() => ListReturnsAsync(s, query)))
            .Errors.Keys);

    private static SalesReturnQueryService QueryService(MyErpDbContext db, long companyId, bool allow, long userId,
        DataScope? scope)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, allow && scope is null,
                allow && scope is { } value
                    ? new Dictionary<string, DataScope> { ["SAL.RETURN.VIEW"] = value }
                    : new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesReturnQueryService(db, new SalesActionAccess(company, auth.Object), company);
    }
}
