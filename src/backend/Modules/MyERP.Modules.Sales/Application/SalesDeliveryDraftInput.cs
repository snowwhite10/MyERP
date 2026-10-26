using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>Untrusted draft fields; saved order and inventory references need transaction checks.</summary>
public sealed record SalesDeliveryLineInput(int LineNo, long OrderLineId, long WarehouseId,
    long? LocationId, decimal Quantity);

public sealed record SalesDeliveryDraftInput(long OrderId, DateOnly BusinessDate,
    IReadOnlyList<SalesDeliveryLineInput>? Lines)
{
    private const decimal DecimalNineteenSixLimit = 10_000_000_000_000m;

    public void Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);

        if (OrderId <= 0) Fail("orderId", "Order is required.");
        if (BusinessDate == default) Fail("businessDate", "Business date is required.");
        if (Lines is not { Count: >= 1 and <= 1000 })
            Fail("lines", "A delivery needs 1 to 1000 lines.");

        var lineNumbers = new HashSet<int>();
        foreach (var (line, index) in (Lines ?? []).Select((line, index) => (line, index)))
        {
            var field = $"lines[{index}]";
            if (line is null) { Fail(field, "Line is required."); continue; }
            if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
            if (!lineNumbers.Add(line.LineNo)) Fail("lines", "Line numbers must be unique.");
            if (line.OrderLineId <= 0) Fail($"{field}.orderLineId", "Order line is required.");
            if (line.WarehouseId <= 0) Fail($"{field}.warehouseId", "Warehouse is required.");
            if (line.LocationId is <= 0) Fail($"{field}.locationId", "Location must be positive.");
            if (line.Quantity <= 0 || line.Quantity >= DecimalNineteenSixLimit ||
                decimal.Round(line.Quantity, 6) != line.Quantity)
                Fail($"{field}.quantity", "Quantity must be positive, below 10^13, with at most 6 decimals.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}
