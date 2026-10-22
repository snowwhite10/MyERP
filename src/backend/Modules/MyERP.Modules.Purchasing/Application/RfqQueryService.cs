using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record RfqQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public RfqStatus? Status { get; init; }
    public long? RequisitionLineId { get; init; }
}

public sealed record RfqSummary(long Id, string DocumentNo, DateOnly BusinessDate,
    DateOnly ReplyDeadline, long OwnerId, RfqStatus Status, int LineCount,
    int InvitationCount, int Version, long? BranchId, long? DepartmentId);
public sealed record RfqLineDetail(long Id, int LineNo, long ItemId, long? RequisitionLineId,
    DateOnly RequiredDate, decimal Quantity, string? Remark);
public sealed record RfqInvitationDetail(long Id, long SupplierId, string? SupplierCodeSnapshot,
    string? SupplierNameSnapshot, DateTime? SentAt);
public sealed record RfqDetail(long Id, string DocumentNo, DateOnly BusinessDate,
    DateOnly ReplyDeadline, long OwnerId, RfqStatus Status, string? Remark, DateTime? SentAt,
    DateTime CreatedAt, int Version, long? BranchId, long? DepartmentId,
    IReadOnlyList<RfqLineDetail> Lines, IReadOnlyList<RfqInvitationDetail> Invitations);

/// <summary>只讀詢價查詢；資料範圍在 SQL 分頁及詳情查詢前套用。</summary>
public sealed class RfqQueryService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization)
{
    private async Task<DataScopeFilter> ScopeAsync(string permission, CancellationToken token) =>
        (await authorization.GetAsync(token))?.FilterFor(permission)
        ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "RFQ permission is required.");

    private static IQueryable<Rfq> ApplyScope(IQueryable<Rfq> query, DataScopeFilter scope) =>
        scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };

    public async Task<PagedResult<RfqSummary>> ListAsync(RfqQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        access.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.RequisitionLineId is <= 0)
            throw new RequestValidationException("requisitionLineId", "Requisition line must be positive.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var query = ApplyScope(db.Set<Rfq>().AsNoTracking(), await ScopeAsync(PurchasingPermissions.RfqView, token));
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } state) query = query.Where(x => x.Status == state);
        if (request.RequisitionLineId is { } lineId)
            query = query.Where(x => x.Lines.Any(line => line.RequisitionLineId == lineId));
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new RfqSummary(x.Id, x.DocumentNo, x.BusinessDate, x.ReplyDeadline,
                x.OwnerId, x.Status, x.Lines.Count, x.Invitations.Count, x.Version,
                x.BranchId, x.DepartmentId)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public Task<RfqDetail> GetAsync(long id, CancellationToken token = default) =>
        GetScopedAsync(id, PurchasingPermissions.RfqView, token);

    public Task<RfqDetail> GetForEditAsync(long id, CancellationToken token = default) =>
        GetScopedAsync(id, PurchasingPermissions.RfqEdit, token);

    private async Task<RfqDetail> GetScopedAsync(long id, string permission, CancellationToken token)
    {
        access.RequireCompanyId();
        var rfq = await ApplyScope(db.Set<Rfq>().AsNoTracking(), await ScopeAsync(permission, token))
            .Include(x => x.Lines).Include(x => x.Invitations)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("PUR.RFQ.NOT_FOUND", "Request for quotation", id);
        access.EnsureVisible(rfq.CompanyId, id);
        return new RfqDetail(rfq.Id, rfq.DocumentNo, rfq.BusinessDate, rfq.ReplyDeadline,
            rfq.OwnerId, rfq.Status, rfq.Remark, rfq.SentAt, rfq.CreatedAt,
            rfq.Version, rfq.BranchId, rfq.DepartmentId,
            [.. rfq.Lines.OrderBy(x => x.LineNo).Select(x => new RfqLineDetail(x.Id,
                x.LineNo, x.ItemId, x.RequisitionLineId, x.RequiredDate, x.Quantity, x.Remark))],
            [.. rfq.Invitations.OrderBy(x => x.SupplierId).Select(x => new RfqInvitationDetail(
                x.Id, x.SupplierId, x.SupplierCodeSnapshot, x.SupplierNameSnapshot, x.SentAt))]);
    }
}
