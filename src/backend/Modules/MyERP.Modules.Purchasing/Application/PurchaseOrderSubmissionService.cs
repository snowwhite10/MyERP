using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>SUBMIT 結果：保存後的訂單狀態／版本與 SYS 審批實例狀態（零步驟時為 Approved）。</summary>
public sealed record PurchaseOrderSubmissionResult(long Id, string DocumentNo, string Status, int Version,
    ApprovalState Approval);

/// <summary>請購來源不足的一行：本單引用量與送出時的可用量（核准量 − 已轉量）。</summary>
public sealed record PurchaseOrderRequisitionShortage(long RequisitionLineId, IReadOnlyList<int> OrderLineNos,
    decimal Requested, decimal Available);

/// <summary>
/// REQ_F4114.1.4／BR-PUR-023（WU-PUR-006b1、ADR-214）：送審只認已保存的訂單行。先以 UPDLOCK 鎖訂單，再按 ID 升冪
/// 鎖全部來源請購行，聚合同單多行引用量後驗證同公司已核准、物料相符及餘量；同一交易增加 <c>ordered_quantity</c>、
/// 設消耗旗標、改狀態並建立 SYS 審批實例。零步驟立即核准仍只消耗一次；任何一行失敗整單零變更。
/// </summary>
public sealed class PurchaseOrderSubmissionService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IRequisitionItemGuard items, ISupplierLookup suppliers,
    IApprovalWorkflowService workflow)
{
    public const string DocumentType = RfqConversionService.DocumentType;

    public async Task<PurchaseOrderSubmissionResult> SubmitAsync(long id, int version,
        CancellationToken token = default)
    {
        if (id <= 0) throw new RequestValidationException("id", "Purchase order ID must be positive.");
        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        var scope = user is not null && user.CompanyId == companyId && user.Has(PurchasingPermissions.OrderSubmit)
            ? user.FilterFor(PurchasingPermissions.OrderSubmit) : null;
        if (scope is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase order submit permission is required.");

        // ReadCommitted + explicit UPDLOCK/HOLDLOCK: the order row and every source requisition line are held until
        // commit, so a competing SUBMIT waits and then re-reads the consumed quantity. Serializable range locks are not
        // needed and would make the SYS instance insert prone to deadlocks between unrelated orders (ADR-214).
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.pur_purchase_order WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1}", id, companyId).ToListAsync(token);
            IQueryable<PurchaseOrderSource> query = db.Set<PurchaseOrderSource>().Where(x => x.CompanyId == companyId);
            query = scope.EffectiveScope switch
            {
                DataScope.Company => query,
                DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
                DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
                _ => query.Where(x => x.OwnerId == scope.UserId)
            };
            var order = await query.SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("PUR.ORDER.NOT_FOUND", "Purchase order", id);
            if (version < 1 || order.Version != version) throw new ConflictException();
            if (order.Status != "Draft")
                throw new BusinessRuleException("PUR.ORDER.NOT_SUBMITTABLE",
                    "Only draft purchase orders can be submitted; revise a rejected order first.");
            if (order.DocumentNo is null || order.BaseGross is null)
                throw new BusinessRuleException("PUR.ORDER.NOT_SUBMITTABLE",
                    "The purchase order has no saved number or amount snapshot.");
            var lines = await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.PurchaseOrderId == id)
                .OrderBy(x => x.LineNo).ToListAsync(token);
            if (lines.Count == 0)
                throw new BusinessRuleException("PUR.ORDER.EMPTY", "Purchase order must contain a line.");

            await suppliers.RequireApprovedAsync(order.SupplierId, token);
            var result = await items.RunAsync(lines.Select(x => x.MaterialId).Distinct().ToArray(), async () =>
            {
                await ConsumeAsync(companyId, lines, token);
                order.Status = "PendingApproval";
                order.RequisitionConsumptionActive = true;
                // SYS saves through the shared DbContext, so the order and requisition changes commit with the instance.
                var approval = await workflow.SubmitAsync(new ApprovalSubmission(DocumentType, order.Id,
                    order.DocumentNo, order.BaseGross.Value, order.Remark is { Length: > 200 } remark
                        ? remark[..200] : order.Remark), token);
                if (approval.Status == ApprovalStatus.Approved)
                {
                    order.Status = "Approved";
                    await db.SaveChangesAsync(token);
                }
                return new PurchaseOrderSubmissionResult(order.Id, order.DocumentNo, order.Status, order.Version,
                    approval);
            }, token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: SqlException sql }
                && sql.Number is 2601 or 2627) throw new ConflictException();
            throw;
        }
    }

    /// <summary>鎖定、驗證並一次增加全部來源請購行的已轉量；任何一行無效或不足即拋出，不做部分更新。</summary>
    private async Task ConsumeAsync(long companyId, IReadOnlyList<PurchaseOrderSourceLine> lines,
        CancellationToken token)
    {
        var demand = lines.Where(x => x.RequisitionLineId.HasValue)
            .GroupBy(x => x.RequisitionLineId!.Value)
            .OrderBy(x => x.Key)
            .Select(g => (SourceId: g.Key, Quantity: g.Sum(x => x.Quantity), Lines: g.ToArray()))
            .ToArray();
        if (demand.Length == 0) return;

        var ids = demand.Select(x => x.SourceId).ToArray();
        // U locks are taken one row at a time in ascending ID order, so concurrent submitters cannot deadlock each other.
        foreach (var sourceId in ids)
            await db.Database.SqlQuery<long>(
                $"SELECT [id] AS [Value] FROM dbo.pur_requisition_line WITH (UPDLOCK, HOLDLOCK) WHERE [company_id] = {companyId} AND [id] = {sourceId}")
                .ToListAsync(token);
        var sources = await db.Set<RequisitionLine>().Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, token);
        var parentIds = sources.Values.Select(x => x.RequisitionId).Distinct().ToArray();
        var parents = await db.Set<Requisition>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && parentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Status, x.IsDeleted }, token);

        var invalid = new List<int>();
        var shortages = new List<PurchaseOrderRequisitionShortage>();
        foreach (var (sourceId, quantity, orderLines) in demand)
        {
            if (!sources.TryGetValue(sourceId, out var source) ||
                !parents.TryGetValue(source.RequisitionId, out var parent) || parent.IsDeleted ||
                parent.Status != RequisitionStatus.Approved || orderLines.Any(x => x.MaterialId != source.ItemId))
            {
                invalid.AddRange(orderLines.Select(x => x.LineNo));
                continue;
            }
            var available = source.Quantity - source.OrderedQuantity;
            if (quantity > available)
                shortages.Add(new PurchaseOrderRequisitionShortage(sourceId,
                    orderLines.Select(x => x.LineNo).ToArray(), quantity, available));
        }
        if (invalid.Count > 0)
            throw new BusinessRuleException("PUR.ORDER.INVALID_REQUISITION_SOURCE",
                $"Lines {string.Join(", ", invalid.Order())} must reference an approved, same-company requisition line for the same item.")
            {
                Extensions = new Dictionary<string, object?> { ["lineNos"] = invalid.Order().ToArray() }
            };
        if (shortages.Count > 0)
            throw new BusinessRuleException("PUR.ORDER.REQUISITION_INSUFFICIENT",
                "Requested quantity exceeds the remaining approved requisition quantity.")
            {
                Extensions = new Dictionary<string, object?> { ["shortages"] = shortages.ToArray() }
            };
        foreach (var (sourceId, quantity, _) in demand)
            sources[sourceId].OrderedQuantity += quantity;
    }
}
