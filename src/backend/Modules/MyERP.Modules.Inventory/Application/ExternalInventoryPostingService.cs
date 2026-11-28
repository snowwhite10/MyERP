using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Application;

/// <summary>PUR／SD 專用過帳入口，來源型別與方向由 INV 固定（ADR-064）。</summary>
public sealed class ExternalInventoryPostingService(MyErpDbContext db, IInventoryPostingService posting)
    : IExternalInventoryPostingService
{
    public Task<InventoryPostingResult> PostPurchaseInboundAsync(long sourceId, DateOnly businessDate,
        IReadOnlyList<PurchaseInboundLine> lines, CancellationToken token = default)
    {
        RequireCallerTransaction();
        ArgumentNullException.ThrowIfNull(lines);
        return posting.PostAsync(new InventoryPostingRequest(ExternalInventorySourceTypes.PurchaseInbound, sourceId, businessDate,
            lines.Select(x => new InventoryPostingLine(x.LineNo, StockDirection.In, x.ItemId, x.WarehouseId,
                x.LocationId, x.Quantity, x.UnitCost, x.Tracking)).ToArray()), token);
    }

    public Task<InventoryPostingResult> PostSalesOutboundAsync(long sourceId, DateOnly businessDate,
        IReadOnlyList<SalesOutboundLine> lines, CancellationToken token = default)
    {
        RequireCallerTransaction();
        ArgumentNullException.ThrowIfNull(lines);
        return posting.PostAsync(new InventoryPostingRequest(ExternalInventorySourceTypes.SalesOutbound, sourceId, businessDate,
            lines.Select(x => new InventoryPostingLine(x.LineNo, StockDirection.Out, x.ItemId, x.WarehouseId,
                x.LocationId, x.Quantity, null, x.Tracking)).ToArray()), token);
    }

    /// <summary>
    /// ADR-188：鎖物料估值（升冪，與 <see cref="StockPostingService"/> 同序）→ 讀並驗證原出庫 → 以原出庫前的移動平均單價入庫。
    /// 原出庫之前的異動只增不改，故重放算出的成本與首次相同，可沿用一般過帳的快照比對做冪等。
    /// </summary>
    public async Task<SalesReturnInboundResult> PostSalesReturnInboundAsync(long sourceId, DateOnly businessDate,
        IReadOnlyList<SalesReturnInboundLine> lines, CancellationToken token = default)
    {
        RequireCallerTransaction();
        ValidateReturn(sourceId, lines);
        if (db.CurrentCompanyId is not long companyId || companyId <= 0)
            throw new BusinessRuleException("COMMON.COMPANY.REQUIRED", "Company is required.");
        var ordered = lines.OrderBy(x => x.LineNo).ToList();

        foreach (var itemId in ordered.Select(x => x.ItemId).Distinct().Order())
            await AssemblyCostSnapshot.LockItemAsync(db, companyId, itemId, token);

        var originalIds = ordered.Select(x => x.OriginalMovementId).Distinct().ToList();
        var originals = await db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && originalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, token);
        Refuse("INV.SALES_RETURN.ORIGINAL_NOT_FOUND", "Original sales issue was not found.",
            ordered.Where(x => !originals.ContainsKey(x.OriginalMovementId)));
        Refuse("INV.SALES_RETURN.ORIGINAL_NOT_ISSUE", "Original movement is not a sales issue.",
            ordered.Where(x => originals[x.OriginalMovementId] is var o
                && (o.SourceType != ExternalInventorySourceTypes.SalesOutbound || o.Direction != StockMovementDirection.Out)));
        Refuse("INV.SALES_RETURN.ITEM_MISMATCH", "Returned item differs from the original sales issue.",
            ordered.Where(x => originals[x.OriginalMovementId].ItemId != x.ItemId));
        Refuse("INV.SALES_RETURN.QUANTITY_EXCEEDED", "Returned quantity exceeds the original sales issue.",
            ordered.GroupBy(x => x.OriginalMovementId)
                .Where(g => g.Sum(x => x.Quantity) > originals[g.Key].Quantity).SelectMany(g => g));

        var valuation = new InventoryValuationQuery(db);
        var costs = new Dictionary<long, decimal?>();
        foreach (var id in originalIds.Order())
            costs[id] = await valuation.GetIssueUnitCostAsync(id, token);

        var result = await posting.PostAsync(new InventoryPostingRequest(ExternalInventorySourceTypes.SalesReturn, sourceId,
            businessDate, ordered.Select(x => new InventoryPostingLine(x.LineNo, StockDirection.In, x.ItemId, x.WarehouseId,
                x.LocationId, x.Quantity, costs[x.OriginalMovementId])).ToArray()), token);
        return new SalesReturnInboundResult(result.Replayed, ordered.Select((x, i) => new SalesReturnInboundMovement(
            x.LineNo, result.MovementIds[i], x.OriginalMovementId, costs[x.OriginalMovementId])).ToArray());
    }

    /// <summary>
    /// ADR-223：鎖物料估值（升冪）→ 讀並驗證原入庫及其批序號 → 以一般出庫過帳（不帶成本、檢查目前結存與批序號可用量）。
    /// 原入庫異動與其批序號只增不改，故重放時驗證結果與回傳成本與首次相同。
    /// </summary>
    public async Task<PurchaseReturnOutboundResult> PostPurchaseReturnAsync(long sourceId, DateOnly businessDate,
        IReadOnlyList<PurchaseReturnOutboundLine> lines, CancellationToken token = default)
    {
        RequireCallerTransaction();
        ValidateReturnShape(sourceId, lines?.Select(x => x is null ? null
            : new ReturnShape(x.LineNo, x.OriginalMovementId, x.ItemId, x.WarehouseId, x.Quantity)).ToList(), "Original purchase receipt");
        if (db.CurrentCompanyId is not long companyId || companyId <= 0)
            throw new BusinessRuleException("COMMON.COMPANY.REQUIRED", "Company is required.");
        var ordered = lines!.OrderBy(x => x.LineNo).ToList();

        foreach (var itemId in ordered.Select(x => x.ItemId).Distinct().Order())
            await AssemblyCostSnapshot.LockItemAsync(db, companyId, itemId, token);

        var originalIds = ordered.Select(x => x.OriginalMovementId).Distinct().ToList();
        var originals = await db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && originalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, token);
        Refuse("INV.PURCHASE_RETURN.ORIGINAL_NOT_FOUND", "Original purchase receipt was not found.",
            ordered.Where(x => !originals.ContainsKey(x.OriginalMovementId)).Select(x => x.LineNo));
        Refuse("INV.PURCHASE_RETURN.ORIGINAL_NOT_RECEIPT", "Original movement is not a purchase receipt.",
            ordered.Where(x => originals[x.OriginalMovementId] is var o
                && (o.SourceType != ExternalInventorySourceTypes.PurchaseInbound || o.Direction != StockMovementDirection.In
                    || o.UnitCost is null)).Select(x => x.LineNo));
        Refuse("INV.PURCHASE_RETURN.ITEM_MISMATCH", "Returned item differs from the original purchase receipt.",
            ordered.Where(x => originals[x.OriginalMovementId].ItemId != x.ItemId).Select(x => x.LineNo));
        Refuse("INV.PURCHASE_RETURN.QUANTITY_EXCEEDED", "Returned quantity exceeds the original purchase receipt.",
            ordered.GroupBy(x => x.OriginalMovementId)
                .Where(g => g.Sum(x => x.Quantity) > originals[g.Key].Quantity).SelectMany(g => g).Select(x => x.LineNo));
        await RefuseForeignTrackingAsync(ordered, token);

        var result = await posting.PostAsync(new InventoryPostingRequest(ExternalInventorySourceTypes.PurchaseReturn, sourceId,
            businessDate, ordered.Select(x => new InventoryPostingLine(x.LineNo, StockDirection.Out, x.ItemId, x.WarehouseId,
                x.LocationId, x.Quantity, null, x.Tracking)).ToArray()), token);
        return new PurchaseReturnOutboundResult(result.Replayed, ordered.Select((x, i) => new PurchaseReturnOutboundMovement(
            x.LineNo, result.MovementIds[i], x.OriginalMovementId, originals[x.OriginalMovementId].UnitCost!.Value)).ToArray());
    }

    /// <summary>
    /// 退貨批號：同一原入庫、同一批號（不分大小寫）的本次合計不得超過原入庫該批數量；序號須曾在原入庫。
    /// 追蹤方式、完整性及目前可用量由 <see cref="StockPostingService"/> 檢查。
    /// </summary>
    private async Task RefuseForeignTrackingAsync(IReadOnlyList<PurchaseReturnOutboundLine> lines, CancellationToken token)
    {
        var tracked = lines.Where(x => x.Tracking is { Count: > 0 }).ToList();
        if (tracked.Count == 0) return;
        var ids = tracked.Select(x => x.OriginalMovementId).Distinct().ToList();
        var received = await db.Set<StockMovementTracking>().AsNoTracking()
            .Where(x => ids.Contains(x.MovementId))
            .Select(x => new { x.MovementId, LotNo = x.Lot!.LotNo, SerialNo = x.Serial!.SerialNo, x.Quantity })
            .ToListAsync(token);
        var lots = received.Where(x => x.LotNo is not null)
            .GroupBy(x => (x.MovementId, StockTrackingRules.Normalize(x.LotNo!)))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var serials = received.Where(x => x.SerialNo is not null)
            .Select(x => (x.MovementId, StockTrackingRules.Normalize(x.SerialNo!))).ToHashSet();

        var entries = tracked.SelectMany(line => line.Tracking!.Where(t => t is not null).Select(t => (Line: line, Entry: t))).ToList();
        // 兩者皆空或皆有的形狀錯誤留給一般過帳回 400。
        var refused = entries.Where(x => x.Entry.LotNo is null && x.Entry.SerialNo is not null
                && !serials.Contains((x.Line.OriginalMovementId, StockTrackingRules.Normalize(x.Entry.SerialNo))))
            .Select(x => x.Line.LineNo)
            .Concat(entries.Where(x => x.Entry.LotNo is not null)
                .GroupBy(x => (x.Line.OriginalMovementId, StockTrackingRules.Normalize(x.Entry.LotNo!)))
                .Where(g => g.Sum(x => x.Entry.Quantity) > lots.GetValueOrDefault(g.Key))
                .SelectMany(g => g.Select(x => x.Line.LineNo)));
        Refuse("INV.PURCHASE_RETURN.TRACKING_NOT_FROM_ORIGINAL",
            "Returned lot or serial numbers were not received on the original purchase receipt.", refused);
    }

    private static void Refuse(string code, string message, IEnumerable<SalesReturnInboundLine> lines) =>
        Refuse(code, message, lines.Select(x => x.LineNo));

    private static void Refuse(string code, string message, IEnumerable<int> lineNos)
    {
        var numbers = lineNos.ToList();
        if (numbers.Count > 0)
            throw new BusinessRuleException(code, message) { Extensions = StockLineRefusal.Lines(numbers) };
    }

    private sealed record ReturnShape(int LineNo, long OriginalMovementId, long ItemId, long WarehouseId, decimal Quantity);

    private static void ValidateReturn(long sourceId, IReadOnlyList<SalesReturnInboundLine> lines) =>
        ValidateReturnShape(sourceId, lines?.Select(x => x is null ? null
            : new ReturnShape(x.LineNo, x.OriginalMovementId, x.ItemId, x.WarehouseId, x.Quantity)).ToList(), "Original sales issue");

    /// <summary>形狀檢查先於任何 SQL；其餘欄位（儲位、數量精度、批序號形狀）由 <see cref="StockPostingService"/> 驗證。</summary>
    private static void ValidateReturnShape(long sourceId, IReadOnlyList<ReturnShape?>? lines, string original)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (sourceId <= 0) Fail("sourceId", "Source id must be positive.");
        if (lines is not { Count: > 0 and <= StockPostingService.MaxLines })
        {
            Fail("lines", $"Posting must contain 1 to {StockPostingService.MaxLines} lines.");
        }
        else
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var field = $"lines[{i}]";
                if (line is null) { Fail(field, "Line is required."); continue; }
                if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
                if (line.OriginalMovementId <= 0) Fail($"{field}.originalMovementId", $"{original} is required.");
                if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
                if (line.WarehouseId <= 0) Fail($"{field}.warehouseId", "Warehouse is required.");
                if (line.Quantity <= 0) Fail($"{field}.quantity", "Quantity must be positive.");
            }
            if (lines.Where(x => x is not null).GroupBy(x => x!.LineNo).Any(g => g.Count() > 1))
                Fail("lines", "Line number must be unique within a source.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private void RequireCallerTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new BusinessRuleException("INV.POSTING.TRANSACTION_REQUIRED",
                "External inventory posting requires the caller's approval transaction.");
    }
}
