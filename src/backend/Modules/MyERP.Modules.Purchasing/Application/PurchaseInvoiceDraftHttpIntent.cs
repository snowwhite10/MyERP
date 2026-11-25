using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Validated command boundary for future authorized Draft HTTP endpoints.</summary>
public sealed class PurchaseInvoiceDraftHttpIntent
{
    private PurchaseInvoiceDraftHttpIntent(long companyId, long? invoiceId,
        int? expectedVersion, PurchaseInvoiceDraftValidatedInput input)
    {
        CompanyId = companyId;
        InvoiceId = invoiceId;
        ExpectedVersion = expectedVersion;
        Input = input;
        ReceiptLineIds = Array.AsReadOnly(input.Lines.Select(line => line.ReceiptLineId)
            .Distinct().Order().ToArray());
    }

    public long CompanyId { get; }
    public long? InvoiceId { get; }
    public int? ExpectedVersion { get; }
    public PurchaseInvoiceDraftValidatedInput Input { get; }
    /// <summary>Frozen source scope for the transaction's ordered authority lookup.</summary>
    public IReadOnlyList<long> ReceiptLineIds { get; }

    public static PurchaseInvoiceDraftHttpIntent Create(long companyId, PurchaseInvoiceDraftInput? body)
    {
        RequireCompany(companyId);
        return new(companyId, null, null, PurchaseInvoiceDraftInput.Validate(body));
    }

    public static PurchaseInvoiceDraftHttpIntent Edit(long companyId, long invoiceId,
        int expectedVersion, PurchaseInvoiceDraftInput? body)
    {
        RequireCompany(companyId);
        if (invoiceId <= 0 || expectedVersion <= 0)
            throw InvalidCommand();
        return new(companyId, invoiceId, expectedVersion, PurchaseInvoiceDraftInput.Validate(body));
    }

    private static void RequireCompany(long companyId)
    {
        if (companyId <= 0)
            throw InvalidCommand();
    }

    private static BusinessRuleException InvalidCommand() =>
        new("PUR.INVOICE.INVALID_DRAFT_COMMAND", "Invoice draft command identity or version is invalid.");
}
