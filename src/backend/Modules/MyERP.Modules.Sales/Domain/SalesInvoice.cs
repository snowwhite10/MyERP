using System.Text.Json.Serialization;
using MyERP.Shared.Domain;

namespace MyERP.Modules.Sales.Domain;

/// <summary>
/// Stored invoice state (ADR-176). <see cref="Issuing"/> exists only inside the ISSUE transaction, between saving the
/// invoice and receiving the FI/AR posting receipt; a committed invoice is always <see cref="Posted"/>.
/// </summary>
[JsonConverter(typeof(SalesInvoiceStatusJsonConverter))]
public enum SalesInvoiceStatus { Issuing, Posted }

public sealed class SalesInvoiceStatusJsonConverter() : JsonStringEnumConverter<SalesInvoiceStatus>(
    namingPolicy: null, allowIntegerValues: false);

/// <summary>
/// WU-SD-007a1 (REQ_DS43.206, BR-SD-037～042): one sales invoice issued from Posted deliveries of one order. Amounts
/// are saved snapshots allocated from the source order lines; FI/AR identities are scalar references returned by the
/// FI public posting contract. SQL triggers make a Posted invoice immutable (51032) and its lines write-once (51033).
/// Invoices have no Draft, so they are never deleted; every unique key therefore covers the full history.
/// </summary>
public sealed class SalesInvoice : BusinessEntity
{
    public const int RequestKeyMaxLength = 64;
    public const int FingerprintMaxLength = 128;

    /// <summary>
    /// Revision reported to FI as <c>SourceVersion</c>. An invoice's content never changes after it is saved, so its
    /// FI source revision is constant even though the row <c>version</c> advances when Issuing becomes Posted.
    /// </summary>
    public const int SourceRevision = 1;

    public required string DocumentNo { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public long CustomerId { get; set; }
    public long OrderId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string CurrencyCode { get; set; }
    public required string BaseCurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; }
    public SalesInvoiceStatus Status { get; set; } = SalesInvoiceStatus.Issuing;
    public DateTime? PostedAt { get; set; }
    public long? FiSalesSourceId { get; set; }
    public long? FiReceivableId { get; set; }
    public long? FiJournalId { get; set; }
    public decimal OriginalNet { get; set; }
    public decimal OriginalTax { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
    public List<SalesInvoiceLine> Lines { get; } = [];
}

/// <summary>One invoiced portion of one Posted delivery line, with its allocated order-line snapshot amounts.</summary>
public sealed class SalesInvoiceLine : BusinessEntity
{
    public long InvoiceId { get; set; }
    public int LineNo { get; set; }
    public long DeliveryId { get; set; }
    public long DeliveryLineId { get; set; }
    public long OrderLineId { get; set; }
    public long ItemId { get; set; }
    public long TaxRateId { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal Quantity { get; set; }
    public decimal OriginalNet { get; set; }
    public decimal OriginalTax { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
}
