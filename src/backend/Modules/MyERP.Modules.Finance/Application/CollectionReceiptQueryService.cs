using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Receipt list filters (WU-FI-005b4a). <c>Keyword</c> matches document number or bank reference;
/// <see cref="From"/>/<see cref="To"/> are inclusive business dates. <see cref="Status"/> excludes Deleted, which is
/// never listed.
/// </summary>
public sealed record CollectionReceiptQuery : PagedRequest
{
    public long? CustomerId { get; init; }
    public long? BankAccountId { get; init; }
    public FinanceCollectionReceiptState? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>
/// One list row. <see cref="UnappliedAmount"/> is the posted receipt's current unapplied balance (null until
/// Posted). Customer and fund account labels come from MD contracts and are null when MD has no current-company
/// record (ADR-077).
/// </summary>
public sealed record CollectionReceiptSummary(long Id, string DocumentNo, long CustomerId, DateOnly BusinessDate,
    string Currency, decimal Amount, long BankAccountId, string BankReference, FinanceCollectionReceiptState Status,
    int Version, decimal? UnappliedAmount, string? CustomerCode = null, string? CustomerName = null,
    string? BankAccountCode = null, string? BankAccountName = null);

/// <summary>
/// REQ_F4116.1.12 read side (WU-FI-005b4a, ADR-129): current-company live receipt documents with company-wide data
/// scope like the other FI documents (ADR-075/126). Labels are fetched once per page: customers in one MD batch,
/// fund accounts once per distinct ID (a page has few). The posted balance is FI's own <c>ar_collection_receipt</c>.
/// </summary>
public sealed class CollectionReceiptQueryService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, ICustomerLabels labels, IFundAccountLookup funds)
{
    private static readonly string[] SortFields = ["businessDate", "documentNo", "amount"];

    public async Task<PagedResult<CollectionReceiptSummary>> ListAsync(CollectionReceiptQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await FinanceAccess.RequireAsync(currentCompany, authorization, CollectionReceiptDocumentService.ViewPermission,
            token);
        request.EnsureValid();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.CustomerId is <= 0) errors["customerId"] = ["Customer must be positive."];
        if (request.BankAccountId is <= 0) errors["bankAccountId"] = ["Fund account must be positive."];
        if (request.Status is { } s && (!Enum.IsDefined(s) || s == FinanceCollectionReceiptState.Deleted))
            errors["status"] = ["Use Draft, Pending, Rejected, Approved or Posted."];
        if (request.From > request.To) errors["to"] = ["End date must not precede start date."];
        if (request.Keyword is { Length: > 50 }) errors["keyword"] = ["Keyword must be at most 50 characters."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        var sorts = SortSpec.Parse(request.Sort, SortFields, "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");

        var query = db.Set<CollectionReceiptDocument>().AsNoTracking();
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        if (request.BankAccountId is { } bankId) query = query.Where(x => x.BankAccountId == bankId);
        if (request.Status is { } state)
        {
            var saved = ToSaved(state);
            query = query.Where(x => x.Status == saved);
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Keyword?.Trim() is { Length: > 0 } keyword)
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.BankReference.Contains(keyword));
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            ("amount", false) => query.OrderBy(x => x.Amount).ThenBy(x => x.Id),
            ("amount", true) => query.OrderByDescending(x => x.Amount).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var rows = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.DocumentNo, x.CustomerId, x.BusinessDate, x.Currency, x.Amount, x.BankAccountId,
                x.BankReference, x.Status, x.Version,
                Unapplied = db.Set<CollectionReceipt>()
                    .Where(r => r.SourceType == CollectionReceipt.SalesCollectionType && r.SourceId == x.Id)
                    .Select(r => (decimal?)r.UnappliedAmount).FirstOrDefault()
            })
            .ToListAsync(token);
        if (rows.Count == 0) return PagedResult.Create(new List<CollectionReceiptSummary>(), request, count);

        var customers = await labels.GetLabelsAsync([.. rows.Select(x => x.CustomerId).Distinct()], token);
        var accounts = new Dictionary<long, FundAccountInfo?>();
        foreach (var fundId in rows.Select(x => x.BankAccountId).Distinct())
            accounts[fundId] = await funds.FindByIdAsync(fundId, token);
        var items = rows.Select(x =>
        {
            var customer = customers.GetValueOrDefault(x.CustomerId);
            var fund = accounts[x.BankAccountId];
            return new CollectionReceiptSummary(x.Id, x.DocumentNo, x.CustomerId, x.BusinessDate, x.Currency, x.Amount,
                x.BankAccountId, x.BankReference, CollectionReceiptDocumentService.ToState(x.Status), x.Version,
                x.Unapplied, customer?.Code, customer?.Name, fund?.Code, fund?.Name);
        }).ToList();
        return PagedResult.Create(items, request, count);
    }

    private static CollectionReceiptDocumentStatus ToSaved(FinanceCollectionReceiptState state) => state switch
    {
        FinanceCollectionReceiptState.Draft => CollectionReceiptDocumentStatus.Draft,
        FinanceCollectionReceiptState.Pending => CollectionReceiptDocumentStatus.Pending,
        FinanceCollectionReceiptState.Rejected => CollectionReceiptDocumentStatus.Rejected,
        FinanceCollectionReceiptState.Approved => CollectionReceiptDocumentStatus.Approved,
        _ => CollectionReceiptDocumentStatus.Posted,
    };
}
