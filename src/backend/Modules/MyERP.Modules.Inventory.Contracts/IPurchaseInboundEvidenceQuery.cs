namespace MyERP.Modules.Inventory.Contracts;

/// <summary>
/// One <c>PURCHASE_INBOUND</c> In movement of the current company (ADR-224): the posted receipt ID and line number it came
/// from, the ledger item/warehouse/location/quantity and the saved base-currency unit cost.
/// </summary>
public sealed record PurchaseInboundEvidence(long ReceiptId, int LineNo, long MovementId, long ItemId, long WarehouseId,
    long? LocationId, decimal Quantity, decimal? UnitCost);

/// <summary>
/// Read-only INV boundary for PUR purchase returns (WU-PUR-007c3, ADR-224). PUR saves the original movement ID on each
/// return line from this evidence instead of reading <c>inv_</c> tables; the posting contract revalidates it under lock
/// (ADR-223). Only the current company's ledger is visible; callers authorise the request themselves.
/// </summary>
public interface IPurchaseInboundEvidenceQuery
{
    public const int MaxReceipts = 1000;

    /// <summary>Inbound movements of the given receipts ordered by receipt ID then line number; unknown IDs return nothing.</summary>
    Task<IReadOnlyList<PurchaseInboundEvidence>> FindAsync(IReadOnlyCollection<long> receiptIds,
        CancellationToken token = default);
}
