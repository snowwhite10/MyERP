using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseReceiptReservationSource(PurchaseReceiptOrderLineSnapshot Line,
    decimal CurrentReceiptReserved);

public sealed record PurchaseReceiptReservationChange(long PurchaseOrderLineId,
    decimal CurrentPending, decimal TargetPending)
{
    public decimal Change => TargetPending - CurrentPending;
}

/// <summary>Pure transition plan. The caller must repeat it with authoritative rows under ordered SQL locks.</summary>
public static class PurchaseReceiptReservationPlan
{
    public static IReadOnlyList<PurchaseReceiptReservationChange> ForSubmit(string status, long companyId,
        PurchaseReceiptValidatedInput draft, PurchaseReceiptOrderSnapshot order,
        IReadOnlyList<PurchaseReceiptReservationSource> sources) =>
        Create(status, companyId, draft, order, sources, rejecting: false);

    public static IReadOnlyList<PurchaseReceiptReservationChange> ForReject(string status, long companyId,
        PurchaseReceiptValidatedInput draft, PurchaseReceiptOrderSnapshot order,
        IReadOnlyList<PurchaseReceiptReservationSource> sources) =>
        Create(status, companyId, draft, order, sources, rejecting: true);

    private static System.Collections.ObjectModel.ReadOnlyCollection<PurchaseReceiptReservationChange> Create(string status, long companyId,
        PurchaseReceiptValidatedInput draft, PurchaseReceiptOrderSnapshot order,
        IReadOnlyList<PurchaseReceiptReservationSource> sources, bool rejecting)
    {
        if (rejecting ? status != "PendingApproval" : status is not ("Draft" or "Rejected" or "PendingApproval"))
            throw InvalidTransition();
        if (sources is null) throw InvalidTransition();

        var adjusted = new List<PurchaseReceiptOrderLineSnapshot>(sources.Count);
        var ownById = new Dictionary<long, decimal>();
        foreach (var source in sources)
        {
            if (source?.Line is null || source.Line.Id <= 0 ||
                !ValidQuantity(source.CurrentReceiptReserved) ||
                !ValidQuantity(source.Line.PendingQuantity) ||
                source.CurrentReceiptReserved > source.Line.PendingQuantity ||
                !ownById.TryAdd(source.Line.Id, source.CurrentReceiptReserved))
                throw InvalidTransition();
            adjusted.Add(source.Line with
            {
                PendingQuantity = source.Line.PendingQuantity - source.CurrentReceiptReserved
            });
        }

        // Removing this receipt's existing reservation allows a replay at full capacity.
        // Check also validates identity, totals, and all requested lines before a plan is emitted.
        var lines = PurchaseReceiptAvailability.Check(companyId, draft, order, adjusted);
        var sourceById = sources.ToDictionary(x => x.Line.Id);
        var changes = new List<PurchaseReceiptReservationChange>(lines.Count);
        foreach (var line in lines)
        {
            var source = sourceById[line.PurchaseOrderLineId];
            var own = ownById[line.PurchaseOrderLineId];
            if (status == "PendingApproval" ? own != line.RequestedQuantity : own != 0m)
                throw InvalidTransition();
            var target = rejecting
                ? source.Line.PendingQuantity - own
                : source.Line.PendingQuantity - own + line.RequestedQuantity;
            if (!ValidQuantity(target)) throw InvalidTransition();
            changes.Add(new(line.PurchaseOrderLineId, source.Line.PendingQuantity, target));
        }
        return changes.AsReadOnly();
    }

    private static bool ValidQuantity(decimal value) =>
        value >= 0m && value < 10_000_000_000_000m && decimal.Round(value, 6) == value;

    private static BusinessRuleException InvalidTransition() =>
        new("PUR.RECEIPT.INVALID_RESERVATION_TRANSITION",
            "Receipt reservation transition is inconsistent with its saved source.");
}
