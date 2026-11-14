using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Settlement list filters (WU-FI-005b4b). <c>Keyword</c> matches the document number; <see cref="ReceiptId"/> is the FI
/// receipt document ID; <see cref="From"/>/<see cref="To"/> are inclusive business dates. <see cref="Status"/> excludes
/// Deleted, which is never listed.
/// </summary>
public sealed record CollectionSettlementQuery : PagedRequest
{
    public long? CustomerId { get; init; }
    public long? ReceiptId { get; init; }
    public FinanceCollectionWriteOffState? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>
/// One list row. <see cref="ReceiptDocumentNo"/> is the receipt document number; <see cref="LineCount"/> the number of
/// allocation lines. Customer labels come from MD contracts and are null when MD has no current-company record (ADR-077).
/// </summary>
public sealed record CollectionSettlementSummary(long Id, string DocumentNo, long CustomerId, DateOnly BusinessDate,
    string Currency, long ReceiptId, string? ReceiptDocumentNo, decimal TotalAmount, int LineCount,
    FinanceCollectionWriteOffState Status, int Version, string? CustomerCode = null, string? CustomerName = null);

/// <summary>
/// REQ_F4116.5.8 read side (WU-FI-005b4b, ADR-130): current-company live settlement documents with company-wide data
/// scope like the other FI documents (ADR-075/127). Customer labels are fetched in one MD batch per page; the receipt
/// number and line count are FI's own tables in the same query.
/// </summary>
public sealed class CollectionSettlementQueryService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, ICustomerLabels labels)
{
    private static readonly string[] SortFields = ["businessDate", "documentNo", "totalAmount"];

    public async Task<PagedResult<CollectionSettlementSummary>> ListAsync(CollectionSettlementQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await FinanceAccess.RequireAsync(currentCompany, authorization, CollectionSettlementDocumentService.ViewPermission,
            token);
        request.EnsureValid();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.CustomerId is <= 0) errors["customerId"] = ["Customer must be positive."];
        if (request.ReceiptId is <= 0) errors["receiptId"] = ["Receipt must be positive."];
        if (request.Status is { } s && (!Enum.IsDefined(s) || s == FinanceCollectionWriteOffState.Deleted))
            errors["status"] = ["Use Draft, Pending, Rejected, Approved or Posted."];
        if (request.From > request.To) errors["to"] = ["End date must not precede start date."];
        if (request.Keyword is { Length: > 50 }) errors["keyword"] = ["Keyword must be at most 50 characters."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        var sorts = SortSpec.Parse(request.Sort, SortFields, "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");

        var query = db.Set<CollectionSettlementDocument>().AsNoTracking();
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        if (request.ReceiptId is { } receiptId) query = query.Where(x => x.ReceiptId == receiptId);
        if (request.Status is { } state)
        {
            var saved = ToSaved(state);
            query = query.Where(x => x.Status == saved);
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Keyword?.Trim() is { Length: > 0 } keyword)
            query = query.Where(x => x.DocumentNo.Contains(keyword));
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            ("totalAmount", false) => query.OrderBy(x => x.TotalAmount).ThenBy(x => x.Id),
            ("totalAmount", true) => query.OrderByDescending(x => x.TotalAmount).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var rows = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.DocumentNo, x.CustomerId, x.BusinessDate, x.Currency, x.ReceiptId, x.TotalAmount, x.Status,
                x.Version,
                // 不可用 IgnoreQueryFilters：它會關閉整個查詢（含核銷單據）的公司篩選。核銷只引用已過帳收款，
                // 已過帳收款不可刪除，所以公司＋未刪除篩選下一定找得到。
                ReceiptNo = db.Set<CollectionReceiptDocument>()
                    .Where(r => r.Id == x.ReceiptId)
                    .Select(r => r.DocumentNo).FirstOrDefault(),
                Lines = db.Set<CollectionSettlementDocumentLine>().Count(l => l.SettlementDocumentId == x.Id)
            })
            .ToListAsync(token);
        if (rows.Count == 0) return PagedResult.Create(new List<CollectionSettlementSummary>(), request, count);

        var customers = await labels.GetLabelsAsync([.. rows.Select(x => x.CustomerId).Distinct()], token);
        var items = rows.Select(x =>
        {
            var customer = customers.GetValueOrDefault(x.CustomerId);
            return new CollectionSettlementSummary(x.Id, x.DocumentNo, x.CustomerId, x.BusinessDate, x.Currency,
                x.ReceiptId, x.ReceiptNo, x.TotalAmount, x.Lines, CollectionSettlementDocumentService.ToState(x.Status),
                x.Version, customer?.Code, customer?.Name);
        }).ToList();
        return PagedResult.Create(items, request, count);
    }

    private static CollectionSettlementDocumentStatus ToSaved(FinanceCollectionWriteOffState state) => state switch
    {
        FinanceCollectionWriteOffState.Draft => CollectionSettlementDocumentStatus.Draft,
        FinanceCollectionWriteOffState.Pending => CollectionSettlementDocumentStatus.Pending,
        FinanceCollectionWriteOffState.Rejected => CollectionSettlementDocumentStatus.Rejected,
        FinanceCollectionWriteOffState.Approved => CollectionSettlementDocumentStatus.Approved,
        _ => CollectionSettlementDocumentStatus.Posted,
    };
}
