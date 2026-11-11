using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseReceiptApprovalSource(PurchaseReceiptOrderLineSnapshot Line,
    decimal CurrentReceiptReserved, decimal CurrentReceiptPosted);

public sealed record PurchaseReceiptApprovalChange(long PurchaseOrderLineId,
    decimal CurrentPending, decimal TargetPending, decimal CurrentPosted, decimal TargetPosted)
{
    public decimal PendingChange => TargetPending - CurrentPending;
    public decimal PostedChange => TargetPosted - CurrentPosted;
}

/// <summary>Pure final approval plan; repeat with saved rows under ordered SQL locks before writing.</summary>
public static class PurchaseReceiptApprovalPlan
{
    public static IReadOnlyList<PurchaseReceiptApprovalChange> Create(string status, long companyId,
        PurchaseReceiptValidatedInput draft, PurchaseReceiptOrderSnapshot order,
        IReadOnlyList<PurchaseReceiptApprovalSource> sources)
    {
        if (status is not ("PendingApproval" or "Posted") || sources is null)
            throw InvalidTransition();

        var adjusted = new List<PurchaseReceiptOrderLineSnapshot>(sources.Count);
        var byId = new Dictionary<long, PurchaseReceiptApprovalSource>();
        foreach (var source in sources)
        {
            if (source?.Line is null || source.Line.Id <= 0 ||
                !ValidQuantity(source.CurrentReceiptReserved) ||
                !ValidQuantity(source.CurrentReceiptPosted) ||
                !ValidQuantity(source.Line.PendingQuantity) ||
                !ValidQuantity(source.Line.PostedQuantity) ||
                source.CurrentReceiptReserved > source.Line.PendingQuantity ||
                source.CurrentReceiptPosted > source.Line.PostedQuantity ||
                !byId.TryAdd(source.Line.Id, source))
                throw InvalidTransition();

            adjusted.Add(source.Line with
            {
                PendingQuantity = source.Line.PendingQuantity - source.CurrentReceiptReserved,
                PostedQuantity = source.Line.PostedQuantity - source.CurrentReceiptPosted
            });
        }

        // Exclude this receipt's own quantity before checking the saved request against capacity.
        var lines = PurchaseReceiptAvailability.Check(companyId, draft, order, adjusted);
        var changes = new List<PurchaseReceiptApprovalChange>(lines.Count);
        foreach (var line in lines)
        {
            var source = byId[line.PurchaseOrderLineId];
            if (status == "PendingApproval"
                ? source.CurrentReceiptReserved != line.RequestedQuantity || source.CurrentReceiptPosted != 0m
                : source.CurrentReceiptReserved != 0m || source.CurrentReceiptPosted != line.RequestedQuantity)
                throw InvalidTransition();

            var targetPending = status == "PendingApproval"
                ? source.Line.PendingQuantity - line.RequestedQuantity : source.Line.PendingQuantity;
            var targetPosted = status == "PendingApproval"
                ? source.Line.PostedQuantity + line.RequestedQuantity : source.Line.PostedQuantity;
            if (!ValidQuantity(targetPending) || !ValidQuantity(targetPosted))
                throw InvalidTransition();
            changes.Add(new(line.PurchaseOrderLineId, source.Line.PendingQuantity,
                targetPending, source.Line.PostedQuantity, targetPosted));
        }
        return changes.AsReadOnly();
    }

    private static bool ValidQuantity(decimal value) =>
        value >= 0m && value < 10_000_000_000_000m && decimal.Round(value, 6) == value;

    private static BusinessRuleException InvalidTransition() =>
        new("PUR.RECEIPT.INVALID_APPROVAL_TRANSITION",
            "Receipt approval transition is inconsistent with its saved source.");
}
