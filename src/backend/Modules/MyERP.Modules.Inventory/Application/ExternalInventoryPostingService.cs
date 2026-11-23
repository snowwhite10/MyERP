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

    private static void Refuse(string code, string message, IEnumerable<SalesReturnInboundLine> lines)
    {
        var lineNos = lines.Select(x => x.LineNo).ToList();
        if (lineNos.Count > 0)
            throw new BusinessRuleException(code, message) { Extensions = StockLineRefusal.Lines(lineNos) };
    }

    /// <summary>形狀檢查先於任何 SQL；其餘欄位（物料、倉庫、數量精度）由 <see cref="StockPostingService"/> 驗證。</summary>
    private static void ValidateReturn(long sourceId, IReadOnlyList<SalesReturnInboundLine> lines)
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
                if (line.OriginalMovementId <= 0) Fail($"{field}.originalMovementId", "Original sales issue is required.");
                if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
                if (line.WarehouseId <= 0) Fail($"{field}.warehouseId", "Warehouse is required.");
                if (line.Quantity <= 0) Fail($"{field}.quantity", "Quantity must be positive.");
            }
            if (lines.Where(x => x is not null).GroupBy(x => x.LineNo).Any(g => g.Count() > 1))
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
