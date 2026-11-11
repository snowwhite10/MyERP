using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Committed source quantities plus this invoice's posted contribution, read under source locks.</summary>
public sealed record PurchaseInvoicePostingSource(long ReceiptLineId,
    PurchaseInvoiceSourceQuantities Quantities, decimal CurrentInvoicePosted);

public sealed record PurchaseInvoicePostingChange(long ReceiptLineId,
    decimal CurrentReserved, decimal TargetReserved, decimal CurrentInvoiced,
    decimal TargetInvoiced)
{
    public decimal ReservedChange => TargetReserved - CurrentReserved;
    public decimal InvoicedChange => TargetInvoiced - CurrentInvoiced;
}

/// <summary>Pure BR-PUR-037 POST quantity plan. Caller owns source locks, FI transaction and persistence.</summary>
public static class PurchaseInvoicePostingPlan
{
    public static IReadOnlyList<PurchaseInvoicePostingChange> Create(string status,
        IReadOnlyList<PurchaseInvoiceReceiptRequest> requests,
        IReadOnlyList<PurchaseInvoicePostingSource> sources)
    {
        if (status is not ("Matched" or "Posted")) throw InvalidTransition();
        var requested = PurchaseInvoiceAvailability.AggregateRequested(requests);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count != requested.Count) throw InvalidTransition();

        var byId = new Dictionary<long, PurchaseInvoicePostingSource>();
        foreach (var source in sources)
        {
            if (source is null || source.ReceiptLineId <= 0 || source.Quantities is null ||
                !byId.TryAdd(source.ReceiptLineId, source)) throw InvalidTransition();
        }

        var changes = new List<PurchaseInvoicePostingChange>(requested.Count);
        foreach (var line in requested)
        {
            if (!byId.TryGetValue(line.ReceiptLineId, out var source)) throw InvalidTransition();
            var q = source.Quantities;
            // Validate the full committed snapshot, including other invoices' reservations.
            PurchaseInvoiceAvailability.Calculate(q);
            if (source.CurrentInvoicePosted < 0 ||
                decimal.Round(source.CurrentInvoicePosted, 6) != source.CurrentInvoicePosted ||
                source.CurrentInvoicePosted > q.PostedInvoiced) throw InvalidTransition();

            if (status == "Matched")
            {
                if (q.CurrentInvoiceReserved != line.Quantity || source.CurrentInvoicePosted != 0 ||
                    !PurchaseInvoiceAvailability.CanMatch(q, line.Quantity)) throw InvalidTransition();
                changes.Add(new(line.ReceiptLineId, line.Quantity, 0,
                    q.PostedInvoiced, q.PostedInvoiced + line.Quantity));
            }
            else
            {
                if (q.CurrentInvoiceReserved != 0 || source.CurrentInvoicePosted != line.Quantity)
                    throw InvalidTransition();
                changes.Add(new(line.ReceiptLineId, 0, 0, q.PostedInvoiced, q.PostedInvoiced));
            }
        }
        return changes.ToArray();
    }

    private static BusinessRuleException InvalidTransition() =>
        new("PUR.INVOICE.INVALID_POSTING_TRANSITION",
            "Invoice posting quantities do not match the saved source.");
}
