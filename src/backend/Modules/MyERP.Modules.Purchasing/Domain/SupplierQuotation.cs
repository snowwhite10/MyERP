using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

public enum SupplierQuotationStatus { Active, Withdrawn, Awarded }

public sealed class SupplierQuotation : BusinessEntity
{
    public long RfqId { get; set; }
    public long SupplierId { get; set; }
    public int CurrentRevisionNo { get; set; }
    public SupplierQuotationStatus Status { get; set; } = SupplierQuotationStatus.Active;
    public List<QuotationRevision> Revisions { get; } = [];
}

public sealed class QuotationRevision : BusinessEntity
{
    public long QuotationId { get; set; }
    public int RevisionNo { get; set; }
    public DateOnly QuoteDate { get; set; }
    public DateOnly ValidUntil { get; set; }
    public string? Remark { get; set; }
    public List<QuotationLine> Lines { get; } = [];
}

public sealed class QuotationLine : BusinessEntity
{
    public long RevisionId { get; set; }
    public long RfqLineId { get; set; }
    public decimal UnitPrice { get; set; }
    public required string CurrencyCode { get; set; }
    public long TaxRateId { get; set; }
}
