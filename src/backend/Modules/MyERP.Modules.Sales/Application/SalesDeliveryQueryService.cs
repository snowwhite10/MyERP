using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesDeliveryLineDetail(long Id, int LineNo, long OrderLineId, int OrderLineNo,
    long ItemId, string ItemCode, string ItemName, long WarehouseId, long? LocationId, decimal Quantity,
    IReadOnlyList<SalesDeliveryTrackingDetail> Tracking, long? InvMovementId, ItemTrackingMode? TrackingMode = null,
    string? WarehouseCode = null, string? WarehouseName = null, string? LocationCode = null, string? LocationName = null,
    bool? WarehouseActive = null, bool? LocationActive = null);

/// <summary>A saved lot or serial selection of a delivery line, in entry order (ADR-145).</summary>
public sealed record SalesDeliveryTrackingDetail(int SeqNo, string? LotNo, string? SerialNo, decimal Quantity);

public sealed record SalesDeliveryDetail(long Id, string DocumentNo, long OrderId, string OrderDocumentNo,
    long CustomerId, string CustomerCode, string CustomerName, DateOnly BusinessDate,
    SalesDeliveryStatus Status, string? Remark, long? ApprovalInstanceId, DateTime? PostedAt, int Version,
    IReadOnlyList<SalesDeliveryLineDetail> Lines,
    IReadOnlyList<ApprovalDocumentHistory>? ApprovalHistory = null);

public sealed record SalesDeliveryQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public SalesDeliveryStatus? Status { get; init; }
    public long? OrderId { get; init; }
    public long? CustomerId { get; init; }
}

public sealed record SalesDeliverySummary(long Id, string DocumentNo, long OrderId, string OrderDocumentNo,
    long CustomerId, string CustomerName, DateOnly BusinessDate, SalesDeliveryStatus Status,
    int LineCount, decimal TotalQuantity, int Version);

/// <summary>
/// REQ_F4112.3.1: delivery headers have no owner, so the exact VIEW data scope is applied to the
/// source order (ADR-138) before counting or paging. Hidden, deleted or other-company IDs are 404.
/// </summary>
public sealed class SalesDeliveryQueryService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, IApprovalHistoryReader history, ISalesItemLookup items,
    IInventorySelectionOptions references)
{
    public async Task<PagedResult<SalesDeliverySummary>> ListAsync(SalesDeliveryQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        company.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.OrderId is <= 0)
            throw new RequestValidationException("orderId", "Order must be positive.");
        if (request.CustomerId is <= 0)
            throw new RequestValidationException("customerId", "Customer must be positive.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await access.RequireScopeAsync("SAL.DELIVERY.VIEW", token);

        var query =
            from delivery in db.Set<SalesDelivery>().AsNoTracking()
            join order in scope.Apply(db.Set<SalesOrder>().AsNoTracking()) on delivery.OrderId equals order.Id
            select new
            {
                delivery.Id, delivery.DocumentNo, delivery.OrderId, OrderDocumentNo = order.DocumentNo,
                order.CustomerId, order.CustomerName, delivery.BusinessDate, delivery.Status, delivery.Version
            };
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.OrderDocumentNo.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } state) query = query.Where(x => x.Status == state);
        if (request.OrderId is { } orderId) query = query.Where(x => x.OrderId == orderId);
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var lines = db.Set<SalesDeliveryLine>().AsNoTracking();
        var rows = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new
            {
                Row = x,
                LineCount = lines.Count(l => l.DeliveryId == x.Id),
                TotalQuantity = lines.Where(l => l.DeliveryId == x.Id).Sum(l => (decimal?)l.Quantity) ?? 0m
            }).ToListAsync(token);
        var items = rows.Select(x => new SalesDeliverySummary(x.Row.Id, x.Row.DocumentNo, x.Row.OrderId,
            x.Row.OrderDocumentNo, x.Row.CustomerId, x.Row.CustomerName, x.Row.BusinessDate, x.Row.Status,
            x.LineCount, x.TotalQuantity, x.Row.Version)).ToList();
        return PagedResult.Create(items, request, count);
    }

    public async Task<SalesDeliveryDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var scope = await access.RequireScopeAsync("SAL.DELIVERY.VIEW", token);
        var visibleOrders = scope.Apply(db.Set<SalesOrder>().AsNoTracking());
        var delivery = await db.Set<SalesDelivery>().AsNoTracking().AsSplitQuery().Include(x => x.Lines).ThenInclude(x => x.Tracking)
            .Where(x => x.Id == id && visibleOrders.Any(o => o.Id == x.OrderId))
            .SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("SAL.DELIVERY.NOT_FOUND", "Sales delivery", id);
        // SYS rounds belong to the VIEW read only; CREATE/EDIT receipts (DescribeAsync) do not expose them.
        return await DescribeAsync(delivery, token) with
        {
            ApprovalHistory = await history.GetHistoryAsync(SalesDeliveryCreateService.DocumentType, delivery.Id, token)
        };
    }

    /// <summary>
    /// Maps a delivery the caller has already authorised (VIEW read, or a CREATE/EDIT command that
    /// checked its own action scope). Writers must not need VIEW to see the document they saved.
    /// </summary>
    public async Task<SalesDeliveryDetail> DescribeAsync(SalesDelivery delivery, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var order = await db.Set<SalesOrder>().AsNoTracking().IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .Where(x => x.Id == delivery.OrderId)
            .Select(x => new { x.DocumentNo, x.CustomerId, x.CustomerCode, x.CustomerName })
            .SingleAsync(token);
        var orderLineIds = delivery.Lines.Select(x => x.OrderLineId).Distinct().ToList();
        var orderLines = await db.Set<SalesOrderLine>().AsNoTracking()
            .Where(x => x.OrderId == delivery.OrderId && orderLineIds.Contains(x.Id))
            .Select(x => new { x.Id, x.LineNo, x.ItemCode, x.ItemName })
            .ToDictionaryAsync(x => x.Id, token);
        // ADR-151: the current MD tracking mode tells the form which picker to show; absent (deleted item) stays null.
        var modes = await items.TrackingModesAsync([.. delivery.Lines.Select(x => x.ItemId).Distinct()], token);
        // ADR-153: saved warehouse/location labels from MD (current company, inactive included, soft-deleted absent).
        var saved = await references.SavedAsync([],
            [.. delivery.Lines.Select(x => x.WarehouseId).Distinct()],
            [.. delivery.Lines.Where(x => x.LocationId != null).Select(x => x.LocationId!.Value).Distinct()], token);
        var warehouses = saved.Warehouses.ToDictionary(x => x.Id);
        var locations = saved.Locations.ToDictionary(x => x.Id);
        return new SalesDeliveryDetail(delivery.Id, delivery.DocumentNo, delivery.OrderId, order.DocumentNo,
            order.CustomerId, order.CustomerCode, order.CustomerName, delivery.BusinessDate, delivery.Status,
            delivery.Remark, delivery.ApprovalInstanceId, delivery.PostedAt, delivery.Version,
            [.. delivery.Lines.OrderBy(x => x.LineNo).Select(x => new SalesDeliveryLineDetail(x.Id, x.LineNo,
                x.OrderLineId, orderLines[x.OrderLineId].LineNo, x.ItemId, orderLines[x.OrderLineId].ItemCode,
                orderLines[x.OrderLineId].ItemName, x.WarehouseId, x.LocationId, x.Quantity,
                [.. x.Tracking.OrderBy(t => t.SeqNo)
                    .Select(t => new SalesDeliveryTrackingDetail(t.SeqNo, t.LotNo, t.SerialNo, t.Quantity))],
                x.InvMovementId, modes.TryGetValue(x.ItemId, out var mode) ? mode : null,
                Label(warehouses, x.WarehouseId)?.Code, Label(warehouses, x.WarehouseId)?.Name,
                Label(locations, x.LocationId)?.Code, Label(locations, x.LocationId)?.Name,
                Label(warehouses, x.WarehouseId)?.IsActive, Label(locations, x.LocationId)?.IsActive))]);

        static InventorySelectionOption? Label(Dictionary<long, InventorySelectionOption> labels, long? id) =>
            id is { } key ? labels.GetValueOrDefault(key) : null;
    }
}
