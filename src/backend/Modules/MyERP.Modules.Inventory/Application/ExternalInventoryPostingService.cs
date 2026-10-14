using MyERP.Modules.Inventory.Contracts;
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

    private void RequireCallerTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new BusinessRuleException("INV.POSTING.TRANSACTION_REQUIRED",
                "External inventory posting requires the caller's approval transaction.");
    }
}
