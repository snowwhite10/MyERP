using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesQuotationLineDetail(long Id, int LineNo, long ItemId, string ItemCode,
    string ItemName, decimal Quantity, decimal UnitPrice, decimal DiscountRate,
    long TaxRateId, decimal TaxRatePercent, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

public sealed record SalesQuotationDetail(long Id, string DocumentNo, long CustomerId,
    string CustomerCode, string CustomerName, DateOnly BusinessDate, DateOnly ExpiryDate,
    string CurrencyCode, string BaseCurrencyCode, decimal ExchangeRate,
    SalesQuotationStatus Status, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross,
    string? Remark, int Version, IReadOnlyList<SalesQuotationLineDetail> Lines);

/// <summary>Reads a quotation after applying the exact VIEW permission and data scope.</summary>
public sealed class SalesQuotationQueryService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company)
{
    public async Task<SalesQuotationDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.VIEW", token);
        var saved = await scope.Apply(db.Set<SalesQuotation>().AsNoTracking())
            .Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("SAL.QUOTATION.NOT_FOUND", "Sales quotation", id);
        return Map(saved);
    }

    public static SalesQuotationDetail Map(SalesQuotation saved) => new(
        saved.Id, saved.DocumentNo, saved.CustomerId, saved.CustomerCode, saved.CustomerName,
        saved.BusinessDate, saved.ExpiryDate, saved.CurrencyCode, saved.BaseCurrencyCode,
        saved.ExchangeRate, saved.Status, saved.OriginalNet, saved.OriginalTax,
        saved.OriginalGross, saved.BaseNet, saved.BaseTax, saved.BaseGross,
        saved.Remark, saved.Version,
        [.. saved.Lines.OrderBy(x => x.LineNo).Select(x => new SalesQuotationLineDetail(
            x.Id, x.LineNo, x.ItemId, x.ItemCode, x.ItemName, x.Quantity, x.UnitPrice,
            x.DiscountRate, x.TaxRateId, x.TaxRatePercent, x.OriginalNet, x.OriginalTax,
            x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross))]);
}
