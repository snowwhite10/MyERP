using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One source reservation's expected current and target values for a locked transaction.</summary>
public sealed record PurchaseInvoiceReservationChange(long ReceiptLineId,
    decimal CurrentReserved, decimal TargetReserved)
{
    public decimal Change => TargetReserved - CurrentReserved;
}

/// <summary>Pure BR-PUR-037 reservation transition; the caller owns locks and persistence.</summary>
public static class PurchaseInvoiceReservationPlan
{
    public static IReadOnlyList<PurchaseInvoiceReservationChange> ForMatch(string status,
        IReadOnlyList<PurchaseInvoiceReceiptRequest> requests,
        IReadOnlyList<PurchaseInvoiceReceiptSource> sources)
    {
        if (status is not ("Matched" or "Disputed")) throw InvalidTransition();
        var requested = PurchaseInvoiceAvailability.AggregateRequested(requests);
        var availability = PurchaseInvoiceAvailability.CheckRequested(requests, sources);
        if (!availability.CanMatch) throw InvalidTransition();
        var sourceById = sources.ToDictionary(x => x.ReceiptLineId);
        return requested.Select(x => new PurchaseInvoiceReservationChange(x.ReceiptLineId,
            sourceById[x.ReceiptLineId].Quantities.CurrentInvoiceReserved, x.Quantity)).ToArray();
    }

    public static IReadOnlyList<PurchaseInvoiceReservationChange> ForResolve(string status,
        IReadOnlyList<PurchaseInvoiceReceiptRequest> requests,
        IReadOnlyList<PurchaseInvoiceReceiptSource> sources)
    {
        if (status is not ("Matched" or "Draft")) throw InvalidTransition();
        var requested = PurchaseInvoiceAvailability.AggregateRequested(requests);
        if (status == "Matched" &&
            !PurchaseInvoiceAvailability.CheckRequested(requests, sources).CanMatch)
            throw InvalidTransition();
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count != requested.Count) throw InvalidTransition();
        var sourceById = new Dictionary<long, PurchaseInvoiceReceiptSource>();
        foreach (var source in sources)
        {
            if (source is null || source.ReceiptLineId <= 0 || source.Quantities is null ||
                !sourceById.TryAdd(source.ReceiptLineId, source)) throw InvalidTransition();
        }

        var changes = new List<PurchaseInvoiceReservationChange>(requested.Count);
        foreach (var line in requested)
        {
            if (!sourceById.TryGetValue(line.ReceiptLineId, out var source))
                throw InvalidTransition();
            PurchaseInvoiceAvailability.Calculate(source.Quantities);
            if (source.Quantities.CurrentInvoiceReserved != line.Quantity)
                throw InvalidTransition();
            changes.Add(new(line.ReceiptLineId, line.Quantity,
                status == "Matched" ? line.Quantity : 0));
        }
        return changes.ToArray();
    }

    private static BusinessRuleException InvalidTransition() =>
        new("PUR.INVOICE.INVALID_RESERVATION_TRANSITION",
            "Invoice reservation transition is inconsistent with the saved source.");
}
