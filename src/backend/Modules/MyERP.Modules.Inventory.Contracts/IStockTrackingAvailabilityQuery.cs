namespace MyERP.Modules.Inventory.Contracts;

/// <summary>A lot or serial number with positive stock at one warehouse/location; serials always have quantity 1.</summary>
public sealed record AvailableTrackingNumber(string Number, decimal Quantity, DateOnly? ManufactureDate, DateOnly? ExpiryDate);

/// <summary>At most the requested limit of numbers; <see cref="Truncated"/> says more matched.</summary>
public sealed record AvailableTrackingNumbers(IReadOnlyList<AvailableTrackingNumber> Items, bool Truncated);

/// <summary>
/// Read-only INV boundary for documents that pick lots/serials to issue (ADR-152). The balance is on hand at exactly
/// the given warehouse and location (null = stock without a location), derived live from the append-only movement
/// tracking rows of the current company. It does not subtract other documents' pending picks; posting rechecks stock.
/// Callers authorise the request themselves.
/// </summary>
public interface IStockTrackingAvailabilityQuery
{
    public const int MaxLimit = 200;

    /// <summary>Lots with positive stock, earliest expiry first (no expiry last), then lot number.</summary>
    Task<AvailableTrackingNumbers> LotsAsync(long itemId, long warehouseId, long? locationId, string? keyword, int limit,
        CancellationToken token = default);

    /// <summary>Serials in stock, by serial number.</summary>
    Task<AvailableTrackingNumbers> SerialsAsync(long itemId, long warehouseId, long? locationId, string? keyword, int limit,
        CancellationToken token = default);
}
