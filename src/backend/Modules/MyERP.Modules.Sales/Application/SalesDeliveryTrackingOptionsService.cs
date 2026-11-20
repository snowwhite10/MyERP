using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Application;

/// <summary>Query of <c>GET /api/v1/sal/deliveries/tracking-options</c>: one source order line and the stock place.</summary>
public sealed record SalesDeliveryTrackingOptionsQuery
{
    public long OrderLineId { get; init; }
    public long WarehouseId { get; init; }
    public long? LocationId { get; init; }
    public string? Keyword { get; init; }
}

/// <summary>A lot or serial with positive stock at the requested warehouse/location (serial quantity is 1).</summary>
public sealed record SalesDeliveryTrackingOption(string Number, decimal Quantity, DateOnly? ManufactureDate, DateOnly? ExpiryDate);

/// <summary>
/// The item's tracking mode (null when the item was deleted) and up to <see cref="SalesDeliveryTrackingOptionsService.Limit"/>
/// available numbers; <c>Truncated</c> asks the user to narrow by keyword.
/// </summary>
public sealed record SalesDeliveryTrackingOptions(ItemTrackingMode? TrackingMode,
    IReadOnlyList<SalesDeliveryTrackingOption> Items, bool Truncated);

/// <summary>
/// ADR-152: lets a delivery writer (SAL.DELIVERY.CREATE or EDIT) choose lots/serials from INV stock without
/// INV.TRACKING.VIEW or MD.ITEM.VIEW. The item is taken from an order line whose order is visible in one of the
/// caller's CREATE/EDIT data scopes, so the endpoint cannot probe arbitrary items or other owners' orders.
/// </summary>
public sealed class SalesDeliveryTrackingOptionsService(MyErpDbContext db, SalesCompanyAccess company,
    ICurrentAuthorization authorization, ISalesItemLookup items, IStockTrackingAvailabilityQuery availability)
{
    public const int Limit = 100;
    public const int MaxKeywordLength = 100;
    private static readonly string[] Permissions = ["SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT"];

    public async Task<SalesDeliveryTrackingOptions> GetAsync(SalesDeliveryTrackingOptionsQuery query,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);
        var companyId = company.RequireCompanyId();
        var current = await authorization.GetAsync(token);
        var scopes = current is null || current.CompanyId != companyId
            ? []
            : Permissions.Select(current.FilterFor).OfType<DataScopeFilter>().ToList();
        if (scopes.Count == 0)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Sales delivery create or edit permission is required.");

        var line = await db.Set<SalesOrderLine>().AsNoTracking().Where(x => x.Id == query.OrderLineId)
            .Select(x => new { x.OrderId, x.ItemId }).SingleOrDefaultAsync(token);
        var visible = false;
        if (line is not null)
            foreach (var scope in scopes)
                if (visible = await scope.Apply(db.Set<SalesOrder>().AsNoTracking()).AnyAsync(x => x.Id == line.OrderId, token))
                    break;
        if (line is null || !visible)
            throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order line", query.OrderLineId);

        var modes = await items.TrackingModesAsync([line.ItemId], token);
        ItemTrackingMode? mode = modes.TryGetValue(line.ItemId, out var found) ? found : null;
        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();
        var page = mode switch
        {
            ItemTrackingMode.Lot => await availability.LotsAsync(line.ItemId, query.WarehouseId, query.LocationId, keyword,
                Limit, token),
            ItemTrackingMode.Serial => await availability.SerialsAsync(line.ItemId, query.WarehouseId, query.LocationId,
                keyword, Limit, token),
            _ => new AvailableTrackingNumbers([], false),
        };
        return new SalesDeliveryTrackingOptions(mode,
            [.. page.Items.Select(x => new SalesDeliveryTrackingOption(x.Number, x.Quantity, x.ManufactureDate, x.ExpiryDate))],
            page.Truncated);
    }

    private static void Validate(SalesDeliveryTrackingOptionsQuery query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.OrderLineId <= 0) errors["orderLineId"] = ["Order line is required."];
        if (query.WarehouseId <= 0) errors["warehouseId"] = ["Warehouse is required."];
        if (query.LocationId is <= 0) errors["locationId"] = ["Location must be positive."];
        if (query.Keyword?.Trim().Length > MaxKeywordLength)
            errors["keyword"] = [$"Keyword must be at most {MaxKeywordLength} characters."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}
