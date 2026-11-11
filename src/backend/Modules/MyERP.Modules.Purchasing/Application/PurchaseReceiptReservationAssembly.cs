using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Builds submit or rejection transitions from rows read by the caller's locked transaction.</summary>
public static class PurchaseReceiptReservationAssembly
{
    public static IReadOnlyList<PurchaseReceiptReservationChange> ForSubmit(long requestedCompanyId,
        long requestedReceiptId, PurchaseReceiptApprovalSnapshot saved,
        PurchaseReceiptOrderSnapshot order, IReadOnlyList<PurchaseReceiptReservationSource> sources) =>
        Build(requestedCompanyId, requestedReceiptId, saved, order, sources, rejecting: false);

    public static IReadOnlyList<PurchaseReceiptReservationChange> ForReject(long requestedCompanyId,
        long requestedReceiptId, PurchaseReceiptApprovalSnapshot saved,
        PurchaseReceiptOrderSnapshot order, IReadOnlyList<PurchaseReceiptReservationSource> sources) =>
        Build(requestedCompanyId, requestedReceiptId, saved, order, sources, rejecting: true);

    private static IReadOnlyList<PurchaseReceiptReservationChange> Build(long requestedCompanyId,
        long requestedReceiptId, PurchaseReceiptApprovalSnapshot saved,
        PurchaseReceiptOrderSnapshot order, IReadOnlyList<PurchaseReceiptReservationSource> sources,
        bool rejecting)
    {
        if (requestedCompanyId <= 0 || requestedReceiptId <= 0 || saved is null ||
            saved.CompanyId != requestedCompanyId || saved.ReceiptId != requestedReceiptId ||
            saved.PurchaseOrderId <= 0 || saved.Lines is null || order is null ||
            order.Id != saved.PurchaseOrderId || order.CompanyId != requestedCompanyId ||
            sources is null)
            throw InvalidSnapshot();

        // Revalidate one independent copy of the persisted rows. The request carries only identity.
        var draft = PurchaseReceiptDraftInput.Validate(new(saved.PurchaseOrderId,
            saved.BusinessDate, saved.Lines.ToArray()));
        var evidence = sources.ToArray();
        return rejecting
            ? PurchaseReceiptReservationPlan.ForReject(saved.Status, saved.CompanyId, draft, order, evidence)
            : PurchaseReceiptReservationPlan.ForSubmit(saved.Status, saved.CompanyId, draft, order, evidence);
    }

    private static BusinessRuleException InvalidSnapshot() =>
        new("PUR.RECEIPT.INVALID_RESERVATION_SNAPSHOT",
            "Requested receipt does not match its saved source identity.");
}
