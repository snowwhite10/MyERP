namespace MyERP.Modules.Inventory.Contracts;

/// <summary>A provisional company-wide moving-average valuation. No journal may be posted from this live result.</summary>
public sealed record InventoryValuation(
    long ItemId, DateTime? CutoffPostedAt, decimal OnHand, decimal? Value,
    decimal? AverageUnitCost, decimal UnpricedQuantity, string Status, long MovementCount, bool Provisional = true);

/// <summary>Read-only INV boundary for valuation consumers. The current company is taken from the request context.</summary>
public interface IInventoryValuationQuery
{
    Task<InventoryValuation> GetItemAsync(long itemId, DateTime? cutoffPostedAt = null, CancellationToken token = default);
}
