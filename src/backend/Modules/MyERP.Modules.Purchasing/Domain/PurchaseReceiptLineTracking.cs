using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

/// <summary>
/// One captured lot or serial of a purchase receipt line (pur_purchase_receipt_line_tracking, WU-PUR-007b2, ADR-221).
/// Exactly one of <see cref="LotNo"/> / <see cref="SerialNo"/>; a serial is one base unit; manufacture and expiry dates
/// apply to lots only. A Draft may be incomplete; SUBMIT requires the full line quantity and INV validates at posting.
/// </summary>
public sealed class PurchaseReceiptLineTracking : BusinessEntity
{
    public const int LotMaxLength = 50;
    public const int SerialMaxLength = 100;

    public long LineId { get; set; }
    public int SeqNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly? ManufactureDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}
