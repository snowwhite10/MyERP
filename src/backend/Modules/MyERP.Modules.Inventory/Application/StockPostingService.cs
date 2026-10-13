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
/// 庫存唯一寫入入口（ADR-053）。鎖順序固定：來源排他鎖 → MD 共享鎖（逐行引用）→ 出庫結存鍵排他鎖（排序後取得），
/// 避免不同過帳之間死結；全部鎖由交易持有至提交。
/// </summary>
public sealed partial class StockPostingService(MyErpDbContext db, InventoryReferenceService references, IClock clock)
    : IInventoryPostingService
{
    public const int MaxLines = 1000;
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
                if (!existing.Select(Snapshot).SequenceEqual(lines.Select(x => Snapshot(x, request.BusinessDate))))
                    throw new ConflictException("INV.POSTING.CONFLICT", "Source was already posted with different lines.");
                return new InventoryPostingResult(true, [.. existing.Select(x => x.Id)]);
            }

            var movements = new List<StockMovement>(lines.Count);
            foreach (var line in lines)
            {
                var reference = await references.RecordAsync(
                    $"STOCK:{request.SourceType}:{request.SourceId}:{line.LineNo}:{line.Direction}",
                    line.ItemId, line.WarehouseId, line.LocationId, token);
                added.Add(reference);
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
            db.AddRange(movements);
            added.AddRange(movements);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            { throw new ConflictException("INV.POSTING.CONFLICT", "Source was already posted."); }
            if (transaction is not null) await transaction.CommitAsync(token);
            return new InventoryPostingResult(false, [.. movements.Select(x => x.Id)]);
        }
        catch
        {
            // 自行開啟的交易回滾後，已追蹤的引用／異動不可留在共用 DbContext 被下一次 SaveChanges 誤寫
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
            }
            if (request.Lines.Where(x => x is not null).GroupBy(x => (x.LineNo, x.Direction)).Any(g => g.Count() > 1))
                Fail("lines", "Line number and direction must be unique within a source.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private static bool IsStorable(decimal value) => value > -MaxValue && value < MaxValue && decimal.Round(value, 6) == value;

    private static StockMovementDirection ToDomain(StockDirection direction) =>
        direction == StockDirection.In ? StockMovementDirection.In : StockMovementDirection.Out;

    private static LineSnapshot Snapshot(StockMovement x) => new(x.SourceLineNo, x.Direction, x.ItemId, x.WarehouseId,
        x.LocationId, x.Quantity, x.UnitCost, x.BusinessDate);

    private static LineSnapshot Snapshot(InventoryPostingLine x, DateOnly date) => new(x.LineNo, ToDomain(x.Direction), x.ItemId,
        x.WarehouseId, x.LocationId, x.Quantity, x.UnitCost, date);

    private sealed record LineSnapshot(int LineNo, StockMovementDirection Direction, long ItemId, long WarehouseId,
        long? LocationId, decimal Quantity, decimal? UnitCost, DateOnly BusinessDate);

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,29}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceTypePattern();
}
