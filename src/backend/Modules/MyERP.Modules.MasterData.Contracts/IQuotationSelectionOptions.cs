namespace MyERP.Modules.MasterData.Contracts;

public sealed record QuotationCurrencyOption(string Code, string Name);
public sealed record QuotationTaxRateOption(long Id, string Code, string Name, decimal RatePercent);
public sealed record QuotationSelectionOptions(IReadOnlyList<QuotationCurrencyOption> Currencies,
    IReadOnlyList<QuotationTaxRateOption> TaxRates);

/// <summary>Minimal current-company choices for a quotation business date; no MD view permission is needed.</summary>
public interface IQuotationSelectionOptions
{
    Task<QuotationSelectionOptions> GetAsync(DateOnly quoteDate, CancellationToken token = default);
}
