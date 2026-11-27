using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Other payment list filters (WU-FI-008b3a, REQ_F4116.4.1). <c>Keyword</c> (≤ 50) matches document number,
/// withdrawal reference or payee; <see cref="From"/>/<see cref="To"/> are inclusive business dates. Deleted documents
/// are never listed.
/// </summary>
public sealed record OtherPaymentQuery : PagedRequest
{
    public OtherPaymentDocumentStatus? Status { get; init; }
    public long? ExpenseCategoryId { get; init; }
    public long? BankAccountId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>
/// One list row: a non-AP expense payment, so there is no supplier, payable or unapplied amount.
/// <see cref="JournalId"/> is the committed GL journal once Posted (null before). Category and fund account labels
/// come from MD contracts and are null when MD has no current-company record (ADR-077).
/// </summary>
public sealed record OtherPaymentSummary(long Id, string DocumentNo, string PayeeDescription, DateOnly BusinessDate,
    string Currency, decimal Amount, long ExpenseCategoryId, string? ExpenseCategoryCode, string? ExpenseCategoryName,
    long BankAccountId, string? BankAccountCode, string? BankAccountName, string BankReference,
    OtherPaymentDocumentStatus Status, int Version, long? JournalId);

/// <summary>
/// REQ_F4116.4.1 read side (WU-FI-008b3a): current-company live other payment documents with company-wide data scope
/// like the other FI documents (ADR-075). Sort: one of businessDate (default descending), documentNo, amount, with ID
/// as the stable tie-breaker. MD labels are fetched once per distinct category / fund account on the page.
/// </summary>
public sealed class OtherPaymentQueryService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, IIncomeExpenseCategoryLookup categories, IFundAccountLookup funds)
{
    public const int KeywordMaxLength = 50;
    private static readonly string[] SortFields = ["businessDate", "documentNo", "amount"];

    public async Task<PagedResult<OtherPaymentSummary>> ListAsync(OtherPaymentQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await FinanceAccess.RequireAsync(currentCompany, authorization, OtherPaymentDocumentService.ViewPermission,
            token);
        request.EnsureValid();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ExpenseCategoryId is <= 0) errors["expenseCategoryId"] = ["Expense category must be positive."];
        if (request.BankAccountId is <= 0) errors["bankAccountId"] = ["Fund account must be positive."];
        if (request.Status is { } s && !Enum.IsDefined(s))
            errors["status"] = ["Use Draft, Pending, Rejected, Approved or Posted."];
        if (request.From > request.To) errors["to"] = ["End date must not precede start date."];
        if (request.Keyword is { Length: > KeywordMaxLength })
            errors["keyword"] = ["Keyword must be at most 50 characters."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        var sorts = SortSpec.Parse(request.Sort, SortFields, "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");

        var query = db.Set<OtherPaymentDocument>().AsNoTracking();
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.ExpenseCategoryId is { } categoryId) query = query.Where(x => x.ExpenseCategoryId == categoryId);
        if (request.BankAccountId is { } bankId) query = query.Where(x => x.BankAccountId == bankId);
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Keyword?.Trim() is { Length: > 0 } keyword)
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.BankReference.Contains(keyword)
                || x.PayeeDescription.Contains(keyword));
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
                x.Id, x.DocumentNo, x.PayeeDescription, x.BusinessDate, x.Currency, x.Amount, x.ExpenseCategoryId,
                x.BankAccountId, x.BankReference, x.Status, x.Version,
                JournalId = db.Set<OtherPaymentSource>()
                    .Where(p => p.SourceType == OtherPaymentSource.PaymentType && p.SourceId == x.Id)
                    .Select(p => (long?)p.GlJournalId).FirstOrDefault()
            })
            .ToListAsync(token);
        if (rows.Count == 0) return PagedResult.Create(new List<OtherPaymentSummary>(), request, count);

        var categoryLabels = new Dictionary<long, IncomeExpenseCategoryInfo?>();
        foreach (var id in rows.Select(x => x.ExpenseCategoryId).Distinct())
            categoryLabels[id] = await categories.FindByIdAsync(id, token);
        var fundLabels = new Dictionary<long, FundAccountInfo?>();
        foreach (var id in rows.Select(x => x.BankAccountId).Distinct())
            fundLabels[id] = await funds.FindByIdAsync(id, token);
        var items = rows.Select(x =>
        {
            var category = categoryLabels[x.ExpenseCategoryId];
            var fund = fundLabels[x.BankAccountId];
            return new OtherPaymentSummary(x.Id, x.DocumentNo, x.PayeeDescription, x.BusinessDate, x.Currency,
                x.Amount, x.ExpenseCategoryId, category?.Code, category?.Name, x.BankAccountId, fund?.Code, fund?.Name,
                x.BankReference, x.Status, x.Version, x.JournalId);
        }).ToList();
        return PagedResult.Create(items, request, count);
    }
}
