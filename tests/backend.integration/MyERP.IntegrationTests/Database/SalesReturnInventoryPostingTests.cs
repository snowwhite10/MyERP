using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-SRET-001～007（WU-SD-008a3，ADR-188）：SD 銷退以原出庫成本入庫、驗證原出庫、冪等與回滾。</summary>
public sealed class SalesReturnInventoryPostingTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 11, 23);
    private static readonly DateTime Start = new(2026, 11, 23, 9, 0, 0);

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Start), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(), new StockMovementDbModel(), new StockTrackingDbModel()],
            company);
    }

    /// <summary>Each step posts at its own minute so valuation order is the posting order.</summary>
    private static ExternalInventoryPostingService External(MyErpDbContext db, int minute) =>
        new(db, new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
            new FixedClock(Start.AddMinutes(minute))));

    private static async Task<(long Item, long Other, long Warehouse, long Location)> SetupAsync(
        MyErpDbContext db, long companyId, CancellationToken token)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, token);
        var other = await new ItemService(db).CreateAsync("J" + companyId, "Other", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, token);
        var location = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", token);
        return (item.Id, other.Id, warehouse.Id, location.Id);
    }

    private static async Task<T> InTransactionAsync<T>(MyErpDbContext db, Func<Task<T>> action, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var result = await action();
        await tx.CommitAsync(token);
        return result;
    }

    /// <summary>10 @ 2 then 10 @ 4 (average 3), deliver 4 (cost 3), then 10 @ 9 raises the average above 5.</summary>
    private static async Task<long> DeliveredAsync(MyErpDbContext db, long item, long warehouse, long location,
        CancellationToken token)
    {
        await InTransactionAsync(db, () => External(db, 1).PostPurchaseInboundAsync(1, Day,
            [new PurchaseInboundLine(1, item, warehouse, location, 10m, 2m)], token), token);
        await InTransactionAsync(db, () => External(db, 2).PostPurchaseInboundAsync(2, Day,
            [new PurchaseInboundLine(1, item, warehouse, location, 10m, 4m)], token), token);
        var issue = await InTransactionAsync(db, () => External(db, 3).PostSalesOutboundAsync(1, Day,
            [new SalesOutboundLine(1, item, warehouse, location, 4m)], token), token);
        await InTransactionAsync(db, () => External(db, 4).PostPurchaseInboundAsync(3, Day,
            [new PurchaseInboundLine(1, item, warehouse, location, 10m, 9m)], token), token);
        return issue.MovementIds.Single();
    }

    [Fact]
    public async Task ReturnUsesOriginalIssueCostAndReplaysIdempotently()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var (item, _, warehouse, location) = await SetupAsync(db, company, token);
        var issue = await DeliveredAsync(db, item, warehouse, location, token);
        SalesReturnInboundLine[] lines = [new(2, issue, item, warehouse, location, 1m), new(1, issue, item, warehouse, location, 2m)];

        // Caller transaction is mandatory, as for every external source.
        Assert.Equal("INV.POSTING.TRANSACTION_REQUIRED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            External(db, 5).PostSalesReturnInboundAsync(70, Day, lines, token))).ErrorCode);

        var posted = await InTransactionAsync(db, () => External(db, 5).PostSalesReturnInboundAsync(70, Day, lines, token), token);
        Assert.False(posted.Replayed);
        Assert.Equal([1, 2], posted.Movements.Select(x => x.LineNo));
        Assert.All(posted.Movements, x => Assert.Equal(3m, x.UnitCost));
        Assert.All(posted.Movements, x => Assert.Equal(issue, x.OriginalMovementId));

        var saved = await db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.SourceType == ExternalInventorySourceTypes.SalesReturn && x.SourceId == 70)
            .OrderBy(x => x.SourceLineNo).ToListAsync(token);
        Assert.Equal(posted.Movements.Select(x => x.MovementId), saved.Select(x => x.Id));
        Assert.All(saved, x => Assert.Equal(StockMovementDirection.In, x.Direction));
        Assert.Equal([2m, 1m], saved.Select(x => x.Quantity));
        Assert.All(saved, x => Assert.Equal(3m, x.UnitCost));
        Assert.Equal(29m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, location, token));

        // Pool 26 units worth 138.0000 (60 - 12 + 90); returning 3 @ 3 gives 29 units worth 147.0000.
        var valuation = await new InventoryValuationQuery(db).GetItemAsync(item, token: token);
        Assert.Equal(147m, valuation.Value);

        var replay = await InTransactionAsync(db, () => External(db, 9).PostSalesReturnInboundAsync(70, Day, lines, token), token);
        Assert.True(replay.Replayed);
        Assert.Equal(posted.Movements, replay.Movements);
        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            Assert.Equal("INV.POSTING.CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
                External(db, 9).PostSalesReturnInboundAsync(70, Day, [lines[0], lines[1] with { Quantity = 3m }], token))).ErrorCode);
        }
        Assert.Equal(3, await db.Set<StockMovement>().CountAsync(x => x.SourceType != ExternalInventorySourceTypes.SalesReturn
            && x.Direction == StockMovementDirection.In, token));
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == ExternalInventorySourceTypes.SalesReturn, token));
    }

    [Fact]
    public async Task OriginalIssueIsValidatedBeforeAnyMovementIsWritten()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        var otherCompany = TestCompanies.Next();
        await using var db = Context(company);
        await using var foreign = Context(otherCompany);
        var (item, other, warehouse, location) = await SetupAsync(db, company, token);
        var issue = await DeliveredAsync(db, item, warehouse, location, token);
        var f = await SetupAsync(foreign, otherCompany, token);
        var foreignIssue = await DeliveredAsync(foreign, f.Item, f.Warehouse, f.Location, token);
        var receipt = await db.Set<StockMovement>().Where(x => x.SourceType == ExternalInventorySourceTypes.PurchaseInbound)
            .Select(x => x.Id).FirstAsync(token);

        async Task<BusinessRuleException> RefuseAsync(params SalesReturnInboundLine[] lines)
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            return await Assert.ThrowsAsync<BusinessRuleException>(() =>
                External(db, 5).PostSalesReturnInboundAsync(71, Day, lines, token));
        }

        var good = new SalesReturnInboundLine(1, issue, item, warehouse, location, 1m);
        var cases = new (SalesReturnInboundLine[] Lines, string Code, int[] LineNos)[]
        {
            ([good, new(2, foreignIssue, item, warehouse, location, 1m)], "INV.SALES_RETURN.ORIGINAL_NOT_FOUND", [2]),
            ([good, new(3, 999_999_999, item, warehouse, location, 1m)], "INV.SALES_RETURN.ORIGINAL_NOT_FOUND", [3]),
            ([new(4, receipt, item, warehouse, location, 1m)], "INV.SALES_RETURN.ORIGINAL_NOT_ISSUE", [4]),
            ([good, new(5, issue, other, warehouse, location, 1m)], "INV.SALES_RETURN.ITEM_MISMATCH", [5]),
            ([new(6, issue, item, warehouse, location, 3m), new(7, issue, item, warehouse, location, 1.5m)],
                "INV.SALES_RETURN.QUANTITY_EXCEEDED", [6, 7]),
        };
        foreach (var (lines, code, lineNos) in cases)
        {
            var refusal = await RefuseAsync(lines);
            Assert.Equal(code, refusal.ErrorCode);
            Assert.Equal(lineNos, Assert.IsType<int[]>(refusal.Extensions!["lineNos"]));
        }
        db.ChangeTracker.Clear();
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == ExternalInventorySourceTypes.SalesReturn, token));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(x => x.SourceKey.Contains(":SALES_RETURN:"), token));

        // Exactly the original quantity is accepted.
        var full = await InTransactionAsync(db, () => External(db, 6).PostSalesReturnInboundAsync(71, Day,
            [new(1, issue, item, warehouse, location, 4m)], token), token);
        Assert.Equal(3m, full.Movements.Single().UnitCost);
    }

    [Fact]
    public async Task RequestShapeIsValidated()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var service = External(db, 5);

        var errors = (await Assert.ThrowsAsync<RequestValidationException>(() => service.PostSalesReturnInboundAsync(72, Day,
            [new(1, 0, 1, 1, null, 1m), null!, new(1, 5, 1, 1, null, 0m)], token))).Errors;
        Assert.Contains("lines[0].originalMovementId", errors.Keys);
        Assert.Contains("lines[1]", errors.Keys);
        Assert.Contains("lines[2].quantity", errors.Keys);
        Assert.Contains("lines", errors.Keys); // duplicate line number
        Assert.Contains("lines", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.PostSalesReturnInboundAsync(72, Day, [], token))).Errors.Keys);
        Assert.Contains("sourceId", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.PostSalesReturnInboundAsync(0, Day, [new(1, 5, 1, 1, null, 1m)], token))).Errors.Keys);
    }

    [Fact]
    public async Task UnpricedIssueReturnsWithoutCostAndRollsBackWithCaller()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var (item, _, warehouse, location) = await SetupAsync(db, company, token);
        // An unpriced receipt (stocktake-like gain with no known pool) followed by a sale: the issue has no known cost.
        await InTransactionAsync(db, () => new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
            new FixedClock(Start.AddMinutes(1))).PostAsync(new InventoryPostingRequest("STOCKTAKE", 1, Day,
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, location, 5m)]), token), token);
        var issue = (await InTransactionAsync(db, () => External(db, 2).PostSalesOutboundAsync(2, Day,
            [new SalesOutboundLine(1, item, warehouse, location, 2m)], token), token)).MovementIds.Single();

        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            var rolled = await External(db, 3).PostSalesReturnInboundAsync(73, Day,
                [new(1, issue, item, warehouse, location, 1m)], token);
            Assert.Null(rolled.Movements.Single().UnitCost);
            await tx.RollbackAsync(token);
        }
        db.ChangeTracker.Clear();
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == ExternalInventorySourceTypes.SalesReturn, token));

        var posted = await InTransactionAsync(db, () => External(db, 4).PostSalesReturnInboundAsync(73, Day,
            [new(1, issue, item, warehouse, location, 1m)], token), token);
        Assert.False(posted.Replayed);
        Assert.Null(posted.Movements.Single().UnitCost);
        Assert.Equal(4m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, location, token));
    }

    [Fact]
    public async Task IssueCostOrdersMovementsSharingTheIssueTimestampById()
    {
        var token = TestContext.Current.CancellationToken;
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var (item, _, warehouse, location) = await SetupAsync(db, company, token);
        await InTransactionAsync(db, () => External(db, 1).PostPurchaseInboundAsync(1, Day,
            [new PurchaseInboundLine(1, item, warehouse, location, 10m, 2m)], token), token);
        // Receipt, issue and a later receipt all at minute 2: only the receipt with the lower id precedes the issue.
        await InTransactionAsync(db, () => External(db, 2).PostPurchaseInboundAsync(2, Day,
            [new PurchaseInboundLine(1, item, warehouse, location, 10m, 4m)], token), token);
        var issue = (await InTransactionAsync(db, () => External(db, 2).PostSalesOutboundAsync(1, Day,
            [new SalesOutboundLine(1, item, warehouse, location, 5m)], token), token)).MovementIds.Single();
        await InTransactionAsync(db, () => External(db, 2).PostPurchaseInboundAsync(3, Day,
            [new PurchaseInboundLine(1, item, warehouse, location, 10m, 100m)], token), token);

        var posted = await InTransactionAsync(db, () => External(db, 3).PostSalesReturnInboundAsync(74, Day,
            [new(1, issue, item, warehouse, location, 5m)], token), token);
        Assert.Equal(3m, posted.Movements.Single().UnitCost);
    }
}
