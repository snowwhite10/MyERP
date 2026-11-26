using System.Text.Json.Serialization;
using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<PurchaseInvoiceStatus>))]
public enum PurchaseInvoiceStatus { Draft, Matched, Disputed, Rejected, Posted }

/// <summary>Invoice header; draft persistence is introduced before the transactional writer.</summary>
public sealed class PurchaseInvoice : BusinessEntity, ISoftDeletable
{
    public long SupplierId { get; set; }
    public long PurchaseOrderId { get; set; }
    public required string Currency { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string SupplierInvoiceNumber { get; set; }
    public required string NumberKey { get; set; }
    public PurchaseInvoiceStatus Status { get; set; } = PurchaseInvoiceStatus.Draft;
    public decimal TotalNet { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalGross { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>WU-PUR-008b4 (ADR-208): set together with the FI IDs only by the POST transaction.</summary>
    public DateTime? PostedAt { get; set; }

    /// <summary>Invoice version FI posted (the FI source version); the Posted save increments <c>Version</c> once more.</summary>
    public int? PostedFromVersion { get; set; }

    public long? FiPurchaseSourceId { get; set; }
    public long? FiPayableId { get; set; }
    public long? FiJournalId { get; set; }
    public List<PurchaseInvoiceLine> Lines { get; } = [];
}

public sealed class PurchaseInvoiceLine : BusinessEntity
{
    public long InvoiceId { get; set; }
    public int LineNo { get; set; }
    public long ReceiptLineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal Net { get; set; }
    public decimal Tax { get; set; }
    public decimal Gross { get; set; }
}

/// <summary>Immutable per-line price/tax difference decision recorded by RESOLVE (BR-PUR-039a).</summary>
public sealed class PurchaseInvoiceResolutionRecord : BusinessEntity
{
    public long InvoiceId { get; set; }
    public int InvoiceVersion { get; set; }
    public int LineNo { get; set; }
    public required string Action { get; set; }
    public required string Reason { get; set; }
    public decimal NetDifference { get; set; }
    public decimal TaxDifference { get; set; }
    public decimal GrossDifference { get; set; }
}
