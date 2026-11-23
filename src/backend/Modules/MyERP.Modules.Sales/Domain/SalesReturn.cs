using System.Text.Json.Serialization;
using MyERP.Shared.Domain;

namespace MyERP.Modules.Sales.Domain;

/// <summary>Stored return state (BR-SD-043); serialised by name only, like delivery status.</summary>
[JsonConverter(typeof(SalesReturnStatusJsonConverter))]
public enum SalesReturnStatus { Draft, Pending, Rejected, Posted }

public sealed class SalesReturnStatusJsonConverter() : JsonStringEnumConverter<SalesReturnStatus>(
    namingPolicy: null, allowIntegerValues: false);

/// <summary>
/// WU-SD-008a1 (ADR-184, REQ_DS43.208, BR-SD-043～049): one sales return of Posted delivery lines of one order and
/// customer. Currency and rate are copied from the source order; header totals equal the line sums. FI/INV identities
/// are scalar references. SQL trigger 51035 makes a Posted return immutable and checks the Posted invariants (every line
/// has its INV inbound movement, billed portions plus unbilled quantity equal the line quantity, an FI credit exists
/// exactly when a billed portion exists). Only a Draft may be soft-deleted; every unique key covers deleted history.
/// </summary>
public sealed class SalesReturn : BusinessEntity, ISoftDeletable
{
    public const int RequestKeyMaxLength = 64;
    public const int FingerprintMaxLength = 128;
    public const int RemarkMaxLength = 500;

    public required string DocumentNo { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public long CustomerId { get; set; }
    public long OrderId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string CurrencyCode { get; set; }
    public required string BaseCurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; }
    public SalesReturnStatus Status { get; set; } = SalesReturnStatus.Draft;
    public string? Remark { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public DateTime? PostedAt { get; set; }

    /// <summary>FI credit note for the billed portions; null when the return is wholly unbilled or not Posted.</summary>
    public long? FiCreditNoteId { get; set; }

    public decimal OriginalNet { get; set; }
    public decimal OriginalTax { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<SalesReturnLine> Lines { get; } = [];
}

/// <summary>
/// Returned quantity of one Posted delivery line (REQ_DS43.209). <see cref="UnbilledQuantity"/> is the portion that
/// releases SD commitment; the rest is split across <see cref="InvoiceAllocations"/>. Amounts are the saved snapshot of
/// the whole line (billed portions from invoice lines, unbilled from the order line).
/// </summary>
public sealed class SalesReturnLine : BusinessEntity
{
    public long ReturnId { get; set; }
    public int LineNo { get; set; }
    public long DeliveryId { get; set; }
    public long DeliveryLineId { get; set; }
    public long OrderLineId { get; set; }
    public long ItemId { get; set; }

    /// <summary>INV <c>SALES_OUTBOUND</c> movement of the source delivery line whose cost the inbound reverses.</summary>
    public long OriginalOutboundMovementId { get; set; }

    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnbilledQuantity { get; set; }

    /// <summary>INV <c>SALES_RETURN</c> inbound movement; set only in the posting transaction.</summary>
    public long? InvMovementId { get; set; }

    public decimal OriginalNet { get; set; }
    public decimal OriginalTax { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
    public List<SalesReturnInvoiceAllocation> InvoiceAllocations { get; } = [];
}

/// <summary>Billed portion of a return line credited against one Posted invoice line (REQ_DS43.210).</summary>
public sealed class SalesReturnInvoiceAllocation : BusinessEntity
{
    public long ReturnLineId { get; set; }
    public long InvoiceId { get; set; }
    public long InvoiceLineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal OriginalNet { get; set; }
    public decimal OriginalTax { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
}
