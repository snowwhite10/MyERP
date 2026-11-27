using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// BR-PUR-024／025：駁回（SYS 決定）與 CANCEL 共用的請購量釋回。呼叫端須已在同一交易內 UPDLOCK 訂單列；本處按已保存
/// 訂單行聚合每條請購行的引用量，以請購行 ID 升冪逐行 UPDLOCK 後一次扣回已轉量並清除消耗旗標。任何一行不足扣回
/// （資料不一致）即拋 409 <see cref="ReleaseInvalid"/>，不做部分釋回，由呼叫端整筆回滾。
/// </summary>
internal static class PurchaseOrderRequisitionRelease
{
    public const string ReleaseInvalid = "PUR.ORDER.REQUISITION_RELEASE_INVALID";

    public static async Task ReleaseAsync(MyErpDbContext db, PurchaseOrderSource order, CancellationToken token)
    {
        var demand = await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            .Where(x => x.CompanyId == order.CompanyId && x.PurchaseOrderId == order.Id && x.RequisitionLineId != null)
            .GroupBy(x => x.RequisitionLineId!.Value)
            .Select(g => new { SourceId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .OrderBy(x => x.SourceId)
            .ToListAsync(token);
        if (demand.Count > 0)
        {
            // Same ascending lock order as SUBMIT so releases and consumptions cannot deadlock each other.
            foreach (var item in demand)
                await db.Database.SqlQuery<long>(
                        $"SELECT [id] AS [Value] FROM dbo.pur_requisition_line WITH (UPDLOCK, HOLDLOCK) WHERE [company_id] = {order.CompanyId} AND [id] = {item.SourceId}")
                    .ToListAsync(token);
            var ids = demand.Select(x => x.SourceId).ToArray();
            var sources = await db.Set<RequisitionLine>().IgnoreQueryFilters()
                .Where(x => x.CompanyId == order.CompanyId && ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, token);
            foreach (var item in demand)
            {
                if (!sources.TryGetValue(item.SourceId, out var source) || source.OrderedQuantity < item.Quantity)
                    throw new ConflictException(ReleaseInvalid,
                        "The requisition quantity consumed by this purchase order cannot be released consistently.");
            }
            foreach (var item in demand)
                sources[item.SourceId].OrderedQuantity -= item.Quantity;
        }
        order.RequisitionConsumptionActive = false;
    }
}
