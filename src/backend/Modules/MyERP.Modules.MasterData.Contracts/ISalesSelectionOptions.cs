using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>Current-company MD choices for an authorised sales document form.</summary>
public interface ISalesSelectionOptions
{
    Task<PagedResult<SalesSelectionOption>> CustomersAsync(PagedRequest request, CancellationToken token = default);
    Task<PagedResult<SalesSelectionOption>> ItemsAsync(PagedRequest request, CancellationToken token = default);
    Task<SalesCommercialOptions> CommercialAsync(DateOnly businessDate, CancellationToken token = default);
}

public sealed record SalesSelectionOption(long Id, string Code, string Name);
public sealed record SalesCurrencyOption(string Code, string Name, int DecimalPlaces);
public sealed record SalesOutputTaxOption(long Id, string Code, string Name, decimal RatePercent);
public sealed record SalesCommercialOptions(IReadOnlyList<SalesCurrencyOption> Currencies,
    IReadOnlyList<SalesOutputTaxOption> OutputTaxRates);
