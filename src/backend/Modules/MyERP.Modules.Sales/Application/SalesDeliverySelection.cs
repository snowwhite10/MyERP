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
    /// <summary>
    /// Proves that the later balance snapshot still contains exactly the current
    /// rows used to choose locks. The SQL caller must keep both reads and the
    /// ordered locks in the same serialized, company-scoped transaction.
    /// </summary>
    public static void CheckSnapshotMatchesSelection(
        IReadOnlyList<SavedDeliverySelectedLine> selected,
        IReadOnlyList<SavedDeliveryLineQuantity> snapshot,
        long currentDeliveryId,
        SalesDeliveryStatus expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(snapshot);
        CreateLockPlan(selected, selected.Count, currentDeliveryId, expectedStatus);

        var selectedById = selected.ToDictionary(line => line.LineId);
        var currentCount = 0;
        var seen = new HashSet<long>();
        foreach (var line in snapshot)
        {
            if (line is null)
                throw InvalidSelection();
            if (line.DeliveryId != currentDeliveryId)
                continue;
            currentCount++;
            if (currentCount > selected.Count || line.LineId <= 0 ||
                !seen.Add(line.LineId) ||
                !selectedById.TryGetValue(line.LineId, out var original) ||
                line.OrderLineId != original.OrderLineId ||
                line.Quantity != original.Quantity ||
                line.Status != expectedStatus || line.IsDeleted)
                throw InvalidSelection();
        }
        if (currentCount != selected.Count)
            throw InvalidSelection();
    }

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
