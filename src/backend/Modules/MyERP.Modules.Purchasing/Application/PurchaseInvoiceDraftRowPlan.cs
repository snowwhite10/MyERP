namespace MyERP.Modules.Purchasing.Application;

/// <summary>Values for one invoice header, excluding audit and concurrency fields stamped by persistence.</summary>
public sealed record PurchaseInvoiceDraftHeaderRow(long CompanyId, long SupplierId,
    long PurchaseOrderId, string Currency, DateOnly BusinessDate, string SupplierInvoiceNumber,
    string NumberKey, decimal TotalNet, decimal TotalTax, decimal TotalGross);

/// <summary>Values for a saved invoice line; the writer may assign its database ID.</summary>
public sealed record PurchaseInvoiceDraftLineRow(long CompanyId, long InvoiceId, int LineNo,
    long ReceiptLineId, decimal Quantity, decimal UnitPrice, decimal TaxRatePercent,
    decimal Net, decimal Tax, decimal Gross);

/// <summary>Immutable row values derived from a single rechecked save intent.</summary>
public sealed record PurchaseInvoiceDraftRowPlan(long InvoiceId, int? ExpectedVersion,
    PurchaseInvoiceDraftHeaderRow Header, IReadOnlyList<PurchaseInvoiceDraftLineRow> Lines);
