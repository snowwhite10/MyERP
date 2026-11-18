using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-SD-004a2b10: SD-DEL-001/006/007/008/011/013/014/015 for Draft creation on real SQL.</summary>
public sealed partial class SalesDeliveryCreateTransactionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);
    private const long Warehouse = 70;
    private const long Location = 71;

    [Fact]
    public async Task ApprovedOrderCreatesDraftWithSavedItemsAndExactRemainingBalance()
    {
        var (companyId, order) = await SeedAsync("DA", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id, lineB = order.Lines[1].Id;
        // Posted 1 and Pending 1.5 consume line A (ordered 5); Draft, Rejected and deleted do not.
        await SeedDeliveryAsync(companyId, order.Id, "X1", SalesDeliveryStatus.Posted, lineA, 1m);
        await SeedDeliveryAsync(companyId, order.Id, "X2", SalesDeliveryStatus.Pending, lineA, 1.5m);
        await SeedDeliveryAsync(companyId, order.Id, "X3", SalesDeliveryStatus.Draft, lineA, 4m);
        await SeedDeliveryAsync(companyId, order.Id, "X4", SalesDeliveryStatus.Rejected, lineA, 4m);
        await SeedDeliveryAsync(companyId, order.Id, "X5", SalesDeliveryStatus.Draft, lineA, 4m, deleted: true);
        var guard = new RecordingGuard();
        await using var db = Context(companyId);

        var saved = await Service(db, companyId, guard).CreateAsync(new SalesDeliveryDraftInput(order.Id, Day,
        [
            new SalesDeliveryLineInput(1, lineA, Warehouse, Location, 1.000001m),
            new SalesDeliveryLineInput(2, lineA, Warehouse, Location, 1.499999m),
            new SalesDeliveryLineInput(3, lineB, Warehouse, null, 0.000001m)
        ]), Ct);

        Assert.Equal("DA001", saved.DocumentNo);
        await using var verify = Context(companyId);
        var reloaded = await verify.Set<SalesDelivery>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == saved.Id, Ct);
        Assert.Equal(SalesDeliveryStatus.Draft, reloaded.Status);
        Assert.Equal(order.Id, reloaded.OrderId);
        Assert.Equal(Day, reloaded.BusinessDate);
        Assert.Null(reloaded.PostedAt);
        Assert.Collection(reloaded.Lines.OrderBy(x => x.LineNo),
            x => Assert.Equal((1, lineA, 31L, Location, 1.000001m), (x.LineNo, x.OrderLineId, x.ItemId, x.LocationId!.Value, x.Quantity)),
            x => Assert.Equal((2, lineA, 31L, 1.499999m), (x.LineNo, x.OrderLineId, x.ItemId, x.Quantity)),
            x => Assert.Equal((3, lineB, 32L, (long?)null, 0.000001m), (x.LineNo, x.OrderLineId, x.ItemId, x.LocationId, x.Quantity)));
        // Each distinct item/warehouse/location reference is validated once through MD.
        Assert.Equal([(31L, Warehouse, (long?)Location), (32L, Warehouse, null)], guard.Calls);
    }

    [Fact]
    public async Task OneMicroUnitOverBalanceAndFailedSaveLeaveNoDeliveryOrConsumedNumber()
    {
        var (companyId, order) = await SeedAsync("DB", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id;
        await SeedDeliveryAsync(companyId, order.Id, "Y1", SalesDeliveryStatus.Pending, lineA, 2.5m);
        await using var db = Context(companyId);
        var over = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, companyId)
            .CreateAsync(Input(order.Id, lineA, 2.500001m), Ct));
        Assert.Equal("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED", over.ErrorCode);

        var ok = await Service(db, companyId).CreateAsync(Input(order.Id, lineA, 2.5m), Ct);
        Assert.Equal("DB001", ok.DocumentNo);
        // The number advances, but a duplicate document number fails the insert: all of it rolls back.
        await using var failing = Context(companyId);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(failing, companyId,
            numbers: new FixedNumberAfterAllocation(Numbers(failing), "DB001"))
            .CreateAsync(Input(order.Id, lineA, 1m), Ct));

        await using var verify = Context(companyId);
        Assert.Equal(1, await verify.Set<SalesDelivery>().CountAsync(x => x.DocumentNo.StartsWith("DB"), Ct));
        Assert.Equal(1, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task SourceOrderMustBeVisibleApprovedAndOwnTheLines()
    {
        var (companyId, approved) = await SeedAsync("DC", SalesOrderStatus.Approved);
        var draft = await SeedOrderAsync(companyId, SalesOrderStatus.Draft);
        var cancelled = await SeedOrderAsync(companyId, SalesOrderStatus.Cancelled);
        var (otherCompanyId, foreign) = await SeedAsync("DO", SalesOrderStatus.Approved);
        await using var db = Context(companyId);

        var notApproved = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, companyId)
            .CreateAsync(Input(draft.Id, draft.Lines[0].Id, 1m), Ct));
        Assert.Equal("SAL.DELIVERY.ORDER_NOT_APPROVED", notApproved.ErrorCode);
        Assert.Equal("SAL.DELIVERY.ORDER_NOT_APPROVED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db, companyId).CreateAsync(Input(cancelled.Id, cancelled.Lines[0].Id, 1m), Ct))).ErrorCode);

        // A line of another order, even in the same company, names the offending line.
        var mismatch = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, companyId)
            .CreateAsync(new SalesDeliveryDraftInput(approved.Id, Day,
            [
                new SalesDeliveryLineInput(1, approved.Lines[0].Id, Warehouse, null, 1m),
                new SalesDeliveryLineInput(7, draft.Lines[0].Id, Warehouse, null, 1m)
            ]), Ct));
        Assert.Equal("SAL.DELIVERY.ORDER_LINE_INVALID", mismatch.ErrorCode);
        Assert.Equal(7, mismatch.Extensions!["lineNo"]);

        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, companyId)
            .CreateAsync(Input(foreign.Id, foreign.Lines[0].Id, 1m), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, companyId)
            .CreateAsync(Input(long.MaxValue, 1, 1m), Ct));
        // CREATE scope "Self" for another user hides the order; missing CREATE is 403.
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, companyId, userId: 8,
            scope: DataScope.Self).CreateAsync(Input(approved.Id, approved.Lines[0].Id, 1m), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, companyId, allowCreate: false)
            .CreateAsync(Input(approved.Id, approved.Lines[0].Id, 1m), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => Service(db, companyId)
            .CreateAsync(Input(approved.Id, approved.Lines[0].Id, 0m), Ct));

        await using var verify = Context(companyId);
        Assert.False(await verify.Set<SalesDelivery>().AnyAsync(Ct));
        Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
        await using var other = Context(otherCompanyId);
        Assert.False(await other.Set<SalesDelivery>().AnyAsync(Ct));
    }

    [Fact]
    public async Task InventoryReferenceFailuresAndTrackingMismatchesRollBackTheWholeDraft()
    {
        var (companyId, order) = await SeedAsync("DD", SalesOrderStatus.Approved);
        await using var db = Context(companyId);
        var inactive = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db, companyId, new RecordingGuard { InactiveWarehouse = 99 }).CreateAsync(
                new SalesDeliveryDraftInput(order.Id, Day,
                [
                    new SalesDeliveryLineInput(1, order.Lines[0].Id, Warehouse, null, 1m),
                    new SalesDeliveryLineInput(2, order.Lines[1].Id, 99, null, 1m)
                ]), Ct));
        Assert.Equal("MD.WAREHOUSE.INACTIVE", inactive.ErrorCode);

        // ADR-140: selections must match the MD tracking mode; the failing line is named.
        var tracked = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db, companyId, new RecordingGuard { TrackedItem = 32 }).CreateAsync(
                new SalesDeliveryDraftInput(order.Id, Day,
                [
                    new SalesDeliveryLineInput(4, order.Lines[1].Id, Warehouse, null, 1m,
                        [new SalesDeliveryTrackingInput(null, "SN-1", 1m)])
                ]), Ct));
        Assert.Equal("SAL.DELIVERY.TRACKING_MODE_MISMATCH", tracked.ErrorCode);
        Assert.Equal(4, tracked.Extensions!["lineNo"]);

        await using var verify = Context(companyId);
        Assert.False(await verify.Set<SalesDelivery>().AnyAsync(Ct));
        Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
    }

    [Fact]
    public async Task SameOrderCreatesSerializeWhileOtherOrdersAndCompaniesProceed()
    {
        var (companyId, order) = await SeedAsync("DE", SalesOrderStatus.Approved);
        var sibling = await SeedOrderAsync(companyId, SalesOrderStatus.Approved);
        var (otherCompanyId, foreign) = await SeedAsync("DF", SalesOrderStatus.Approved);
        await using (var warm = Context(otherCompanyId))
            await Service(warm, otherCompanyId).CreateAsync(Input(foreign.Id, foreign.Lines[0].Id, 1m), Ct);

        var outcomes = new List<string>();
        var guard = new RecordingGuard
        {
            // Runs after the first Draft is inserted, while its transaction is still open.
            AfterSave = async () =>
            {
                outcomes.Add(await TryCreateAsync(otherCompanyId, foreign.Id, foreign.Lines[0].Id));
                outcomes.Add(await TryCreateAsync(companyId, sibling.Id, sibling.Lines[0].Id));
                outcomes.Add(await TryCreateAsync(companyId, order.Id, order.Lines[0].Id));
            }
        };
        await using (var first = Context(companyId))
            await Service(first, companyId, guard).CreateAsync(Input(order.Id, order.Lines[0].Id, 1m), Ct);

        // SD reads seek only their own order; the same order waits on the source-order update lock.
        Assert.Equal(["created", "created", "blocked"], outcomes);
        await using var verify = Context(companyId);
        Assert.Equal(2, await verify.Set<SalesDelivery>().CountAsync(Ct));
        await using var other = Context(otherCompanyId);
        Assert.Equal(2, await other.Set<SalesDelivery>().CountAsync(Ct));
    }

    private async Task<string> TryCreateAsync(long companyId, long orderId, long orderLineId)
    {
        await using var db = Context(companyId);
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000", Ct);
        try
        {
            // SYS numbering has its own cross-company locking (backlog WU-SYS-NUMSEQ-LOCK);
            // a fixed unique number isolates the SD reads and locks under test.
            await Service(db, companyId, numbers: new StaticNumber("P" + Guid.NewGuid().ToString("N")[..12]))
                .CreateAsync(Input(orderId, orderLineId, 1m), Ct);
            return "created";
        }
        catch (Exception error) when (IsLockTimeout(error))
        {
            return "blocked";
        }
    }

    private sealed class StaticNumber(string number) : INumberGenerator
    {
        public Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default) => Task.FromResult(number);
    }

    private static SalesDeliveryDraftInput Input(long orderId, long orderLineId, decimal quantity, int lineNo = 1) =>
        new(orderId, Day, [new SalesDeliveryLineInput(lineNo, orderLineId, Warehouse, null, quantity)]);

    private async Task<(long CompanyId, SalesOrder Order)> SeedAsync(string prefix, SalesOrderStatus status)
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales delivery test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using (var rule = Context(companyId))
        {
            rule.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.DELIVERY", Name = "Sales delivery",
                Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
                SequenceLength = 3 });
            await rule.SaveChangesAsync(Ct);
        }
        return (companyId, await SeedOrderAsync(companyId, status));
    }

    private async Task<SalesOrder> SeedOrderAsync(long companyId, SalesOrderStatus status)
    {
        await using var db = Context(companyId);
        var order = new SalesOrder { CompanyId = companyId, OwnerUserId = 7,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10], CustomerId = 21, CustomerCode = "C21",
            CustomerName = "Customer", BusinessDate = Day, CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
            ExchangeRate = 1m, OriginalNet = 65m, OriginalGross = 65m, BaseNet = 65m, BaseGross = 65m };
        order.Lines.Add(OrderLine(companyId, 1, 31, 5m));
        order.Lines.Add(OrderLine(companyId, 2, 32, 1.5m));
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        if (status != SalesOrderStatus.Draft)
        {
            // Lines are inserted under Draft (trigger 51004); then the header follows the allowed transitions.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2026-10-27' WHERE id = {0}", [order.Id], Ct);
            if (status == SalesOrderStatus.Cancelled)
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [order.Id], Ct);
        }
        return order;
    }

    private static SalesOrderLine OrderLine(long companyId, int lineNo, long itemId, decimal quantity) => new()
    {
        CompanyId = companyId, LineNo = lineNo, ItemId = itemId, ItemCode = "I" + itemId, ItemName = "Item",
        Quantity = quantity, UnitPrice = 10m, TaxRateId = 41, OriginalNet = quantity * 10m,
        OriginalGross = quantity * 10m, BaseNet = quantity * 10m, BaseGross = quantity * 10m
    };

    private async Task SeedDeliveryAsync(long companyId, long orderId, string number, SalesDeliveryStatus status,
        long orderLineId, decimal quantity, bool deleted = false)
    {
        await using var db = Context(companyId);
        var delivery = new SalesDelivery { CompanyId = companyId, DocumentNo = number + companyId, OrderId = orderId,
            BusinessDate = Day, Status = status, IsDeleted = deleted,
            DeletedAt = deleted ? new DateTime(2026, 10, 27) : null,
            PostedAt = status == SalesDeliveryStatus.Posted ? new DateTime(2026, 10, 27) : null };
        delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 1, OrderLineId = orderLineId,
            ItemId = 31, WarehouseId = Warehouse, Quantity = quantity });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesDeliveryDbModel(), new SalesOrderDbModel(), new SalesQuotationDbModel(),
            new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(), new CustomerDbModel(),
            new PaymentSetupDbModel(), new CurrencyDbModel(), new TaxDbModel()],
        new FixedCompany(companyId));

    private static NumberGenerator Numbers(MyErpDbContext db) =>
        new(db, new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7));

    private static SalesDeliveryCreateService Service(MyErpDbContext db, long companyId,
        IInventoryReferenceGuard? guard = null, INumberGenerator? numbers = null, bool allowCreate = true,
        long userId = 7, DataScope? scope = null)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, allowCreate && scope is null,
                allowCreate && scope is { } s
                    ? new Dictionary<string, DataScope> { ["SAL.DELIVERY.CREATE"] = s }
                    : new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesDeliveryCreateService(db, new SalesActionAccess(company, auth.Object), company,
            numbers ?? Numbers(db), Lines(db, guard));
    }

    private sealed class RecordingGuard : IInventoryReferenceGuard
    {
        public long? InactiveWarehouse { get; init; }
        public long? TrackedItem { get; init; }
        public long? SerialItem { get; init; }
        public Func<Task>? AfterSave { get; set; }
        public List<(long, long, long?)> Calls { get; } = [];

        public Task<T> RunAsync<T>(long itemId, long warehouseId, long? locationId, Func<Task<T>> action,
            CancellationToken token = default) =>
            RunWithCodesAsync(itemId, warehouseId, locationId, _ => action(), token);

        public async Task<T> RunWithCodesAsync<T>(long itemId, long warehouseId, long? locationId,
            Func<InventoryReferenceCodes, Task<T>> action, CancellationToken token = default)
        {
            Calls.Add((itemId, warehouseId, locationId));
            if (warehouseId == InactiveWarehouse)
                throw new BusinessRuleException("MD.WAREHOUSE.INACTIVE", "Warehouse is inactive.");
            var result = await action(new InventoryReferenceCodes("I" + itemId, "W" + warehouseId, null,
                itemId == TrackedItem ? ItemTrackingMode.Lot
                    : itemId == SerialItem ? ItemTrackingMode.Serial : ItemTrackingMode.None));
            if (AfterSave is { } callback)
            {
                AfterSave = null;
                await callback();
            }
            return result;
        }

        public Task RequireWarehousesAsync(IReadOnlyCollection<long> warehouseIds, CancellationToken token = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedNumberAfterAllocation(INumberGenerator inner, string number) : INumberGenerator
    {
        public async Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default)
        {
            await inner.NextAsync(documentType, documentDate, cancellationToken);
            return number;
        }
    }
}
