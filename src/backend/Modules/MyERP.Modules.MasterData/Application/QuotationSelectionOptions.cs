using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

public sealed class QuotationSelectionOptionsService(MyErpDbContext db) : IQuotationSelectionOptions
{
    public async Task<QuotationSelectionOptions> GetAsync(DateOnly quoteDate, CancellationToken token = default)
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
        var currencies = await db.Set<Currency>().AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Code).Select(x => new QuotationCurrencyOption(x.Code, x.Name)).ToListAsync(token);
        var rates = await db.Set<TaxRate>().AsNoTracking()
            .Where(x => x.TaxCode!.IsActive && x.TaxCode.TaxType != "Output" &&
                x.EffectiveFrom <= quoteDate && (x.EffectiveTo == null || x.EffectiveTo >= quoteDate))
            .OrderBy(x => x.TaxCode!.Code).ThenBy(x => x.Id)
            .Select(x => new QuotationTaxRateOption(x.Id, x.TaxCode!.Code,
                x.TaxCode.Name, x.RatePercent)).ToListAsync(token);
        return new QuotationSelectionOptions(currencies, rates);
    }
}
