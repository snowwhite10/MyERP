namespace MyERP.Modules.Purchasing.Application;

using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

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
    PurchaseInvoiceDraftHeaderRow Header, IReadOnlyList<PurchaseInvoiceDraftLineRow> Lines)
{
    /// <summary>Creates a fresh EF graph from validated row values; persistence stamps audit fields.</summary>
    public PurchaseInvoice Materialize()
    {
        if (InvoiceId <= 0 || ExpectedVersion is <= 0)
            throw new BusinessRuleException("PUR.INVOICE.INVALID_SAVED_IDENTITY",
                "Invoice identity or revision version is invalid.");
        if (Header is null || Header.CompanyId <= 0 || Header.SupplierId <= 0 ||
            Header.PurchaseOrderId <= 0 || Lines is null || Lines.Count is < 1 or > 1000 ||
            Lines.Any(x => x is null || x.CompanyId != Header.CompanyId ||
                x.InvoiceId != InvoiceId || x.LineNo <= 0 || x.ReceiptLineId <= 0) ||
            Lines.Select(x => x.LineNo).Distinct().Count() != Lines.Count ||
            Lines.Sum(x => x.Net) != Header.TotalNet ||
            Lines.Sum(x => x.Tax) != Header.TotalTax ||
            Lines.Sum(x => x.Gross) != Header.TotalGross)
            throw new BusinessRuleException("PUR.INVOICE.INVALID_DRAFT_ROWS",
                "Invoice draft rows do not match the validated header.");

        var invoice = new PurchaseInvoice
        {
            Id = InvoiceId,
            CompanyId = Header.CompanyId,
            SupplierId = Header.SupplierId,
            PurchaseOrderId = Header.PurchaseOrderId,
            Currency = Header.Currency,
            BusinessDate = Header.BusinessDate,
            SupplierInvoiceNumber = Header.SupplierInvoiceNumber,
            NumberKey = Header.NumberKey,
            TotalNet = Header.TotalNet,
            TotalTax = Header.TotalTax,
            TotalGross = Header.TotalGross,
            Version = ExpectedVersion ?? 0
        };
        foreach (var row in Lines.OrderBy(x => x.LineNo))
            invoice.Lines.Add(new PurchaseInvoiceLine
            {
                CompanyId = row.CompanyId,
                InvoiceId = row.InvoiceId,
                LineNo = row.LineNo,
                ReceiptLineId = row.ReceiptLineId,
                Quantity = row.Quantity,
                UnitPrice = row.UnitPrice,
                TaxRatePercent = row.TaxRatePercent,
                Net = row.Net,
                Tax = row.Tax,
                Gross = row.Gross
            });
        return invoice;
    }
}
