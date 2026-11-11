using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceLineResolution(int LineNo, string Action, string Reason);

public sealed record PurchaseInvoiceResolvedLine(int LineNo, string Action, string Reason,
    IReadOnlyList<PurchaseInvoiceAmountDifference> Differences);

public sealed record PurchaseInvoiceResolutionDecision(string Status,
    IReadOnlyList<PurchaseInvoiceResolvedLine> Decisions);

/// <summary>BR-PUR-039a: preflight a complete decision for saved price and tax differences.</summary>
public static class PurchaseInvoiceResolution
{
    public static PurchaseInvoiceResolutionDecision Check(string status,
        PurchaseInvoiceAmountDecision amount,
        IReadOnlyList<PurchaseInvoiceLineResolution> decisions)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(decisions);
        if (status != "Disputed" || amount.CanMatch || amount.Lines is null ||
            amount.Lines.Count == 0 || amount.Lines.Any(x => x is null) ||
            amount.Lines.Select(x => x.LineNo).Distinct().Count() != amount.Lines.Count)
            throw Invalid();

        var disputed = amount.Lines.Where(x => x.Differences is { Count: > 0 })
            .ToDictionary(x => x.LineNo);
        if (disputed.Count == 0 || disputed.Count != decisions.Count)
            throw Invalid();

        var resolved = new List<PurchaseInvoiceResolvedLine>(decisions.Count);
        var seen = new HashSet<int>();
        foreach (var decision in decisions)
        {
            if (decision is null || !seen.Add(decision.LineNo) ||
                !disputed.TryGetValue(decision.LineNo, out var line) ||
                decision.Action is not ("Accept" or "Reject") ||
                decision.Reason is null)
                throw Invalid();
            var reason = decision.Reason.Trim();
            if (reason.Length is < 1 or > 500)
                throw Invalid();
            resolved.Add(new(decision.LineNo, decision.Action, reason,
                line.Differences.ToArray()));
        }

        var ordered = resolved.OrderBy(x => x.LineNo).ToArray();
        return new(ordered.Any(x => x.Action == "Reject") ? "Draft" : "Matched", ordered);
    }

    private static BusinessRuleException Invalid() =>
        new("PUR.INVOICE.INVALID_RESOLUTION", "Invoice difference resolution is incomplete or invalid.");
}
