using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>One lot (any positive quantity) or one serial (exactly 1) for a delivery line (ADR-145).</summary>
public sealed record SalesDeliveryTrackingInput(string? LotNo, string? SerialNo, decimal Quantity);

/// <summary>Untrusted draft fields; saved order and inventory references need transaction checks.</summary>
public sealed record SalesDeliveryLineInput(int LineNo, long OrderLineId, long WarehouseId,
    long? LocationId, decimal Quantity, IReadOnlyList<SalesDeliveryTrackingInput>? Tracking = null);

public sealed record SalesDeliveryDraftInput(long OrderId, DateOnly BusinessDate,
    IReadOnlyList<SalesDeliveryLineInput>? Lines)
{
    private const decimal DecimalNineteenSixLimit = 10_000_000_000_000m;
    public const int MaxTrackingPerLine = 1000;

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
            if (!IsQuantity(line.Quantity))
                Fail($"{field}.quantity", "Quantity must be positive, below 10^13, with at most 6 decimals.");
            ValidateTracking(line, $"{field}.tracking", Fail);
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    /// <summary>
    /// Mode-independent format of one line's selections: lot or serial (not both), trimmed length,
    /// quantity, serial = 1, no mixing, unique (case-insensitive) and never more than the line quantity.
    /// The item's tracking mode is checked under the MD lock by <see cref="SalesDeliveryDraftLines"/>.
    /// </summary>
    private static void ValidateTracking(SalesDeliveryLineInput line, string field, Action<string, string> fail)
    {
        var tracking = line.Tracking ?? [];
        if (tracking.Count > MaxTrackingPerLine)
        {
            fail(field, $"A line can carry at most {MaxTrackingPerLine} lot or serial entries.");
            return;
        }
        for (var j = 0; j < tracking.Count; j++)
        {
            var entry = tracking[j];
            var at = $"{field}[{j}]";
            if (entry is null) { fail(at, "Lot or serial entry is required."); continue; }
            if ((entry.LotNo is null) == (entry.SerialNo is null))
                fail(at, "Entry must have either a lot number or a serial number.");
            else if (entry.LotNo is not null &&
                     entry.LotNo.Trim() is not { Length: > 0 and <= SalesDeliveryLineTracking.LotMaxLength })
                fail($"{at}.lotNo", $"Lot number must contain 1 to {SalesDeliveryLineTracking.LotMaxLength} characters.");
            else if (entry.SerialNo is not null &&
                     entry.SerialNo.Trim() is not { Length: > 0 and <= SalesDeliveryLineTracking.SerialMaxLength })
                fail($"{at}.serialNo",
                    $"Serial number must contain 1 to {SalesDeliveryLineTracking.SerialMaxLength} characters.");
            if (!IsQuantity(entry.Quantity))
                fail($"{at}.quantity", "Quantity must be positive, below 10^13, with at most 6 decimals.");
            else if (entry.SerialNo is not null && entry.Quantity != 1m)
                fail($"{at}.quantity", "Each serial number represents exactly one base unit.");
        }
        var present = tracking.Where(x => x is not null).ToList();
        if (present.Any(x => x.LotNo is not null) && present.Any(x => x.SerialNo is not null))
            fail(field, "A line cannot mix lot and serial numbers.");
        else if (present.GroupBy(x => Normalize(x.LotNo ?? x.SerialNo)).Any(g => g.Count() > 1))
            fail(field, "Lot or serial numbers must be unique within a line.");
        else if (present.All(x => IsQuantity(x.Quantity)) && present.Sum(x => x.Quantity) > line.Quantity)
            fail(field, "Lot or serial quantities cannot exceed the line quantity.");
    }

    internal static string Normalize(string? number) => (number ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsQuantity(decimal value) =>
        value > 0 && value < DecimalNineteenSixLimit && decimal.Round(value, 6) == value;
}
