using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// WU-SD-005b1b1 (ADR-162): the choices a price rule can be saved with, authorised by the pricing permissions instead
/// of MD.ITEM.VIEW / MD.CUSTOMER.VIEW. Items are active, customers active and approved, currencies active — exactly
/// what <see cref="SalesPriceRuleService"/> accepts on save — and all come from MD Contracts (SD never reads MD tables).
/// The list page filters use the same options; rules of a now-inactive item stay reachable without a filter or by URL.
/// </summary>
public sealed class SalesPricingOptionsService(ISalesSelectionOptions options, IClock clock)
{
    /// <summary>Active items of the current company; keyword matches code or name; code then ID order.</summary>
    public Task<PagedResult<SalesSelectionOption>> ItemsAsync(PagedRequest request, CancellationToken token = default) =>
        options.ItemsAsync(request, token);

    /// <summary>Active, approved customers of the current company; keyword matches code or name; code then ID order.</summary>
    public Task<PagedResult<SalesSelectionOption>> CustomersAsync(PagedRequest request,
        CancellationToken token = default) => options.CustomersAsync(request, token);

    /// <summary>Active currencies of the current company in code order, with their amount decimal places.</summary>
    public async Task<IReadOnlyList<SalesCurrencyOption>> CurrenciesAsync(CancellationToken token = default) =>
        (await options.CommercialAsync(clock.Today, token)).Currencies;
}
