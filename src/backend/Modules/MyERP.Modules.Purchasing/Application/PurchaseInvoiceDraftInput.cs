using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceDraftInput(
    long SupplierId,
    long PurchaseOrderId,
    string? Currency,
    DateOnly BusinessDate,
    string? SupplierInvoiceNumber,
    IReadOnlyList<PurchaseInvoiceDraftLine>? Lines)
{
    /// <summary>BR-PUR-036d: checks the entire client draft shape before source lookup or write.</summary>
    public static PurchaseInvoiceDraftValidatedInput Validate(PurchaseInvoiceDraftInput? input)
    {
        if (input is null || input.SupplierId <= 0 || input.PurchaseOrderId <= 0 ||
            input.BusinessDate == default || !IsCurrencyCode(input.Currency))
            throw new BusinessRuleException("PUR.INVOICE.INVALID_HEADER", "Invoice header is invalid.");

        var number = PurchaseInvoiceNumber.Normalize(input.SupplierInvoiceNumber);
        PurchaseInvoiceDraftLines.Validate(input.Lines);
        return new(input.SupplierId, input.PurchaseOrderId, input.Currency!.ToUpperInvariant(),
            input.BusinessDate, number, Array.AsReadOnly(input.Lines!.ToArray()));
    }

    private static bool IsCurrencyCode(string? value) => value is { Length: 3 } &&
        value.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
}

public sealed record PurchaseInvoiceDraftValidatedInput(
    long SupplierId,
    long PurchaseOrderId,
    string Currency,
    DateOnly BusinessDate,
    PurchaseInvoiceNumberValue Number,
    IReadOnlyList<PurchaseInvoiceDraftLine> Lines);
