using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesDeliveryQuantity(long OrderLineId, decimal Quantity);

/// <summary>OtherPending excludes the delivery currently being decided.</summary>
public sealed record SalesOrderLineBalance(long OrderLineId, decimal Ordered,
    decimal Posted, decimal OtherPending);

/// <summary>
/// Pure six-place order-line guard for REQ_F4112.3.2/4. The caller must load these
/// values from saved SD documents while holding the order-line transaction locks.
/// </summary>
public static class SalesDeliveryBalance
{
    private const decimal DecimalNineteenSixLimit = 10_000_000_000_000m;

    public static void Check(IReadOnlyList<SalesDeliveryQuantity> requested,
        IReadOnlyList<SalesOrderLineBalance> balances)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(balances);
        if (requested.Count is < 1 or > 1000)
            throw new RequestValidationException("lines", "A delivery needs 1 to 1000 lines.");

        var byId = new Dictionary<long, SalesOrderLineBalance>();
        foreach (var balance in balances)
        {
            if (balance is null || balance.OrderLineId <= 0 ||
                !ValidQuantity(balance.Ordered, positive: true) ||
                !ValidQuantity(balance.Posted) || !ValidQuantity(balance.OtherPending) ||
                !byId.TryAdd(balance.OrderLineId, balance))
                throw new RequestValidationException("balances", "Order-line balance snapshot is invalid.");
        }

        var totals = new Dictionary<long, decimal>();
        for (var index = 0; index < requested.Count; index++)
        {
            var line = requested[index];
            if (line is null || line.OrderLineId <= 0 || !ValidQuantity(line.Quantity, positive: true))
                throw new RequestValidationException($"lines[{index}].quantity",
                    "A saved order line and positive six-place quantity are required.");
            if (!byId.ContainsKey(line.OrderLineId))
                throw new RequestValidationException($"lines[{index}].orderLineId",
                    "Order-line balance snapshot is missing.");
            totals[line.OrderLineId] = totals.GetValueOrDefault(line.OrderLineId) + line.Quantity;
        }

        foreach (var (id, total) in totals)
        {
            var balance = byId[id];
            // Subtraction avoids a large intermediate and counts the current Pending
            // delivery only in 'total', not again in 'OtherPending'.
            if (total > balance.Ordered - balance.Posted - balance.OtherPending)
                throw new BusinessRuleException("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED",
                    "Delivery quantity exceeds the saved order-line balance.");
        }
    }

    private static bool ValidQuantity(decimal quantity, bool positive = false) =>
        (positive ? quantity > 0 : quantity >= 0) &&
        quantity < DecimalNineteenSixLimit && decimal.Round(quantity, 6) == quantity;
}
