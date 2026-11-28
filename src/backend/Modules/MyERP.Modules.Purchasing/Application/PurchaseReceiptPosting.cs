using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// REQ_F4114.2.5 / BR-PUR-030 (WU-PUR-007b1, ADR-220): the final-approval posting step shared by zero-step SUBMIT and
/// the SYS result handler. The caller owns the Read Committed transaction and has locked the purchase order and the
/// receipt (order → receipt); this step locks the order lines in ascending ID, rebuilds the transfer from saved rows
/// only (<see cref="PurchaseReceiptApprovalAssembly"/>), moves this receipt's pending quantity to received, posts the
/// saved lines through INV as <c>PURCHASE_INBOUND</c> (saved base-currency unit cost and warehouse snapshot) and marks
/// the receipt Posted. Any refusal throws, so SYS, PUR and INV changes roll back together.
/// </summary>
public sealed class PurchaseReceiptPosting(MyErpDbContext db, IExternalInventoryPostingService inventory)
{
    public const string PostingConflict = "PUR.RECEIPT.POSTING_CONFLICT";

    public async Task PostAsync(PurchaseReceiptSource receipt, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned receipt transaction is required.");
        if (receipt.Status != "PendingApproval" || receipt.BusinessDate is not { } businessDate)
            throw new ConflictException(PurchaseReceiptApprovalResultHandler.StateChanged,
                "The receipt is no longer waiting for approval.");

        var rows = await PurchaseReceiptLocks.SavedLinesAsync(db, receipt.CompanyId, receipt.Id, token);
        var (order, sources) = await PurchaseReceiptLocks.LockSourcesAsync(db, receipt.CompanyId, receipt, rows, token);
        var own = rows.GroupBy(x => x.PurchaseOrderLineId)
            .ToDictionary(x => x.Key, x => x.Sum(r => r.Quantity!.Value));
        var changes = PurchaseReceiptApprovalAssembly.Build(receipt.CompanyId, receipt.Id,
            PurchaseReceiptLocks.Snapshot(receipt, rows), order,
            sources.Values.Select(x => new PurchaseReceiptApprovalSource(PurchaseReceiptLocks.LineSnapshot(x),
                own.GetValueOrDefault(x.Id), 0m)).ToArray());
        foreach (var change in changes)
        {
            var source = sources[change.PurchaseOrderLineId];
            source.PendingReceiptReserved = change.TargetPending;
            source.PostedReceived = change.TargetPosted;
        }

        // Each saved line becomes received evidence for invoice matching (posted_received = quantity).
        var saved = await db.Set<PurchaseReceiptSourceLine>()
            .Where(x => x.CompanyId == receipt.CompanyId && x.PurchaseReceiptId == receipt.Id)
            .OrderBy(x => x.LineNo).ToListAsync(token);
        foreach (var line in saved)
            line.PostedReceived = line.Quantity!.Value;
        var lines = saved.Select(x => new PurchaseInboundLine(x.LineNo, x.MaterialId, x.WarehouseId!.Value, null,
            x.Quantity!.Value, x.UnitCost!.Value)).ToArray();

        var result = await inventory.PostPurchaseInboundAsync(receipt.Id, businessDate, lines, token);
        // A replay means INV already holds stock for this receipt while PUR never recorded it: refuse, never re-count.
        if (result is null || result.Replayed || result.MovementIds.Count != lines.Length)
            throw new ConflictException(PostingConflict,
                "Inventory already holds a posting for this receipt; it cannot be posted again.");
        receipt.Status = "Posted";
        await db.SaveChangesAsync(token);
    }
}
