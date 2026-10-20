using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record RequisitionQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public RequisitionStatus? Status { get; init; }
    public long? RequesterId { get; init; }
}

public sealed record RequisitionSummary(long Id, string DocumentNo, DateOnly BusinessDate,
    long RequesterId, RequisitionStatus Status, int LineCount, int Version,
    long? BranchId, long? DepartmentId);
public sealed record RequisitionLineDetail(long Id, int LineNo, long ItemId, DateOnly RequiredDate,
    decimal Quantity, decimal OrderedQuantity, string? Remark);
public sealed record RequisitionDetail(long Id, string DocumentNo, DateOnly BusinessDate,
    long RequesterId, RequisitionStatus Status, string? Remark, DateTime CreatedAt, int Version,
    IReadOnlyList<RequisitionLineDetail> Lines, long? BranchId, long? DepartmentId)
{
    public IReadOnlyList<ApprovalDocumentHistory> ApprovalHistory { get; init; } = [];
}

/// <summary>只讀取 PUR 請購的已保存資料；公司與軟刪隔離由 EF 篩選及公司守衛雙重保護。</summary>
public sealed class RequisitionQueryService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization)
{
    private async Task<DataScopeFilter> ViewScopeAsync(CancellationToken token) =>
        (await authorization.GetAsync(token))?.FilterFor(PurchasingPermissions.RequisitionView)
        ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Requisition view permission is required.");

    private static IQueryable<Requisition> ApplyScope(IQueryable<Requisition> query, DataScopeFilter scope) =>
        scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.RequesterId == scope.UserId)
        };

    public async Task<PagedResult<RequisitionSummary>> ListAsync(RequisitionQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        access.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.RequesterId is <= 0)
            throw new RequestValidationException("requesterId", "Requester must be positive.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var query = ApplyScope(db.Set<Requisition>().AsNoTracking(), await ViewScopeAsync(token));
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } state) query = query.Where(x => x.Status == state);
        if (request.RequesterId is { } requesterId) query = query.Where(x => x.RequesterId == requesterId);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new RequisitionSummary(x.Id, x.DocumentNo, x.BusinessDate, x.RequesterId,
                x.Status, x.Lines.Count, x.Version, x.BranchId, x.DepartmentId)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<RequisitionDetail> GetAsync(long id, CancellationToken token = default)
    {
        access.RequireCompanyId();
        var scope = await ViewScopeAsync(token);
        var document = await ApplyScope(db.Set<Requisition>().AsNoTracking(), scope).Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("PUR.REQUISITION.NOT_FOUND", "Purchase requisition", id);
        access.EnsureVisible(document.CompanyId, id);
        return new RequisitionDetail(document.Id, document.DocumentNo, document.BusinessDate,
            document.RequesterId, document.Status, document.Remark, document.CreatedAt, document.Version,
            [.. document.Lines.OrderBy(x => x.LineNo).Select(x => new RequisitionLineDetail(x.Id, x.LineNo,
                x.ItemId, x.RequiredDate, x.Quantity, x.OrderedQuantity, x.Remark))],
            document.BranchId, document.DepartmentId);
    }
}
