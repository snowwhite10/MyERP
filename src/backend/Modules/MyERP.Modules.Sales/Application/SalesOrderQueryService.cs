using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesOrderLineDetail(long Id, int LineNo, long ItemId, string ItemCode,
    string ItemName, decimal Quantity, decimal UnitPrice, decimal DiscountRate,
    long TaxRateId, decimal TaxRatePercent, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

public sealed record SalesOrderDetail(long Id, string DocumentNo, long CustomerId,
    string CustomerCode, string CustomerName, DateOnly BusinessDate, string CurrencyCode,
    string BaseCurrencyCode, decimal ExchangeRate, SalesOrderStatus Status,
    long? SourceQuotationId, decimal OriginalNet, decimal OriginalTax, decimal OriginalGross,
    decimal BaseNet, decimal BaseTax, decimal BaseGross, string? Remark, int Version,
    IReadOnlyList<SalesOrderLineDetail> Lines);

/// <summary>Reads saved orders only after the exact VIEW action and its SYS data scope are applied.</summary>
public sealed class SalesOrderQueryService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company)
{
    public async Task<SalesOrderDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var scope = await access.RequireScopeAsync("SAL.ORDER.VIEW", token);
        var order = await scope.Apply(db.Set<SalesOrder>().AsNoTracking())
            .Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", id);
        return Map(order);
    }

    public static SalesOrderDetail Map(SalesOrder order) => new(order.Id, order.DocumentNo,
        order.CustomerId, order.CustomerCode, order.CustomerName, order.BusinessDate,
        order.CurrencyCode, order.BaseCurrencyCode, order.ExchangeRate, order.Status,
        order.SourceQuotationId, order.OriginalNet, order.OriginalTax, order.OriginalGross,
        order.BaseNet, order.BaseTax, order.BaseGross, order.Remark, order.Version,
        [.. order.Lines.OrderBy(x => x.LineNo).Select(x => new SalesOrderLineDetail(
            x.Id, x.LineNo, x.ItemId, x.ItemCode, x.ItemName, x.Quantity, x.UnitPrice,
            x.DiscountRate, x.TaxRateId, x.TaxRatePercent, x.OriginalNet, x.OriginalTax,
            x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross))]);
}
