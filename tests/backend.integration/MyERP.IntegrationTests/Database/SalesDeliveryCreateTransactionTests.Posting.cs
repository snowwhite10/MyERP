using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-SD-004b1 (BR-SD-024, ADR-144): SD-DEL-003/005/007/010/011/014/016 — SYS final approval and zero-step submission post
/// the saved delivery through INV in one transaction; every refusal rolls SYS, SD and INV back together.
/// </summary>
public sealed partial class SalesDeliveryCreateTransactionTests
{
    private static readonly DateTime PostedAt = new(2026, 10, 27, 15, 30, 0);

    [Fact]
    public async Task FinalApprovalRefusalsRollBackSysDecisionAndKeepTheDeliveryPending()
    {
        var (companyId, order) = await SeedAsync("PA", SalesOrderStatus.Approved);
        var second = await SeedOrderAsync(companyId, SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id;
        var (submitter, approver) = await SeedFlowAsync(companyId);
        var draft = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 3m)]);
        var other = await SeedSavedAsync(companyId, second.Id, SalesDeliveryStatus.Draft, [(second.Lines[0].Id, 1m)]);
        long instance, otherInstance;
        await using (var db = Context(companyId))
        {
            instance = (await Submission(db, companyId, Live(db, submitter)).SubmitAsync(draft.Id, 1, Ct))
                .Approval.InstanceId;
            otherInstance = (await Submission(db, companyId, Live(db, submitter)).SubmitAsync(other.Id, 1, Ct))
                .Approval.InstanceId;
        }

        // INV refuses (for example insufficient stock): its error reaches the approver unchanged.
        var refusing = new RecordingInventory
        {
            Failure = new BusinessRuleException("INV.STOCK.INSUFFICIENT", "Insufficient stock.")
        };
        Assert.Equal("INV.STOCK.INSUFFICIENT", (await ApproveAsync(companyId, approver, instance, refusing)).ErrorCode);
        // INV already holds a posting for this delivery: never a second success.
        Assert.Equal("SAL.DELIVERY.POSTING_CONFLICT",
            (await ApproveAsync(companyId, approver, instance, new RecordingInventory { Replay = true })).ErrorCode);

        // The final recheck excludes only this Pending reservation: a Posted 2.000001 elsewhere leaves 2.999999 < 3.
        await SeedDeliveryAsync(companyId, order.Id, "PX", SalesDeliveryStatus.Posted, lineA, 2.000001m);
        var untouched = new RecordingInventory();
        Assert.Equal("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED",
            (await ApproveAsync(companyId, approver, instance, untouched)).ErrorCode);
        Assert.Empty(untouched.Calls);

        // The source order must still be Approved at final approval.
        await using (var db = Context(companyId))
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [second.Id], Ct);
        Assert.Equal("SAL.DELIVERY.ORDER_NOT_APPROVED",
            (await ApproveAsync(companyId, approver, otherInstance, untouched)).ErrorCode);
        Assert.Empty(untouched.Calls);

        await using var verify = Context(companyId);
        foreach (var (id, round) in new[] { (draft.Id, instance), (other.Id, otherInstance) })
        {
            Assert.Equal((SalesDeliveryStatus.Pending, 2, round, (DateTime?)null), await verify.Set<SalesDelivery>()
                .Where(x => x.Id == id)
                .Select(x => ValueTuple.Create(x.Status, x.Version, x.ApprovalInstanceId!.Value, x.PostedAt))
                .SingleAsync(Ct));
            Assert.Equal(ApprovalStatus.Pending, (await Live(verify, submitter).GetStateAsync("SAL.DELIVERY", id, Ct))!.Status);
        }
        // A cancelled source order still lets the approver reject, which releases the reservation without INV.
        await using (var rejectDb = Context(companyId))
            await Live(rejectDb, approver, untouched).RejectAsync(otherInstance, new ApprovalDecision("Cancelled", 1), Ct);
        Assert.Empty(untouched.Calls);
        Assert.Equal(SalesDeliveryStatus.Rejected,
            (await verify.Set<SalesDelivery>().AsNoTracking().SingleAsync(x => x.Id == other.Id, Ct)).Status);
    }

    [Fact]
    public async Task RealInventoryPostsTheOutboundAndInsufficientStockLeavesNoMovementOrSysRound()
    {
        long companyId, item, warehouse;
        var (seedCompany, _) = await SeedAsync("PB", SalesOrderStatus.Approved);
        companyId = seedCompany;
        await using (var setup = Context(companyId))
        {
            var uom = await new UomService(setup).CreateAsync("U" + companyId, "Unit", 0, Ct);
            item = (await new ItemService(setup).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct)).Id;
            warehouse = (await new WarehouseService(setup).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct)).Id;
            await using var receipt = await setup.Database.BeginTransactionAsync(Ct);
            await RealInventory(setup).PostPurchaseInboundAsync(1, Day,
                [new PurchaseInboundLine(1, item, warehouse, null, 3m, 10m)], Ct);
            await receipt.CommitAsync(Ct);
        }
        var order = await SeedOrderAsync(companyId, SalesOrderStatus.Approved, itemA: item);
        long lineA = order.Lines[0].Id;
        var tooMuch = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 3.000001m)],
            itemId: item, warehouseId: warehouse);
        var fits = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 1m), (lineA, 2m)],
            itemId: item, warehouseId: warehouse);

        // Zero-step submission (no flow): INV checks stock in the SD transaction.
        await using (var db = Context(companyId))
        {
            var live = Live(db, 7);
            Assert.Equal("INV.STOCK.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Submission(db, companyId, live, inventory: RealInventory(db)).SubmitAsync(tooMuch.Id, 1, Ct))).ErrorCode);
            Assert.Null(await live.GetStateAsync("SAL.DELIVERY", tooMuch.Id, Ct));

            var posted = await Submission(db, companyId, live, inventory: RealInventory(db)).SubmitAsync(fits.Id, 1, Ct);
            Assert.Equal(SalesDeliveryStatus.Posted, posted.Delivery.Status);
        }

        await using var verify = Context(companyId);
        var movements = await verify.Set<StockMovement>().AsNoTracking()
            .Where(x => x.SourceType == ExternalInventorySourceTypes.SalesOutbound)
            .OrderBy(x => x.SourceLineNo).ToListAsync(Ct);
        // Only the posted delivery left stock, one Out movement per saved line; nothing from the refused one.
        Assert.Equal([(fits.Id, 1, 1m), (fits.Id, 2, 2m)],
            movements.Select(x => (x.SourceId, x.SourceLineNo, x.Quantity)));
        Assert.All(movements, x => Assert.Equal((StockMovementDirection.Out, item, warehouse, Day),
            (x.Direction, x.ItemId, x.WarehouseId, x.BusinessDate)));
        // SD-DEL-020 (WU-SD-004b2, ADR-146): each saved line keeps the INV movement it posted, by line number;
        // the refused delivery stores none.
        Assert.Equal(movements.Select(x => (long?)x.Id), await verify.Set<SalesDeliveryLine>().AsNoTracking()
            .Where(x => x.DeliveryId == fits.Id).OrderBy(x => x.LineNo).Select(x => x.InvMovementId).ToListAsync(Ct));
        Assert.All(await verify.Set<SalesDeliveryLine>().AsNoTracking().Where(x => x.DeliveryId == tooMuch.Id)
            .Select(x => x.InvMovementId).ToListAsync(Ct), Assert.Null);
        Assert.Equal((SalesDeliveryStatus.Draft, 1), await verify.Set<SalesDelivery>().Where(x => x.Id == tooMuch.Id)
            .Select(x => ValueTuple.Create(x.Status, x.Version)).SingleAsync(Ct));
        // All 3 received units have left; the Draft over-quantity cannot pass the balance check either way.
        Assert.Equal(0m, await verify.Set<StockMovement>().AsNoTracking().Where(x => x.ItemId == item)
            .SumAsync(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity, Ct));
    }

    private async Task<ErpException> ApproveAsync(long companyId, long approver, long instance,
        IExternalInventoryPostingService inventory)
    {
        await using var db = Context(companyId);
        return await Assert.ThrowsAnyAsync<ErpException>(() =>
            Live(db, approver, inventory).ApproveAsync(instance, new ApprovalDecision(null, 1), Ct));
    }
}
