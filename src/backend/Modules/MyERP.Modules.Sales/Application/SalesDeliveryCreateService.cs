using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// REQ_F4112.3.2 / BR-SD-021–023: creates a Draft delivery from one visible Approved order.
/// A Draft reserves nothing; the create check rejects quantities above
/// ordered − Posted − active Pending so an obviously unshippable Draft is never saved.
/// Submission (WU-SD-004a2b13) locks source lines and and rechecks the balance before reserving it.
/// </summary>
public sealed class SalesDeliveryCreateService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, INumberGenerator numbers, IInventoryReferenceGuard references,
    SalesOrderDraftStore orders)
{
    public const string DocumentType = "SAL.DELIVERY";

    public async Task<SalesDelivery> CreateAsync(SalesDeliveryDraftInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync("SAL.DELIVERY.CREATE", token);
        var companyId = company.RequireCompanyId();
        // ADR-138: Read Committed plus a primary-key update lock on the source order.
        // Creates for the same order serialize on that row; a Serializable balance join
        // would range-lock other orders' and companies' deliveries and deadlock inserts.
        // The balance here is advisory because a Draft reserves nothing; submit rechecks it.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                input.OrderId, companyId).ToListAsync(token);
            var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
                .Where(x => x.Id == input.OrderId)
                .Select(x => new { x.Id, x.Status }).SingleOrDefaultAsync(token)
                ?? throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", input.OrderId);
            if (order.Status != SalesOrderStatus.Approved)
                throw new BusinessRuleException("SAL.DELIVERY.ORDER_NOT_APPROVED",
                    "The source order must be approved before delivery.");

            var orderLines = await orders.OrderLinesQuery(companyId, order.Id).AsNoTracking()
                .Select(x => new { x.Id, x.ItemId, x.Quantity }).ToDictionaryAsync(x => x.Id, token);
            var lines = input.Lines!;
            foreach (var line in lines)
                if (!orderLines.ContainsKey(line.OrderLineId))
                    throw LineError("SAL.DELIVERY.ORDER_LINE_INVALID",
                        "The order line does not belong to the source order.", line.LineNo);

            var sourceIds = lines.Select(x => x.OrderLineId).Distinct().ToArray();
            // Seek only this order's Posted/Pending rows: a scan would wait on other
            // transactions' uncommitted delivery inserts (other orders or companies).
            var consumed = await db.Database.SqlQuery<ConsumedQuantity>($"""
                SELECT l.order_line_id, d.status, SUM(l.quantity) AS quantity
                FROM dbo.sal_delivery d WITH (FORCESEEK)
                JOIN dbo.sal_delivery_line l WITH (FORCESEEK)
                  ON l.company_id = d.company_id AND l.delivery_id = d.id
                WHERE d.company_id = {companyId} AND d.order_id = {order.Id}
                  AND d.status IN ('Pending', 'Posted') AND d.is_deleted = 0
                GROUP BY l.order_line_id, d.status
                """).ToListAsync(token);
            decimal Sum(long id, string status) => consumed
                .Where(x => x.OrderLineId == id && x.Status == status).Sum(x => x.Quantity);
            SalesDeliveryBalance.Check(
                lines.Select(x => new SalesDeliveryQuantity(x.OrderLineId, x.Quantity)).ToArray(),
                sourceIds.Select(id => new SalesOrderLineBalance(id, orderLines[id].Quantity,
                    Sum(id, nameof(SalesDeliveryStatus.Posted)),
                    Sum(id, nameof(SalesDeliveryStatus.Pending)))).ToArray());

            var delivery = new SalesDelivery
            {
                CompanyId = companyId,
                DocumentNo = string.Empty,
                OrderId = order.Id,
                BusinessDate = input.BusinessDate
            };
            foreach (var line in lines.OrderBy(x => x.LineNo))
                delivery.Lines.Add(new SalesDeliveryLine
                {
                    CompanyId = companyId,
                    LineNo = line.LineNo,
                    OrderLineId = line.OrderLineId,
                    ItemId = orderLines[line.OrderLineId].ItemId,
                    WarehouseId = line.WarehouseId,
                    LocationId = line.LocationId,
                    Quantity = line.Quantity
                });

            // MD keeps each distinct item/warehouse/location shared-locked until commit.
            var referenceSets = delivery.Lines
                .GroupBy(x => (x.ItemId, x.WarehouseId, x.LocationId))
                .OrderBy(x => x.Key.ItemId).ThenBy(x => x.Key.WarehouseId).ThenBy(x => x.Key.LocationId ?? 0)
                .ToArray();
            await GuardAsync(referenceSets, 0, async () =>
            {
                delivery.DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token);
                db.Add(delivery);
                await db.SaveChangesAsync(token);
            }, token);
            await transaction.CommitAsync(token);
            return delivery;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task GuardAsync(
        IReadOnlyList<IGrouping<(long ItemId, long WarehouseId, long? LocationId), SalesDeliveryLine>> sets,
        int index, Func<Task> save, CancellationToken token)
    {
        if (index == sets.Count)
        {
            await save();
            return;
        }
        var set = sets[index];
        await references.RunWithCodesAsync(set.Key.ItemId, set.Key.WarehouseId, set.Key.LocationId, async codes =>
        {
            // Lot/serial selections have no saved table yet (ADR-138); never save a tracked line without them.
            if (codes.TrackingMode != ItemTrackingMode.None)
                throw LineError("SAL.DELIVERY.TRACKING_UNSUPPORTED",
                    "Lot or serial tracked items cannot be delivered until tracking selection is available.",
                    set.Min(x => x.LineNo));
            await GuardAsync(sets, index + 1, save, token);
            return true;
        }, token);
    }

    private sealed record ConsumedQuantity(long OrderLineId, string Status, decimal Quantity);

    private static BusinessRuleException LineError(string code, string message, int lineNo) =>
        new(code, message) { Extensions = new Dictionary<string, object?> { ["lineNo"] = lineNo } };
}
