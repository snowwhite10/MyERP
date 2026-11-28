using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseOrderReadLine(long Id, int LineNo, long ItemId, long? RequisitionLineId,
    long? AwardId, decimal Quantity, decimal UnitPrice, long? TaxRateId, decimal TaxRatePercent,
    decimal? OriginalNet, decimal? OriginalTax, decimal? OriginalGross,
    decimal? BaseNet, decimal? BaseTax, decimal? BaseGross,
    long? RequisitionId = null, string? RequisitionDocumentNo = null, int? RequisitionLineNo = null,
    decimal? RequisitionQuantity = null, decimal? RequisitionOrderedQuantity = null,
    decimal? RequisitionRemainingQuantity = null);

public sealed record PurchaseOrderReadDetail(long Id, string? DocumentNo, string Status, int Version,
    long SupplierId, long? RfqId, DateOnly? BusinessDate, string Currency, string? BaseCurrencyCode,
    decimal? ExchangeRate, decimal? BaseNet, decimal? BaseTax, decimal? BaseGross,
    string? Remark, long? OwnerId, long? BranchId, long? DepartmentId,
    IReadOnlyList<PurchaseOrderReadLine> Lines)
{
    public string? RfqDocumentNo { get; init; }
    public IReadOnlyList<ApprovalDocumentHistory> ApprovalHistory { get; init; } = [];
}

public sealed record PurchaseOrderQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public long? SupplierId { get; init; }
    public string? Status { get; init; }
    public long? RequisitionLineId { get; init; }
}

public sealed record PurchaseOrderSummary(long Id, string? DocumentNo, DateOnly? BusinessDate,
    long SupplierId, string Status, string Currency, decimal? BaseGross, int LineCount, int Version);

/// <summary>REQ_F4114.1.1：從已保存單據讀取人工與決標來源，先套公司及動作資料範圍。</summary>
public sealed class PurchaseOrderQueryService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization)
{
    public async Task<PagedResult<PurchaseOrderSummary>> ListAsync(PurchaseOrderQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var companyId = access.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.SupplierId is <= 0)
            throw new RequestValidationException("supplierId", "Supplier must be positive.");
        if (request.RequisitionLineId is <= 0)
            throw new RequestValidationException("requisitionLineId", "Requisition line must be positive.");
        if (request.Status is not null && request.Status is not ("Draft" or "PendingApproval" or "Approved" or "Rejected" or "Cancelled"))
            throw new RequestValidationException("status", "Unknown purchase order status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await ScopeAsync(companyId, PurchasingPermissions.OrderView, token);
        var query = ApplyScope(db.Set<PurchaseOrderSource>().AsNoTracking(), scope);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo != null && x.DocumentNo.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.SupplierId is { } supplierId) query = query.Where(x => x.SupplierId == supplierId);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.RequisitionLineId is { } sourceId)
            query = query.Where(x => db.Set<PurchaseOrderSourceLine>().Any(line =>
                line.CompanyId == companyId && line.PurchaseOrderId == x.Id && line.RequisitionLineId == sourceId));
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var page = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new PurchaseOrderSummary(x.Id, x.DocumentNo, x.BusinessDate, x.SupplierId,
                x.Status, x.Currency, x.BaseGross,
                db.Set<PurchaseOrderSourceLine>().Count(line => line.CompanyId == companyId && line.PurchaseOrderId == x.Id),
                x.Version)).ToListAsync(token);
        return PagedResult.Create(page, request, count);
    }

    public Task<PurchaseOrderReadDetail> GetAsync(long id, CancellationToken token = default) =>
        GetForPermissionAsync(id, PurchasingPermissions.OrderView, token);

    public Task<PurchaseOrderReadDetail> GetForEditAsync(long id, CancellationToken token = default) =>
        GetForPermissionAsync(id, PurchasingPermissions.OrderEdit, token);

    private async Task<PurchaseOrderReadDetail> GetForPermissionAsync(long id, string permission,
        CancellationToken token)
    {
        if (id <= 0) throw new RequestValidationException("id", "Purchase order ID must be positive.");
        var companyId = access.RequireCompanyId();
        var scope = await ScopeAsync(companyId, permission, token);
        var query = ApplyScope(db.Set<PurchaseOrderSource>().AsNoTracking(), scope);
        var order = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("PUR.ORDER.NOT_FOUND", "Purchase order", id);
        var rows = await (from line in db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            join source in db.Set<PurchaseOrderAwardSource>().AsNoTracking()
                on line.Id equals source.PurchaseOrderLineId into linked
            from source in linked.DefaultIfEmpty()
            join requisitionLine in db.Set<RequisitionLine>().AsNoTracking()
                on line.RequisitionLineId equals requisitionLine.Id into requisitionLines
            from requisitionLine in requisitionLines.DefaultIfEmpty()
            join requisition in db.Set<Requisition>().IgnoreQueryFilters().AsNoTracking()
                on requisitionLine.RequisitionId equals requisition.Id into requisitions
            from requisition in requisitions.DefaultIfEmpty()
            where line.PurchaseOrderId == id && line.CompanyId == companyId
                && (source == null || source.CompanyId == companyId)
                && (requisitionLine == null || requisitionLine.CompanyId == companyId)
                && (requisition == null || requisition.CompanyId == companyId)
            orderby line.LineNo, line.Id
            select new PurchaseOrderReadLine(line.Id, line.LineNo, line.MaterialId,
                line.RequisitionLineId, source == null ? null : source.AwardId,
                line.Quantity, line.UnitPrice, line.TaxRateId, line.TaxRatePercent,
                line.OriginalNet, line.OriginalTax, line.OriginalGross,
                line.BaseNet, line.BaseTax, line.BaseGross,
                requisitionLine == null ? null : requisitionLine.RequisitionId,
                requisition == null ? null : requisition.DocumentNo,
                requisitionLine == null ? null : requisitionLine.LineNo,
                requisitionLine == null ? null : requisitionLine.Quantity,
                requisitionLine == null ? null : requisitionLine.OrderedQuantity,
                requisitionLine == null ? null : requisitionLine.Quantity - requisitionLine.OrderedQuantity))
            .ToArrayAsync(token);
        var rfqDocumentNo = order.RfqId is { } rfqId
            ? await db.Set<Rfq>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == rfqId && x.CompanyId == companyId)
                .Select(x => x.DocumentNo).SingleOrDefaultAsync(token)
            : null;
        return new PurchaseOrderReadDetail(order.Id, order.DocumentNo, order.Status, order.Version,
            order.SupplierId, order.RfqId, order.BusinessDate, order.Currency, order.BaseCurrencyCode,
            order.ExchangeRate, order.BaseNet, order.BaseTax, order.BaseGross, order.Remark,
            order.OwnerId, order.BranchId, order.DepartmentId, rows) { RfqDocumentNo = rfqDocumentNo };
    }

    private async Task<DataScopeFilter> ScopeAsync(long companyId, string permission, CancellationToken token)
    {
        var grants = await authorization.GetAsync(token);
        return (grants?.CompanyId == companyId ? grants.FilterFor(permission) : null)
            ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase order read permission is required.");
    }

    private static IQueryable<PurchaseOrderSource> ApplyScope(IQueryable<PurchaseOrderSource> query,
        DataScopeFilter scope) => scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
}
