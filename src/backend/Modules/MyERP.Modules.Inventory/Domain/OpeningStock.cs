using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

/// <summary>
/// 期初庫存批次（inv_opening_stock，REQ_DS43.140，ADR-059）。提交即在同一交易以 <c>OPENING_STOCK</c> 入庫過帳；
/// 不提供修改或刪除，輸入錯誤以其他入庫／出庫調整。<see cref="RequestKey"/> 在公司內唯一，供重送冪等。
/// </summary>
public sealed class OpeningStock : BusinessEntity
{
    public required string RequestKey { get; init; }
    public DateOnly OpeningDate { get; init; }
    public string? Remark { get; init; }
    public string? CustomFields { get; set; }
    public DateTime PostedAt { get; init; }
    public List<OpeningStockLine> Lines { get; } = [];

    /// <summary>批次金額 = 明細金額加總（不另存）。</summary>
    public decimal TotalAmount => Lines.Sum(x => x.Amount);
}

/// <summary>
/// 期初明細（inv_opening_stock_line，REQ_DS43.141）：每個公司的物料＋倉庫＋儲位只能有一行期初；
/// 數量為基本單位，金額 = 數量 × 單價在明細行取 4 位小數。
/// </summary>
public sealed class OpeningStockLine : BusinessEntity
{
    public long OpeningStockId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Amount { get; set; }
}
