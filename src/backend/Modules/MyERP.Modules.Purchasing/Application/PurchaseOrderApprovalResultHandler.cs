using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// REQ_F4114.1.4／1.5、BR-PUR-024（WU-PUR-006b2）：SYS 最終決定在 SYS 的同一交易與共用 DbContext 內改採購單狀態。
/// 核准保留 SUBMIT 時的請購消耗；駁回按已保存訂單行聚合、以請購行 ID 升冪 UPDLOCK 逐行鎖定後一次釋回並清除消耗旗標。
/// 訂單已不在待審或旗標不符（重複／過時決定）回 409，不做第二次釋回；釋回後低於 0 視為資料不一致，同樣 409 整筆回滾。
/// 多級審批的中間核准不會呼叫本處理器，因此不會重複消耗。
/// </summary>
public sealed class PurchaseOrderApprovalResultHandler(MyErpDbContext db) : IApprovalResultHandler
{
    public const string StateChanged = "PUR.ORDER.APPROVAL_STATE_CHANGED";
    public const string ReleaseInvalid = "PUR.ORDER.REQUISITION_RELEASE_INVALID";

    public string DocumentType => PurchaseOrderSubmissionService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));

        // Same lock order as SUBMIT (order first, then requisition lines by ascending ID) so the two cannot deadlock.
        await db.Database.SqlQuery<long>(
                $"SELECT [id] AS [Value] FROM dbo.pur_purchase_order WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {result.DocumentId}")
            .ToListAsync(cancellationToken);
        var order = await db.Set<PurchaseOrderSource>().SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException("PUR.ORDER.NOT_FOUND", "Purchase order", result.DocumentId);
        if (order.Status != "PendingApproval" || !order.RequisitionConsumptionActive)
            throw new ConflictException(StateChanged, "The purchase order is no longer waiting for approval.");

        if (result.Status == ApprovalStatus.Approved)
        {
            order.Status = "Approved";
            return;
        }

        await ReleaseAsync(order, cancellationToken);
        order.RequisitionConsumptionActive = false;
        order.Status = "Rejected";
    }

    /// <summary>按本單已保存行聚合每條請購行的引用量並一次扣回已轉量；任何一行不一致即拋出，不做部分釋回。</summary>
    private async Task ReleaseAsync(PurchaseOrderSource order, CancellationToken token)
    {
        var demand = await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            .Where(x => x.CompanyId == order.CompanyId && x.PurchaseOrderId == order.Id && x.RequisitionLineId != null)
            .GroupBy(x => x.RequisitionLineId!.Value)
            .Select(g => new { SourceId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .OrderBy(x => x.SourceId)
            .ToListAsync(token);
        if (demand.Count == 0) return;

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
}
