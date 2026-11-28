using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-PRET-001～007（WU-PUR-007c1，ADR-223）：PUR 退貨以原入庫驗證出庫、回傳原入庫成本、批序號來源、冪等與回滾。</summary>
public sealed class PurchaseReturnInventoryPostingTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 11, 28);
    private static readonly DateTime Start = new(2026, 11, 28, 9, 0, 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private static ExternalInventoryPostingService External(MyErpDbContext db, int minute) =>
        new(db, new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
            new FixedClock(Start.AddMinutes(minute))));

    private sealed record Master(long Item, long Other, long Lot, long Serial, long Warehouse, long A, long B);

    private static async Task<Master> SetupAsync(MyErpDbContext db, long companyId)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var item = await items.CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, ItemTrackingMode.None, Ct);
        var other = await items.CreateAsync("J" + companyId, "Other", null, uom.Id, null, null, ItemTrackingMode.None, Ct);
        var lot = await items.CreateAsync("L" + companyId, "Lot", null, uom.Id, null, null, ItemTrackingMode.Lot, Ct);
        var serial = await items.CreateAsync("S" + companyId, "Serial", null, uom.Id, null, null, ItemTrackingMode.Serial, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var a = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        var b = await new StorageLocationService(db).CreateAsync(warehouse.Id, "B" + companyId, "B", Ct);
        return new Master(item.Id, other.Id, lot.Id, serial.Id, warehouse.Id, a.Id, b.Id);
    }

    private static async Task<T> InTransactionAsync<T>(MyErpDbContext db, Func<Task<T>> action)
    {
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var result = await action();
        await tx.CommitAsync(Ct);
        return result;
    }

    /// <summary>10 @ 2 (receipt 1) then 10 @ 4 (receipt 2): average 3, the return cost of receipt 2 is 4.</summary>
    private static async Task<(long First, long Second)> ReceivedAsync(MyErpDbContext db, Master m)
    {
        var first = await InTransactionAsync(db, () => External(db, 1).PostPurchaseInboundAsync(1, Day,
            [new PurchaseInboundLine(1, m.Item, m.Warehouse, m.A, 10m, 2m)], Ct));
        var second = await InTransactionAsync(db, () => External(db, 2).PostPurchaseInboundAsync(2, Day,
            [new PurchaseInboundLine(1, m.Item, m.Warehouse, m.A, 10m, 4m)], Ct));
        return (first.MovementIds.Single(), second.MovementIds.Single());
    }

    private static async Task<BusinessRuleException> RefuseAsync(MyErpDbContext db, long sourceId, params PurchaseReturnOutboundLine[] lines)
    {
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            External(db, 5).PostPurchaseReturnAsync(sourceId, Day, lines, Ct));
        await tx.RollbackAsync(Ct);
        db.ChangeTracker.Clear();
        return error;
    }

    [Fact]
    public async Task ReturnIssuesAtAverageReportsOriginalCostAndReplaysIdempotently()
    {
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var m = await SetupAsync(db, company);
        var (_, second) = await ReceivedAsync(db, m);
        PurchaseReturnOutboundLine[] lines = [new(2, second, m.Item, m.Warehouse, m.A, 1m), new(1, second, m.Item, m.Warehouse, m.A, 3m)];

        Assert.Equal("INV.POSTING.TRANSACTION_REQUIRED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            External(db, 5).PostPurchaseReturnAsync(80, Day, lines, Ct))).ErrorCode);

        var posted = await InTransactionAsync(db, () => External(db, 5).PostPurchaseReturnAsync(80, Day, lines, Ct));
        Assert.False(posted.Replayed);
        Assert.Equal([1, 2], posted.Movements.Select(x => x.LineNo));
        Assert.All(posted.Movements, x => Assert.Equal(4m, x.OriginalUnitCost));
        Assert.All(posted.Movements, x => Assert.Equal(second, x.OriginalMovementId));

        var saved = await db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.SourceType == ExternalInventorySourceTypes.PurchaseReturn && x.SourceId == 80)
            .OrderBy(x => x.SourceLineNo).ToListAsync(Ct);
        Assert.Equal(posted.Movements.Select(x => x.MovementId), saved.Select(x => x.Id));
        Assert.All(saved, x => Assert.Equal(StockMovementDirection.Out, x.Direction));
        Assert.All(saved, x => Assert.Null(x.UnitCost));
        Assert.Equal([3m, 1m], saved.Select(x => x.Quantity));
        Assert.Equal(16m, await new StockBalanceQuery(db).GetOnHandAsync(m.Item, m.Warehouse, m.A, Ct));

        // Issues leave the moving-average pool at 3 (ADR-223): 60 - 9 - 3 = 48 for 16 units.
        var valuation = await new InventoryValuationQuery(db).GetItemAsync(m.Item, token: Ct);
        Assert.Equal(48m, valuation.Value);

        var replay = await InTransactionAsync(db, () => External(db, 9).PostPurchaseReturnAsync(80, Day, lines, Ct));
        Assert.True(replay.Replayed);
        Assert.Equal(posted.Movements, replay.Movements);
        await using (var tx = await db.Database.BeginTransactionAsync(Ct))
        {
            Assert.Equal("INV.POSTING.CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
                External(db, 9).PostPurchaseReturnAsync(80, Day, [lines[0], lines[1] with { Quantity = 2m }], Ct))).ErrorCode);
        }
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == ExternalInventorySourceTypes.PurchaseReturn, Ct));
    }

    [Fact]
    public async Task OriginalReceiptIsValidatedBeforeAnyMovementIsWritten()
    {
        var company = TestCompanies.Next();
        var otherCompany = TestCompanies.Next();
        await using var db = Context(company);
        await using var foreign = Context(otherCompany);
        var m = await SetupAsync(db, company);
        var (first, second) = await ReceivedAsync(db, m);
        var f = await SetupAsync(foreign, otherCompany);
        var (foreignReceipt, _) = await ReceivedAsync(foreign, f);
        var issue = (await InTransactionAsync(db, () => External(db, 3).PostSalesOutboundAsync(1, Day,
            [new SalesOutboundLine(1, m.Item, m.Warehouse, m.A, 1m)], Ct))).MovementIds.Single();

        var good = new PurchaseReturnOutboundLine(1, second, m.Item, m.Warehouse, m.A, 1m);
        var cases = new (PurchaseReturnOutboundLine[] Lines, string Code, int[] LineNos)[]
        {
            ([good, new(2, foreignReceipt, m.Item, m.Warehouse, m.A, 1m)], "INV.PURCHASE_RETURN.ORIGINAL_NOT_FOUND", [2]),
            ([good, new(3, 999_999_999, m.Item, m.Warehouse, m.A, 1m)], "INV.PURCHASE_RETURN.ORIGINAL_NOT_FOUND", [3]),
            ([new(4, issue, m.Item, m.Warehouse, m.A, 1m)], "INV.PURCHASE_RETURN.ORIGINAL_NOT_RECEIPT", [4]),
            ([good, new(5, second, m.Other, m.Warehouse, m.A, 1m)], "INV.PURCHASE_RETURN.ITEM_MISMATCH", [5]),
            ([new(6, second, m.Item, m.Warehouse, m.A, 8m), new(7, second, m.Item, m.Warehouse, m.A, 2.5m),
              new(8, first, m.Item, m.Warehouse, m.A, 1m)], "INV.PURCHASE_RETURN.QUANTITY_EXCEEDED", [6, 7]),
        };
        foreach (var (lines, code, lineNos) in cases)
        {
            var refusal = await RefuseAsync(db, 81, lines);
            Assert.Equal(code, refusal.ErrorCode);
            Assert.Equal(lineNos, Assert.IsType<int[]>(refusal.Extensions!["lineNos"]));
        }
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == ExternalInventorySourceTypes.PurchaseReturn, Ct));
        Assert.False(await db.Set<InventoryReference>().AnyAsync(x => x.SourceKey.Contains(":PURCHASE_RETURN:"), Ct));

        // Exactly the original quantity of each receipt is accepted; costs follow each original.
        var full = await InTransactionAsync(db, () => External(db, 6).PostPurchaseReturnAsync(81, Day,
            [new(1, second, m.Item, m.Warehouse, m.A, 10m), new(2, first, m.Item, m.Warehouse, m.A, 9m)], Ct));
        Assert.Equal([4m, 2m], full.Movements.Select(x => x.OriginalUnitCost));
        Assert.Equal(0m, await new StockBalanceQuery(db).GetOnHandAsync(m.Item, m.Warehouse, m.A, Ct));
    }

    [Fact]
    public async Task CurrentStockIsCheckedAtTheGivenLocation()
    {
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var m = await SetupAsync(db, company);
        var (_, second) = await ReceivedAsync(db, m);

        // Nothing is on hand at B although the original received 10 at A.
        var refusal = await RefuseAsync(db, 82, new PurchaseReturnOutboundLine(3, second, m.Item, m.Warehouse, m.B, 1m));
        Assert.Equal("INV.STOCK.INSUFFICIENT", refusal.ErrorCode);
        Assert.Equal([3], Assert.IsType<int[]>(refusal.Extensions!["lineNos"]).ToArray());
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == ExternalInventorySourceTypes.PurchaseReturn, Ct));

        // A sale consumed stock: returning more than the remaining on-hand is refused even within the original quantity.
        await InTransactionAsync(db, () => External(db, 3).PostSalesOutboundAsync(1, Day,
            [new SalesOutboundLine(1, m.Item, m.Warehouse, m.A, 15m)], Ct));
        Assert.Equal("INV.STOCK.INSUFFICIENT",
            (await RefuseAsync(db, 82, new PurchaseReturnOutboundLine(1, second, m.Item, m.Warehouse, m.A, 6m))).ErrorCode);
        var posted = await InTransactionAsync(db, () => External(db, 6).PostPurchaseReturnAsync(82, Day,
            [new(1, second, m.Item, m.Warehouse, m.A, 5m)], Ct));
        Assert.Equal(4m, posted.Movements.Single().OriginalUnitCost);
    }

    [Fact]
    public async Task LotsAndSerialsMustComeFromTheOriginalReceipt()
    {
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var m = await SetupAsync(db, company);
        var receipt = await InTransactionAsync(db, () => External(db, 1).PostPurchaseInboundAsync(1, Day,
            [new PurchaseInboundLine(1, m.Lot, m.Warehouse, m.A, 5m, 7m, [InventoryTrackingLine.Lot("L-1", 3m), InventoryTrackingLine.Lot("L-2", 2m)]),
             new PurchaseInboundLine(2, m.Serial, m.Warehouse, m.A, 2m, 50m, [InventoryTrackingLine.Serial("S-1"), InventoryTrackingLine.Serial("S-2")])], Ct));
        // A different receipt brings lot L-9 and serial S-9 of the same items into stock.
        await InTransactionAsync(db, () => External(db, 2).PostPurchaseInboundAsync(2, Day,
            [new PurchaseInboundLine(1, m.Lot, m.Warehouse, m.A, 4m, 7m, [InventoryTrackingLine.Lot("L-9", 4m)]),
             new PurchaseInboundLine(2, m.Serial, m.Warehouse, m.A, 1m, 50m, [InventoryTrackingLine.Serial("S-9")])], Ct));
        var lotOriginal = receipt.MovementIds[0];
        var serialOriginal = receipt.MovementIds[1];

        var cases = new (PurchaseReturnOutboundLine[] Lines, string Code, int[] LineNos)[]
        {
            ([new(1, lotOriginal, m.Lot, m.Warehouse, m.A, 1m, [InventoryTrackingLine.Lot("L-9", 1m)])],
                "INV.PURCHASE_RETURN.TRACKING_NOT_FROM_ORIGINAL", [1]),
            // Two lines of the same original together return 4 of lot L-1 that received only 3.
            ([new(2, lotOriginal, m.Lot, m.Warehouse, m.A, 2m, [InventoryTrackingLine.Lot("l-1", 2m)]),
              new(3, lotOriginal, m.Lot, m.Warehouse, m.A, 2m, [InventoryTrackingLine.Lot("L-1", 2m)])],
                "INV.PURCHASE_RETURN.TRACKING_NOT_FROM_ORIGINAL", [2, 3]),
            ([new(4, serialOriginal, m.Serial, m.Warehouse, m.A, 1m, [InventoryTrackingLine.Serial("S-9")])],
                "INV.PURCHASE_RETURN.TRACKING_NOT_FROM_ORIGINAL", [4]),
            ([new(5, lotOriginal, m.Lot, m.Warehouse, m.A, 1m)], "INV.TRACKING.REQUIRED", [5]),
        };
        foreach (var (lines, code, lineNos) in cases)
        {
            var refusal = await RefuseAsync(db, 83, lines);
            Assert.Equal(code, refusal.ErrorCode);
            if (code.StartsWith("INV.PURCHASE_RETURN.", StringComparison.Ordinal))
                Assert.Equal(lineNos, Assert.IsType<int[]>(refusal.Extensions!["lineNos"]));
        }
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == ExternalInventorySourceTypes.PurchaseReturn, Ct));

        var posted = await InTransactionAsync(db, () => External(db, 6).PostPurchaseReturnAsync(83, Day,
            [new(1, lotOriginal, m.Lot, m.Warehouse, m.A, 3m, [InventoryTrackingLine.Lot("L-1", 1m), InventoryTrackingLine.Lot("L-2", 2m)]),
             new(2, serialOriginal, m.Serial, m.Warehouse, m.A, 1m, [InventoryTrackingLine.Serial("s-2")])], Ct));
        Assert.Equal([7m, 50m], posted.Movements.Select(x => x.OriginalUnitCost));
        var tracked = await db.Set<StockMovementTracking>().AsNoTracking()
            .Where(x => posted.Movements.Select(p => p.MovementId).Contains(x.MovementId))
            .Select(x => new { LotNo = x.Lot!.LotNo, SerialNo = x.Serial!.SerialNo, x.Quantity }).ToListAsync(Ct);
        Assert.Equal(["L-1:1", "L-2:2", "S-2:1"], tracked
            .Select(x => $"{x.LotNo ?? x.SerialNo}:{x.Quantity:0.######}").Order(StringComparer.Ordinal).ToArray());

        // A serial already issued elsewhere is unavailable even though it came from the original.
        await InTransactionAsync(db, () => External(db, 7).PostSalesOutboundAsync(1, Day,
            [new SalesOutboundLine(1, m.Serial, m.Warehouse, m.A, 1m, [InventoryTrackingLine.Serial("S-1")])], Ct));
        Assert.Equal("INV.SERIAL.NOT_AVAILABLE", (await RefuseAsync(db, 84,
            new PurchaseReturnOutboundLine(1, serialOriginal, m.Serial, m.Warehouse, m.A, 1m, [InventoryTrackingLine.Serial("S-1")]))).ErrorCode);
    }

    [Fact]
    public async Task RequestShapeIsValidatedBeforeSql()
    {
        var company = TestCompanies.Next();
        await using var db = Context(company);
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var service = External(db, 5);

        var errors = (await Assert.ThrowsAsync<RequestValidationException>(() => service.PostPurchaseReturnAsync(85, Day,
            [new(1, 0, 1, 1, null, 1m), null!, new(1, 5, 1, 1, null, 0m), new(2, 5, 0, 0, null, 1m)], Ct))).Errors;
        Assert.Contains("lines[0].originalMovementId", errors.Keys);
        Assert.Contains("lines[1]", errors.Keys);
        Assert.Contains("lines[2].quantity", errors.Keys);
        Assert.Contains("lines[3].itemId", errors.Keys);
        Assert.Contains("lines[3].warehouseId", errors.Keys);
        Assert.Contains("lines", errors.Keys); // duplicate line number
        Assert.Contains("lines", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.PostPurchaseReturnAsync(85, Day, [], Ct))).Errors.Keys);
        Assert.Contains("lines", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.PostPurchaseReturnAsync(85, Day, null!, Ct))).Errors.Keys);
        Assert.Contains("sourceId", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.PostPurchaseReturnAsync(0, Day, [new(1, 5, 1, 1, null, 1m)], Ct))).Errors.Keys);
    }

    [Fact]
    public async Task CallerRollbackLeavesNoMovementAndRetrySucceeds()
    {
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var m = await SetupAsync(db, company);
        var (first, _) = await ReceivedAsync(db, m);

        await using (var tx = await db.Database.BeginTransactionAsync(Ct))
        {
            var rolled = await External(db, 5).PostPurchaseReturnAsync(86, Day, [new(1, first, m.Item, m.Warehouse, m.A, 2m)], Ct);
            Assert.False(rolled.Replayed);
            await tx.RollbackAsync(Ct);
        }
        db.ChangeTracker.Clear();
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == ExternalInventorySourceTypes.PurchaseReturn, Ct));

        var posted = await InTransactionAsync(db, () => External(db, 6).PostPurchaseReturnAsync(86, Day,
            [new(1, first, m.Item, m.Warehouse, m.A, 2m)], Ct));
        Assert.False(posted.Replayed);
        Assert.Equal(2m, posted.Movements.Single().OriginalUnitCost);
        Assert.Equal(18m, await new StockBalanceQuery(db).GetOnHandAsync(m.Item, m.Warehouse, m.A, Ct));
    }
}
