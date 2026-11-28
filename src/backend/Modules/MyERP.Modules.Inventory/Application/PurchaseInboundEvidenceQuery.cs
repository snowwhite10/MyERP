using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Application;

/// <summary>
/// ADR-224: posted purchase-receipt movements from the append-only ledger; the company filter applies automatically, and
/// a missing company sees nothing.
/// </summary>
public sealed class PurchaseInboundEvidenceQuery(MyErpDbContext db) : IPurchaseInboundEvidenceQuery
{
    public async Task<IReadOnlyList<PurchaseInboundEvidence>> FindAsync(IReadOnlyCollection<long> receiptIds,
        CancellationToken token = default)
    {
        if (receiptIds is null || receiptIds.Count > IPurchaseInboundEvidenceQuery.MaxReceipts ||
            receiptIds.Any(x => x <= 0))
            throw new RequestValidationException("receiptIds",
                $"Receipt IDs must be 0 to {IPurchaseInboundEvidenceQuery.MaxReceipts} positive values.");
        if (receiptIds.Count == 0) return [];
        var ids = receiptIds.Distinct().ToArray();
        return await db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.SourceType == ExternalInventorySourceTypes.PurchaseInbound
                && x.Direction == StockMovementDirection.In && ids.Contains(x.SourceId))
            .OrderBy(x => x.SourceId).ThenBy(x => x.SourceLineNo)
            .Select(x => new PurchaseInboundEvidence(x.SourceId, x.SourceLineNo, x.Id, x.ItemId, x.WarehouseId,
                x.LocationId, x.Quantity, x.UnitCost))
            .ToListAsync(token);
    }
}
