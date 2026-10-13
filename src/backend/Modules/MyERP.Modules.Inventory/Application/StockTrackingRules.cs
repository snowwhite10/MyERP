using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Inventory.Application;

/// <summary>
/// 批號／序號規則（ADR-060），過帳服務與各單據草稿共用：格式檢查不需資料庫；
/// 追蹤方式檢查使用在 MD 共享鎖內讀到的 <see cref="ItemTrackingMode"/>。
/// </summary>
internal static class StockTrackingRules
{
    private const decimal MaxValue = 10_000_000_000_000m;

    /// <summary>一行的批序號格式：批號或序號擇一、長度、數量、日期只用於入庫批號、行內不重複、不混用。</summary>
    public static void Validate(StockDirection direction, IReadOnlyList<InventoryTrackingLine> tracking, string field,
        Action<string, string> fail)
    {
        for (var j = 0; j < tracking.Count; j++)
        {
            var entry = tracking[j];
            var at = $"{field}[{j}]";
            if (entry is null) { fail(at, "Lot or serial entry is required."); continue; }
            var lot = entry.LotNo?.Trim();
            var serial = entry.SerialNo?.Trim();
            if ((entry.LotNo is not null) == (entry.SerialNo is not null))
                fail(at, "Entry must have either a lot number or a serial number.");
            else if (entry.LotNo is not null && lot is not { Length: > 0 and <= StockLot.MaxLength })
                fail($"{at}.lotNo", $"Lot number must contain 1 to {StockLot.MaxLength} characters.");
            else if (entry.SerialNo is not null && serial is not { Length: > 0 and <= StockSerial.MaxLength })
                fail($"{at}.serialNo", $"Serial number must contain 1 to {StockSerial.MaxLength} characters.");
            if (!IsStorable(entry.Quantity) || entry.Quantity <= 0)
                fail($"{at}.quantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
            else if (entry.SerialNo is not null && entry.Quantity != 1m)
                fail($"{at}.quantity", "Each serial number represents exactly one base unit.");
            if ((entry.ManufactureDate is not null || entry.ExpiryDate is not null)
                && (entry.LotNo is null || direction != StockDirection.In))
                fail(at, "Manufacture and expiry dates apply only to received lots.");
            else if (entry.ManufactureDate is { } made && entry.ExpiryDate is { } expiry && expiry < made)
                fail($"{at}.expiryDate", "Expiry date cannot be earlier than manufacture date.");
        }
        var present = tracking.Where(x => x is not null).ToList();
        if (present.Any(x => x.LotNo is not null) && present.Any(x => x.SerialNo is not null))
            fail(field, "A line cannot mix lot and serial numbers.");
        if (present.GroupBy(x => Normalize(x.LotNo ?? x.SerialNo ?? "")).Any(g => g.Count() > 1))
            fail(field, "Lot or serial numbers must be unique within a line.");
    }

    /// <summary>同一來源內，同物料同方向的序號只能出現一次。</summary>
    public static bool HasDuplicateSerials(IEnumerable<(long ItemId, StockDirection Direction, IReadOnlyList<InventoryTrackingLine>? Tracking)> lines) =>
        lines.SelectMany(x => (x.Tracking ?? []).Where(t => t?.SerialNo is not null)
                .Select(t => (x.ItemId, x.Direction, Serial: Normalize(t.SerialNo!))))
            .GroupBy(x => x).Any(g => g.Count() > 1);

    /// <summary>
    /// 依物料追蹤方式檢查：不追蹤的物料不可帶批序號、種類須相符、序號物料數量為整數（400）；
    /// <paramref name="requireComplete"/> 時批序號合計必須等於數量（422 <c>INV.TRACKING.REQUIRED</c>），草稿可先不填。
    /// </summary>
    public static void CheckMode(int lineNo, decimal quantity, IReadOnlyList<InventoryTrackingLine>? tracking, string field,
        ItemTrackingMode mode, bool requireComplete)
    {
        tracking ??= [];
        switch (mode)
        {
            case ItemTrackingMode.None when tracking.Count > 0:
                throw new RequestValidationException($"{field}.tracking", "Item is not lot or serial tracked.");
            case ItemTrackingMode.Lot when tracking.Any(x => x.SerialNo is not null):
                throw new RequestValidationException($"{field}.tracking", "Item is lot tracked; use lot numbers.");
            case ItemTrackingMode.Serial when tracking.Any(x => x.LotNo is not null):
                throw new RequestValidationException($"{field}.tracking", "Item is serial tracked; use serial numbers.");
            case ItemTrackingMode.Serial when decimal.Truncate(quantity) != quantity:
                throw new RequestValidationException($"{field}.quantity", "Serial-tracked quantity must be a whole number.");
        }
        if (requireComplete && mode != ItemTrackingMode.None && tracking.Sum(x => x.Quantity) != quantity)
            throw new BusinessRuleException("INV.TRACKING.REQUIRED",
                $"Line {lineNo} must carry {mode.ToString().ToLowerInvariant()} numbers totalling its quantity.");
    }

    public static string Normalize(string number) => number.Trim().ToUpperInvariant();

    private static bool IsStorable(decimal value) => value > -MaxValue && value < MaxValue && decimal.Round(value, 6) == value;
}
