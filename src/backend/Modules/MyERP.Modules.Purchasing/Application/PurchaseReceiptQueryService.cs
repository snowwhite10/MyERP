using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>LIST／EXPORT filters (WU-PUR-007a10); <c>keyword</c> matches the receipt or purchase order number.</summary>
public sealed record PurchaseReceiptQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public long? PurchaseOrderId { get; init; }
    public string? Status { get; init; }
}

public sealed record PurchaseReceiptSummary(long Id, string DocumentNo, DateOnly BusinessDate, long PurchaseOrderId,
    string? PurchaseOrderDocumentNo, long SupplierId, string Status, string Currency, int LineCount,
    decimal TotalQuantity, int Version);

/// <summary>
/// REQ_F4114.2.1：收貨單列表與 CSV 只讀已保存單據，先套公司與 VIEW 資料範圍；匯出另交集 EXPORT 範圍。
/// 舊發票專用來源列（無單號或業務日）與軟刪單據不出現。
/// </summary>
public sealed class PurchaseReceiptQueryService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization)
{
    private static readonly string[] Statuses = ["Draft", "PendingApproval", "Rejected", "Posted"];
    private static readonly string[] SortFields = ["documentNo", "businessDate"];
    private const string DefaultSort = "-businessDate";

    public async Task<PagedResult<PurchaseReceiptSummary>> ListAsync(PurchaseReceiptQuery request,
        CancellationToken token = default)
    {
        var (query, companyId) = await FilterAsync(request, token);
        var count = await query.LongCountAsync(token);
        var page = await Sort(query, request).Skip(request.Skip).Take(request.PageSize)
            .Select(x => new PurchaseReceiptSummary(x.Id, x.DocumentNo!, x.BusinessDate!.Value, x.PurchaseOrderId,
                db.Set<PurchaseOrderSource>().IgnoreQueryFilters()
                    .Where(o => o.CompanyId == companyId && o.Id == x.PurchaseOrderId)
                    .Select(o => o.DocumentNo).FirstOrDefault(),
                x.SupplierId, x.Status, x.Currency,
                db.Set<PurchaseReceiptSourceLine>().Count(l => l.CompanyId == companyId && l.PurchaseReceiptId == x.Id),
                db.Set<PurchaseReceiptSourceLine>().Where(l => l.CompanyId == companyId && l.PurchaseReceiptId == x.Id)
                    .Sum(l => l.Quantity) ?? 0m,
                x.Version)).ToListAsync(token);
        return PagedResult.Create(page, request, count);
    }

    /// <summary>Exports every saved line under VIEW ∩ EXPORT scope from one snapshot; over the limit nothing is written.</summary>
    public async Task<long> ExportAsync(PurchaseReceiptQuery request, string language, Stream output,
        Func<long, Task>? onReady = null, int maxRows = PurchaseReceiptCsv.MaxRows,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var (receipts, companyId) = await FilterAsync(request, token);
        receipts = ApplyScope(receipts, await ScopeAsync(companyId, PurchasingPermissions.ReceiptExport, token));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var lines = from receipt in receipts
            join line in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
                on new { receipt.CompanyId, PurchaseReceiptId = receipt.Id }
                equals new { line.CompanyId, line.PurchaseReceiptId }
            select new { receipt, line };
        var count = await lines.LongCountAsync(token);
        PurchaseReceiptCsv.EnsureWithinLimit(count, maxRows);
        var sort = SortSpec.Parse(request.Sort, SortFields, DefaultSort)[0];
        lines = (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => lines.OrderBy(x => x.receipt.DocumentNo).ThenBy(x => x.receipt.Id).ThenBy(x => x.line.LineNo),
            ("documentNo", true) => lines.OrderByDescending(x => x.receipt.DocumentNo).ThenByDescending(x => x.receipt.Id).ThenBy(x => x.line.LineNo),
            (_, false) => lines.OrderBy(x => x.receipt.BusinessDate).ThenBy(x => x.receipt.Id).ThenBy(x => x.line.LineNo),
            _ => lines.OrderByDescending(x => x.receipt.BusinessDate).ThenByDescending(x => x.receipt.Id).ThenBy(x => x.line.LineNo)
        };
        var saved = await lines.Select(x => new
        {
            x.receipt.DocumentNo, x.receipt.BusinessDate, x.receipt.Status, x.receipt.PurchaseOrderId,
            x.receipt.SupplierId, x.receipt.Currency, x.line.LineNo, x.line.MaterialId, x.line.Quantity,
            x.line.WarehouseId, x.line.UnitCost, x.line.PostedReceived
        }).ToListAsync(token);
        var orderIds = saved.Select(x => x.PurchaseOrderId).Distinct().ToArray();
        var orders = await db.Set<PurchaseOrderSource>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DocumentNo, token);
        var csv = new StringBuilder(PurchaseReceiptCsv.HeaderLine(language)).Append("\r\n");
        foreach (var x in saved)
            csv.Append(PurchaseReceiptCsv.Line(new PurchaseReceiptCsvRow(x.DocumentNo, x.BusinessDate, x.Status,
                orders.GetValueOrDefault(x.PurchaseOrderId), x.SupplierId, x.Currency, x.LineNo, x.MaterialId,
                x.Quantity ?? 0m, x.WarehouseId ?? 0, x.UnitCost ?? 0m, x.PostedReceived), language)).Append("\r\n");
        await tx.CommitAsync(token);
        if (onReady is not null) await onReady(count);
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), token);
        await output.WriteAsync(Encoding.UTF8.GetBytes(csv.ToString()), token);
        return count;
    }

    private async Task<(IQueryable<PurchaseReceiptSource> Query, long CompanyId)> FilterAsync(
        PurchaseReceiptQuery request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var companyId = access.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.PurchaseOrderId is <= 0)
            throw new RequestValidationException("purchaseOrderId", "Purchase order must be positive.");
        if (request.Status is not null && !Statuses.Contains(request.Status))
            throw new RequestValidationException("status", "Unknown purchase receipt status.");
        if (SortSpec.Parse(request.Sort, SortFields, DefaultSort).Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        var scope = await ScopeAsync(companyId, PurchasingPermissions.ReceiptView, token);
        var query = ApplyScope(db.Set<PurchaseReceiptSource>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DocumentNo != null && x.BusinessDate != null), scope);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo!.Contains(keyword) ||
                db.Set<PurchaseOrderSource>().IgnoreQueryFilters().Any(o => o.CompanyId == companyId
                    && o.Id == x.PurchaseOrderId && o.DocumentNo != null && o.DocumentNo.Contains(keyword)));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.PurchaseOrderId is { } orderId) query = query.Where(x => x.PurchaseOrderId == orderId);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        return (query, companyId);
    }

    private static IQueryable<PurchaseReceiptSource> Sort(IQueryable<PurchaseReceiptSource> query,
        PurchaseReceiptQuery request)
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
            ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase receipt permission is required.");
    }

    internal static IQueryable<PurchaseReceiptSource> ApplyScope(IQueryable<PurchaseReceiptSource> query,
        DataScopeFilter scope) => scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
}
