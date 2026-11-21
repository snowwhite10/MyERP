using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesOrderLineDetail(long Id, int LineNo, long ItemId, string ItemCode,
    string ItemName, decimal Quantity, decimal UnitPrice, decimal DiscountRate,
    long TaxRateId, decimal TaxRatePercent, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross,
    decimal DeliveredQuantity, decimal PendingQuantity, decimal RemainingQuantity);

public sealed record SalesOrderDetail(long Id, string DocumentNo, long CustomerId,
    string CustomerCode, string CustomerName, DateOnly BusinessDate, string CurrencyCode,
    string BaseCurrencyCode, decimal ExchangeRate, SalesOrderStatus Status,
    long? SourceQuotationId, decimal OriginalNet, decimal OriginalTax, decimal OriginalGross,
    decimal BaseNet, decimal BaseTax, decimal BaseGross, string? Remark, int Version,
    IReadOnlyList<SalesOrderLineDetail> Lines,
    long? ApprovalInstanceId = null,
    IReadOnlyList<ApprovalDocumentHistory>? ApprovalHistory = null);

public sealed record SalesOrderQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public SalesOrderStatus? Status { get; init; }
    public long? CustomerId { get; init; }
}

public sealed record SalesOrderSummary(long Id, string DocumentNo, long CustomerId,
    string CustomerName, DateOnly BusinessDate, SalesOrderStatus Status,
    decimal OriginalGross, int Version);

/// <summary>Reads saved orders only after the exact VIEW action and its SYS data scope are applied.</summary>
public sealed class SalesOrderQueryService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, IApprovalHistoryReader history)
{
    public async Task<PagedResult<SalesOrderSummary>> ListAsync(SalesOrderQuery request,
        CancellationToken token = default)
    {
        var (query, sort) = await FilterAsync(request, "SAL.ORDER.VIEW", token);
        query = (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            ("customerId", false) => query.OrderBy(x => x.CustomerId).ThenBy(x => x.Id),
            ("customerId", true) => query.OrderByDescending(x => x.CustomerId).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new SalesOrderSummary(x.Id, x.DocumentNo, x.CustomerId,
                x.CustomerName, x.BusinessDate, x.Status, x.OriginalGross, x.Version)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    /// <summary>
    /// Streams every saved line of the filtered orders as CSV under the EXPORT action scope (not VIEW),
    /// inside one snapshot so the reported count equals the rows written. Paging is ignored; over-limit
    /// fails with SAL.ORDER.EXPORT_LIMIT before any byte is written.
    /// </summary>
    public async Task<long> ExportAsync(SalesOrderQuery request, string language, Stream output,
        Func<long, Task>? onReady = null, int maxRows = SalesOrderCsv.MaxRows, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var (orders, sort) = await FilterAsync(request, "SAL.ORDER.EXPORT", token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var lines = from order in orders
            join line in db.Set<SalesOrderLine>().AsNoTracking()
                on new { order.CompanyId, OrderId = order.Id } equals new { line.CompanyId, line.OrderId }
            select new { order, line };
        var count = await lines.LongCountAsync(token);
        SalesOrderCsv.EnsureWithinLimit(count, maxRows);
        if (onReady is not null) await onReady(count);
        // Documents keep the list order; each document's lines follow their saved line number.
        lines = (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => lines.OrderBy(x => x.order.DocumentNo).ThenBy(x => x.order.Id).ThenBy(x => x.line.LineNo),
            ("documentNo", true) => lines.OrderByDescending(x => x.order.DocumentNo).ThenByDescending(x => x.order.Id).ThenBy(x => x.line.LineNo),
            ("customerId", false) => lines.OrderBy(x => x.order.CustomerId).ThenBy(x => x.order.Id).ThenBy(x => x.line.LineNo),
            ("customerId", true) => lines.OrderByDescending(x => x.order.CustomerId).ThenByDescending(x => x.order.Id).ThenBy(x => x.line.LineNo),
            (_, false) => lines.OrderBy(x => x.order.BusinessDate).ThenBy(x => x.order.Id).ThenBy(x => x.line.LineNo),
            _ => lines.OrderByDescending(x => x.order.BusinessDate).ThenByDescending(x => x.order.Id).ThenBy(x => x.line.LineNo)
        };
        var rows = lines
            .Select(x => new SalesOrderCsvRow(x.order.DocumentNo, x.order.BusinessDate, x.order.Status,
                x.order.CustomerCode, x.order.CustomerName, x.order.CurrencyCode, x.order.ExchangeRate,
                x.line.LineNo, x.line.ItemCode, x.line.ItemName, x.line.Quantity, x.line.UnitPrice,
                x.line.DiscountRate, x.line.TaxRatePercent, x.line.OriginalNet, x.line.OriginalTax,
                x.line.OriginalGross, x.line.BaseNet, x.line.BaseTax, x.line.BaseGross, x.order.Remark));
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), token);
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteAsync((SalesOrderCsv.HeaderLine(language) + CsvNewLine).AsMemory(), token);
        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(token))
            await writer.WriteAsync((SalesOrderCsv.Line(row, language) + CsvNewLine).AsMemory(), token);
        await writer.FlushAsync(token);
        await tx.CommitAsync(token);
        return count;
    }

    private const string CsvNewLine = "\r\n";

    private async Task<(IQueryable<SalesOrder> Query, SortField Sort)> FilterAsync(SalesOrderQuery request,
        string permission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        company.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.CustomerId is <= 0)
            throw new RequestValidationException("customerId", "Customer must be positive.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate", "customerId"], "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await access.RequireScopeAsync(permission, token);
        var query = scope.Apply(db.Set<SalesOrder>().AsNoTracking());
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || x.CustomerCode.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } state) query = query.Where(x => x.Status == state);
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        return (query, sorts[0]);
    }

    public async Task<SalesOrderDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        var scope = await access.RequireScopeAsync("SAL.ORDER.VIEW", token);
        return await GetScopedAsync(id, scope, null, token);
    }

    /// <summary>Re-read only the caller's own creation under the CREATE action scope.</summary>
    public async Task<SalesOrderDetail> GetCreatedAsync(long id, long creatorId,
        CancellationToken token = default)
    {
        company.RequireCompanyId();
        if (creatorId <= 0) throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", id);
        var scope = await access.RequireScopeAsync("SAL.ORDER.CREATE", token);
        return await GetScopedAsync(id, scope, creatorId, token);
    }

    private async Task<SalesOrderDetail> GetScopedAsync(long id, DataScopeFilter scope,
        long? creatorId, CancellationToken token)
    {
        var order = await scope.Apply(db.Set<SalesOrder>().AsNoTracking())
            .Where(x => creatorId == null || x.CreatedBy == creatorId)
            .Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", id);
        var balances = await DeliveryBalancesAsync(db, order.Id, token);
        var detail = Map(order);
        return detail with
        {
            Lines = [.. detail.Lines.Select(line => balances.TryGetValue(line.Id, out var balance)
                ? line with { DeliveredQuantity = balance.Delivered,
                    PendingQuantity = balance.Pending,
                    RemainingQuantity = line.Quantity - balance.Delivered - balance.Pending }
                : line)],
            ApprovalHistory = await history.GetHistoryAsync("SAL.ORDER", order.Id, token)
        };
    }

    /// <summary>
    /// Advisory posted and pending delivery quantities per order line from saved SD delivery lines (Drafts and
    /// Rejected reserve nothing). Commands recheck under their own locks; callers must already have scoped the order.
    /// </summary>
    internal static async Task<Dictionary<long, (decimal Delivered, decimal Pending)>> DeliveryBalancesAsync(
        MyErpDbContext db, long orderId, CancellationToken token) =>
        await (from line in db.Set<SalesDeliveryLine>().AsNoTracking()
            join delivery in db.Set<SalesDelivery>().AsNoTracking()
                on new { line.CompanyId, Id = line.DeliveryId }
                equals new { delivery.CompanyId, delivery.Id }
            where delivery.OrderId == orderId &&
                (delivery.Status == SalesDeliveryStatus.Posted || delivery.Status == SalesDeliveryStatus.Pending)
            group new { line, delivery } by line.OrderLineId into grouped
            select new
            {
                OrderLineId = grouped.Key,
                Delivered = grouped.Sum(x => x.delivery.Status == SalesDeliveryStatus.Posted ? x.line.Quantity : 0m),
                Pending = grouped.Sum(x => x.delivery.Status == SalesDeliveryStatus.Pending ? x.line.Quantity : 0m)
            }).ToDictionaryAsync(x => x.OrderLineId, x => (x.Delivered, x.Pending), token);

    public static SalesOrderDetail Map(SalesOrder order) => new(order.Id, order.DocumentNo,
        order.CustomerId, order.CustomerCode, order.CustomerName, order.BusinessDate,
        order.CurrencyCode, order.BaseCurrencyCode, order.ExchangeRate, order.Status,
        order.SourceQuotationId, order.OriginalNet, order.OriginalTax, order.OriginalGross,
        order.BaseNet, order.BaseTax, order.BaseGross, order.Remark, order.Version,
        [.. order.Lines.OrderBy(x => x.LineNo).Select(x => new SalesOrderLineDetail(
            x.Id, x.LineNo, x.ItemId, x.ItemCode, x.ItemName, x.Quantity, x.UnitPrice,
            x.DiscountRate, x.TaxRateId, x.TaxRatePercent, x.OriginalNet, x.OriginalTax,
            x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross, 0m, 0m, x.Quantity))], order.ApprovalInstanceId);
}
