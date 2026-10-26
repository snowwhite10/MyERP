using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// Produces the order-line lock sequence from the saved current delivery rows.
/// The SQL caller must verify that its row selection is complete and lock each ID
/// in this order before loading all Posted and Pending rows for the balance check.
/// </summary>
public static class SalesDeliveryLockPlan
{
    private const decimal DecimalNineteenSixLimit = 10_000_000_000_000m;

    public static IReadOnlyList<long> Create(
        IReadOnlyList<SavedDeliveryLineQuantity> currentLines,
        long currentDeliveryId,
        SalesDeliveryStatus expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(currentLines);
        if (currentDeliveryId <= 0 || expectedStatus is not (SalesDeliveryStatus.Draft or SalesDeliveryStatus.Pending) ||
            currentLines.Count is < 1 or > 1000)
            throw InvalidSnapshot();

        var orderLineIds = new SortedSet<long>();
        foreach (var line in currentLines)
        {
            if (line is null || line.DeliveryId != currentDeliveryId || line.OrderLineId <= 0 ||
                line.IsDeleted || line.Status != expectedStatus || line.Quantity <= 0m ||
                line.Quantity >= DecimalNineteenSixLimit || decimal.Round(line.Quantity, 6) != line.Quantity)
                throw InvalidSnapshot();
            orderLineIds.Add(line.OrderLineId);
        }

        return orderLineIds.ToArray();
    }

    private static RequestValidationException InvalidSnapshot() =>
        new("lines", "Saved delivery lock plan is invalid.");
}
