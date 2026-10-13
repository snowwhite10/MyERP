using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Inventory.Application;

/// <summary>
/// 庫存唯一寫入入口（ADR-053）。鎖順序固定：來源排他鎖 → MD 共享鎖（逐行引用）→ 出庫結存鍵排他鎖（排序後取得）
/// → 有批序號入庫的物料追蹤排他鎖（排序後取得，ADR-060），避免不同過帳之間死結；全部鎖由交易持有至提交。
/// </summary>
public sealed partial class StockPostingService(MyErpDbContext db, InventoryReferenceService references, IClock clock)
    : IInventoryPostingService
{
    public const int MaxLines = 1000;
    public const int MaxTrackingEntries = 10_000;
    private const decimal MaxValue = 10_000_000_000_000m;

    public async Task<InventoryPostingResult> PostAsync(InventoryPostingRequest request, CancellationToken token = default)
    {
        Validate(request);
        if (db.CurrentCompanyId is not long companyId || companyId <= 0)
            throw new BusinessRuleException("COMMON.COMPANY.REQUIRED", "Company is required.");
        var lines = request.Lines.OrderBy(x => x.LineNo).ThenBy(x => x.Direction).ToList();

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(token) : null;
        var added = new List<object>();
        try
        {
            await LockAsync($"inv_source:{companyId}:{request.SourceType}:{request.SourceId}", token);
            var existing = await db.Set<StockMovement>().AsNoTracking()
                .Where(x => x.SourceType == request.SourceType && x.SourceId == request.SourceId)
                .OrderBy(x => x.SourceLineNo).ThenBy(x => x.Direction)
                .ToListAsync(token);
            if (existing.Count > 0)
            {
                var keys = await TrackingKeysAsync(existing, token);
                if (!existing.Select(x => Snapshot(x, keys.GetValueOrDefault(x.Id, "")))
                        .SequenceEqual(lines.Select(x => Snapshot(x, request.BusinessDate))))
                    throw new ConflictException("INV.POSTING.CONFLICT", "Source was already posted with different lines.");
                return new InventoryPostingResult(true, [.. existing.Select(x => x.Id)]);
            }

            var movements = new List<StockMovement>(lines.Count);
            foreach (var line in lines)
            {
                var (reference, trackingMode) = await references.RecordWithTrackingAsync(
                    $"STOCK:{request.SourceType}:{request.SourceId}:{line.LineNo}:{line.Direction}",
                    line.ItemId, line.WarehouseId, line.LocationId, token);
                added.Add(reference);
                StockTrackingRules.CheckMode(line.LineNo, line.Quantity, line.Tracking, $"lines[{IndexOf(request, line)}]",
                    trackingMode, requireComplete: true);
                movements.Add(new StockMovement
                {
                    CompanyId = companyId, SourceType = request.SourceType, SourceId = request.SourceId,
                    SourceLineNo = line.LineNo, Direction = ToDomain(line.Direction), ReferenceId = reference.Id,
                    ItemId = line.ItemId, WarehouseId = line.WarehouseId, LocationId = line.LocationId,
                    Quantity = line.Quantity, UnitCost = line.UnitCost, BusinessDate = request.BusinessDate,
                    PostedAt = clock.LocalNow,
                });
            }

            await CheckAvailabilityAsync(companyId, lines, token);
            var tracking = await ResolveTrackingAsync(companyId, lines, movements, added, token);
            db.AddRange(movements);
            added.AddRange(movements);
            db.AddRange(tracking);
            added.AddRange(tracking);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            { throw new ConflictException("INV.POSTING.CONFLICT", "Source was already posted."); }
            if (transaction is not null) await transaction.CommitAsync(token);
            return new InventoryPostingResult(false, [.. movements.Select(x => x.Id)]);
        }
        catch
        {
            // 自行開啟的交易回滾後，已追蹤的引用／異動／批序號不可留在共用 DbContext 被下一次 SaveChanges 誤寫
            if (transaction is not null)
                foreach (var entity in added) db.Entry(entity).State = EntityState.Detached;
            throw;
        }
    }

    /// <summary>每個有出庫的結存鍵：取排他鎖後，以「既有結存 + 本次淨額」不得為負檢查；只有入庫的鍵不需序列化。</summary>
    private async Task CheckAvailabilityAsync(long companyId, IReadOnlyList<InventoryPostingLine> lines, CancellationToken token)
    {
        var keys = lines.GroupBy(x => (x.ItemId, x.WarehouseId, x.LocationId))
            .Where(g => g.Any(x => x.Direction == StockDirection.Out))
            .Select(g => (g.Key, Net: g.Sum(x => x.Direction == StockDirection.In ? x.Quantity : -x.Quantity)))
            .OrderBy(x => x.Key.ItemId).ThenBy(x => x.Key.WarehouseId).ThenBy(x => x.Key.LocationId ?? 0)
            .ToList();
        foreach (var (key, _) in keys)
            await LockAsync($"inv_stock:{companyId}:{key.ItemId}:{key.WarehouseId}:{key.LocationId ?? 0}", token);
        foreach (var (key, net) in keys)
        {
            var onHand = await StockBalanceQuery.Signed(db.Set<StockMovement>().Where(x =>
                x.ItemId == key.ItemId && x.WarehouseId == key.WarehouseId && x.LocationId == key.LocationId)).SumAsync(token);
            if (onHand + net < 0)
                throw new BusinessRuleException("INV.STOCK.INSUFFICIENT",
                    $"Insufficient stock for item {key.ItemId} in warehouse {key.WarehouseId}: on hand {onHand}, required {-net}.");
        }
    }

    /// <summary>
    /// 批序號解析與檢查（ADR-060 第 4–7 點）：入庫物料先取追蹤排他鎖，序列化批號建立與序號重複入庫檢查；
    /// 出庫的批序號結存由已取得的結存鍵排他鎖保護。回傳待寫入的異動明細（以導覽屬性連到本次異動與新建批序號）。
    /// </summary>
    private async Task<List<StockMovementTracking>> ResolveTrackingAsync(long companyId, IReadOnlyList<InventoryPostingLine> lines,
        List<StockMovement> movements, List<object> added, CancellationToken token)
    {
        var entries = lines.Select((line, i) => (Line: line, Movement: movements[i]))
            .Where(x => x.Line.Tracking is { Count: > 0 })
            .SelectMany(x => x.Line.Tracking!.Select(t => new Entry(x.Line, x.Movement, t)))
            .ToList();
        if (entries.Count == 0) return [];

        foreach (var item in entries.Where(x => x.Line.Direction == StockDirection.In).Select(x => x.Line.ItemId).Distinct().Order())
            await LockAsync($"inv_tracking:{companyId}:{item}", token);

        var itemIds = entries.Select(x => x.Line.ItemId).Distinct().ToList();
        var lotNos = entries.Where(x => x.IsLot).Select(x => x.Number).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var serialNos = entries.Where(x => !x.IsLot).Select(x => x.Number).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lots = lotNos.Count == 0 ? [] : await db.Set<StockLot>()
            .Where(x => itemIds.Contains(x.ItemId) && lotNos.Contains(x.LotNo)).ToListAsync(token);
        var serials = serialNos.Count == 0 ? [] : await db.Set<StockSerial>()
            .Where(x => itemIds.Contains(x.ItemId) && serialNos.Contains(x.SerialNo)).ToListAsync(token);
        var lotByKey = lots.ToDictionary(x => Key(x.ItemId, x.LotNo));
        var serialByKey = serials.ToDictionary(x => Key(x.ItemId, x.SerialNo));
        var existingLotIds = lots.Select(x => x.Id).ToList();
        var existingSerialIds = serials.Select(x => x.Id).ToList();

        var rows = new List<StockMovementTracking>(entries.Count);
        foreach (var entry in entries)
        {
            var key = Key(entry.Line.ItemId, entry.Number);
            var isIn = entry.Line.Direction == StockDirection.In;
            if (entry.IsLot)
            {
                if (!lotByKey.TryGetValue(key, out var lot))
                {
                    if (!isIn)
                        throw new BusinessRuleException("INV.LOT.INSUFFICIENT", $"Lot {entry.Number} of item {entry.Line.ItemId} has no stock.");
                    lot = new StockLot { CompanyId = companyId, ItemId = entry.Line.ItemId, LotNo = entry.Number,
                        ManufactureDate = entry.Tracking.ManufactureDate, ExpiryDate = entry.Tracking.ExpiryDate };
                    lotByKey.Add(key, lot);
                    db.Add(lot);
                    added.Add(lot);
                }
                else if (entry.Tracking.ManufactureDate is { } made && made != lot.ManufactureDate
                    || entry.Tracking.ExpiryDate is { } expiry && expiry != lot.ExpiryDate)
                {
                    throw new BusinessRuleException("INV.LOT.ATTRIBUTE_MISMATCH",
                        $"Lot {entry.Number} of item {entry.Line.ItemId} already exists with different dates.");
                }
                rows.Add(new StockMovementTracking { CompanyId = companyId, Movement = entry.Movement, Lot = lot, Quantity = entry.Tracking.Quantity });
            }
            else
            {
                if (!serialByKey.TryGetValue(key, out var serial))
                {
                    if (!isIn)
                        throw new BusinessRuleException("INV.SERIAL.NOT_AVAILABLE", $"Serial {entry.Number} of item {entry.Line.ItemId} is not in stock.");
                    serial = new StockSerial { CompanyId = companyId, ItemId = entry.Line.ItemId, SerialNo = entry.Number };
                    serialByKey.Add(key, serial);
                    db.Add(serial);
                    added.Add(serial);
                }
                rows.Add(new StockMovementTracking { CompanyId = companyId, Movement = entry.Movement, Serial = serial, Quantity = 1m });
            }
        }

        await CheckTrackingBalancesAsync(rows, existingLotIds, existingSerialIds, token);
        return rows;
    }

    /// <summary>
    /// 出庫：每個（批號或序號、結存鍵）的既有結存 + 本次淨額不得為負；入庫：序號全公司結存 + 本次淨額不得超過 1。
    /// 新建的批序號沒有既有結存，只以本次淨額判斷。
    /// </summary>
    private async Task CheckTrackingBalancesAsync(IReadOnlyList<StockMovementTracking> rows,
        List<long> lotIds, List<long> serialIds, CancellationToken token)
    {
        var lotBalances = lotIds.Count == 0 ? [] : await db.Set<StockMovementTracking>()
            .Where(x => x.LotId != null && lotIds.Contains(x.LotId.Value))
            .GroupBy(x => new { Id = x.LotId!.Value, x.Movement!.WarehouseId, x.Movement.LocationId })
            .Select(g => new TrackedBalance(g.Key.Id, g.Key.WarehouseId, g.Key.LocationId,
                g.Sum(x => x.Movement!.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity)))
            .ToListAsync(token);
        var serialBalances = serialIds.Count == 0 ? [] : await db.Set<StockMovementTracking>()
            .Where(x => x.SerialId != null && serialIds.Contains(x.SerialId.Value))
            .GroupBy(x => new { Id = x.SerialId!.Value, x.Movement!.WarehouseId, x.Movement.LocationId })
            .Select(g => new TrackedBalance(g.Key.Id, g.Key.WarehouseId, g.Key.LocationId,
                g.Sum(x => x.Movement!.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity)))
            .ToListAsync(token);

        static decimal Signed(StockMovementTracking x) => x.Movement!.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity;

        foreach (var group in rows.Where(x => x.Lot is not null)
                     .GroupBy(x => (Lot: x.Lot!, x.Movement!.WarehouseId, x.Movement.LocationId))
                     .Where(g => g.Any(x => x.Movement!.Direction == StockMovementDirection.Out)))
        {
            var (lot, warehouseId, locationId) = group.Key;
            var onHand = lotBalances.Where(x => x.Id == lot.Id && x.WarehouseId == warehouseId && x.LocationId == locationId).Sum(x => x.Quantity);
            if (onHand + group.Sum(Signed) < 0)
                throw new BusinessRuleException("INV.LOT.INSUFFICIENT",
                    $"Insufficient stock for lot {lot.LotNo} in warehouse {warehouseId}: on hand {onHand}.");
        }

        foreach (var group in rows.Where(x => x.Serial is not null).GroupBy(x => x.Serial!))
        {
            var serial = group.Key;
            foreach (var at in group.GroupBy(x => (x.Movement!.WarehouseId, x.Movement.LocationId))
                         .Where(g => g.Any(x => x.Movement!.Direction == StockMovementDirection.Out)))
            {
                var onHand = serialBalances.Where(x => x.Id == serial.Id && x.WarehouseId == at.Key.WarehouseId
                    && x.LocationId == at.Key.LocationId).Sum(x => x.Quantity);
                if (onHand + at.Sum(Signed) < 0)
                    throw new BusinessRuleException("INV.SERIAL.NOT_AVAILABLE",
                        $"Serial {serial.SerialNo} is not in stock in warehouse {at.Key.WarehouseId}.");
            }
            if (serialBalances.Where(x => x.Id == serial.Id).Sum(x => x.Quantity) + group.Sum(Signed) > 1)
                throw new ConflictException("INV.SERIAL.IN_STOCK", $"Serial {serial.SerialNo} is already in stock.");
        }
    }

    private async Task<Dictionary<long, string>> TrackingKeysAsync(IReadOnlyList<StockMovement> movements, CancellationToken token)
    {
        var ids = movements.Select(x => x.Id).ToList();
        var rows = await db.Set<StockMovementTracking>().AsNoTracking()
            .Where(x => ids.Contains(x.MovementId))
            .Select(x => new { x.MovementId, LotNo = x.Lot!.LotNo, SerialNo = x.Serial!.SerialNo, x.Quantity })
            .ToListAsync(token);
        return rows.GroupBy(x => x.MovementId).ToDictionary(g => g.Key,
            g => TrackingKey(g.Select(x => (x.LotNo is not null, x.LotNo ?? x.SerialNo!, x.Quantity))));
    }

    private static string TrackingKey(IEnumerable<(bool IsLot, string Number, decimal Quantity)> entries) =>
        string.Join(";", entries
            .Select(x => $"{(x.IsLot ? 'L' : 'S')}|{StockTrackingRules.Normalize(x.Number)}|{x.Quantity.ToString("0.######", CultureInfo.InvariantCulture)}")
            .Order(StringComparer.Ordinal));

    private Task<int> LockAsync(string resource, CancellationToken token) => db.Database.ExecuteSqlAsync($"""
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
        IF @result < 0 THROW 51000, 'Inventory lock was not granted.', 1;
        """, token);

    private static void Validate(InventoryPostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>();
        void Fail(string field, string message) => errors.TryAdd(field, [message]);

        if (request.SourceType is null || !SourceTypePattern().IsMatch(request.SourceType))
            Fail("sourceType", "Source type must be 1 to 30 upper-case letters, digits or underscores starting with a letter.");
        if (request.SourceId <= 0) Fail("sourceId", "Source id must be positive.");
        if (request.BusinessDate == default) Fail("businessDate", "Business date is required.");
        if (request.Lines is not { Count: > 0 and <= MaxLines })
        {
            Fail("lines", $"Posting must contain 1 to {MaxLines} lines.");
        }
        else
        {
            for (var i = 0; i < request.Lines.Count; i++)
            {
                var line = request.Lines[i];
                var field = $"lines[{i}]";
                if (line is null) { Fail(field, "Line is required."); continue; }
                if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
                if (!Enum.IsDefined(line.Direction)) Fail($"{field}.direction", "Direction must be In or Out.");
                if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
                if (line.WarehouseId <= 0) Fail($"{field}.warehouseId", "Warehouse is required.");
                if (line.LocationId is <= 0) Fail($"{field}.locationId", "Location id must be positive.");
                if (!IsStorable(line.Quantity) || line.Quantity <= 0)
                    Fail($"{field}.quantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
                if (line.UnitCost is decimal cost && (!IsStorable(cost) || cost < 0))
                    Fail($"{field}.unitCost", "Unit cost must be non-negative, below 10^13 and have at most 6 decimals.");
                if (line.UnitCost is not null && line.Direction == StockDirection.Out)
                    Fail($"{field}.unitCost", "Issue cost is determined by inventory valuation.");
                if (line.Tracking is { } tracking) StockTrackingRules.Validate(line.Direction, tracking, $"{field}.tracking", Fail);
            }
            var valid = request.Lines.Where(x => x is not null).ToList();
            if (valid.GroupBy(x => (x.LineNo, x.Direction)).Any(g => g.Count() > 1))
                Fail("lines", "Line number and direction must be unique within a source.");
            if (valid.Sum(x => x.Tracking?.Count ?? 0) > MaxTrackingEntries)
                Fail("lines", $"Posting must contain at most {MaxTrackingEntries} lot or serial entries.");
            if (StockTrackingRules.HasDuplicateSerials(valid.Select(x => (x.ItemId, x.Direction, x.Tracking))))
                Fail("lines", "A serial number can appear only once per item and direction within a source.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private static int IndexOf(InventoryPostingRequest request, InventoryPostingLine line)
    {
        for (var i = 0; i < request.Lines.Count; i++)
            if (ReferenceEquals(request.Lines[i], line)) return i;
        return -1;
    }

    private static (long ItemId, string Number) Key(long itemId, string number) => (itemId, StockTrackingRules.Normalize(number));

    private static bool IsStorable(decimal value) => value > -MaxValue && value < MaxValue && decimal.Round(value, 6) == value;

    private static StockMovementDirection ToDomain(StockDirection direction) =>
        direction == StockDirection.In ? StockMovementDirection.In : StockMovementDirection.Out;

    private static LineSnapshot Snapshot(StockMovement x, string trackingKey) => new(x.SourceLineNo, x.Direction, x.ItemId,
        x.WarehouseId, x.LocationId, x.Quantity, x.UnitCost, x.BusinessDate, trackingKey);

    private static LineSnapshot Snapshot(InventoryPostingLine x, DateOnly date) => new(x.LineNo, ToDomain(x.Direction), x.ItemId,
        x.WarehouseId, x.LocationId, x.Quantity, x.UnitCost, date,
        TrackingKey((x.Tracking ?? []).Select(t => (t.LotNo is not null, t.LotNo ?? t.SerialNo!, t.Quantity))));

    private sealed record LineSnapshot(int LineNo, StockMovementDirection Direction, long ItemId, long WarehouseId,
        long? LocationId, decimal Quantity, decimal? UnitCost, DateOnly BusinessDate, string Tracking);

    private sealed record TrackedBalance(long Id, long WarehouseId, long? LocationId, decimal Quantity);

    /// <summary>一個待解析的批序號：號碼已去除前後空白。</summary>
    private sealed record Entry(InventoryPostingLine Line, StockMovement Movement, InventoryTrackingLine Tracking)
    {
        public bool IsLot => Tracking.LotNo is not null;
        public string Number => (Tracking.LotNo ?? Tracking.SerialNo!).Trim();
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,29}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceTypePattern();
}
