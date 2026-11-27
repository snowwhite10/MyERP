using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// REQ_F4114.1.6／BR-PUR-025（WU-PUR-006c1／c2）：採購單生命週期收尾動作。DELETE 只軟刪未消耗請購量的 Draft 或已駁回
/// （駁回時已釋回）訂單；CANCEL 取消尚無收貨的已核准訂單並原子釋回一次請購量。兩者皆保留單號、保存行、決標來源連結與
/// SYS 歷史，單號永久不重用。
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

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await LockOrderAsync(id, companyId, token);
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

    /// <summary>
    /// WU-PUR-006c2：以 <c>version</c> 樂觀鎖取消 CANCEL 範圍內、尚無收貨引用的已核准訂單。同一 READ COMMITTED 交易先
    /// UPDLOCK 訂單，再按請購行 ID 升冪鎖定並一次釋回消耗量、清旗標、改 Cancelled 並保存原因；SYS 審批、單號、保存行及
    /// 決標來源連結原樣保留。原因空白或超過 500 字 400；版本不符 409；非已核准（或旗標已清）422
    /// <c>PUR.ORDER.NOT_CANCELLABLE</c>；有未軟刪收貨 422 <c>PUR.ORDER.HAS_RECEIPTS</c>；範圍外、他公司或已刪 404。
    /// </summary>
    public async Task<PurchaseOrderCancelResult> CancelAsync(long id, PurchaseOrderCancelInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (id <= 0) throw new RequestValidationException("id", "Purchase order ID must be positive.");
        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            throw new RequestValidationException("reason", "A cancellation reason is required.");
        if (reason.Length > MaxReasonLength)
            throw new RequestValidationException("reason", $"Cancellation reason must be at most {MaxReasonLength} characters.");
        var companyId = access.RequireCompanyId();
        var scope = await ScopeAsync(PurchasingPermissions.OrderCancel, companyId, token);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await LockOrderAsync(id, companyId, token);
            var order = await LoadAsync(id, companyId, scope, token);
            if (input.Version < 1 || order.Version != input.Version) throw new ConflictException();
            // PendingApproval must be decided in SYS first; Draft/Rejected hold nothing (delete them instead);
            // Cancelled or a cleared flag means the quantity was already released and must not be released twice.
            if (order.Status != "Approved" || !order.RequisitionConsumptionActive)
                throw new BusinessRuleException("PUR.ORDER.NOT_CANCELLABLE",
                    "Only approved purchase orders that still hold requisition quantity can be cancelled.");
            // Any saved receipt (even a draft) references the order lines; received goods go through returns (BR-PUR-032).
            if (await db.Set<PurchaseReceiptSource>().AnyAsync(x => x.CompanyId == companyId && x.PurchaseOrderId == id, token))
                throw new BusinessRuleException("PUR.ORDER.HAS_RECEIPTS",
                    "The purchase order already has receipts and can no longer be cancelled.");

            await PurchaseOrderRequisitionRelease.ReleaseAsync(db, order, token);
            order.Status = "Cancelled";
            order.CancelReason = reason;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new PurchaseOrderCancelResult(order.Id, order.DocumentNo, order.Status, order.Version);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    public const int MaxReasonLength = 500;

    /// <summary>Same first lock as SUBMIT and the SYS decision handler: the order row, held until commit.</summary>
    private async Task LockOrderAsync(long id, long companyId, CancellationToken token) =>
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.pur_purchase_order WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1}", id, companyId).ToListAsync(token);

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

/// <summary>CANCEL 請求：目前保存版本與必填原因（去頭尾空白後 1～500 字）。</summary>
public sealed record PurchaseOrderCancelInput(int Version, string? Reason);

/// <summary>CANCEL 結果：取消後的狀態（Cancelled）與新版本。</summary>
public sealed record PurchaseOrderCancelResult(long Id, string? DocumentNo, string Status, int Version);
