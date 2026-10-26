using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

public sealed record SavedDeliverySelectedLine(long LineId, long DeliveryId,
    long OrderLineId, decimal Quantity, SalesDeliveryStatus Status, bool IsDeleted);

/// <summary>
/// Verifies the persisted current-line selection against an independently read
/// count, then produces the order-line lock sequence. Both reads must occur in
/// one company-scoped, serialized SQL transaction.
/// </summary>
public static class SalesDeliverySelection
{
    public static IReadOnlyList<long> CreateLockPlan(
        IReadOnlyList<SavedDeliverySelectedLine> selected,
        int savedLineCount,
        long currentDeliveryId,
        SalesDeliveryStatus expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (savedLineCount is < 1 or > 1000 || selected.Count != savedLineCount)
            throw InvalidSelection();

        var lineIds = new HashSet<long>();
        var currentLines = new SavedDeliveryLineQuantity[selected.Count];
        for (var index = 0; index < selected.Count; index++)
        {
            var line = selected[index];
            if (line is null || line.LineId <= 0 || !lineIds.Add(line.LineId))
                throw InvalidSelection();
            currentLines[index] = new SavedDeliveryLineQuantity(line.DeliveryId,
                line.OrderLineId, line.Quantity, line.Status, line.IsDeleted);
        }

        return SalesDeliveryLockPlan.Create(currentLines, currentDeliveryId, expectedStatus);
    }

    private static RequestValidationException InvalidSelection() =>
        new("lines", "Saved delivery line selection is incomplete or duplicated.");
}
