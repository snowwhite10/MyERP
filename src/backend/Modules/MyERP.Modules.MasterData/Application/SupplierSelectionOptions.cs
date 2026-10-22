using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

public sealed class SupplierSelectionOptions(MyErpDbContext db) : ISupplierSelectionOptions
{
    public async Task<PagedResult<SupplierSelectionOption>> ApprovedAsync(PagedRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (request.Sort is not null && request.Sort != "code")
            throw new RequestValidationException("sort", "Only code ordering is supported.");
        var query = db.Set<Supplier>().AsNoTracking()
            .Where(x => x.IsActive && x.ReviewStatus == PartnerReviewStatus.Approved);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword));
        }
        var count = await query.LongCountAsync(token);
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new SupplierSelectionOption(x.Id, x.Code, x.Name, x.IsActive, true))
            .ToListAsync(token);
        return PagedResult.Create(rows, request, count);
    }

    public async Task<IReadOnlyList<SupplierSelectionOption>> SavedAsync(IReadOnlyCollection<long> ids,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return await db.Set<Supplier>().AsNoTracking().Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Select(x => new SupplierSelectionOption(x.Id, x.Code, x.Name, x.IsActive,
                x.ReviewStatus == PartnerReviewStatus.Approved)).ToListAsync(token);
    }
}
