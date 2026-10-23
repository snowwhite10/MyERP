using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

public sealed class SalesSelectionOptions(MyErpDbContext db) : ISalesSelectionOptions
{
    public Task<PagedResult<SalesSelectionOption>> CustomersAsync(PagedRequest request, CancellationToken token = default) =>
        PageAsync(db.Set<Customer>().AsNoTracking().Where(x => x.IsActive && x.ReviewStatus == PartnerReviewStatus.Approved), request,
            x => new SalesSelectionOption(x.Id, x.Code, x.Name), token);

    public Task<PagedResult<SalesSelectionOption>> ItemsAsync(PagedRequest request, CancellationToken token = default) =>
        PageAsync(db.Set<Item>().AsNoTracking().Where(x => x.IsActive), request,
            x => new SalesSelectionOption(x.Id, x.Code, x.Name), token);

    public async Task<SalesCommercialOptions> CommercialAsync(DateOnly businessDate, CancellationToken token = default)
    {
        RequireCompany();
        var currencies = await db.Set<Currency>().AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Code).Select(x => new SalesCurrencyOption(x.Code, x.Name, x.DecimalPlaces)).ToListAsync(token);
        var taxes = await db.Set<TaxRate>().AsNoTracking()
            .Where(x => x.TaxCode!.IsActive && x.TaxCode.TaxType != "Input" &&
                x.EffectiveFrom <= businessDate && (x.EffectiveTo == null || x.EffectiveTo >= businessDate))
            .OrderBy(x => x.TaxCode!.Code).ThenBy(x => x.Id)
            .Select(x => new SalesOutputTaxOption(x.Id, x.TaxCode!.Code, x.TaxCode.Name, x.RatePercent)).ToListAsync(token);
        return new SalesCommercialOptions(currencies, taxes);
    }

    private async Task<PagedResult<SalesSelectionOption>> PageAsync<T>(IQueryable<T> source, PagedRequest request,
        System.Linq.Expressions.Expression<Func<T, SalesSelectionOption>> selector, CancellationToken token) where T : class
    {
        RequireCompany();
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (request.Sort is not null && request.Sort != "code")
            throw new RequestValidationException("sort", "Only code ordering is supported.");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            source = source.Where(x => EF.Property<string>(x, "Code").Contains(keyword) || EF.Property<string>(x, "Name").Contains(keyword));
        }
        var count = await source.LongCountAsync(token);
        var rows = await source.OrderBy(x => EF.Property<string>(x, "Code")).ThenBy(x => EF.Property<long>(x, "Id"))
            .Skip(request.Skip).Take(request.PageSize).Select(selector).ToListAsync(token);
        return PagedResult.Create(rows, request, count);
    }

    private void RequireCompany()
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
    }
}
