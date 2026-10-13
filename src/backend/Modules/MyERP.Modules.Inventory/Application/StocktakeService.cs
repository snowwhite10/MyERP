using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.Inventory.Application;

public sealed record StocktakeScope(IReadOnlyList<long>? WarehouseIds, IReadOnlyList<long>? CategoryIds = null,
    IReadOnlyList<long>? ItemIds = null);
public sealed record StocktakeInput(DateOnly BusinessDate, string? Remark, StocktakeScope? Scope);
public sealed record StocktakeTrackingCount(string? LotNo, string? SerialNo, decimal? CountedQuantity);
/// <summary>以 <see cref="LineId"/> 指向既有行；省略時為實地新增行，須帶物料、倉庫與可選儲位。</summary>
public sealed record StocktakeCountLine(long? LineId, decimal? CountedQuantity, IReadOnlyList<StocktakeTrackingCount>? Tracking = null,
    long? ItemId = null, long? WarehouseId = null, long? LocationId = null);
public sealed record StocktakeCountInput(DateOnly BusinessDate, string? Remark, IReadOnlyList<StocktakeCountLine>? Lines);
public sealed record StocktakeQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public InventoryDocumentStatus? Status { get; init; }
}
public sealed record StocktakeSummary(long Id, string DocumentNo, DateOnly BusinessDate, InventoryDocumentStatus Status,
    string? Remark, int WarehouseCount, int LineCount, int CountedLineCount, int Version);
public sealed record StocktakeDetail(Stocktake Stocktake, StocktakeScope Scope, ApprovalState? Approval);
/// <summary>單張盤點的行數與批序號數上限（ADR-062 第 3 點）；測試可縮小。</summary>
public sealed record StocktakeLimits(int MaxLines = 5000, int MaxTrackingEntries = 20_000);

/// <summary>
/// REQ_F4115.7.1／7.3／7.4／7.7：盤點建立（快照）、實盤保存、重新快照、軟刪除與查詢（ADR-062）。
/// 建立、保存與重新快照都在公司級 <c>inv_stocktake</c> 排他鎖內檢查範圍互斥（BR-INV-032）；快照只讀異動帳。
/// </summary>
public sealed class StocktakeService(MyErpDbContext db, INumberGenerator numbers, IInventoryReferenceGuard guard,
    IItemCategoryScope categoryScope, IApprovalWorkflowService workflow, IClock clock, StocktakeLimits? limits = null)
{
    public const string DocumentType = "INV.STOCKTAKE";
    public const string SourceType = "STOCKTAKE";
    public const string NotFound = "INV.STOCKTAKE.NOT_FOUND";
    public const string NotDraft = "INV.STOCKTAKE.NOT_DRAFT";
    public const string TooManyLines = "INV.STOCKTAKE.TOO_MANY_LINES";
    public const string ScopeInUse = "INV.STOCKTAKE.SCOPE_IN_USE";
    public const string LineOutOfScope = "INV.STOCKTAKE.LINE_OUT_OF_SCOPE";
    private const int MaxWarehouses = 20;
    private const int MaxCategories = 50;
    private const int MaxItems = 500;
    private const decimal MaxQuantity = 10_000_000_000_000m;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly StocktakeLimits limits = limits ?? new StocktakeLimits();

    public async Task<PagedResult<StocktakeSummary>> ListAsync(StocktakeQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        if (request.From > request.To) throw new RequestValidationException("to", "End date must not be earlier than start date.");
        if (request.Status is { } s && !Enum.IsDefined(s)) throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<Stocktake>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || (x.Remark != null && x.Remark.Contains(keyword)));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        var sort = sorts[0];
        query = (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id),
        };
        var count = await query.LongCountAsync(token);
        var rows = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new { x.Id, x.DocumentNo, x.BusinessDate, x.Status, x.Remark, x.ScopeJson,
                LineCount = x.Lines.Count, CountedLineCount = x.Lines.Count(l => l.CountedQuantity != null), x.Version })
            .ToListAsync(token);
        return PagedResult.Create(rows.Select(x => new StocktakeSummary(x.Id, x.DocumentNo, x.BusinessDate, x.Status, x.Remark,
            ParseScope(x.ScopeJson).WarehouseIds!.Count, x.LineCount, x.CountedLineCount, x.Version)).ToList(), request, count);
    }

    public async Task<StocktakeDetail> GetAsync(long id, CancellationToken token = default)
    {
        RequireCompany();
        var stocktake = await WithLines(db.Set<Stocktake>().AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw Missing(id);
        return new StocktakeDetail(stocktake, ParseScope(stocktake.ScopeJson), await workflow.GetStateAsync(DocumentType, id, token));
    }

    public async Task<StocktakeDetail> CreateAsync(StocktakeInput input, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var (remark, scope) = Validate(input);
        var stocktake = await InTransactionAsync(async () =>
        {
            await LockAsync(companyId, token);
            var snapshot = await SnapshotAsync(scope, token);
            await CheckScopeFreeAsync(null, snapshot.Lines.Select(x => (x.ItemId, x.WarehouseId)), token);
            var created = new Stocktake
            {
                CompanyId = companyId, DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token),
                BusinessDate = input.BusinessDate, Remark = remark, ScopeJson = JsonSerializer.Serialize(scope, Json),
                SnapshotAt = clock.LocalNow, SnapshotMovementId = snapshot.MaxMovementId,
            };
            var lineNo = 0;
            foreach (var line in snapshot.Lines)
            {
                var entity = new StocktakeLine { LineNo = ++lineNo, ItemId = line.ItemId, WarehouseId = line.WarehouseId,
                    LocationId = line.LocationId, TrackingMode = line.TrackingMode, BookQuantity = line.Quantity };
                entity.Tracking.AddRange(line.Tracking.Select((t, i) => new StocktakeLineTracking
                    { SeqNo = i + 1, LotNo = t.LotNo, SerialNo = t.SerialNo, BookQuantity = t.Quantity }));
                created.Lines.Add(entity);
            }
            db.Add(created);
            await db.SaveChangesAsync(token);
            return created;
        }, token);
        return new StocktakeDetail(stocktake, scope, null);
    }

    /// <summary>整組保存實盤：未列出的快照行清空實盤，未列出且帳面為 0 的行或批序號移除；駁回單保存後回草稿。</summary>
    public async Task<StocktakeDetail> UpdateAsync(long id, StocktakeCountInput input, int version, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var (remark, counts) = Validate(input);
        var stocktake = await InTransactionAsync(async () =>
        {
            await LockAsync(companyId, token);
            var current = await LoadAsync(id, version, token);
            if (current.Status is not (InventoryDocumentStatus.Draft or InventoryDocumentStatus.Rejected)) throw NotEditable();
            await ApplyCountsAsync(current, ParseScope(current.ScopeJson), counts, token);
            CheckLimits(current.Lines);
            current.BusinessDate = input.BusinessDate;
            current.Remark = remark;
            current.Status = InventoryDocumentStatus.Draft;
            db.Entry(current).Property(x => x.Status).IsModified = true;
            await db.SaveChangesAsync(token);
            return current;
        }, token);
        return new StocktakeDetail(stocktake, ParseScope(stocktake.ScopeJson), null);
    }

    /// <summary>
    /// 依原範圍重新快照（ADR-062 第 4 點）：更新帳面、保留實盤、帳面有變的行標記重盤、加入新鍵、
    /// 移除帳面為 0 且未盤的鍵或批序號，並更新快照時間與最大異動 ID。只限草稿。
    /// </summary>
    public async Task<StocktakeDetail> RefreshSnapshotAsync(long id, int version, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var stocktake = await InTransactionAsync(async () =>
        {
            await LockAsync(companyId, token);
            var current = await LoadAsync(id, version, token);
            if (current.Status != InventoryDocumentStatus.Draft) throw NotEditable();
            var snapshot = await SnapshotAsync(ParseScope(current.ScopeJson), token);
            await CheckScopeFreeAsync(current.Id, snapshot.Lines.Select(x => (x.ItemId, x.WarehouseId)), token);
            Merge(current, snapshot.Lines);
            CheckLimits(current.Lines);
            current.SnapshotAt = clock.LocalNow;
            current.SnapshotMovementId = snapshot.MaxMovementId;
            db.Entry(current).Property(x => x.Status).IsModified = true;
            await db.SaveChangesAsync(token);
            return current;
        }, token);
        return new StocktakeDetail(stocktake, ParseScope(stocktake.ScopeJson), null);
    }

    /// <summary>按版本軟刪除草稿；單號保留，並釋放範圍互斥。</summary>
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        await InTransactionAsync(async () =>
        {
            var current = await LoadAsync(id, version, token);
            if (current.Status != InventoryDocumentStatus.Draft) throw NotEditable();
            current.IsDeleted = true;
            current.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            return current;
        }, token);
    }

    internal static IQueryable<Stocktake> WithLines(IQueryable<Stocktake> query) =>
        query.Include(x => x.Lines).ThenInclude(x => x.Tracking).AsSplitQuery();
    internal static NotFoundException Missing(long id) => new(NotFound, "Stocktake", id);
    private static BusinessRuleException NotEditable() =>
        new(NotDraft, "Only a draft stocktake can be changed, refreshed or deleted; a rejected stocktake must be revised first.");

    internal static StocktakeScope ParseScope(string json)
    {
        var scope = JsonSerializer.Deserialize<StocktakeScope>(json, Json)
            ?? throw new InvalidOperationException("Stocktake scope is missing.");
        return new StocktakeScope(scope.WarehouseIds ?? [], scope.CategoryIds ?? [], scope.ItemIds ?? []);
    }

    private sealed record SnapshotTracking(string? LotNo, string? SerialNo, decimal Quantity);
    private sealed record SnapshotLine(long ItemId, long WarehouseId, long? LocationId, decimal Quantity, string TrackingMode,
        List<SnapshotTracking> Tracking);
    private sealed record Snapshot(long? MaxMovementId, List<SnapshotLine> Lines);

    /// <summary>
    /// 只讀異動帳（BR-INV-031）：先記最大異動 ID，再取範圍內結存大於 0 的鍵及其批序號結存；
    /// 兩次讀取之間提交的異動只會令送審時判定過期（安全方向）。
    /// </summary>
    private async Task<Snapshot> SnapshotAsync(StocktakeScope scope, CancellationToken token)
    {
        var warehouseIds = scope.WarehouseIds!.ToList();
        await guard.RequireWarehousesAsync(warehouseIds, token);
        var itemIds = await categoryScope.ResolveItemsAsync(scope.CategoryIds!, scope.ItemIds!, token);
        var maxMovementId = await db.Set<StockMovement>().MaxAsync(x => (long?)x.Id, token);
        if (itemIds is { Count: 0 }) return new Snapshot(maxMovementId, []);
        var items = itemIds?.ToList();

        var movements = db.Set<StockMovement>().Where(x => warehouseIds.Contains(x.WarehouseId));
        if (items is not null) movements = movements.Where(x => items.Contains(x.ItemId));
        var balances = await movements
            .GroupBy(x => new { x.ItemId, x.WarehouseId, x.LocationId })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, g.Key.LocationId,
                Quantity = g.Sum(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity) })
            .Where(x => x.Quantity > 0)
            .ToListAsync(token);
        if (balances.Count > limits.MaxLines) throw LimitExceeded();

        var tracked = db.Set<StockMovementTracking>().Where(x => warehouseIds.Contains(x.Movement!.WarehouseId));
        if (items is not null) tracked = tracked.Where(x => items.Contains(x.Movement!.ItemId));
        var trackingBalances = await tracked
            .GroupBy(x => new { x.Movement!.ItemId, x.Movement.WarehouseId, x.Movement.LocationId, x.LotId, x.SerialId })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, g.Key.LocationId, g.Key.LotId, g.Key.SerialId,
                Quantity = g.Sum(x => x.Movement!.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity) })
            .Where(x => x.Quantity > 0)
            .ToListAsync(token);
        if (trackingBalances.Count > limits.MaxTrackingEntries) throw LimitExceeded();
        var lotIds = trackingBalances.Where(x => x.LotId != null).Select(x => x.LotId!.Value).Distinct().ToList();
        var serialIds = trackingBalances.Where(x => x.SerialId != null).Select(x => x.SerialId!.Value).Distinct().ToList();
        var lots = lotIds.Count == 0 ? [] : await db.Set<StockLot>().Where(x => lotIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.LotNo, token);
        var serials = serialIds.Count == 0 ? [] : await db.Set<StockSerial>().Where(x => serialIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.SerialNo, token);
        var trackingByKey = trackingBalances.ToLookup(x => (x.ItemId, x.WarehouseId, x.LocationId), x =>
            new SnapshotTracking(x.LotId is long lot ? lots[lot] : null, x.SerialId is long serial ? serials[serial] : null, x.Quantity));

        var lines = balances
            .OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId ?? 0)
            .Select(x =>
            {
                var tracking = trackingByKey[(x.ItemId, x.WarehouseId, x.LocationId)]
                    .OrderBy(t => t.LotNo ?? t.SerialNo, StringComparer.OrdinalIgnoreCase).ToList();
                var mode = tracking.Count == 0 ? ItemTrackingMode.None
                    : tracking[0].LotNo is not null ? ItemTrackingMode.Lot : ItemTrackingMode.Serial;
                return new SnapshotLine(x.ItemId, x.WarehouseId, x.LocationId, x.Quantity, mode.ToString(), tracking);
            }).ToList();
        return new Snapshot(maxMovementId, lines);
    }

    /// <summary>BR-INV-032：同公司同一物料＋倉庫只能在一張未結（未刪除、未過帳）盤點單；呼叫端須持有盤點鎖。</summary>
    private async Task CheckScopeFreeAsync(long? selfId, IEnumerable<(long ItemId, long WarehouseId)> pairs, CancellationToken token)
    {
        var wanted = pairs.ToHashSet();
        if (wanted.Count == 0) return;
        var warehouseIds = wanted.Select(x => x.WarehouseId).Distinct().ToList();
        var taken = await db.Set<StocktakeLine>()
            .Where(l => warehouseIds.Contains(l.WarehouseId) && l.StocktakeId != selfId
                && db.Set<Stocktake>().Any(h => h.Id == l.StocktakeId && h.Status != InventoryDocumentStatus.Posted))
            .Select(l => new { l.ItemId, l.WarehouseId }).Distinct().ToListAsync(token);
        var conflict = taken.Where(x => wanted.Contains((x.ItemId, x.WarehouseId)))
            .OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).FirstOrDefault();
        if (conflict is not null)
            throw new BusinessRuleException(ScopeInUse,
                $"Item {conflict.ItemId} in warehouse {conflict.WarehouseId} is already in another open stocktake.");
    }

    private static void Merge(Stocktake stocktake, List<SnapshotLine> snapshot)
    {
        var byKey = snapshot.ToDictionary(x => (x.ItemId, x.WarehouseId, x.LocationId));
        var nextLineNo = stocktake.Lines.Select(x => x.LineNo).DefaultIfEmpty().Max();
        foreach (var line in stocktake.Lines.ToList())
        {
            var fresh = byKey.GetValueOrDefault((line.ItemId, line.WarehouseId, line.LocationId));
            byKey.Remove((line.ItemId, line.WarehouseId, line.LocationId));
            var changed = MergeTracking(line, fresh?.Tracking ?? []) | line.BookQuantity != (fresh?.Quantity ?? 0m);
            line.BookQuantity = fresh?.Quantity ?? 0m;
            if (fresh is not null)
            {
                line.IsAdded = false;
                if (fresh.TrackingMode != nameof(ItemTrackingMode.None)) line.TrackingMode = fresh.TrackingMode;
            }
            if (line.BookQuantity == 0 && line.CountedQuantity is null) { stocktake.Lines.Remove(line); continue; }
            line.RecountRequired = changed;
        }
        foreach (var fresh in snapshot.Where(x => byKey.ContainsKey((x.ItemId, x.WarehouseId, x.LocationId))))
        {
            var line = new StocktakeLine { LineNo = ++nextLineNo, ItemId = fresh.ItemId, WarehouseId = fresh.WarehouseId,
                LocationId = fresh.LocationId, TrackingMode = fresh.TrackingMode, BookQuantity = fresh.Quantity };
            line.Tracking.AddRange(fresh.Tracking.Select((t, i) => new StocktakeLineTracking
                { SeqNo = i + 1, LotNo = t.LotNo, SerialNo = t.SerialNo, BookQuantity = t.Quantity }));
            stocktake.Lines.Add(line);
        }
    }

    /// <summary>合併一行的批序號帳面；回傳是否有號碼的帳面改變。</summary>
    private static bool MergeTracking(StocktakeLine line, List<SnapshotTracking> fresh)
    {
        var changed = false;
        var byNumber = fresh.ToDictionary(x => StockTrackingRules.Normalize(x.LotNo ?? x.SerialNo!));
        var nextSeq = line.Tracking.Select(x => x.SeqNo).DefaultIfEmpty().Max();
        foreach (var entry in line.Tracking.ToList())
        {
            var number = StockTrackingRules.Normalize(entry.LotNo ?? entry.SerialNo!);
            var book = byNumber.Remove(number, out var match) ? match.Quantity : 0m;
            changed |= entry.BookQuantity != book;
            entry.BookQuantity = book;
            if (book == 0 && entry.CountedQuantity is null) line.Tracking.Remove(entry);
        }
        foreach (var entry in fresh.Where(x => byNumber.ContainsKey(StockTrackingRules.Normalize(x.LotNo ?? x.SerialNo!))))
        {
            changed = true;
            line.Tracking.Add(new StocktakeLineTracking { SeqNo = ++nextSeq, LotNo = entry.LotNo, SerialNo = entry.SerialNo,
                BookQuantity = entry.Quantity });
        }
        return changed;
    }

    private async Task ApplyCountsAsync(Stocktake stocktake, StocktakeScope scope, IReadOnlyList<StocktakeCountLine> counts,
        CancellationToken token)
    {
        var byId = stocktake.Lines.ToDictionary(x => x.Id);
        var keys = stocktake.Lines.Select(x => (x.ItemId, x.WarehouseId, x.LocationId)).ToHashSet();
        var provided = new Dictionary<long, (StocktakeCountLine Count, string Field)>();
        var added = new List<(StocktakeCountLine Count, string Field)>();
        for (var i = 0; i < counts.Count; i++)
        {
            var count = counts[i];
            var field = $"lines[{i}]";
            if (count.LineId is long lineId)
            {
                if (!byId.ContainsKey(lineId)) throw new RequestValidationException($"{field}.lineId", "Line does not belong to this stocktake.");
                provided[lineId] = (count, field);
                continue;
            }
            if (!scope.WarehouseIds!.Contains(count.WarehouseId!.Value))
                throw new BusinessRuleException(LineOutOfScope, $"Warehouse {count.WarehouseId} is not in the stocktake scope.");
            if (!keys.Add((count.ItemId!.Value, count.WarehouseId.Value, count.LocationId)))
                throw new RequestValidationException($"{field}.itemId", "Stock key is already in this stocktake; use its line ID.");
            added.Add((count, field));
        }

        var modes = new Dictionary<(long, long, long?), ItemTrackingMode>();
        foreach (var (count, _) in added.OrderBy(x => x.Count.ItemId).ThenBy(x => x.Count.WarehouseId).ThenBy(x => x.Count.LocationId ?? 0))
        {
            var key = (count.ItemId!.Value, count.WarehouseId!.Value, count.LocationId);
            modes[key] = await guard.RunWithCodesAsync(key.Item1, key.Item2, key.LocationId,
                codes => Task.FromResult(codes.TrackingMode), token);
        }
        var ownPairs = stocktake.Lines.Select(x => (x.ItemId, x.WarehouseId)).ToHashSet();
        await CheckScopeFreeAsync(stocktake.Id, added.Select(x => (x.Count.ItemId!.Value, x.Count.WarehouseId!.Value))
            .Where(x => !ownPairs.Contains(x)), token);

        foreach (var line in stocktake.Lines.ToList())
        {
            if (provided.TryGetValue(line.Id, out var entry))
            {
                ApplyLine(line, Enum.Parse<ItemTrackingMode>(line.TrackingMode), entry.Count, entry.Field);
                continue;
            }
            if (line.BookQuantity == 0) { stocktake.Lines.Remove(line); continue; }
            line.CountedQuantity = null;
            foreach (var tracking in line.Tracking.ToList())
            {
                if (tracking.BookQuantity == 0) line.Tracking.Remove(tracking);
                else tracking.CountedQuantity = null;
            }
        }
        var nextLineNo = stocktake.Lines.Select(x => x.LineNo).DefaultIfEmpty().Max();
        foreach (var (count, field) in added)
        {
            var mode = modes[(count.ItemId!.Value, count.WarehouseId!.Value, count.LocationId)];
            var line = new StocktakeLine { LineNo = ++nextLineNo, ItemId = count.ItemId.Value, WarehouseId = count.WarehouseId.Value,
                LocationId = count.LocationId, TrackingMode = mode.ToString(), IsAdded = true };
            ApplyLine(line, mode, count, field);
            stocktake.Lines.Add(line);
        }
    }

    /// <summary>套用一行實盤：批序號種類須符合追蹤方式；行已盤時，全部批序號都須已盤且合計等於行實盤。</summary>
    private static void ApplyLine(StocktakeLine line, ItemTrackingMode mode, StocktakeCountLine count, string field)
    {
        var tracking = count.Tracking ?? [];
        switch (mode)
        {
            case ItemTrackingMode.None when tracking.Count > 0:
                throw new RequestValidationException($"{field}.tracking", "Item is not lot or serial tracked.");
            case ItemTrackingMode.Lot when tracking.Any(x => x.SerialNo is not null):
                throw new RequestValidationException($"{field}.tracking", "Item is lot tracked; use lot numbers.");
            case ItemTrackingMode.Serial when tracking.Any(x => x.LotNo is not null):
                throw new RequestValidationException($"{field}.tracking", "Item is serial tracked; use serial numbers.");
            case ItemTrackingMode.Serial when count.CountedQuantity is { } q && decimal.Truncate(q) != q:
                throw new RequestValidationException($"{field}.countedQuantity", "Serial-tracked quantity must be a whole number.");
        }
        var byNumber = tracking.ToDictionary(x => StockTrackingRules.Normalize(x.LotNo ?? x.SerialNo!));
        var nextSeq = line.Tracking.Select(x => x.SeqNo).DefaultIfEmpty().Max();
        foreach (var entry in line.Tracking.ToList())
        {
            if (byNumber.Remove(StockTrackingRules.Normalize(entry.LotNo ?? entry.SerialNo!), out var match))
                entry.CountedQuantity = match.CountedQuantity;
            else if (entry.BookQuantity == 0) line.Tracking.Remove(entry);
            else entry.CountedQuantity = null;
        }
        foreach (var entry in tracking.Where(x => byNumber.ContainsKey(StockTrackingRules.Normalize(x.LotNo ?? x.SerialNo!))))
            line.Tracking.Add(new StocktakeLineTracking { SeqNo = ++nextSeq, LotNo = entry.LotNo?.Trim(),
                SerialNo = entry.SerialNo?.Trim(), CountedQuantity = entry.CountedQuantity });
        if (count.CountedQuantity is { } counted && mode != ItemTrackingMode.None
            && (line.Tracking.Any(x => x.CountedQuantity is null) || line.Tracking.Sum(x => x.CountedQuantity ?? 0m) != counted))
            throw new RequestValidationException($"{field}.countedQuantity",
                "Counted quantity must equal the total counted lot or serial numbers, and every number must be counted.");
        line.CountedQuantity = count.CountedQuantity;
    }

    private void CheckLimits(List<StocktakeLine> lines)
    {
        if (lines.Count > limits.MaxLines || lines.Sum(x => x.Tracking.Count) > limits.MaxTrackingEntries) throw LimitExceeded();
    }

    private BusinessRuleException LimitExceeded() => new(TooManyLines,
        $"A stocktake is limited to {limits.MaxLines} lines and {limits.MaxTrackingEntries} lot or serial numbers; narrow the scope.");

    private Task<int> LockAsync(long companyId, CancellationToken token) => db.Database.ExecuteSqlAsync($"""
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = {"inv_stocktake:" + companyId}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
        IF @result < 0 THROW 51000, 'Stocktake lock was not granted.', 1;
        """, token);

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            var result = await action();
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    private async Task<Stocktake> LoadAsync(long id, int version, CancellationToken token)
    {
        var stocktake = await WithLines(db.Set<Stocktake>()).SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing(id);
        if (version < 1 || stocktake.Version != version) throw new ConflictException();
        return stocktake;
    }

    private long RequireCompany() => db.CurrentCompanyId is long id && id > 0 ? id :
        throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static (string? Remark, StocktakeScope Scope) Validate(StocktakeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        var remark = Remark(input.Remark, Fail);
        var warehouses = Ids(input.Scope?.WarehouseIds);
        var categories = Ids(input.Scope?.CategoryIds);
        var items = Ids(input.Scope?.ItemIds);
        if (warehouses is not { Count: > 0 and <= MaxWarehouses })
            Fail("scope.warehouseIds", $"Choose 1 to {MaxWarehouses} warehouses.");
        if (categories is null || categories.Count > MaxCategories)
            Fail("scope.categoryIds", $"Choose at most {MaxCategories} item categories.");
        if (items is null || items.Count > MaxItems) Fail("scope.itemIds", $"Choose at most {MaxItems} items.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, new StocktakeScope(warehouses, categories, items));

        static List<long>? Ids(IReadOnlyList<long>? ids) =>
            ids is null ? [] : ids.Any(x => x <= 0) ? null : [.. ids.Distinct().Order()];
    }

    private static (string? Remark, IReadOnlyList<StocktakeCountLine> Lines) Validate(StocktakeCountInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        var remark = Remark(input.Remark, Fail);
        var lines = input.Lines ?? [];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var field = $"lines[{i}]";
            if (line is null) { Fail(field, "Line is required."); continue; }
            if (line.LineId is <= 0) Fail($"{field}.lineId", "Line ID must be positive.");
            if (line.LineId is null)
            {
                if (line.ItemId is not > 0) Fail($"{field}.itemId", "Item is required for an added line.");
                if (line.WarehouseId is not > 0) Fail($"{field}.warehouseId", "Warehouse is required for an added line.");
                if (line.LocationId is <= 0) Fail($"{field}.locationId", "Location must be positive.");
            }
            if (line.CountedQuantity is { } counted && !IsCount(counted))
                Fail($"{field}.countedQuantity", "Counted quantity must be 0 or more, below 10^13 and have at most 6 decimals.");
            var tracking = line.Tracking ?? [];
            for (var j = 0; j < tracking.Count; j++)
            {
                var entry = tracking[j];
                var at = $"{field}.tracking[{j}]";
                if (entry is null) { Fail(at, "Lot or serial entry is required."); continue; }
                if ((entry.LotNo is not null) == (entry.SerialNo is not null))
                    Fail(at, "Entry must have either a lot number or a serial number.");
                else if (entry.LotNo is not null && entry.LotNo.Trim() is not { Length: > 0 and <= StockLot.MaxLength })
                    Fail($"{at}.lotNo", $"Lot number must contain 1 to {StockLot.MaxLength} characters.");
                else if (entry.SerialNo is not null && entry.SerialNo.Trim() is not { Length: > 0 and <= StockSerial.MaxLength })
                    Fail($"{at}.serialNo", $"Serial number must contain 1 to {StockSerial.MaxLength} characters.");
                if (entry.CountedQuantity is { } q && !IsCount(q))
                    Fail($"{at}.countedQuantity", "Counted quantity must be 0 or more, below 10^13 and have at most 6 decimals.");
                else if (entry.SerialNo is not null && entry.CountedQuantity is not (null or 0m or 1m))
                    Fail($"{at}.countedQuantity", "A serial number is counted as 0 or 1.");
            }
            var present = tracking.Where(x => x is not null && (x.LotNo ?? x.SerialNo) is not null).ToList();
            if (present.Any(x => x.LotNo is not null) && present.Any(x => x.SerialNo is not null))
                Fail($"{field}.tracking", "A line cannot mix lot and serial numbers.");
            if (present.GroupBy(x => StockTrackingRules.Normalize(x.LotNo ?? x.SerialNo!)).Any(g => g.Count() > 1))
                Fail($"{field}.tracking", "Lot or serial numbers must be unique within a line.");
        }
        var ids = lines.Where(x => x?.LineId is not null).Select(x => x.LineId!.Value).ToList();
        if (ids.Count != ids.Distinct().Count()) Fail("lines", "Each line can appear only once.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, lines);

        static bool IsCount(decimal value) => value >= 0 && value < MaxQuantity && decimal.Round(value, 6) == value;
    }

    private static string? Remark(string? value, Action<string, string> fail)
    {
        var remark = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (remark?.Length > 1000) fail("remark", "Remark must not exceed 1000 characters.");
        return remark;
    }
}
