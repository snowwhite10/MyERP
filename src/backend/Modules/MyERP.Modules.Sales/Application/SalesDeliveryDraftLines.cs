using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// BR-SD-022/023 for Draft create and edit (ADR-138): order lock, saved order-line ownership,
/// advisory remaining balance and MD reference checks. The caller owns the Read Committed
/// transaction, permission, scope, order state and the delivery header.
/// </summary>
public sealed class SalesDeliveryDraftLines(MyErpDbContext db, IInventoryReferenceGuard references,
    SalesOrderDraftStore orders)
{
    /// <summary>
    /// Primary-key update lock on the source order. Every delivery command for one order
    /// serializes here before touching delivery rows; other orders and companies are never locked.
    /// </summary>
    public async Task LockOrderAsync(long companyId, long orderId, CancellationToken token)
    {
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            orderId, companyId).ToListAsync(token);
    }

    /// <summary>
    /// Builds new lines from saved order lines. Item identity comes from the order line;
    /// quantities may not exceed ordered − Posted − active Pending of other deliveries.
    /// </summary>
    public async Task<List<SalesDeliveryLine>> BuildAsync(long companyId, long orderId,
        long? currentDeliveryId, IReadOnlyList<SalesDeliveryLineInput> lines, CancellationToken token)
    {
        var orderLines = await orders.OrderLinesQuery(companyId, orderId).AsNoTracking()
            .Select(x => new { x.Id, x.ItemId, x.Quantity }).ToDictionaryAsync(x => x.Id, token);
        foreach (var line in lines)
            if (!orderLines.ContainsKey(line.OrderLineId))
                throw LineError("SAL.DELIVERY.ORDER_LINE_INVALID",
                    "The order line does not belong to the source order.", line.LineNo);

        var excluded = currentDeliveryId ?? 0L;
        // Seek only this order's Posted/Pending rows: a scan would wait on other
        // transactions' uncommitted delivery writes (other orders or companies).
        var consumed = await db.Database.SqlQuery<ConsumedQuantity>($"""
            SELECT l.order_line_id, d.status, SUM(l.quantity) AS quantity
            FROM dbo.sal_delivery d WITH (FORCESEEK)
            JOIN dbo.sal_delivery_line l WITH (FORCESEEK)
              ON l.company_id = d.company_id AND l.delivery_id = d.id
            WHERE d.company_id = {companyId} AND d.order_id = {orderId}
              AND d.status IN ('Pending', 'Posted') AND d.is_deleted = 0 AND d.id <> {excluded}
            GROUP BY l.order_line_id, d.status
            """).ToListAsync(token);
        decimal Sum(long id, string status) => consumed
            .Where(x => x.OrderLineId == id && x.Status == status).Sum(x => x.Quantity);
        SalesDeliveryBalance.Check(
            lines.Select(x => new SalesDeliveryQuantity(x.OrderLineId, x.Quantity)).ToArray(),
            lines.Select(x => x.OrderLineId).Distinct()
                .Select(id => new SalesOrderLineBalance(id, orderLines[id].Quantity,
                    Sum(id, nameof(SalesDeliveryStatus.Posted)),
                    Sum(id, nameof(SalesDeliveryStatus.Pending)))).ToArray());

        return lines.OrderBy(x => x.LineNo).Select(line => new SalesDeliveryLine
        {
            CompanyId = companyId,
            LineNo = line.LineNo,
            OrderLineId = line.OrderLineId,
            ItemId = orderLines[line.OrderLineId].ItemId,
            WarehouseId = line.WarehouseId,
            LocationId = line.LocationId,
            Quantity = line.Quantity
        }).ToList();
    }

    /// <summary>
    /// Validates each distinct item/warehouse/location through MD, keeps them shared-locked
    /// and runs <paramref name="save"/> inside the innermost check.
    /// </summary>
    public Task GuardAsync(IEnumerable<SalesDeliveryLine> lines, Func<Task> save, CancellationToken token)
    {
        var sets = lines
            .GroupBy(x => (x.ItemId, x.WarehouseId, x.LocationId))
            .OrderBy(x => x.Key.ItemId).ThenBy(x => x.Key.WarehouseId).ThenBy(x => x.Key.LocationId ?? 0)
            .ToArray();
        return GuardAsync(sets, 0, save, token);
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
