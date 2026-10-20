using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record RequisitionSubmissionResult(RequisitionDetail Requisition, ApprovalState Approval);

/// <summary>REQ_F4114.3.4：在同一資料庫交易中送審請購與更新單據狀態。</summary>
public sealed class RequisitionSubmissionService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IRequisitionItemGuard items, IApprovalWorkflowService workflow)
{
    public const string DocumentType = "PUR.REQUISITION";

    public async Task<RequisitionSubmissionResult> SubmitAsync(long id, int version,
        CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var owner = await authorization.GetAsync(token);
        if (owner is null || owner.CompanyId != companyId || !owner.Has(PurchasingPermissions.RequisitionSubmit))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Requisition submit permission is required.");
        var scope = owner.FilterFor(PurchasingPermissions.RequisitionSubmit)
            ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Requisition submit scope is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            IQueryable<Requisition> query = db.Set<Requisition>();
            query = scope.EffectiveScope switch
            {
                DataScope.Company => query,
                DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
                DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
                _ => query.Where(x => x.RequesterId == scope.UserId)
            };
            var document = await query.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("PUR.REQUISITION.NOT_FOUND", "Purchase requisition", id);
            access.EnsureVisible(document.CompanyId, id);
            if (version < 1 || document.Version != version) throw new ConflictException();
            if (document.Status != RequisitionStatus.Draft)
                throw new BusinessRuleException("PUR.REQUISITION.NOT_SUBMITTABLE", "Only draft requisitions can be submitted.");
            if (document.Lines.Count == 0)
                throw new BusinessRuleException("PUR.REQUISITION.EMPTY", "Requisition must contain a line.");

            var result = await items.RunAsync(document.Lines.Select(x => x.ItemId).Distinct().ToArray(), async () =>
            {
                document.Status = RequisitionStatus.PendingApproval;
                var approval = await workflow.SubmitAsync(new ApprovalSubmission(DocumentType, document.Id,
                    document.DocumentNo, 0m, document.Remark), token);
                if (approval.Status == ApprovalStatus.Approved)
                    document.Status = RequisitionStatus.Approved;
                await db.SaveChangesAsync(token);
                return new RequisitionSubmissionResult(new RequisitionDetail(document.Id, document.DocumentNo,
                    document.BusinessDate, document.RequesterId, document.Status, document.Remark,
                    document.CreatedAt, document.Version,
                    [.. document.Lines.OrderBy(x => x.LineNo).Select(x => new RequisitionLineDetail(x.Id,
                        x.LineNo, x.ItemId, x.RequiredDate, x.Quantity, x.OrderedQuantity, x.Remark))],
                    document.BranchId, document.DepartmentId), approval);
            }, token);
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
}
