using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Application;

/// <summary>Paged output-tax-rate choices on one business date; keyword matches tax code or name.</summary>
public sealed record SalesOutputTaxOptionQuery : PagedRequest
{
    /// <summary>The document's business date (required); only rates effective on it are listed.</summary>
    public DateOnly? BusinessDate { get; init; }
}

/// <summary>
/// WU-SD-FORM-OPTIONS (ADR-167): the customer, item and output-tax-rate choices of the quotation and order forms,
/// authorised by the quotation/order CREATE or EDIT permission instead of MD.CUSTOMER/ITEM/TAX.VIEW. They are exactly
/// what the document save accepts — active approved customers, active items, active output/both tax codes' rates
/// effective on the business date — and all come from MD Contracts (SD never reads MD tables). The save still
/// revalidates every reference.
/// </summary>
public sealed class SalesDocumentOptionsService(ISalesSelectionOptions options)
{
    /// <summary>Active, approved customers of the current company; keyword matches code or name; code then ID order.</summary>
    public Task<PagedResult<SalesSelectionOption>> CustomersAsync(PagedRequest request,
        CancellationToken token = default) => options.CustomersAsync(request, token);

    /// <summary>Active items of the current company; keyword matches code or name; code then ID order.</summary>
    public Task<PagedResult<SalesSelectionOption>> ItemsAsync(PagedRequest request, CancellationToken token = default) =>
        options.ItemsAsync(request, token);

    /// <summary>Output tax rates effective on the business date, in tax-code order, paged in memory (a company has
    /// few tax codes).</summary>
    public async Task<PagedResult<SalesOutputTaxOption>> OutputTaxRatesAsync(SalesOutputTaxOptionQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (request.Sort is not null && request.Sort != "code")
            throw new RequestValidationException("sort", "Only code ordering is supported.");
        if (request.BusinessDate is not { } date)
            throw new RequestValidationException("businessDate", "businessDate is required.");
        IEnumerable<SalesOutputTaxOption> rates = (await options.CommercialAsync(date, token)).OutputTaxRates;
        var keyword = request.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
            rates = rates.Where(x => x.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        var all = rates.ToList();
        return PagedResult.Create(all.Skip(request.Skip).Take(request.PageSize).ToList(), request, all.Count);
    }
}
