using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Credit limit list filters. <see cref="From"/>/<see cref="To"/> select periods that share at least one inclusive
/// date with the window (either bound may be open); a single day finds the period covering it.
/// </summary>
public sealed record CreditLimitQuery : PagedRequest
{
    public long? CustomerId { get; init; }
    public CreditLimitStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record CreditLimitSummary(long Id, long CustomerId, string BaseCurrency, DateOnly EffectiveFrom,
    DateOnly EffectiveTo, decimal Amount, CreditLimitStatus Status, int Version);

/// <summary>REQ_F4116.7.1 read side (WU-FI-001b2): current-company limits including deactivated history.</summary>
public sealed class CreditLimitQueryService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization)
{
    public const string ViewPermission = "AR.CREDIT.VIEW";
    private static readonly string[] SortFields = ["effectiveFrom", "effectiveTo", "customerId", "amount"];

    public async Task<PagedResult<CreditLimitSummary>> ListAsync(CreditLimitQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await FinanceAccess.RequireAsync(currentCompany, authorization, ViewPermission, token);
        request.EnsureValid();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            throw new RequestValidationException("keyword", "Keyword search is not supported; filter by customer.");
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.CustomerId is <= 0)
            throw new RequestValidationException("customerId", "Customer must be positive.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, SortFields, "-effectiveFrom");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");

        var query = db.Set<CreditLimit>().AsNoTracking();
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        if (request.Status is { } state) query = query.Where(x => x.Status == state);
        if (request.From is { } from) query = query.Where(x => x.EffectiveTo >= from);
        if (request.To is { } to) query = query.Where(x => x.EffectiveFrom <= to);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("effectiveTo", false) => query.OrderBy(x => x.EffectiveTo).ThenBy(x => x.Id),
            ("effectiveTo", true) => query.OrderByDescending(x => x.EffectiveTo).ThenByDescending(x => x.Id),
            ("customerId", false) => query.OrderBy(x => x.CustomerId).ThenBy(x => x.EffectiveFrom).ThenBy(x => x.Id),
            ("customerId", true) => query.OrderByDescending(x => x.CustomerId).ThenByDescending(x => x.EffectiveFrom)
                .ThenByDescending(x => x.Id),
            ("amount", false) => query.OrderBy(x => x.Amount).ThenBy(x => x.Id),
            ("amount", true) => query.OrderByDescending(x => x.Amount).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new CreditLimitSummary(x.Id, x.CustomerId, x.BaseCurrency, x.EffectiveFrom, x.EffectiveTo,
                x.Amount, x.Status, x.Version))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<CreditLimitDetail> GetAsync(long id, CancellationToken token = default)
    {
        await FinanceAccess.RequireAsync(currentCompany, authorization, ViewPermission, token);
        var limit = await db.Set<CreditLimit>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(CreditLimitErrors.NotFound, "Credit limit", id);
        return CreditLimitDetail.From(limit);
    }
}
