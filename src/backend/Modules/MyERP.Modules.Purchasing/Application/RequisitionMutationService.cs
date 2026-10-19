using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>REQ_F4114.3.3／3.6：受公司及動作資料範圍約束的請購草稿修訂與軟刪。</summary>
public sealed class RequisitionMutationService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IRequisitionItemGuard items, IClock clock)
{
    public async Task<RequisitionDetail> UpdateAsync(long id, int version, RequisitionInput input,
        CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var (remark, lines) = RequisitionCreateService.Validate(input);
        var scope = await ScopeAsync("PUR.REQUISITION.EDIT", companyId, token);
        return await InTransactionAsync(async () =>
        {
            var document = await LoadAsync(id, version, scope, token);
            if (document.Status is not (RequisitionStatus.Draft or RequisitionStatus.Rejected))
                throw new BusinessRuleException("PUR.REQUISITION.NOT_EDITABLE", "Only draft or rejected requisitions can be edited.");
            return await items.RunAsync(lines.Select(x => x.ItemId).Distinct().ToArray(), async () =>
            {
                db.RemoveRange(document.Lines);
                document.Lines.Clear();
                foreach (var line in lines)
                {
                    line.CompanyId = companyId;
                    document.Lines.Add(line);
                }
                document.BusinessDate = input.BusinessDate;
                document.Remark = remark;
                document.Status = RequisitionStatus.Draft;
                db.Entry(document).Property(x => x.Status).IsModified = true;
                await db.SaveChangesAsync(token);
                return Detail(document);
            }, token);
        }, token);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var scope = await ScopeAsync("PUR.REQUISITION.DELETE", companyId, token);
        await InTransactionAsync(async () =>
        {
            var document = await LoadAsync(id, version, scope, token);
            if (document.Status != RequisitionStatus.Draft)
                throw new BusinessRuleException("PUR.REQUISITION.NOT_DELETABLE", "Only draft requisitions can be deleted.");
            document.IsDeleted = true;
            document.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            return true;
        }, token);
    }

    private async Task<DataScopeFilter> ScopeAsync(string permission, long companyId, CancellationToken token)
    {
        var owner = await authorization.GetAsync(token);
        if (owner is null || owner.CompanyId != companyId || !owner.Has(permission))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Requisition action permission is required.");
        return owner.FilterFor(permission)
            ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Requisition action scope is required.");
    }

    private async Task<Requisition> LoadAsync(long id, int version, DataScopeFilter scope, CancellationToken token)
    {
        IQueryable<Requisition> query = db.Set<Requisition>();
        query = scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.RequesterId == scope.UserId)
        };
        var document = await query.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("PUR.REQUISITION.NOT_FOUND", "Purchase requisition", id);
        access.EnsureVisible(document.CompanyId, id);
        if (version < 1 || document.Version != version) throw new ConflictException();
        return document;
    }

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

    private static RequisitionDetail Detail(Requisition document) => new(document.Id, document.DocumentNo,
        document.BusinessDate, document.RequesterId, document.Status, document.Remark, document.CreatedAt,
        document.Version, [.. document.Lines.OrderBy(x => x.LineNo).Select(x => new RequisitionLineDetail(
            x.Id, x.LineNo, x.ItemId, x.RequiredDate, x.Quantity, x.OrderedQuantity, x.Remark))],
        document.BranchId, document.DepartmentId);
}
