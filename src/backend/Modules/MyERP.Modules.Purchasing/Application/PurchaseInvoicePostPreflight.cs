using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoicePostDecision(bool IsReplay,
    IReadOnlyList<PurchaseInvoicePostingChange> Changes);

/// <summary>Pure whole-invoice POST preflight over saved invoice and locked authority snapshots.</summary>
public static class PurchaseInvoicePostPreflight
{
    public static PurchaseInvoicePostDecision Check(string status,
        IReadOnlyList<PurchaseInvoiceMatchLine> lines,
        PurchaseInvoiceIdentity invoice, PurchaseInvoiceOrderIdentity order,
        IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts,
        IReadOnlyList<PurchaseInvoicePostingSource> sources,
        IReadOnlyList<PurchaseInvoiceResolvedLine> acceptedDifferences)
    {
        if (status is not ("Matched" or "Posted"))
            throw new BusinessRuleException("PUR.INVOICE.INVALID_POSTING_TRANSITION",
                "Only matched invoices can be posted or replayed.");
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(acceptedDifferences);
        var requests = lines.Select(line => line is null
            ? new PurchaseInvoiceReceiptRequest(0, 0)
            : new PurchaseInvoiceReceiptRequest(line.ReceiptLineId, line.Amount?.Quantity ?? 0)).ToArray();
        PurchaseInvoiceAvailability.AggregateRequested(requests);
        PurchaseInvoiceSourceIdentity.Check(requests, invoice, order, receipts);

        var amount = PurchaseInvoiceAmountMatch.Check(lines.Select(line => line.Amount).ToArray());
        CheckAcceptedDifferences(amount, acceptedDifferences);
        var changes = PurchaseInvoicePostingPlan.Create(status, requests, sources);
        return new(status == "Posted", changes);
    }

    private static void CheckAcceptedDifferences(PurchaseInvoiceAmountDecision amount,
        IReadOnlyList<PurchaseInvoiceResolvedLine> accepted)
    {
        var disputed = amount.Lines.Where(x => x.Differences.Count > 0).ToArray();
        if (disputed.Length != accepted.Count) throw InvalidEvidence();
        var byLine = new Dictionary<int, PurchaseInvoiceResolvedLine>();
        foreach (var item in accepted)
        {
            if (item is null || item.LineNo <= 0 || item.Action != "Accept" ||
                string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Trim().Length > 500 ||
                item.Differences is null || !byLine.TryAdd(item.LineNo, item))
                throw InvalidEvidence();
        }
        foreach (var line in disputed)
        {
            if (!byLine.TryGetValue(line.LineNo, out var item) ||
                line.Differences.Count != item.Differences.Count ||
                !line.Differences.SequenceEqual(item.Differences))
                throw InvalidEvidence();
        }
    }

    private static BusinessRuleException InvalidEvidence() =>
        new("PUR.INVOICE.INVALID_POSTING_DIFFERENCE",
            "Saved difference decisions do not match current invoice and order amounts.");
}
