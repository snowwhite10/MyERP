using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One returned lot (any positive quantity) or serial (exactly 1); it must come from the original receipt line.</summary>
public sealed record PurchaseReturnTrackingInput(string? LotNo, string? SerialNo, decimal Quantity);

/// <summary>
/// A return line references one Posted receipt line. <paramref name="WarehouseId"/> defaults to the receipt line's
/// warehouse (goods may since have been transferred, ADR-223); storage locations are not captured yet (ADR-224).
/// </summary>
public sealed record PurchaseReturnSaveLine(int LineNumber, long ReceiptLineId, decimal Quantity,
    long? WarehouseId = null, IReadOnlyList<PurchaseReturnTrackingInput>? Tracking = null);

public sealed record PurchaseReturnSaveInput(long PurchaseOrderId, DateOnly BusinessDate,
    IReadOnlyList<PurchaseReturnSaveLine>? Lines);

public sealed record PurchaseReturnTrackingRead(int SeqNo, string? LotNo, string? SerialNo, decimal Quantity);

public sealed record PurchaseReturnReadLine(long Id, int LineNumber, long ReceiptId, string ReceiptDocumentNo,
    long ReceiptLineId, int ReceiptLineNumber, long OriginalMovementId, long MaterialId, long WarehouseId,
    long? LocationId, decimal Quantity, decimal ReceiptUnitCost, decimal? OriginalUnitCost, decimal ReturnableQuantity,
    IReadOnlyList<PurchaseReturnTrackingRead> Tracking);

public sealed record PurchaseReturnReadDetail(long Id, string DocumentNo, string Status, int Version,
    long PurchaseOrderId, string? PurchaseOrderNo, long SupplierId, string Currency, DateOnly BusinessDate,
    IReadOnlyList<PurchaseReturnReadLine> Lines);

/// <summary>
/// A Posted receipt line of one purchase order that still has returnable quantity
/// (received − posted returns − pending return reservations), with its saved lots/serials.
/// </summary>
public sealed record PurchaseReturnSourceLine(long ReceiptId, string ReceiptDocumentNo, DateOnly ReceiptDate,
    long ReceiptLineId, int ReceiptLineNumber, long MaterialId, long WarehouseId, decimal ReceivedQuantity,
    decimal PostedReturned, decimal PendingReturnReserved, decimal ReturnableQuantity, decimal UnitCost,
    IReadOnlyList<PurchaseReturnTrackingRead> Tracking);

/// <summary>
/// WU-PUR-007c3 shape rules: header and line errors are 422 like receipts; lot/serial format errors are 400 with field
/// paths (lot xor serial, trimmed length, quantity, serial = 1, no mixing, unique within a line, not above the line).
/// </summary>
public static class PurchaseReturnDraftInput
{
    public const int MaxLines = 1000;
    public const int MaxEntriesPerLine = 1000;
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public static IReadOnlyList<PurchaseReturnSaveLine> Validate(PurchaseReturnSaveInput? input)
    {
        if (input is null || input.PurchaseOrderId <= 0 || input.BusinessDate == default)
            throw new BusinessRuleException("PUR.RETURN.INVALID_HEADER", "Return header is invalid.");
        if (input.Lines is null || input.Lines.Count is < 1 or > MaxLines) throw InvalidLines();
        var numbers = new HashSet<int>();
        foreach (var line in input.Lines)
            if (line is null || line.LineNumber <= 0 || !numbers.Add(line.LineNumber) || line.ReceiptLineId <= 0 ||
                !IsQuantity(line.Quantity) || line.WarehouseId is <= 0)
                throw InvalidLines();
        ValidateTracking(input.Lines);
        return input.Lines.OrderBy(x => x.LineNumber).ToArray();
    }

    public static void EnsureEditable(string? status)
    {
        if (status is not ("Draft" or "Rejected"))
            throw new BusinessRuleException("PUR.RETURN.STATUS_NOT_EDITABLE",
                "Only draft or rejected returns can be revised.");
    }

    public static string Normalize(string? number) => (number ?? string.Empty).Trim().ToUpperInvariant();

    private static void ValidateTracking(IReadOnlyList<PurchaseReturnSaveLine> lines)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Tracking is not { } tracking) continue;
            var field = $"lines[{i}].tracking";
            if (tracking.Count > MaxEntriesPerLine)
            {
                Fail(field, $"A line can carry at most {MaxEntriesPerLine} lot or serial entries.");
                continue;
            }
            for (var j = 0; j < tracking.Count; j++)
            {
                var entry = tracking[j];
                var at = $"{field}[{j}]";
                if (entry is null) { Fail(at, "Lot or serial entry is required."); continue; }
                if ((entry.LotNo is null) == (entry.SerialNo is null))
                    Fail(at, "Entry must have either a lot number or a serial number.");
                else if (entry.LotNo is not null &&
                         entry.LotNo.Trim() is not { Length: > 0 and <= PurchaseReceiptLineTracking.LotMaxLength })
                    Fail($"{at}.lotNo", $"Lot number must contain 1 to {PurchaseReceiptLineTracking.LotMaxLength} characters.");
                else if (entry.SerialNo is not null &&
                         entry.SerialNo.Trim() is not { Length: > 0 and <= PurchaseReceiptLineTracking.SerialMaxLength })
                    Fail($"{at}.serialNo",
                        $"Serial number must contain 1 to {PurchaseReceiptLineTracking.SerialMaxLength} characters.");
                if (!IsQuantity(entry.Quantity))
                    Fail($"{at}.quantity", "Quantity must be positive, below 10^13, with at most 6 decimals.");
                else if (entry.SerialNo is not null && entry.Quantity != 1m)
                    Fail($"{at}.quantity", "Each serial number represents exactly one base unit.");
            }
            var present = tracking.Where(x => x is not null).Select(x => x!).ToList();
            if (present.Any(x => x.LotNo is not null) && present.Any(x => x.SerialNo is not null))
                Fail(field, "A line cannot mix lot and serial numbers.");
            else if (present.GroupBy(x => Normalize(x.LotNo ?? x.SerialNo)).Any(g => g.Count() > 1))
                Fail(field, "Lot or serial numbers must be unique within a line.");
            else if (present.All(x => IsQuantity(x.Quantity)) && present.Sum(x => x.Quantity) > lines[i].Quantity)
                Fail(field, "Lot or serial quantities cannot exceed the line quantity.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private static bool IsQuantity(decimal value) =>
        value > 0 && value < QuantityLimit && decimal.Round(value, 6) == value;

    private static BusinessRuleException InvalidLines() =>
        new("PUR.RETURN.INVALID_LINES", "Return lines have invalid numbers, sources, warehouses or quantities.");
}
