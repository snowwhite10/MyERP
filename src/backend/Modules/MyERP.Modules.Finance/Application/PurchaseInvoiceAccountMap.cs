namespace MyERP.Modules.Finance.Application;

/// <summary>
/// FI account mapping for PUR purchase invoices (BR-FI-018; ADR-116). The version must equal the one the GL posting
/// path reads for <c>PUR/PurchaseInvoice</c> through <see cref="IGlPostingMappingVersionSource"/>.
/// </summary>
public sealed record PurchaseInvoiceAccountMap(int MappingVersion, string PayableAccountCode,
    string PurchaseAccountCode, string InputTaxAccountCode);

/// <summary>Reads the company's current purchase-invoice account map; null means no usable mapping (422).</summary>
public interface IPurchaseInvoiceAccountMapSource
{
    Task<PurchaseInvoiceAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken);
}
