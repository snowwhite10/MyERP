using Microsoft.Data.SqlClient;
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

/// <summary>INV-LED-001～008、INV-BASE-003：異動帳過帳、防重放、負庫存、並發與不可改寫。</summary>
public sealed class StockLedgerTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 10, 13);
    private static readonly DateTime Now = new(2026, 10, 13, 17, 30, 0);

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(), new StockMovementDbModel()],
            company);
    }

    private static StockPostingService Posting(MyErpDbContext db) =>
        new(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));

    private static async Task<(long Item, long Warehouse, long LocationA, long LocationB)> SetupAsync(
        MyErpDbContext db, long companyId, CancellationToken token)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, token);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, token);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, token);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", token);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", token);
        return (item.Id, warehouse.Id, a.Id, b.Id);
    }

    private static InventoryPostingRequest Request(string type, long id, params InventoryPostingLine[] lines) =>
        new(type, id, Day, lines);

    [Fact]
    public async Task PostsReceiptAndIssueAndDerivesBalanceFromLedger()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await SetupAsync(db, companyId, token);
        var posting = Posting(db);

        var receipt = await posting.PostAsync(Request("INV_OTHER_IN", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 10.123456m, 12.5m)), token);
        var issue = await posting.PostAsync(Request("INV_OTHER_OUT", 1,
            new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 3m)), token);

        Assert.False(receipt.Replayed);
        Assert.Single(receipt.MovementIds);
        Assert.Single(issue.MovementIds);
        var query = new StockBalanceQuery(db);
        Assert.Equal(7.123456m, await query.GetOnHandAsync(item, warehouse, a, token));
        Assert.Equal(0m, await query.GetOnHandAsync(item, warehouse, null, token));
        var movement = await db.Set<StockMovement>().AsNoTracking().SingleAsync(x => x.Id == receipt.MovementIds[0], token);
        Assert.Equal(("INV_OTHER_IN", 1L, 1, StockMovementDirection.In), (movement.SourceType, movement.SourceId, movement.SourceLineNo, movement.Direction));
        Assert.Equal((10.123456m, 12.5m, Day, Now), (movement.Quantity, movement.UnitCost, movement.BusinessDate, movement.PostedAt));
        Assert.Equal(("I" + companyId, "A" + companyId), await db.Set<InventoryReference>()
            .Where(x => x.Id == movement.ReferenceId).Select(x => ValueTuple.Create(x.ItemCode, x.LocationCode!)).SingleAsync(token));
        var balance = Assert.Single(await query.ListAsync(item, null, token));
        Assert.Equal(new StockBalance(item, warehouse, a, 7.123456m), balance);
    }

    [Fact]
    public async Task ReplayReturnsExistingMovementsAndConflictingReplayIsRejected()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await SetupAsync(db, companyId, token);
        var request = Request("INV_OTHER_IN", 9,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 5m, 2m),
            new InventoryPostingLine(2, StockDirection.In, item, warehouse, b, 1m));

        var first = await Posting(db).PostAsync(request, token);
        await using var other = Context(companyId);
        var replay = await Posting(other).PostAsync(request with { Lines = [.. request.Lines.Reverse()] }, token);

        Assert.True(replay.Replayed);
        Assert.Equal(first.MovementIds, replay.MovementIds);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == "INV_OTHER_IN" && x.SourceId == 9, token));
        var changed = request with { Lines = [request.Lines[0] with { Quantity = 6m }, request.Lines[1]] };
        Assert.Equal("INV.POSTING.CONFLICT",
            (await Assert.ThrowsAsync<ConflictException>(() => Posting(other).PostAsync(changed, token))).ErrorCode);
        var missingLine = request with { Lines = [request.Lines[0]] };
        Assert.Equal("INV.POSTING.CONFLICT",
            (await Assert.ThrowsAsync<ConflictException>(() => Posting(other).PostAsync(missingLine, token))).ErrorCode);
        Assert.Equal(6m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, a, token) +
            await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, b, token));
    }

    [Fact]
    public async Task InsufficientStockRejectsWholePostingWithoutPartialWrites()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await SetupAsync(db, companyId, token);
        await Posting(db).PostAsync(Request("INV_OTHER_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 2m)), token);

        await using var failing = Context(companyId);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Posting(failing).PostAsync(Request("INV_TRANSFER", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, b, 5m),
            new InventoryPostingLine(2, StockDirection.Out, item, warehouse, a, 2.000001m)), token));

        Assert.Equal("INV.STOCK.INSUFFICIENT", error.ErrorCode);
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == "INV_TRANSFER", token));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(x => x.SourceKey.StartsWith("STOCK:INV_TRANSFER:"), token));
        Assert.Equal(2m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, a, token));
    }

    [Fact]
    public async Task TransferMovesStockAtomicallyAndConservesTotal()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, b) = await SetupAsync(db, companyId, token);
        var posting = Posting(db);
        await posting.PostAsync(Request("INV_OTHER_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 8m)), token);

        await posting.PostAsync(Request("INV_TRANSFER", 1,
            new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 8m),
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, b, 8m)), token);

        var balances = await new StockBalanceQuery(db).ListAsync(null, warehouse, token);
        Assert.Equal([new StockBalance(item, warehouse, b, 8m)], balances);
    }

    [Fact]
    public async Task ConcurrentIssuesForSameStockCannotOversell()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using (var setup = Context(companyId))
        {
            var (item, warehouse, a, _) = await SetupAsync(setup, companyId, token);
            await Posting(setup).PostAsync(Request("INV_OTHER_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 10m)), token);

            await using var first = Context(companyId);
            await using var second = Context(companyId);
            InventoryPostingRequest Issue(long id) => Request("INV_OTHER_OUT", id, new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 6m));
            Task<InventoryPostingResult> held;
            Task<InventoryPostingResult> waiting;
            await using (var transaction = await first.Database.BeginTransactionAsync(token))
            {
                held = Posting(first).PostAsync(Issue(1), token);
                await held;
                waiting = Task.Run(() => Posting(second).PostAsync(Issue(2), token), token);
                await Task.Delay(250, token);
                Assert.False(waiting.IsCompleted);
                await transaction.CommitAsync(token);
            }

            Assert.Equal("INV.STOCK.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() => waiting)).ErrorCode);
            Assert.Equal(4m, await new StockBalanceQuery(setup).GetOnHandAsync(item, warehouse, a, token));
        }
    }

    [Fact]
    public async Task CallerRollbackDiscardsPostingAndReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await SetupAsync(db, companyId, token);

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await Posting(db).PostAsync(Request("INV_OTHER_IN", 3, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)), token);
            await transaction.RollbackAsync(token);
        }

        db.ChangeTracker.Clear();
        Assert.False(await db.Set<StockMovement>().AnyAsync(token));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(token));
        var retried = await Posting(db).PostAsync(Request("INV_OTHER_IN", 3, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)), token);
        Assert.False(retried.Replayed);
    }

    [Fact]
    public async Task LedgerRowsCannotBeUpdatedOrDeleted()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse, a, _) = await SetupAsync(db, companyId, token);
        var id = (await Posting(db).PostAsync(Request("INV_OTHER_IN", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)), token)).MovementIds[0];
        await using var raw = database.CreateRawContext();

        await Assert.ThrowsAsync<SqlException>(() =>
            raw.Database.ExecuteSqlAsync($"UPDATE inv_stock_movement SET quantity = 99 WHERE id = {id}", token));
        await Assert.ThrowsAsync<SqlException>(() =>
            raw.Database.ExecuteSqlAsync($"DELETE FROM inv_stock_movement WHERE id = {id}", token));
        Assert.Equal(1m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, a, token));
    }

    [Fact]
    public async Task BalancesAndMovementsAreCompanyIsolated()
    {
        var token = TestContext.Current.CancellationToken;
        var companyA = TestCompanies.Next();
        var companyB = TestCompanies.Next();
        await using var dbA = Context(companyA);
        var (item, warehouse, a, _) = await SetupAsync(dbA, companyA, token);
        await Posting(dbA).PostAsync(Request("INV_OTHER_IN", 1, new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 4m)), token);

        await using var dbB = Context(companyB);
        var queryB = new StockBalanceQuery(dbB);
        Assert.Equal(0m, await queryB.GetOnHandAsync(item, warehouse, a, token));
        Assert.Empty(await queryB.ListAsync(null, null, token));
        Assert.False(await dbB.Set<StockMovement>().AnyAsync(token));
        await Assert.ThrowsAsync<NotFoundException>(() => Posting(dbB).PostAsync(Request("INV_OTHER_IN", 1,
            new InventoryPostingLine(1, StockDirection.In, item, warehouse, a, 1m)), token));
        Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => Posting(dbB).PostAsync(Request("INV_OTHER_OUT", 1,
            new InventoryPostingLine(1, StockDirection.Out, item, warehouse, a, 1m)), token))).ErrorCode);
        Assert.False(await dbB.Set<InventoryReference>().AnyAsync(token));
    }

    public static TheoryData<string, InventoryPostingLine[]> InvalidRequests => new()
    {
        { "", [new(1, StockDirection.In, 1, 1, null, 1m)] },
        { "inv_lower", [new(1, StockDirection.In, 1, 1, null, 1m)] },
        { "INV_OTHER_IN", [] },
        { "INV_OTHER_IN", [new(0, StockDirection.In, 1, 1, null, 1m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 0m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, -1m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, decimal.MinValue)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 0.0000001m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 10_000_000_000_000m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 1m, -0.01m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 1m, 1.0000001m)] },
        { "INV_OTHER_OUT", [new(1, StockDirection.Out, 1, 1, null, 1m, 1m)] },
        { "INV_OTHER_IN", [new(1, StockDirection.In, 1, 1, null, 1m), new(1, StockDirection.In, 1, 1, null, 2m)] },
        { "INV_OTHER_IN", [new(1, (StockDirection)9, 1, 1, null, 1m)] },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequestsAreRejectedBeforeWriting(string sourceType, InventoryPostingLine[] lines)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(TestCompanies.Next());

        await Assert.ThrowsAsync<RequestValidationException>(() => Posting(db).PostAsync(new(sourceType, 1, Day, lines), token));
        await Assert.ThrowsAsync<RequestValidationException>(() => Posting(db).PostAsync(new("INV_OTHER_IN", 0, Day,
            [new(1, StockDirection.In, 1, 1, null, 1m)]), token));
        Assert.False(await db.Set<StockMovement>().AnyAsync(token));
    }
}
