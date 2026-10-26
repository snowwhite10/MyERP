using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

public sealed record SavedOrderLineQuantity(long OrderLineId, decimal Ordered);

public sealed record SavedDeliveryLineQuantity(long DeliveryId, long OrderLineId,
    decimal Quantity, SalesDeliveryStatus Status, bool IsDeleted);

/// <summary>
/// Converts saved SD rows into the balance guard's input. The caller must read rows
/// inside one transaction after locking distinct order lines in ascending ID order.
/// </summary>
public static class SalesDeliverySnapshot
{
    private const decimal DecimalNineteenSixLimit = 10_000_000_000_000m;

    public static IReadOnlyList<SalesOrderLineBalance> Build(
        IReadOnlyList<SavedOrderLineQuantity> orderLines,
        IReadOnlyList<SavedDeliveryLineQuantity> deliveryLines,
        long? currentPendingDeliveryId)
    {
        ArgumentNullException.ThrowIfNull(orderLines);
        ArgumentNullException.ThrowIfNull(deliveryLines);
        if (orderLines.Count == 0 || currentPendingDeliveryId is <= 0)
            throw InvalidSnapshot();

        var totals = new Dictionary<long, (decimal Ordered, decimal Posted, decimal Pending)>();
        foreach (var line in orderLines)
        {
            if (line is null || line.OrderLineId <= 0 || !ValidQuantity(line.Ordered) ||
                !totals.TryAdd(line.OrderLineId, (line.Ordered, 0m, 0m)))
                throw InvalidSnapshot();
        }

        foreach (var line in deliveryLines)
        {
            if (line is null || line.DeliveryId <= 0 || !ValidQuantity(line.Quantity) ||
                !totals.TryGetValue(line.OrderLineId, out var total) ||
                !Enum.IsDefined(line.Status))
                throw InvalidSnapshot();

            // Posted rows remain consumed even if an old record carries a delete flag.
            if (line.Status == SalesDeliveryStatus.Posted)
            {
                if (line.DeliveryId == currentPendingDeliveryId) throw InvalidSnapshot();
                total.Posted += line.Quantity;
            }
            else if (line.Status == SalesDeliveryStatus.Pending && !line.IsDeleted &&
                     line.DeliveryId != currentPendingDeliveryId)
                total.Pending += line.Quantity;
            totals[line.OrderLineId] = total;
        }

        var result = new List<SalesOrderLineBalance>(totals.Count);
        foreach (var (id, total) in totals.OrderBy(pair => pair.Key))
        {
            if (total.Posted >= DecimalNineteenSixLimit || total.Pending >= DecimalNineteenSixLimit)
                throw InvalidSnapshot();
            if (total.Posted + total.Pending > total.Ordered)
                throw new BusinessRuleException("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED",
                    "Saved deliveries exceed the order-line quantity.");
            result.Add(new SalesOrderLineBalance(id, total.Ordered, total.Posted, total.Pending));
        }
        return result;
    }

    private static bool ValidQuantity(decimal value) => value > 0m &&
        value < DecimalNineteenSixLimit && decimal.Round(value, 6) == value;

    private static RequestValidationException InvalidSnapshot() =>
        new("balances", "Saved order-line balance snapshot is invalid.");
}
