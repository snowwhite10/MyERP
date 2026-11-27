using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// REQ_F4114.1.6／BR-PUR-025（WU-PUR-006c1）：採購單生命週期收尾動作。DELETE 只軟刪未消耗請購量的 Draft 或已駁回
/// （駁回時已釋回）訂單；單號、保存行、決標來源連結與 SYS 歷史原樣保留，請購已轉量不變，單號永久不重用。
/// </summary>
public sealed class PurchaseOrderLifecycleService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IClock clock)
{
    /// <summary>
    /// 以 <paramref name="version"/> 樂觀鎖軟刪 DELETE 範圍內的 Draft／Rejected 訂單。版本不符 409；其他狀態 422
    /// <c>PUR.ORDER.NOT_DELETABLE</c>；範圍外、他公司或已刪 404。
    /// </summary>
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        if (id <= 0) throw new RequestValidationException("id", "Purchase order ID must be positive.");
        var companyId = access.RequireCompanyId();
        var scope = await ScopeAsync(PurchasingPermissions.OrderDelete, companyId, token);

        // Same lock order as SUBMIT and the SYS decision handler: the order row first, held until commit.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.pur_purchase_order WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1}", id, companyId).ToListAsync(token);
            var order = await LoadAsync(id, companyId, scope, token);
            if (version < 1 || order.Version != version) throw new ConflictException();
            // Rejected orders have already released their requisition quantity (BR-PUR-024); the CHECK
            // ck_pur_order_consumption_state guarantees neither state still holds a consumption.
            if (order.Status is not ("Draft" or "Rejected") || order.RequisitionConsumptionActive)
                throw new BusinessRuleException("PUR.ORDER.NOT_DELETABLE",
                    "Only unsubmitted draft or rejected purchase orders can be deleted; cancel an approved order instead.");
            order.IsDeleted = true;
            order.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    private async Task<DataScopeFilter> ScopeAsync(string permission, long companyId, CancellationToken token)
    {
        var user = await authorization.GetAsync(token);
        var scope = user is not null && user.CompanyId == companyId && user.Has(permission)
            ? user.FilterFor(permission) : null;
        return scope ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED",
            "Purchase order action permission is required.");
    }

    private async Task<PurchaseOrderSource> LoadAsync(long id, long companyId, DataScopeFilter scope,
        CancellationToken token)
    {
        IQueryable<PurchaseOrderSource> query = db.Set<PurchaseOrderSource>().Where(x => x.CompanyId == companyId);
        query = scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
        return await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("PUR.ORDER.NOT_FOUND", "Purchase order", id);
    }
}
