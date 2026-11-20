using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-SD-004a2b11: SD-DEL-002/005/006/007/009/010/011/013/014/015 for Draft edit and soft delete.</summary>
public sealed partial class SalesDeliveryCreateTransactionTests
{
    private static readonly DateOnly NextDay = new(2028, 3, 1);

    [Fact]
    public async Task EditReplacesEveryLineAndRejectedRevisionReturnsDraftWithItsHistory()
    {
        var (companyId, order) = await SeedAsync("EA", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id, lineB = order.Lines[1].Id;
        await SeedDeliveryAsync(companyId, order.Id, "Z1", SalesDeliveryStatus.Pending, lineA, 4m);
        var draft = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 1m), (lineB, 1m)]);
        var guard = new RecordingGuard();
        await using var db = Context(companyId);

        // Pending 4 consumes line A (ordered 5); the edited Draft itself reserves nothing, so exactly 1 is left.
        var over = await Assert.ThrowsAsync<BusinessRuleException>(() => Commands(db, companyId)
            .ReplaceAsync(draft.Id, 1, Input(order.Id, lineA, 1.000001m), Ct));
        Assert.Equal("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED", over.ErrorCode);
        var edited = await Commands(db, companyId, guard).ReplaceAsync(draft.Id, 1, new SalesDeliveryDraftInput(
            order.Id, NextDay,
            [
                new SalesDeliveryLineInput(5, lineA, Warehouse, Location, 1m),
                new SalesDeliveryLineInput(6, lineB, 72, null, 1.5m)
            ]), Ct);

        Assert.Equal((2, draft.DocumentNo, SalesDeliveryStatus.Draft, NextDay),
            (edited.Version, edited.DocumentNo, edited.Status, edited.BusinessDate));
        await using var verify = Context(companyId);
        var lines = await verify.Set<SalesDeliveryLine>().Where(x => x.DeliveryId == draft.Id)
            .OrderBy(x => x.LineNo).ToListAsync(Ct);
        Assert.Collection(lines,
            x => Assert.Equal((5, lineA, 31L, Warehouse, (long?)Location, 1m),
                (x.LineNo, x.OrderLineId, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity)),
            x => Assert.Equal((6, lineB, 32L, 72L, (long?)null, 1.5m),
                (x.LineNo, x.OrderLineId, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity)));
        // Every old line is replaced, never patched in place.
        Assert.DoesNotContain(lines, x => draft.Lines.Any(old => old.Id == x.Id));
        Assert.Equal([(31L, Warehouse, (long?)Location), (32L, 72L, null)], guard.Calls);

        var rejected = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Rejected, [(lineB, 1m)],
            approvalInstanceId: 55);
        var revised = await Commands(db, companyId).ReplaceAsync(rejected.Id, 1, Input(order.Id, lineB, 0.5m), Ct);
        // A Rejected revision returns to Draft and keeps the link to its SYS rounds.
        Assert.Equal((SalesDeliveryStatus.Draft, 2, 55L, 1),
            (revised.Status, revised.Version, revised.ApprovalInstanceId!.Value, revised.Lines.Count));
    }

    [Fact]
    public async Task StaleVersionsInvalidStatesAndChangedSourcesLeaveTheDeliveryUnchanged()
    {
        var (companyId, order) = await SeedAsync("EB", SalesOrderStatus.Approved);
        var other = await SeedOrderAsync(companyId, SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id;
        var draft = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 1m)]);
        var pending = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Pending, [(lineA, 1m)]);
        var posted = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Posted, [(lineA, 1m)]);
        var rejected = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Rejected, [(lineA, 1m)]);
        await using var db = Context(companyId);

        await Assert.ThrowsAsync<RequestValidationException>(() => Commands(db, companyId)
            .ReplaceAsync(draft.Id, 1, Input(order.Id, lineA, 0m), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => Commands(db, companyId)
            .ReplaceAsync(draft.Id, 2, Input(order.Id, lineA, 2m), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => Commands(db, companyId).DeleteAsync(draft.Id, 0, Ct));
        foreach (var id in new[] { pending.Id, posted.Id })
            Assert.Equal("SAL.DELIVERY.INVALID_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Commands(db, companyId).ReplaceAsync(id, 1, Input(order.Id, lineA, 1m), Ct))).ErrorCode);
        // Only a Draft is soft-deleted; Rejected must be revised first, Pending/Posted are immutable.
        foreach (var id in new[] { pending.Id, posted.Id, rejected.Id })
            Assert.Equal("SAL.DELIVERY.INVALID_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Commands(db, companyId).DeleteAsync(id, 1, Ct))).ErrorCode);

        Assert.Equal("SAL.DELIVERY.SOURCE_IMMUTABLE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Commands(db, companyId).ReplaceAsync(draft.Id, 1, Input(other.Id, other.Lines[0].Id, 1m), Ct))).ErrorCode);
        var mismatch = await Assert.ThrowsAsync<BusinessRuleException>(() => Commands(db, companyId)
            .ReplaceAsync(draft.Id, 1, new SalesDeliveryDraftInput(order.Id, Day,
            [
                new SalesDeliveryLineInput(1, lineA, Warehouse, null, 1m),
                new SalesDeliveryLineInput(3, other.Lines[0].Id, Warehouse, null, 1m)
            ]), Ct));
        Assert.Equal(("SAL.DELIVERY.ORDER_LINE_INVALID", (object?)3), (mismatch.ErrorCode, mismatch.Extensions!["lineNo"]));
        var tracked = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Commands(db, companyId, new RecordingGuard { TrackedItem = 31 })
                .ReplaceAsync(draft.Id, 1, Input(order.Id, lineA, 1m, lineNo: 2), Ct));
        Assert.Equal(("SAL.DELIVERY.TRACKING_UNSUPPORTED", (object?)2), (tracked.ErrorCode, tracked.Extensions!["lineNo"]));
        Assert.Equal("MD.WAREHOUSE.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Commands(db, companyId, new RecordingGuard { InactiveWarehouse = Warehouse })
                .ReplaceAsync(draft.Id, 1, Input(order.Id, lineA, 1m), Ct))).ErrorCode);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [order.Id], Ct);
        Assert.Equal("SAL.DELIVERY.ORDER_NOT_APPROVED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Commands(db, companyId).ReplaceAsync(draft.Id, 1, Input(order.Id, lineA, 1m), Ct))).ErrorCode);

        await using (var verify = Context(companyId))
        {
            var saved = await verify.Set<SalesDelivery>().Include(x => x.Lines).SingleAsync(x => x.Id == draft.Id, Ct);
            Assert.Equal((1, SalesDeliveryStatus.Draft, Day), (saved.Version, saved.Status, saved.BusinessDate));
            Assert.Equal([(draft.Lines[0].Id, 1m)], saved.Lines.Select(x => (x.Id, x.Quantity)));
            Assert.All(await verify.Set<SalesDelivery>().Where(x => x.Id != draft.Id).ToListAsync(Ct),
                x => Assert.Equal(1, x.Version));
        }
        // A Draft of a cancelled order can still be discarded.
        await Commands(db, companyId).DeleteAsync(draft.Id, 1, Ct);
    }

    [Fact]
    public async Task DeleteIsSoftVersionedAndADeletedDraftCannotBeChangedAgain()
    {
        var (companyId, order) = await SeedAsync("EC", SalesOrderStatus.Approved);
        var draft = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(order.Lines[0].Id, 1m)]);
        await using var db = Context(companyId);

        await Commands(db, companyId).DeleteAsync(draft.Id, 1, Ct);

        await using var verify = Context(companyId);
        var row = await verify.Set<SalesDelivery>().IgnoreQueryFilters().Include(x => x.Lines)
            .SingleAsync(x => x.Id == draft.Id, Ct);
        Assert.Equal((true, new DateTime(2026, 10, 27), 2, SalesDeliveryStatus.Draft, 1),
            (row.IsDeleted, row.DeletedAt!.Value, row.Version, row.Status, row.Lines.Count));
        Assert.False(await verify.Set<SalesDelivery>().AnyAsync(x => x.Id == draft.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, companyId).DeleteAsync(draft.Id, 2, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, companyId)
            .ReplaceAsync(draft.Id, 2, Input(order.Id, order.Lines[0].Id, 1m), Ct));
    }

    [Fact]
    public async Task EditAndDeleteNeedTheirOwnActionAndTheSourceOrderScope()
    {
        var (companyId, order) = await SeedAsync("ED", SalesOrderStatus.Approved);
        var (otherCompanyId, foreign) = await SeedAsync("EE", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id;
        var draft = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 1m)]);
        var foreignDraft = await SeedSavedAsync(otherCompanyId, foreign.Id, SalesDeliveryStatus.Draft,
            [(foreign.Lines[0].Id, 1m)]);
        var editOnly = new Dictionary<string, DataScope> { ["SAL.DELIVERY.EDIT"] = DataScope.Company };
        var deleteOnly = new Dictionary<string, DataScope> { ["SAL.DELIVERY.DELETE"] = DataScope.Company };
        var self = new Dictionary<string, DataScope>
            { ["SAL.DELIVERY.EDIT"] = DataScope.Self, ["SAL.DELIVERY.DELETE"] = DataScope.Self };
        await using var db = Context(companyId);

        await Assert.ThrowsAsync<ForbiddenException>(() => Commands(db, companyId, permissions: deleteOnly)
            .ReplaceAsync(draft.Id, 1, Input(order.Id, lineA, 1m), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Commands(db, companyId, permissions: editOnly)
            .DeleteAsync(draft.Id, 1, Ct));
        // Delivery headers have no owner; the source order (owner user 7) decides the action scope.
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, companyId, permissions: self, userId: 8)
            .ReplaceAsync(draft.Id, 1, Input(order.Id, lineA, 1m), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, companyId, permissions: self, userId: 8)
            .DeleteAsync(draft.Id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, companyId)
            .ReplaceAsync(foreignDraft.Id, 1, Input(foreign.Id, foreign.Lines[0].Id, 1m), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, companyId).DeleteAsync(foreignDraft.Id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, companyId).DeleteAsync(long.MaxValue, 1, Ct));

        var owned = await Commands(db, companyId, permissions: self)
            .ReplaceAsync(draft.Id, 1, Input(order.Id, lineA, 0.5m), Ct);
        Assert.Equal(2, owned.Version);
        await using var other = Context(otherCompanyId);
        Assert.Equal(1, (await other.Set<SalesDelivery>().SingleAsync(x => x.Id == foreignDraft.Id, Ct)).Version);
    }

    [Fact]
    public async Task EditingADraftBlocksOnlyItsOwnOrderNotOtherOrdersOrCompanies()
    {
        var (companyId, order) = await SeedAsync("EF", SalesOrderStatus.Approved);
        var sibling = await SeedOrderAsync(companyId, SalesOrderStatus.Approved);
        var (otherCompanyId, foreign) = await SeedAsync("EG", SalesOrderStatus.Approved);
        var draft = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(order.Lines[0].Id, 1m)]);
        var siblingDraft = await SeedSavedAsync(companyId, sibling.Id, SalesDeliveryStatus.Draft,
            [(sibling.Lines[0].Id, 1m)]);
        var foreignDraft = await SeedSavedAsync(otherCompanyId, foreign.Id, SalesDeliveryStatus.Draft,
            [(foreign.Lines[0].Id, 1m)]);

        var outcomes = new List<string>();
        var guard = new RecordingGuard
        {
            // Runs after the first edit replaced its lines, while that transaction is still open.
            AfterSave = async () =>
            {
                outcomes.Add(await TryCommandAsync(otherCompanyId, x => x.ReplaceAsync(foreignDraft.Id, 1,
                    Input(foreign.Id, foreign.Lines[0].Id, 1.5m), Ct)));
                outcomes.Add(await TryCommandAsync(companyId, x => x.ReplaceAsync(siblingDraft.Id, 1,
                    Input(sibling.Id, sibling.Lines[0].Id, 1.5m), Ct)));
                outcomes.Add(await TryCommandAsync(companyId, x => x.DeleteAsync(draft.Id, 1, Ct)));
                outcomes.Add(await TryCreateAsync(companyId, order.Id, order.Lines[0].Id));
            }
        };
        await using (var first = Context(companyId))
            await Commands(first, companyId, guard).ReplaceAsync(draft.Id, 1, Input(order.Id, order.Lines[0].Id, 2m), Ct);

        Assert.Equal(["done", "done", "blocked", "blocked"], outcomes);
        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesDelivery>().Include(x => x.Lines).OrderBy(x => x.Id).ToListAsync(Ct);
        Assert.Equal([(draft.Id, 2, 2m), (siblingDraft.Id, 2, 1.5m)],
            saved.Select(x => (x.Id, x.Version, x.Lines.Single().Quantity)));
        await using var other = Context(otherCompanyId);
        Assert.Equal(2, (await other.Set<SalesDelivery>().SingleAsync(x => x.Id == foreignDraft.Id, Ct)).Version);
    }

    private async Task<string> TryCommandAsync(long companyId,
        Func<SalesDeliveryDraftCommandService, Task> command)
    {
        await using var db = Context(companyId);
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000", Ct);
        try
        {
            await command(Commands(db, companyId));
            return "done";
        }
        catch (Exception error) when (IsLockTimeout(error))
        {
            return "blocked";
        }
    }

    private static bool IsLockTimeout(Exception error)
    {
        for (var inner = error; inner is not null; inner = inner.InnerException)
            if (inner is Microsoft.Data.SqlClient.SqlException { Number: 1222 }) return true;
        return false;
    }

    private async Task<SalesDelivery> SeedSavedAsync(long companyId, long orderId, SalesDeliveryStatus status,
        (long OrderLineId, decimal Quantity)[] lines, long? approvalInstanceId = null, long itemId = 31,
        long warehouseId = Warehouse)
    {
        await using var db = Context(companyId);
        var delivery = new SalesDelivery { CompanyId = companyId, DocumentNo = "S" + Guid.NewGuid().ToString("N")[..12],
            OrderId = orderId, BusinessDate = Day, Status = status, ApprovalInstanceId = approvalInstanceId,
            PostedAt = status == SalesDeliveryStatus.Posted ? new DateTime(2026, 10, 27) : null };
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = index + 1,
                OrderLineId = line.OrderLineId, ItemId = itemId, WarehouseId = warehouseId, Quantity = line.Quantity });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
        return delivery;
    }

    private static SalesDeliveryDraftLines Lines(MyErpDbContext db, IInventoryReferenceGuard? guard) =>
        new(db, guard ?? new RecordingGuard(), new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27))));

    private static SalesDeliveryDraftCommandService Commands(MyErpDbContext db, long companyId,
        IInventoryReferenceGuard? guard = null, IReadOnlyDictionary<string, DataScope>? permissions = null,
        long userId = 7)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, permissions is null,
                permissions ?? new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesDeliveryDraftCommandService(db, new SalesActionAccess(company, auth.Object), company,
            Lines(db, guard), new FixedClock(new DateTime(2026, 10, 27)));
    }
}
