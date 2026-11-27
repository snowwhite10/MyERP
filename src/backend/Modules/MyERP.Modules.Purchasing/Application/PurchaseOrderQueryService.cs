using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseOrderReadLine(long Id, int LineNo, long ItemId, long? RequisitionLineId,
    long? AwardId, decimal Quantity, decimal UnitPrice, long? TaxRateId, decimal TaxRatePercent,
    decimal? OriginalNet, decimal? OriginalTax, decimal? OriginalGross,
    decimal? BaseNet, decimal? BaseTax, decimal? BaseGross);

public sealed record PurchaseOrderReadDetail(long Id, string? DocumentNo, string Status, int Version,
    long SupplierId, long? RfqId, DateOnly? BusinessDate, string Currency, string? BaseCurrencyCode,
    decimal? ExchangeRate, decimal? BaseNet, decimal? BaseTax, decimal? BaseGross,
    string? Remark, long? OwnerId, long? BranchId, long? DepartmentId,
    IReadOnlyList<PurchaseOrderReadLine> Lines);

/// <summary>REQ_F4114.1.1：從已保存單據讀取人工與決標來源，先套公司及動作資料範圍。</summary>
public sealed class PurchaseOrderQueryService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization)
{
    public Task<PurchaseOrderReadDetail> GetAsync(long id, CancellationToken token = default) =>
        GetForPermissionAsync(id, PurchasingPermissions.OrderView, token);

    public Task<PurchaseOrderReadDetail> GetForEditAsync(long id, CancellationToken token = default) =>
        GetForPermissionAsync(id, PurchasingPermissions.OrderEdit, token);

    private async Task<PurchaseOrderReadDetail> GetForPermissionAsync(long id, string permission,
        CancellationToken token)
    {
        if (id <= 0) throw new RequestValidationException("id", "Purchase order ID must be positive.");
        var companyId = access.RequireCompanyId();
        var grants = await authorization.GetAsync(token);
        var scope = grants?.CompanyId == companyId ? grants.FilterFor(permission) : null;
        if (scope is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase order read permission is required.");
        IQueryable<PurchaseOrderSource> query = db.Set<PurchaseOrderSource>().AsNoTracking();
        query = scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
        var order = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("PUR.ORDER.NOT_FOUND", "Purchase order", id);
        var rows = await (from line in db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            join source in db.Set<PurchaseOrderAwardSource>().AsNoTracking()
                on line.Id equals source.PurchaseOrderLineId into linked
            from source in linked.DefaultIfEmpty()
            where line.PurchaseOrderId == id && line.CompanyId == companyId
            orderby line.LineNo, line.Id
            select new PurchaseOrderReadLine(line.Id, line.LineNo, line.MaterialId,
                line.RequisitionLineId, source == null ? null : source.AwardId,
                line.Quantity, line.UnitPrice, line.TaxRateId, line.TaxRatePercent,
                line.OriginalNet, line.OriginalTax, line.OriginalGross,
                line.BaseNet, line.BaseTax, line.BaseGross)).ToArrayAsync(token);
        return new PurchaseOrderReadDetail(order.Id, order.DocumentNo, order.Status, order.Version,
            order.SupplierId, order.RfqId, order.BusinessDate, order.Currency, order.BaseCurrencyCode,
            order.ExchangeRate, order.BaseNet, order.BaseTax, order.BaseGross, order.Remark,
            order.OwnerId, order.BranchId, order.DepartmentId, rows);
    }
}
