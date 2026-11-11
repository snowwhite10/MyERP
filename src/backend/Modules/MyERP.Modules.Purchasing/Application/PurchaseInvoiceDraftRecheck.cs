using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Revalidates a draft against authority read inside the save transaction.</summary>
public static class PurchaseInvoiceDraftRecheck
{
    public static PurchaseInvoiceDraftSnapshot Check(PurchaseInvoiceDraftSnapshot? saved,
        PurchaseInvoiceOrderIdentity order, IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts,
        IReadOnlyList<PurchaseInvoiceOrderLinePrice> prices)
    {
        if (saved is null || saved.Identity is null || saved.Number is null ||
            saved.Lines is null || saved.AmountEvidence is null)
            throw Changed();

        PurchaseInvoiceDraftSnapshot current;
        try
        {
            current = PurchaseInvoiceDraftAssembly.Create(saved.Identity.CompanyId,
                new PurchaseInvoiceDraftInput(saved.Identity.SupplierId,
                    saved.Identity.PurchaseOrderId, saved.Identity.Currency, saved.BusinessDate,
                    saved.Number.Display, saved.Lines), order, receipts, prices);
        }
        catch (BusinessRuleException)
        {
            throw Changed();
        }

        if (saved.Identity != current.Identity || saved.BusinessDate != current.BusinessDate ||
            saved.Number != current.Number || saved.Totals != current.Totals ||
            !saved.Lines.SequenceEqual(current.Lines) ||
            !SameEvidence(saved.AmountEvidence, current.AmountEvidence))
            throw Changed();

        return current;
    }

    private static bool SameEvidence(PurchaseInvoiceAmountDecision left, PurchaseInvoiceAmountDecision right)
    {
        if (left.CanMatch != right.CanMatch || left.BaselineNet != right.BaselineNet ||
            left.BaselineTax != right.BaselineTax || left.BaselineGross != right.BaselineGross ||
            left.InvoiceNet != right.InvoiceNet || left.InvoiceTax != right.InvoiceTax ||
            left.InvoiceGross != right.InvoiceGross || left.Lines is null ||
            left.Lines.Count != right.Lines.Count)
            return false;

        for (var index = 0; index < left.Lines.Count; index++)
        {
            var a = left.Lines[index];
            var b = right.Lines[index];
            if (a is null || a.LineNo != b.LineNo || a.BaselineNet != b.BaselineNet ||
                a.BaselineTax != b.BaselineTax || a.BaselineGross != b.BaselineGross ||
                a.Differences is null || !a.Differences.SequenceEqual(b.Differences))
                return false;
        }
        return true;
    }

    private static BusinessRuleException Changed() => new("PUR.INVOICE.DRAFT_SNAPSHOT_CHANGED",
        "Invoice draft or its authoritative sources changed before save.");
}
