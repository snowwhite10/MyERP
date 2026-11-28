using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One lot (any positive quantity, optional dates) or one serial (exactly 1) for a receipt line (ADR-221).</summary>
public sealed record PurchaseReceiptTrackingInput(string? LotNo, string? SerialNo, decimal Quantity,
    DateOnly? ManufactureDate = null, DateOnly? ExpiryDate = null);

/// <summary>A saved lot or serial of a receipt line, in capture order.</summary>
public sealed record PurchaseReceiptTrackingRead(int SeqNo, string? LotNo, string? SerialNo, decimal Quantity,
    DateOnly? ManufactureDate, DateOnly? ExpiryDate);

/// <summary>A saved or requested receipt line with its item, for the MD tracking-mode check.</summary>
public sealed record PurchaseReceiptTrackedLine(int LineNo, long ItemId, long WarehouseId, decimal Quantity,
    IReadOnlyList<PurchaseReceiptTrackingInput> Tracking);

/// <summary>
/// WU-PUR-007b2 (ADR-221): mode-independent format of receipt lot/serial capture. Errors are 400 with field paths:
/// lot xor serial, trimmed length, quantity, serial = 1, dates only for lots and expiry ≥ manufacture, no mixing,
/// unique (case-insensitive) within a line and never more than the line quantity.
/// </summary>
public static class PurchaseReceiptTrackingRules
{
    public const int MaxEntriesPerLine = 1000;
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public static void Validate(IReadOnlyList<PurchaseReceiptSaveLine?>? lines)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        foreach (var (line, index) in (lines ?? []).Select((line, index) => (line, index)))
            if (line?.Tracking is { } tracking)
                ValidateLine(tracking, line.Quantity, $"lines[{index}].tracking", Fail);
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private static void ValidateLine(IReadOnlyList<PurchaseReceiptTrackingInput?> tracking, decimal lineQuantity,
        string field, Action<string, string> fail)
    {
        if (tracking.Count > MaxEntriesPerLine)
        {
            fail(field, $"A line can carry at most {MaxEntriesPerLine} lot or serial entries.");
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
                     entry.LotNo.Trim() is not { Length: > 0 and <= PurchaseReceiptLineTracking.LotMaxLength })
                fail($"{at}.lotNo", $"Lot number must contain 1 to {PurchaseReceiptLineTracking.LotMaxLength} characters.");
            else if (entry.SerialNo is not null &&
                     entry.SerialNo.Trim() is not { Length: > 0 and <= PurchaseReceiptLineTracking.SerialMaxLength })
                fail($"{at}.serialNo",
                    $"Serial number must contain 1 to {PurchaseReceiptLineTracking.SerialMaxLength} characters.");
            if (!IsQuantity(entry.Quantity))
                fail($"{at}.quantity", "Quantity must be positive, below 10^13, with at most 6 decimals.");
            else if (entry.SerialNo is not null && entry.Quantity != 1m)
                fail($"{at}.quantity", "Each serial number represents exactly one base unit.");
            if ((entry.ManufactureDate is not null || entry.ExpiryDate is not null) && entry.LotNo is null)
                fail(at, "Manufacture and expiry dates apply only to lots.");
            else if (entry.ManufactureDate is { } made && entry.ExpiryDate is { } expiry && expiry < made)
                fail($"{at}.expiryDate", "Expiry date cannot be earlier than manufacture date.");
        }
        var present = tracking.Where(x => x is not null).Select(x => x!).ToList();
        if (present.Any(x => x.LotNo is not null) && present.Any(x => x.SerialNo is not null))
            fail(field, "A line cannot mix lot and serial numbers.");
        else if (present.GroupBy(x => Normalize(x.LotNo ?? x.SerialNo)).Any(g => g.Count() > 1))
            fail(field, "Lot or serial numbers must be unique within a line.");
        else if (present.All(x => IsQuantity(x.Quantity)) && present.Sum(x => x.Quantity) > lineQuantity)
            fail(field, "Lot or serial quantities cannot exceed the line quantity.");
    }

    public static string Normalize(string? number) => (number ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsQuantity(decimal value) =>
        value > 0 && value < QuantityLimit && decimal.Round(value, 6) == value;
}

/// <summary>
/// WU-PUR-007b2 (ADR-221): tracking-mode checks under the MD item/warehouse shared lock (held to the caller's commit),
/// saved capture loading and conversion to INV posting lines. The caller owns the transaction and PUR locks.
/// </summary>
public sealed class PurchaseReceiptTracking(MyErpDbContext db, IInventoryReferenceGuard references)
{
    public const string ModeMismatch = "PUR.RECEIPT.TRACKING_MODE_MISMATCH";
    public const string Required = "PUR.RECEIPT.TRACKING_REQUIRED";
    public const string SerialDuplicate = "PUR.RECEIPT.SERIAL_DUPLICATE";

    /// <summary>
    /// Checks each line against its item's MD tracking mode. With <paramref name="requireComplete"/> (SUBMIT) a tracked
    /// line must carry entries totalling its quantity; a Draft may be incomplete. A serial may appear once per item.
    /// </summary>
    public async Task CheckAsync(IReadOnlyList<PurchaseReceiptTrackedLine> lines, bool requireComplete,
        CancellationToken token)
    {
        var seen = new HashSet<(long ItemId, string Serial)>();
        foreach (var line in lines.OrderBy(x => x.LineNo))
            foreach (var entry in line.Tracking.Where(x => x.SerialNo is not null))
                if (!seen.Add((line.ItemId, PurchaseReceiptTrackingRules.Normalize(entry.SerialNo))))
                    throw LineError(SerialDuplicate,
                        $"Serial {entry.SerialNo!.Trim()} appears on more than one line of this receipt.", line.LineNo);

        foreach (var set in lines.GroupBy(x => (x.ItemId, x.WarehouseId))
                     .OrderBy(x => x.Key.ItemId).ThenBy(x => x.Key.WarehouseId))
        {
            var mode = await references.RunWithCodesAsync(set.Key.ItemId, set.Key.WarehouseId, null,
                codes => Task.FromResult(codes.TrackingMode), token);
            foreach (var line in set.OrderBy(x => x.LineNo))
                CheckLine(line, mode, requireComplete);
        }
    }

    /// <summary>Saved captures of a receipt's lines by line ID, in sequence order.</summary>
    public async Task<Dictionary<long, PurchaseReceiptLineTracking[]>> LoadAsync(long companyId, long receiptId,
        CancellationToken token)
    {
        var rows = await (from t in db.Set<PurchaseReceiptLineTracking>().AsNoTracking()
                join l in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
                    on new { t.CompanyId, Id = t.LineId } equals new { l.CompanyId, l.Id }
                where l.CompanyId == companyId && l.PurchaseReceiptId == receiptId
                select t).ToListAsync(token);
        return rows.GroupBy(x => x.LineId).ToDictionary(x => x.Key, x => x.OrderBy(t => t.SeqNo).ToArray());
    }

    /// <summary>Removes every saved capture of a receipt's lines (Draft edit replaces them as a whole).</summary>
    public Task<int> DeleteAsync(long companyId, long receiptId, CancellationToken token) =>
        db.Set<PurchaseReceiptLineTracking>()
            .Where(t => db.Set<PurchaseReceiptSourceLine>().Any(l => l.CompanyId == companyId &&
                l.PurchaseReceiptId == receiptId && l.CompanyId == t.CompanyId && l.Id == t.LineId))
            .ExecuteDeleteAsync(token);

    public static PurchaseReceiptLineTracking[] Build(long companyId, long lineId,
        IReadOnlyList<PurchaseReceiptTrackingInput>? tracking) => (tracking ?? [])
        .Select((x, i) => new PurchaseReceiptLineTracking
        {
            CompanyId = companyId, LineId = lineId, SeqNo = i + 1, LotNo = x.LotNo?.Trim(),
            SerialNo = x.SerialNo?.Trim(), Quantity = x.Quantity,
            ManufactureDate = x.LotNo is null ? null : x.ManufactureDate,
            ExpiryDate = x.LotNo is null ? null : x.ExpiryDate
        }).ToArray();

    public static PurchaseReceiptTrackingInput[] ToInput(IEnumerable<PurchaseReceiptLineTracking>? saved) =>
        (saved ?? []).OrderBy(x => x.SeqNo)
        .Select(x => new PurchaseReceiptTrackingInput(x.LotNo, x.SerialNo, x.Quantity, x.ManufactureDate, x.ExpiryDate))
        .ToArray();

    public static PurchaseReceiptTrackingRead[] ToRead(IEnumerable<PurchaseReceiptLineTracking>? saved) =>
        (saved ?? []).OrderBy(x => x.SeqNo)
        .Select(x => new PurchaseReceiptTrackingRead(x.SeqNo, x.LotNo, x.SerialNo, x.Quantity, x.ManufactureDate,
            x.ExpiryDate)).ToArray();

    /// <summary>Saved captures as INV tracking lines; an untracked line sends none.</summary>
    public static InventoryTrackingLine[]? ToInventory(IEnumerable<PurchaseReceiptLineTracking>? saved)
    {
        var rows = (saved ?? []).OrderBy(x => x.SeqNo)
            .Select(x => x.SerialNo is { } serial
                ? InventoryTrackingLine.Serial(serial)
                : InventoryTrackingLine.Lot(x.LotNo!, x.Quantity, x.ManufactureDate, x.ExpiryDate))
            .ToArray();
        return rows.Length == 0 ? null : rows;
    }

    private static void CheckLine(PurchaseReceiptTrackedLine line, ItemTrackingMode mode, bool requireComplete)
    {
        var mismatch = mode switch
        {
            ItemTrackingMode.None when line.Tracking.Count > 0 => "The item is not lot or serial tracked.",
            ItemTrackingMode.Lot when line.Tracking.Any(x => x.SerialNo is not null) =>
                "The item is lot tracked; enter lot numbers.",
            ItemTrackingMode.Serial when line.Tracking.Any(x => x.LotNo is not null) =>
                "The item is serial tracked; enter serial numbers.",
            ItemTrackingMode.Serial when decimal.Truncate(line.Quantity) != line.Quantity =>
                "A serial-tracked line must receive a whole number of units.",
            _ => null
        };
        if (mismatch is not null) throw LineError(ModeMismatch, mismatch, line.LineNo);
        if (requireComplete && mode != ItemTrackingMode.None && line.Tracking.Sum(x => x.Quantity) != line.Quantity)
            throw LineError(Required,
                $"Enter {mode.ToString().ToLowerInvariant()} numbers totalling the line quantity before submission.",
                line.LineNo);
    }

    private static BusinessRuleException LineError(string code, string message, int lineNo) =>
        new(code, message) { Extensions = new Dictionary<string, object?> { ["lineNo"] = lineNo } };
}
