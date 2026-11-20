using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>Saved order-line figures read after the ordered source locks.</summary>
public sealed record LockedOrderLine(long Id, decimal Quantity, decimal BaseGross);

/// <summary>
/// BR-SD-022/024 (WU-SD-004b1, ADR-144): the balance recheck and INV outbound posting shared by Draft submission
/// (including zero-step SYS approval) and the SYS final-approval handler. The caller owns the Read Committed
/// transaction and has already locked the source order and the delivery header; this step locks the order lines in
/// ascending ID, rereads every saved row and posts the saved lines through INV before the delivery becomes Posted.
/// Any refusal throws so SYS, SD and INV changes roll back together.
/// </summary>
public sealed class SalesDeliveryPosting(MyErpDbContext db, SalesDeliverySelectionStore selections,
    SalesOrderDraftStore orders, IExternalInventoryPostingService inventory, IClock clock)
{
    public const string PostingConflict = "SAL.DELIVERY.POSTING_CONFLICT";

    /// <summary>Locks the affected order lines and rechecks the full balance from one saved snapshot.</summary>
    public async Task<IReadOnlyList<LockedOrderLine>> LockAndRecheckAsync(SalesDelivery saved,
        SalesDeliveryStatus expectedStatus, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(saved);
        RequireTransaction();
        var selection = await selections.SelectAsync(saved.Id, expectedStatus, token);
        foreach (var orderLineId in selection.OrderLineLockPlan)
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_order_line WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
                "WHERE [id] = {0} AND [company_id] = {1}",
                orderLineId, saved.CompanyId).ToListAsync(token);

        var orderLines = await orders.OrderLinesQuery(saved.CompanyId, saved.OrderId).AsNoTracking()
            .Select(x => new LockedOrderLine(x.Id, x.Quantity, x.BaseGross)).ToListAsync(token);
        var snapshot = await SnapshotAsync(saved, token);
        SalesDeliverySelection.CheckLockedDecision(selection.Lines,
            orderLines.Select(x => new SavedOrderLineQuantity(x.Id, x.Quantity)).ToArray(),
            snapshot, saved.Id, expectedStatus);
        return orderLines;
    }

    /// <summary>Tracks the saved lines of <paramref name="saved"/> and their lot/serial selections with seeks only.</summary>
    public async Task LoadLinesAsync(SalesDelivery saved, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(saved);
        await db.Set<SalesDeliveryLine>().FromSqlInterpolated($"""
            SELECT * FROM dbo.sal_delivery_line
              WITH (INDEX(ix_sal_delivery_line_company_id_delivery_id_line_no), FORCESEEK)
            WHERE company_id = {saved.CompanyId} AND delivery_id = {saved.Id}
            """).LoadAsync(token);
        // Selections of this delivery's lines only (ADR-145); the delivery lock keeps them stable.
        await db.Set<SalesDeliveryLineTracking>().FromSqlInterpolated($"""
            SELECT t.* FROM dbo.sal_delivery_line l
              WITH (INDEX(ix_sal_delivery_line_company_id_delivery_id_line_no), FORCESEEK)
            JOIN dbo.sal_delivery_line_tracking t
              WITH (INDEX(ix_sal_delivery_line_tracking_company_id_line_id_seq_no), FORCESEEK)
              ON t.company_id = l.company_id AND t.line_id = l.id
            WHERE l.company_id = {saved.CompanyId} AND l.delivery_id = {saved.Id}
            """).LoadAsync(token);
    }

    /// <summary>
    /// Posts the loaded saved lines as a <c>SALES_OUTBOUND</c> source (delivery ID, saved business date, no cost or
    /// commercial price) and marks the delivery Posted. An INV replay means stock already left for this delivery while
    /// SD never recorded it, so it is a conflict rather than a second success.
    /// </summary>
    public async Task<InventoryPostingResult> PostAsync(SalesDelivery saved, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(saved);
        RequireTransaction();
        if (saved.Lines.Count == 0)
            throw new ConflictException(PostingConflict, "Delivery has no saved lines to post.");
        var lines = saved.Lines.OrderBy(x => x.LineNo)
            .Select(x => new SalesOutboundLine(x.LineNo, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity,
                ToInventoryTracking(x.Tracking)))
            .ToArray();
        var result = await inventory.PostSalesOutboundAsync(saved.Id, saved.BusinessDate, lines, token);
        if (result is null || result.Replayed || result.MovementIds.Count != lines.Length)
            throw new ConflictException(PostingConflict,
                "Inventory already holds a posting for this delivery; it cannot be posted again.");
        // ADR-146: INV returns one Out movement per line in line-number order; each saved line keeps its own.
        var ordered = saved.Lines.OrderBy(x => x.LineNo).ToArray();
        for (var i = 0; i < ordered.Length; i++)
            ordered[i].InvMovementId = result.MovementIds[i];
        saved.Status = SalesDeliveryStatus.Posted;
        saved.PostedAt = clock.LocalNow;
        await db.SaveChangesAsync(token);
        return result;
    }

    /// <summary>Saved lot/serial selections (ADR-145) in sequence order; an untracked line sends none.</summary>
    private static InventoryTrackingLine[]? ToInventoryTracking(IEnumerable<SalesDeliveryLineTracking> tracking)
    {
        var rows = tracking.OrderBy(x => x.SeqNo)
            .Select(x => x.SerialNo is { } serial
                ? InventoryTrackingLine.Serial(serial)
                : InventoryTrackingLine.Lot(x.LotNo!, x.Quantity))
            .ToArray();
        return rows.Length == 0 ? null : rows;
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned delivery transaction is required.");
    }

    /// <summary>Current delivery lines plus every Posted/Pending line of the same order, by seeks only.</summary>
    private async Task<IReadOnlyList<SavedDeliveryLineQuantity>> SnapshotAsync(SalesDelivery saved,
        CancellationToken token)
    {
        var rows = await db.Database.SqlQuery<SnapshotRow>($"""
            SELECT l.id AS line_id, l.delivery_id, l.order_line_id, l.quantity, d.status, d.is_deleted
            FROM dbo.sal_delivery d WITH (FORCESEEK)
            JOIN dbo.sal_delivery_line l WITH (FORCESEEK)
              ON l.company_id = d.company_id AND l.delivery_id = d.id
            WHERE d.company_id = {saved.CompanyId} AND d.order_id = {saved.OrderId}
              AND d.status IN ('Pending', 'Posted') AND d.is_deleted = 0 AND d.id <> {saved.Id}
            UNION ALL
            SELECT l.id, l.delivery_id, l.order_line_id, l.quantity, d.status, d.is_deleted
            FROM dbo.sal_delivery d WITH (FORCESEEK)
            JOIN dbo.sal_delivery_line l WITH (FORCESEEK)
              ON l.company_id = d.company_id AND l.delivery_id = d.id
            WHERE d.company_id = {saved.CompanyId} AND d.id = {saved.Id}
            """).ToListAsync(token);
        return rows.Select(x => new SavedDeliveryLineQuantity(x.DeliveryId, x.OrderLineId, x.Quantity,
            Enum.Parse<SalesDeliveryStatus>(x.Status), x.IsDeleted, x.LineId)).ToArray();
    }

    private sealed record SnapshotRow(long LineId, long DeliveryId, long OrderLineId, decimal Quantity,
        string Status, bool IsDeleted);
}
