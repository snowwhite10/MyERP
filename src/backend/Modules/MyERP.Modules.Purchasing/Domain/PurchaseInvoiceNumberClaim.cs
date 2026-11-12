using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

/// <summary>Permanent ownership of a supplier invoice number, including historical numbers.</summary>
public sealed class PurchaseInvoiceNumberClaim : BusinessEntity
{
    public long SupplierId { get; set; }
    public long InvoiceId { get; set; }
    public PurchaseInvoice? Invoice { get; set; }
    public required string NumberKey { get; set; }
    public bool IsActive { get; set; }
}
