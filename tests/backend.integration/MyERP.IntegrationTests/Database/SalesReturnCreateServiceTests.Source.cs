using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-RETURN-SRC-001～004 (WU-SD-008b1, ADR-193): what the return form may offer, on real SQL — Approved orders in the
/// SAL.RETURN.CREATE scope with returnable Posted delivery lines; per line the returnable, unbilled and per-invoice-line
/// uncredited balances against committed Posted returns only (Drafts, Pending and deleted Drafts reserve nothing);
/// tracked lines flagged and not counted; scope applied before counting.
/// </summary>
public sealed partial class SalesReturnCreateServiceTests
{
    [Fact]
    public async Task SourceOrderShowsReturnableUnbilledAndUncreditedBalancesAfterPostedReturnsOnly()
    {
        // SD-RETURN-SRC-001
        var s = await SeedAsync("SA");

        var fresh = await GetSourceAsync(s, s.Order.Id);
        Assert.Equal((s.Order.Id, s.Order.DocumentNo, 21L, "C21", "Customer", Day, "USD", "TWD", 31m,
                await OrderVersionAsync(s, s.Order.Id)), (fresh.Id, fresh.DocumentNo, fresh.CustomerId, fresh.CustomerCode,
            fresh.CustomerName, fresh.BusinessDate, fresh.CurrencyCode, fresh.BaseCurrencyCode, fresh.ExchangeRate,
            fresh.Version));
        Assert.Equal([s.DeliveryA, s.DeliveryB], fresh.Lines.Select(x => x.DeliveryLineId));
        var a = fresh.Lines[0];
        Assert.Equal((s.Delivery.Id, s.Delivery.DocumentNo, Day, 1, s.Order.Lines[0].Id, 1, 31L, "I31", "Item",
                51L, (long?)null, false), (a.DeliveryId, a.DeliveryDocumentNo, a.DeliveryDate, a.DeliveryLineNo,
            a.OrderLineId, a.OrderLineNo, a.ItemId, a.ItemCode, a.ItemName, a.WarehouseId, a.LocationId,
            a.TrackingUnsupported));
        // WU-SD-008b3a: the default destination is labelled from MD (warehouse 51 active, no location).
        Assert.Equal(("WH51", "Main", (bool?)true, (string?)null, (string?)null, (bool?)null),
            (a.WarehouseCode, a.WarehouseName, a.WarehouseActive, a.LocationCode, a.LocationName, a.LocationActive));
        Assert.Equal((2m, 0m, 2m, 1m, 1m), (a.PostedQuantity, a.ReturnedQuantity, a.ReturnableQuantity,
            a.InvoicedQuantity, a.UnbilledRemainingQuantity));
        var invoiceLine = Assert.Single(a.InvoiceLines);
        Assert.Equal((s.InvoiceLineA, s.Invoice.Id, s.Invoice.DocumentNo, Day.AddDays(1), 1m, 0m, 1m),
            (invoiceLine.InvoiceLineId, invoiceLine.InvoiceId, invoiceLine.InvoiceDocumentNo, invoiceLine.InvoiceDate,
                invoiceLine.Quantity, invoiceLine.CreditedQuantity, invoiceLine.RemainingQuantity));
        var b = fresh.Lines[1];
        Assert.Equal((1.5m, 0m, 1.5m, 0m, 1.5m), (b.PostedQuantity, b.ReturnedQuantity, b.ReturnableQuantity,
            b.InvoicedQuantity, b.UnbilledRemainingQuantity));
        Assert.Empty(b.InvoiceLines);

        // Draft, Pending and soft-deleted Drafts reserve nothing.
        var draft = (await CreateAsync(s, Line("SRC-D", s.DeliveryA, 0.5m, (s.InvoiceLineA, 1m)))).Return.Id;
        var pending = (await CreateAsync(s, Line("SRC-P", s.DeliveryB, 1m))).Return.Id;
        await SetStatusAsync(s.CompanyId, pending, "Pending");
        var deleted = (await CreateAsync(s, Line("SRC-X", s.DeliveryB, 0.5m))).Return.Id;
        await DeleteAsync(s, deleted, 1);
        Assert.Equal([2m, 1.5m], (await GetSourceAsync(s, s.Order.Id)).Lines.Select(x => x.ReturnableQuantity));

        // Posting the Draft (A: 0.5 unbilled + 1 billed) consumes all three balances of line A.
        await PostAsync(s.CompanyId, draft, 9002);
        var after = await GetSourceAsync(s, s.Order.Id);
        var a2 = after.Lines.Single(x => x.DeliveryLineId == s.DeliveryA);
        Assert.Equal((1.5m, 0.5m, 1m, 0.5m), (a2.ReturnedQuantity, a2.ReturnableQuantity, a2.InvoicedQuantity,
            a2.UnbilledRemainingQuantity));
        // The fully credited invoice line is no longer offered.
        Assert.Empty(a2.InvoiceLines);

        // A line returned in full disappears; an order with no returnable line returns no lines.
        await PostAsync(s.CompanyId, (await CreateAsync(s, Line("SRC-A", s.DeliveryA, 0.5m))).Return.Id, null);
        await PostAsync(s.CompanyId, pending, null);
        await PostAsync(s.CompanyId, (await CreateAsync(s, Line("SRC-B", s.DeliveryB, 0.5m))).Return.Id, null);
        Assert.Empty((await GetSourceAsync(s, s.Order.Id)).Lines);
    }

    [Fact]
    public async Task SourceOrderListOffersApprovedOrdersWithReturnableLinesFilteredSortedAndPaged()
    {
        // SD-RETURN-SRC-002
        var s = await SeedAsync("SB");
        var zephyr = await SeedAsync("SC", sameCompany: s.CompanyId, customerName: "Zephyr Trading");
        var done = await SeedAsync("SD", sameCompany: s.CompanyId);
        var closed = await SeedAsync("SE", sameCompany: s.CompanyId);
        await using (var db = Context(s.CompanyId))
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [closed.Order.Id], Ct);
        }
        await PostAsync(s.CompanyId, (await CreateAsync(done, new SalesReturnDraftInput("SB-ALL", Day.AddDays(1), null,
        [
            new SalesReturnLineInput(1, done.DeliveryA, 51, null, 1m, [new(done.InvoiceLineA, 1m)]),
            new SalesReturnLineInput(2, done.DeliveryB, 51, null, 1.5m, null)
        ]))).Return.Id, 9004);
        // One returnable line left on the first order.
        await PostAsync(s.CompanyId, (await CreateAsync(s, Line("SB-B", s.DeliveryB, 1.5m))).Return.Id, null);

        var all = await ListSourcesAsync(s, new SalesReturnSourceOrderQuery());
        Assert.Equal([zephyr.Order.Id, s.Order.Id], all.Items.Select(x => x.Id));
        Assert.Equal(2L, all.TotalCount);
        var row = all.Items[1];
        Assert.Equal((s.Order.DocumentNo, 21L, "C21", "Customer", Day, "USD", 1,
                await OrderVersionAsync(s, s.Order.Id)),
            (row.DocumentNo, row.CustomerId, row.CustomerCode, row.CustomerName, row.BusinessDate, row.CurrencyCode,
                row.OpenLineCount, row.Version));
        Assert.Equal(2, all.Items[0].OpenLineCount);

        Assert.Equal([s.Order.Id, zephyr.Order.Id],
            await SourceIdsAsync(s, new SalesReturnSourceOrderQuery { Sort = "businessDate" }));
        var byNo = new[] { s.Order, zephyr.Order }.OrderBy(x => x.DocumentNo, StringComparer.Ordinal)
            .Select(x => x.Id).ToArray();
        Assert.Equal(byNo, await SourceIdsAsync(s, new SalesReturnSourceOrderQuery { Sort = "documentNo" }));
        var page2 = await ListSourcesAsync(s, new SalesReturnSourceOrderQuery { Page = 2, PageSize = 1 });
        Assert.Equal([s.Order.Id], page2.Items.Select(x => x.Id));
        Assert.Equal(2L, page2.TotalCount);
        Assert.Equal([zephyr.Order.Id], await SourceIdsAsync(s, new SalesReturnSourceOrderQuery { Keyword = "zephyr" }));
        Assert.Equal([s.Order.Id],
            await SourceIdsAsync(s, new SalesReturnSourceOrderQuery { Keyword = $" {s.Order.DocumentNo} " }));
        Assert.Equal(2, (await SourceIdsAsync(s, new SalesReturnSourceOrderQuery { Keyword = "C21" })).Length);
        Assert.Equal(2, (await SourceIdsAsync(s, new SalesReturnSourceOrderQuery { CustomerId = 21 })).Length);
        Assert.Empty(await SourceIdsAsync(s, new SalesReturnSourceOrderQuery { CustomerId = 22 }));

        Assert.Equal("customerId", await InvalidSourceFieldAsync(s, new SalesReturnSourceOrderQuery { CustomerId = 0 }));
        Assert.Equal("sort",
            await InvalidSourceFieldAsync(s, new SalesReturnSourceOrderQuery { Sort = "documentNo,businessDate" }));
        Assert.Equal("sort", await InvalidSourceFieldAsync(s, new SalesReturnSourceOrderQuery { Sort = "customerName" }));
    }

    [Fact]
    public async Task SourceReadsApplyTheCreateScopeCompanyAndOrderStatusLikeCreate()
    {
        // SD-RETURN-SRC-003
        var s = await SeedAsync("SF");
        var sibling = await SeedAsync("SG", sameCompany: s.CompanyId);
        var pendingOrder = await SeedAsync("SH", sameCompany: s.CompanyId);
        var foreign = await SeedAsync("SI");
        await using (var db = Context(s.CompanyId))
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET owner_user_id = 8 WHERE id = {0}", [sibling.Order.Id], Ct);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [pendingOrder.Order.Id], Ct);
        }

        var self7 = await ListSourcesAsync(s, new SalesReturnSourceOrderQuery(), userId: 7, scope: DataScope.Self);
        Assert.Equal([s.Order.Id], self7.Items.Select(x => x.Id));
        Assert.Equal(1L, self7.TotalCount);
        Assert.Equal(sibling.Order.Id, (await GetSourceAsync(s, sibling.Order.Id, userId: 8, scope: DataScope.Self)).Id);
        Assert.Equal("SAL.RETURN.SOURCE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            GetSourceAsync(s, sibling.Order.Id, userId: 7, scope: DataScope.Self))).ErrorCode);
        Assert.Equal("SAL.RETURN.SOURCE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            GetSourceAsync(s, foreign.Order.Id))).ErrorCode);
        Assert.Equal("SAL.RETURN.SOURCE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            GetSourceAsync(s, long.MaxValue))).ErrorCode);
        Assert.Equal("SAL.RETURN.ORDER_NOT_APPROVED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            GetSourceAsync(s, pendingOrder.Order.Id))).ErrorCode);
        Assert.DoesNotContain(foreign.Order.Id, await SourceIdsAsync(s, new SalesReturnSourceOrderQuery()));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ListSourcesAsync(s, new SalesReturnSourceOrderQuery(), allow: false));
        await Assert.ThrowsAsync<ForbiddenException>(() => GetSourceAsync(s, s.Order.Id, allow: false));
    }

    [Fact]
    public async Task TrackedLinesAreFlaggedAndUnpostedDeliveriesAreNotOffered()
    {
        // SD-RETURN-SRC-004
        var s = await SeedAsync("SJ");
        var draftDelivery = await SeedDeliveryAsync(s.CompanyId, s.Order, SalesDeliveryStatus.Draft, 0.5m);
        await using (var db = Context(s.CompanyId))
        {
            db.Add(new SalesDeliveryLineTracking
            {
                CompanyId = s.CompanyId, LineId = s.DeliveryB, SeqNo = 1, LotNo = "LOT-1", Quantity = 1.5m
            });
            await db.SaveChangesAsync(Ct);
        }

        var source = await GetSourceAsync(s, s.Order.Id);
        Assert.Equal([(s.DeliveryA, false), (s.DeliveryB, true)],
            source.Lines.Select(x => (x.DeliveryLineId, x.TrackingUnsupported)));
        Assert.DoesNotContain(draftDelivery.Lines[0].Id, source.Lines.Select(x => x.DeliveryLineId));
        Assert.Equal(1, Assert.Single((await ListSourcesAsync(s, new SalesReturnSourceOrderQuery())).Items)
            .OpenLineCount);

        // An order whose only returnable line is tracked is not offered.
        await PostAsync(s.CompanyId, (await CreateAsync(s, Line("SJ-A", s.DeliveryA, 1m, (s.InvoiceLineA, 1m))))
            .Return.Id, 9003);
        Assert.Empty((await ListSourcesAsync(s, new SalesReturnSourceOrderQuery())).Items);
        Assert.Equal([s.DeliveryB], (await GetSourceAsync(s, s.Order.Id)).Lines.Select(x => x.DeliveryLineId));
    }

    private async Task<int> OrderVersionAsync(Seeded s, long orderId)
    {
        await using var db = Context(s.CompanyId);
        return await db.Set<SalesOrder>().AsNoTracking().Where(x => x.Id == orderId).Select(x => x.Version)
            .SingleAsync(Ct);
    }

    private async Task<SalesReturnSourceOrder> GetSourceAsync(Seeded s, long orderId, bool allow = true,
        long userId = 7, DataScope? scope = null)
    {
        await using var db = Context(s.CompanyId);
        return await SourceService(db, s.CompanyId, allow, userId, scope).GetOrderAsync(orderId, Ct);
    }

    private async Task<MyERP.Shared.Paging.PagedResult<SalesReturnSourceOrderSummary>> ListSourcesAsync(Seeded s,
        SalesReturnSourceOrderQuery query, bool allow = true, long userId = 7, DataScope? scope = null)
    {
        await using var db = Context(s.CompanyId);
        return await SourceService(db, s.CompanyId, allow, userId, scope).ListOrdersAsync(query, Ct);
    }

    private async Task<long[]> SourceIdsAsync(Seeded s, SalesReturnSourceOrderQuery query) =>
        [.. (await ListSourcesAsync(s, query)).Items.Select(x => x.Id)];

    private async Task<string> InvalidSourceFieldAsync(Seeded s, SalesReturnSourceOrderQuery query) =>
        Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(() => ListSourcesAsync(s, query)))
            .Errors.Keys);

    private static SalesReturnSourceQueryService SourceService(MyErpDbContext db, long companyId, bool allow,
        long userId, DataScope? scope)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, allow && scope is null,
                allow && scope is { } value
                    ? new Dictionary<string, DataScope> { ["SAL.RETURN.CREATE"] = value }
                    : new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        // MD labels: only warehouse 51 exists (active); other IDs stay unlabelled.
        var references = new Mock<IInventorySelectionOptions>();
        references.Setup(x => x.SavedAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<long> _, IReadOnlyCollection<long> warehouses,
                IReadOnlyCollection<long> _, CancellationToken _) => new InventorySavedReferences([],
                [.. warehouses.Where(x => x == 51).Select(x => new InventorySelectionOption(x, "WH51", "Main"))], []));
        return new SalesReturnSourceQueryService(db, new SalesActionAccess(company, auth.Object), company,
            references.Object);
    }
}
