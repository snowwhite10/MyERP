using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Receipt values read from storage within the caller's locked transaction.</summary>
public sealed record PurchaseReceiptApprovalSnapshot(long ReceiptId, long CompanyId,
    long PurchaseOrderId, string Status, DateOnly BusinessDate,
    IReadOnlyList<PurchaseReceiptDraftLine> Lines);

/// <summary>Builds final approval from saved rows and per-source evidence, never client quantities.</summary>
public static class PurchaseReceiptApprovalAssembly
{
    public static IReadOnlyList<PurchaseReceiptApprovalChange> Build(long requestedCompanyId,
        long requestedReceiptId, PurchaseReceiptApprovalSnapshot saved,
        PurchaseReceiptOrderSnapshot order, IReadOnlyList<PurchaseReceiptApprovalSource> sources)
    {
        if (requestedCompanyId <= 0 || requestedReceiptId <= 0 || saved is null ||
            saved.CompanyId != requestedCompanyId || saved.ReceiptId != requestedReceiptId ||
            saved.PurchaseOrderId <= 0 || saved.Lines is null || order is null ||
            order.Id != saved.PurchaseOrderId || order.CompanyId != requestedCompanyId ||
            sources is null)
            throw InvalidSnapshot();

        // Keep one stable copy of the persisted rows for validation and the transfer plan.
        var draft = PurchaseReceiptDraftInput.Validate(new(saved.PurchaseOrderId,
            saved.BusinessDate, saved.Lines.ToArray()));
        var evidence = sources.ToArray();
        return PurchaseReceiptApprovalPlan.Create(saved.Status, saved.CompanyId,
            draft, order, evidence);
    }

    private static BusinessRuleException InvalidSnapshot() =>
        new("PUR.RECEIPT.INVALID_APPROVAL_SNAPSHOT",
            "Requested receipt does not match its saved source identity.");
}
