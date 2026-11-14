using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Open receivable filters (WU-FI-005c2a). <see cref="CustomerId"/> is required; <see cref="PostedOnOrBefore"/> keeps
/// receivables posted on or before the settlement date (a later receivable cannot be settled, BR-FI-157).
/// </summary>
public sealed record OpenReceivableQuery : PagedRequest
{
    public long? CustomerId { get; init; }
    public DateOnly? PostedOnOrBefore { get; init; }
}

/// <summary>
/// One posted receivable with an open amount. <see cref="ReceivableId"/> is the ID used by settlement lines;
/// <see cref="SourceType"/>/<see cref="SourceId"/> identify the posted source document (for example Invoice and the SD
/// invoice ID). Amounts are in company base currency.
/// </summary>
public sealed record OpenReceivableSummary(long ReceivableId, string SourceType, long SourceId, DateOnly PostingDate,
    string Currency, decimal OriginalAmount, decimal OpenAmount);

/// <summary>
/// REQ_F4116.5.9 (WU-FI-005c2a, ADR-132): the current company's posted receivables of one customer that still have an
/// open amount, for choosing settlement lines. Reads FI's own <c>ar_posted_receivable</c> only. Drafts reserve nothing
/// (BR-FI-157), so the open amount is the committed balance; final posting re-checks it.
/// </summary>
public sealed class OpenReceivableQueryService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization)
{
    private static readonly string[] SortFields = ["postingDate", "openAmount"];

    public async Task<PagedResult<OpenReceivableSummary>> ListAsync(OpenReceivableQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await FinanceAccess.RequireAsync(currentCompany, authorization, CollectionSettlementDocumentService.ViewPermission,
            token);
        request.EnsureValid();
        if (request.CustomerId is not > 0)
            throw new RequestValidationException("customerId", "Choose a customer.");
        var sorts = SortSpec.Parse(request.Sort, SortFields, "postingDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");

        var customerId = request.CustomerId.Value;
        var query = db.Set<PostedReceivable>().AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.OpenBaseAmount > 0m);
        if (request.PostedOnOrBefore is { } limit) query = query.Where(x => x.PostingDate <= limit);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("openAmount", false) => query.OrderBy(x => x.OpenBaseAmount).ThenBy(x => x.Id),
            ("openAmount", true) => query.OrderByDescending(x => x.OpenBaseAmount).ThenByDescending(x => x.Id),
            (_, true) => query.OrderByDescending(x => x.PostingDate).ThenByDescending(x => x.Id),
            _ => query.OrderBy(x => x.PostingDate).ThenBy(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new OpenReceivableSummary(x.Id, x.SourceType, x.SourceId, x.PostingDate, x.BaseCurrency,
                x.OriginalBaseAmount, x.OpenBaseAmount))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }
}
