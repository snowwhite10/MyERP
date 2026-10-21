using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

public sealed record QuotationQuery : PagedRequest
{
    public long? SupplierId { get; init; }
    public DateOnly? ValidOn { get; init; }
    public SupplierQuotationStatus? Status { get; init; }
}

public sealed record QuotationSummary(long Id, long RfqId, long SupplierId,
    SupplierQuotationStatus Status, int CurrentRevisionNo, DateOnly QuoteDate,
    DateOnly ValidUntil, int Version, bool IsExpired);
public sealed record QuotationLineDetail(long Id, long RfqLineId, decimal UnitPrice,
    string CurrencyCode, long TaxRateId);
public sealed record QuotationRevisionDetail(long Id, int RevisionNo, DateOnly QuoteDate,
    DateOnly ValidUntil, string? Remark, IReadOnlyList<QuotationLineDetail> Lines);
public sealed record QuotationDetail(long Id, long RfqId, long SupplierId,
    SupplierQuotationStatus Status, int CurrentRevisionNo, int Version,
    IReadOnlyList<QuotationRevisionDetail> Revisions, bool IsExpired);

/// <summary>報價只從可見詢價讀取；撤回及舊修訂保留在唯讀歷史。</summary>
public sealed class QuotationQueryService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IClock clock)
{
    private async Task<DataScopeFilter> ViewScopeAsync(CancellationToken token) =>
        (await authorization.GetAsync(token))?.FilterFor(PurchasingPermissions.QuotationView)
        ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Quotation view permission is required.");

    private static IQueryable<Rfq> ApplyScope(IQueryable<Rfq> query, DataScopeFilter scope) =>
        scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };

    private async Task RequireVisibleRfqAsync(long rfqId, CancellationToken token)
    {
        access.RequireCompanyId();
        var scope = await ViewScopeAsync(token);
        if (!await ApplyScope(db.Set<Rfq>().AsNoTracking(), scope).AnyAsync(x => x.Id == rfqId, token))
            throw new NotFoundException("PUR.RFQ.NOT_FOUND", "Request for quotation", rfqId);
    }

    public async Task<PagedResult<QuotationSummary>> ListAsync(long rfqId,
        QuotationQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (request.SupplierId is <= 0)
            throw new RequestValidationException("supplierId", "Supplier must be positive.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown quotation status.");
        var sorts = SortSpec.Parse(request.Sort, ["supplierId", "quoteDate"], "supplierId");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        await RequireVisibleRfqAsync(rfqId, token);
        var query = db.Set<SupplierQuotation>().AsNoTracking().Where(x => x.RfqId == rfqId);
        if (request.SupplierId is { } supplierId)
            query = query.Where(x => x.SupplierId == supplierId);
        if (request.Status is { } state)
            query = query.Where(x => x.Status == state);
        if (request.ValidOn is { } validOn)
            query = query.Where(x => x.Revisions.Any(r => r.RevisionNo == x.CurrentRevisionNo &&
                r.QuoteDate <= validOn && r.ValidUntil >= validOn));
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("supplierId", false) => query.OrderBy(x => x.SupplierId).ThenBy(x => x.Id),
            ("supplierId", true) => query.OrderByDescending(x => x.SupplierId).ThenByDescending(x => x.Id),
            ("quoteDate", false) => query.OrderBy(x => x.Revisions.Where(r =>
                r.RevisionNo == x.CurrentRevisionNo).Select(r => r.QuoteDate).FirstOrDefault()).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.Revisions.Where(r =>
                r.RevisionNo == x.CurrentRevisionNo).Select(r => r.QuoteDate).FirstOrDefault()).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var today = DateOnly.FromDateTime(clock.LocalNow);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new QuotationSummary(x.Id, x.RfqId, x.SupplierId, x.Status,
                x.CurrentRevisionNo, x.Revisions.Where(r => r.RevisionNo == x.CurrentRevisionNo)
                    .Select(r => r.QuoteDate).Single(),
                x.Revisions.Where(r => r.RevisionNo == x.CurrentRevisionNo)
                    .Select(r => r.ValidUntil).Single(), x.Version,
                x.Revisions.Any(r => r.RevisionNo == x.CurrentRevisionNo && r.ValidUntil < today)))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<QuotationDetail> GetAsync(long rfqId, long quotationId,
        CancellationToken token = default)
    {
        await RequireVisibleRfqAsync(rfqId, token);
        var quotation = await db.Set<SupplierQuotation>().AsNoTracking()
            .Where(x => x.RfqId == rfqId && x.Id == quotationId)
            .Include(x => x.Revisions).ThenInclude(x => x.Lines)
            .AsSplitQuery().SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("PUR.QUOTATION.NOT_FOUND", "Supplier quotation", quotationId);
        return new QuotationDetail(quotation.Id, quotation.RfqId, quotation.SupplierId,
            quotation.Status, quotation.CurrentRevisionNo, quotation.Version,
            [.. quotation.Revisions.OrderBy(x => x.RevisionNo).Select(r => new QuotationRevisionDetail(
                r.Id, r.RevisionNo, r.QuoteDate, r.ValidUntil, r.Remark,
                [.. r.Lines.OrderBy(x => x.RfqLineId).Select(line => new QuotationLineDetail(
                    line.Id, line.RfqLineId, line.UnitPrice, line.CurrencyCode, line.TaxRateId))]))],
            quotation.Revisions.Single(r => r.RevisionNo == quotation.CurrentRevisionNo).ValidUntil
                < DateOnly.FromDateTime(clock.LocalNow));
    }
}
