using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Application;

/// <summary>An Approved order the caller may deliver against; identity and customer only, no amounts.</summary>
public sealed record SalesDeliveryOrderOption(long Id, string DocumentNo, string CustomerCode, string CustomerName,
    DateOnly BusinessDate);

/// <summary>A source order line with its advisory balance (ordered − posted − pending); no prices.</summary>
public sealed record SalesDeliverySourceLine(long Id, int LineNo, long ItemId, string ItemCode, string ItemName,
    decimal Quantity, decimal DeliveredQuantity, decimal PendingQuantity, decimal RemainingQuantity);

/// <summary>The order a delivery form is built from; any status is returned so the page can explain non-Approved.</summary>
public sealed record SalesDeliverySourceOrder(long Id, string DocumentNo, SalesOrderStatus Status, string CustomerCode,
    string CustomerName, DateOnly BusinessDate, int Version, IReadOnlyList<SalesDeliverySourceLine> Lines);

/// <summary>
/// ADR-155 (WU-SD-004c4): options of the delivery create/edit forms, authorised by the delivery action instead of
/// SAL.ORDER.VIEW or MD.WAREHOUSE.VIEW. The eligible-order list uses the CREATE data scope (only creation chooses an
/// order); one order's balance is visible under the CREATE or EDIT scope, like tracking options (ADR-152).
/// Warehouses and locations are the active MD options; the server still revalidates every saved reference.
/// </summary>
public sealed class SalesDeliveryFormOptionsService(MyErpDbContext db, SalesCompanyAccess company,
    SalesActionAccess access, ICurrentAuthorization authorization, IInventorySelectionOptions references)
{
    private static readonly string[] WriterPermissions = ["SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT"];

    public async Task<PagedResult<SalesDeliveryOrderOption>> OrdersAsync(PagedRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        company.RequireCompanyId();
        request.EnsureValid();
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await access.RequireScopeAsync("SAL.DELIVERY.CREATE", token);
        var query = scope.Apply(db.Set<SalesOrder>().AsNoTracking()).Where(x => x.Status == SalesOrderStatus.Approved);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.CustomerCode.Contains(keyword) ||
                x.CustomerName.Contains(keyword));
        }
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new SalesDeliveryOrderOption(x.Id, x.DocumentNo, x.CustomerCode, x.CustomerName, x.BusinessDate))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<SalesDeliverySourceOrder> OrderAsync(long orderId, CancellationToken token = default)
    {
        var companyId = company.RequireCompanyId();
        var current = await authorization.GetAsync(token);
        var scopes = current is null || current.CompanyId != companyId
            ? []
            : WriterPermissions.Select(current.FilterFor).OfType<DataScopeFilter>().ToList();
        if (scopes.Count == 0)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Sales delivery create or edit permission is required.");

        SalesOrder? order = null;
        if (orderId > 0)
            foreach (var scope in scopes)
                if ((order = await scope.Apply(db.Set<SalesOrder>().AsNoTracking()).Include(x => x.Lines)
                        .SingleOrDefaultAsync(x => x.Id == orderId, token)) is not null)
                    break;
        if (order is null)
            throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", orderId);

        var balances = await SalesOrderQueryService.DeliveryBalancesAsync(db, order.Id, token);
        return new SalesDeliverySourceOrder(order.Id, order.DocumentNo, order.Status, order.CustomerCode,
            order.CustomerName, order.BusinessDate, order.Version,
            [.. order.Lines.OrderBy(x => x.LineNo).Select(line =>
            {
                var (delivered, pending) = balances.TryGetValue(line.Id, out var balance) ? balance : (0m, 0m);
                return new SalesDeliverySourceLine(line.Id, line.LineNo, line.ItemId, line.ItemCode, line.ItemName,
                    line.Quantity, delivered, pending, line.Quantity - delivered - pending);
            })]);
    }

    public Task<PagedResult<InventorySelectionOption>> WarehousesAsync(PagedRequest request, CancellationToken token = default)
    {
        company.RequireCompanyId();
        return references.WarehousesAsync(request, token);
    }

    public Task<PagedResult<InventorySelectionOption>> LocationsAsync(long warehouseId, PagedRequest request,
        CancellationToken token = default)
    {
        company.RequireCompanyId();
        return references.LocationsAsync(warehouseId, request, token);
    }
}
