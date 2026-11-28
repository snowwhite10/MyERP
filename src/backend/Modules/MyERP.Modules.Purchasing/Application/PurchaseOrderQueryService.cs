using System.Data;
using System.Text;
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
        var (query, companyId) = await FilterAsync(request, token);
        var count = await query.LongCountAsync(token);
        var page = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new PurchaseOrderSummary(x.Id, x.DocumentNo, x.BusinessDate, x.SupplierId,
                x.Status, x.Currency, x.BaseGross,
                db.Set<PurchaseOrderSourceLine>().Count(line => line.CompanyId == companyId && line.PurchaseOrderId == x.Id),
                x.Version)).ToListAsync(token);
        return PagedResult.Create(page, request, count);
    }

    /// <summary>Export all saved lines under VIEW scope and an independent EXPORT grant in one SQL snapshot.</summary>
    public async Task<long> ExportAsync(PurchaseOrderQuery request, string language, Stream output,
        Func<long, Task>? onReady = null, int maxRows = PurchaseOrderCsv.MaxRows,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var (orders, companyId) = await FilterAsync(request, token);
        var exportScope = await ScopeAsync(companyId, PurchasingPermissions.OrderExport, token);
        orders = ApplyScope(orders, exportScope);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var lines = from order in orders
            join line in db.Set<PurchaseOrderSourceLine>().AsNoTracking()
                on new { order.CompanyId, PurchaseOrderId = order.Id }
                equals new { line.CompanyId, line.PurchaseOrderId }
            select new { order, line };
        var count = await lines.LongCountAsync(token);
        PurchaseOrderCsv.EnsureWithinLimit(count, maxRows);
        var sort = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate")[0];
        lines = (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => lines.OrderBy(x => x.order.DocumentNo).ThenBy(x => x.order.Id).ThenBy(x => x.line.LineNo),
            ("documentNo", true) => lines.OrderByDescending(x => x.order.DocumentNo).ThenByDescending(x => x.order.Id).ThenBy(x => x.line.LineNo),
            (_, false) => lines.OrderBy(x => x.order.BusinessDate).ThenBy(x => x.order.Id).ThenBy(x => x.line.LineNo),
            _ => lines.OrderByDescending(x => x.order.BusinessDate).ThenByDescending(x => x.order.Id).ThenBy(x => x.line.LineNo)
        };
        var saved = await lines.Select(x => new
        {
            x.order.Id, x.order.DocumentNo, x.order.BusinessDate, x.order.Status, x.order.SupplierId,
            x.order.Currency, x.order.RfqId, x.line.LineNo, x.line.MaterialId, x.line.Quantity,
            x.line.UnitPrice, x.line.TaxRatePercent, x.line.BaseNet, x.line.BaseTax, x.line.BaseGross,
            x.line.RequisitionLineId
        }).ToListAsync(token);
        var rfqIds = saved.Where(x => x.RfqId.HasValue).Select(x => x.RfqId!.Value).Distinct().ToArray();
        var rfqs = await db.Set<Rfq>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && rfqIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DocumentNo, token);
        var requisitionIds = saved.Where(x => x.RequisitionLineId.HasValue)
            .Select(x => x.RequisitionLineId!.Value).Distinct().ToArray();
        var requisitions = await (from line in db.Set<RequisitionLine>().AsNoTracking()
            join requisition in db.Set<Requisition>().IgnoreQueryFilters().AsNoTracking()
                on line.RequisitionId equals requisition.Id
            where line.CompanyId == companyId && requisition.CompanyId == companyId
                && requisitionIds.Contains(line.Id)
            select new { line.Id, line.LineNo, requisition.DocumentNo })
            .ToDictionaryAsync(x => x.Id, token);
        var rows = saved.Select(x => new PurchaseOrderCsvRow(x.DocumentNo, x.BusinessDate, x.Status,
            x.SupplierId, x.Currency, x.LineNo, x.MaterialId, x.Quantity, x.UnitPrice,
            x.TaxRatePercent, x.BaseNet, x.BaseTax, x.BaseGross,
            x.RfqId is { } rfqId ? rfqs.GetValueOrDefault(rfqId) : null,
            x.RequisitionLineId is { } reqId ? requisitions.GetValueOrDefault(reqId)?.DocumentNo : null,
            x.RequisitionLineId is { } reqLineId ? requisitions.GetValueOrDefault(reqLineId)?.LineNo : null)).ToArray();
        var csv = new StringBuilder(PurchaseOrderCsv.HeaderLine(language)).Append("\r\n");
        foreach (var row in rows) csv.Append(PurchaseOrderCsv.Line(row, language)).Append("\r\n");
        await tx.CommitAsync(token);
        if (onReady is not null) await onReady(count);
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), token);
        await output.WriteAsync(Encoding.UTF8.GetBytes(csv.ToString()), token);
        return count;
    }

    private async Task<(IQueryable<PurchaseOrderSource> Query, long CompanyId)> FilterAsync(
        PurchaseOrderQuery request, CancellationToken token)
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
        return (query, companyId);
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
