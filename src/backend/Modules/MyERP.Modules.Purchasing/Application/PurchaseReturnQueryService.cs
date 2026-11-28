using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>LIST／EXPORT filters (WU-PUR-007c5); <c>keyword</c> matches the return or purchase order number.</summary>
public sealed record PurchaseReturnQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public long? PurchaseOrderId { get; init; }
    public string? Status { get; init; }
}

/// <summary>
/// Saved return header with its order's supplier／currency, line count, total quantity and base-currency return amount
/// (Σ line round(quantity × unit cost, 4)). Posted lines use the original INV receipt cost; until then the receipt line's
/// unit cost is used and <paramref name="AmountEstimated"/> is true.
/// </summary>
public sealed record PurchaseReturnSummary(long Id, string DocumentNo, DateOnly BusinessDate, long PurchaseOrderId,
    string? PurchaseOrderDocumentNo, long SupplierId, string Currency, string Status, int LineCount,
    decimal TotalQuantity, decimal ReturnAmount, bool AmountEstimated, int Version);

/// <summary>
/// REQ_F4114.7.1（ADR-226）：退貨列表與 CSV 只讀已保存單據，先套公司與 VIEW 資料範圍；匯出另交集 EXPORT 範圍。
/// 軟刪單據不出現。
/// </summary>
public sealed class PurchaseReturnQueryService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization)
{
    private static readonly string[] Statuses = ["Draft", "PendingApproval", "Rejected", "Posted"];
    private static readonly string[] SortFields = ["documentNo", "businessDate"];
    private const string DefaultSort = "-businessDate";

    public async Task<PagedResult<PurchaseReturnSummary>> ListAsync(PurchaseReturnQuery request,
        CancellationToken token = default)
    {
        var (query, companyId) = await FilterAsync(request, token);
        var count = await query.LongCountAsync(token);
        var page = await Sort(query, request).Skip(request.Skip).Take(request.PageSize)
            .Select(x => new { x.Id, x.DocumentNo, x.BusinessDate, x.PurchaseOrderId, x.Status, x.Version })
            .ToListAsync(token);
        var ids = page.Select(x => x.Id).ToArray();
        var orderIds = page.Select(x => x.PurchaseOrderId).Distinct().ToArray();
        var orders = await db.Set<PurchaseOrderSource>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.DocumentNo, x.SupplierId, x.Currency }, token);
        var lines = (await (from l in db.Set<PurchaseReturnLine>().AsNoTracking()
                    join s in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
                        on new { l.CompanyId, Id = l.ReceiptLineId } equals new { s.CompanyId, s.Id }
                    where l.CompanyId == companyId && ids.Contains(l.PurchaseReturnId)
                    select new { l.PurchaseReturnId, l.Quantity, l.OriginalUnitCost, s.UnitCost })
                .ToListAsync(token))
            .ToLookup(x => x.PurchaseReturnId);
        var items = page.Select(x =>
        {
            var order = orders[x.PurchaseOrderId];
            var own = lines[x.Id].ToArray();
            return new PurchaseReturnSummary(x.Id, x.DocumentNo, x.BusinessDate, x.PurchaseOrderId, order.DocumentNo,
                order.SupplierId, order.Currency, x.Status, own.Length, own.Sum(l => l.Quantity),
                own.Sum(l => PurchaseReturnCsv.Amount(l.Quantity, l.OriginalUnitCost ?? l.UnitCost ?? 0m)),
                own.Any(l => l.OriginalUnitCost is null), x.Version);
        }).ToList();
        return PagedResult.Create(items, request, count);
    }

    /// <summary>Exports every saved line under VIEW ∩ EXPORT scope from one snapshot; over the limit nothing is written.</summary>
    public async Task<long> ExportAsync(PurchaseReturnQuery request, string language, Stream output,
        Func<long, Task>? onReady = null, int maxRows = PurchaseReturnCsv.MaxRows,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var (returns, companyId) = await FilterAsync(request, token);
        returns = ApplyScope(returns, await ScopeAsync(companyId, PurchasingPermissions.ReturnExport, token));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var lines = from header in returns
            join line in db.Set<PurchaseReturnLine>().AsNoTracking()
                on new { header.CompanyId, PurchaseReturnId = header.Id }
                equals new { line.CompanyId, line.PurchaseReturnId }
            join source in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
                on new { line.CompanyId, Id = line.ReceiptLineId } equals new { source.CompanyId, source.Id }
            join receipt in db.Set<PurchaseReceiptSource>().AsNoTracking().IgnoreQueryFilters()
                on new { source.CompanyId, Id = source.PurchaseReceiptId } equals new { receipt.CompanyId, receipt.Id }
            select new { header, line, source, ReceiptNo = receipt.DocumentNo };
        var count = await lines.LongCountAsync(token);
        PurchaseReturnCsv.EnsureWithinLimit(count, maxRows);
        var sort = SortSpec.Parse(request.Sort, SortFields, DefaultSort)[0];
        lines = (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => lines.OrderBy(x => x.header.DocumentNo).ThenBy(x => x.header.Id).ThenBy(x => x.line.LineNo),
            ("documentNo", true) => lines.OrderByDescending(x => x.header.DocumentNo).ThenByDescending(x => x.header.Id).ThenBy(x => x.line.LineNo),
            (_, false) => lines.OrderBy(x => x.header.BusinessDate).ThenBy(x => x.header.Id).ThenBy(x => x.line.LineNo),
            _ => lines.OrderByDescending(x => x.header.BusinessDate).ThenByDescending(x => x.header.Id).ThenBy(x => x.line.LineNo)
        };
        var saved = await lines.Select(x => new
        {
            x.header.DocumentNo, x.header.BusinessDate, x.header.Status, x.header.PurchaseOrderId, x.line.LineNo,
            x.ReceiptNo, ReceiptLineNo = x.source.LineNo, x.line.MaterialId, x.line.WarehouseId, x.line.Quantity,
            x.line.OriginalUnitCost, ReceiptUnitCost = x.source.UnitCost
        }).ToListAsync(token);
        var orderIds = saved.Select(x => x.PurchaseOrderId).Distinct().ToArray();
        var orders = await db.Set<PurchaseOrderSource>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.DocumentNo, x.SupplierId }, token);
        var csv = new StringBuilder(PurchaseReturnCsv.HeaderLine(language)).Append("\r\n");
        foreach (var x in saved)
        {
            var order = orders[x.PurchaseOrderId];
            var cost = x.OriginalUnitCost ?? x.ReceiptUnitCost ?? 0m;
            csv.Append(PurchaseReturnCsv.Line(new PurchaseReturnCsvRow(x.DocumentNo, x.BusinessDate, x.Status,
                order.DocumentNo, order.SupplierId, x.LineNo, x.ReceiptNo, x.ReceiptLineNo, x.MaterialId,
                x.WarehouseId, x.Quantity, cost, PurchaseReturnCsv.Amount(x.Quantity, cost),
                x.OriginalUnitCost is not null), language)).Append("\r\n");
        }
        await tx.CommitAsync(token);
        if (onReady is not null) await onReady(count);
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), token);
        await output.WriteAsync(Encoding.UTF8.GetBytes(csv.ToString()), token);
        return count;
    }

    private async Task<(IQueryable<PurchaseReturn> Query, long CompanyId)> FilterAsync(
        PurchaseReturnQuery request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var companyId = access.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.PurchaseOrderId is <= 0)
            throw new RequestValidationException("purchaseOrderId", "Purchase order must be positive.");
        if (request.Status is not null && !Statuses.Contains(request.Status))
            throw new RequestValidationException("status", "Unknown purchase return status.");
        if (SortSpec.Parse(request.Sort, SortFields, DefaultSort).Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await ScopeAsync(companyId, PurchasingPermissions.ReturnView, token);
        // Explicit IsDeleted: IgnoreQueryFilters on the joined order/receipt sets disables the soft-delete filter for the
        // whole query (keyword subquery, CSV join), so it cannot be relied on here.
        var query = ApplyScope(db.Set<PurchaseReturn>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted), scope);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) ||
                db.Set<PurchaseOrderSource>().IgnoreQueryFilters().Any(o => o.CompanyId == companyId
                    && o.Id == x.PurchaseOrderId && o.DocumentNo != null && o.DocumentNo.Contains(keyword)));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.PurchaseOrderId is { } orderId) query = query.Where(x => x.PurchaseOrderId == orderId);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        return (query, companyId);
    }

    private static IQueryable<PurchaseReturn> Sort(IQueryable<PurchaseReturn> query, PurchaseReturnQuery request)
    {
        var sort = SortSpec.Parse(request.Sort, SortFields, DefaultSort)[0];
        return (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
    }

    private async Task<DataScopeFilter> ScopeAsync(long companyId, string permission, CancellationToken token)
    {
        var grants = await authorization.GetAsync(token);
        return (grants?.CompanyId == companyId ? grants.FilterFor(permission) : null)
            ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase return permission is required.");
    }

    private static IQueryable<PurchaseReturn> ApplyScope(IQueryable<PurchaseReturn> query, DataScopeFilter scope) =>
        scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
}
