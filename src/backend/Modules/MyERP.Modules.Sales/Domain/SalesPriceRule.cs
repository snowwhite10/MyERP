using MyERP.Shared.Domain;

namespace MyERP.Modules.Sales.Domain;

/// <summary>
/// Dated selling price for one item and currency (REQ_F4112.4.1, BR-SD-026/027, ADR-157). A null
/// <see cref="CustomerId"/> is the company default; a customer rule wins over the default on its dates.
/// Item and customer remain scalar MD IDs. Active same-key intervals never overlap (application check under a
/// SQL application lock).
/// </summary>
public sealed class SalesPriceRule : MasterEntity
{
    public long ItemId { get; set; }
    public long? CustomerId { get; set; }
    public required string CurrencyCode { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Highest ordinary discount percentage (0–100) a line may use without pricing override.</summary>
    public decimal DiscountCeiling { get; set; }
}
