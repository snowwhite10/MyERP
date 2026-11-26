using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Invoice list filters; keyword matches the supplier invoice number.</summary>
public sealed record PurchaseInvoiceQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public PurchaseInvoiceStatus? Status { get; init; }
    public long? SupplierId { get; init; }
    public long? PurchaseOrderId { get; init; }
}

public sealed record PurchaseInvoiceSummary(long Id, long SupplierId, long PurchaseOrderId,
    string SupplierInvoiceNumber, DateOnly BusinessDate, string Currency, PurchaseInvoiceStatus Status,
    decimal TotalNet, decimal TotalTax, decimal TotalGross, int LineCount, int Version);

public sealed record PurchaseInvoiceLineDetail(long Id, int LineNo, long ReceiptLineId, decimal Quantity,
    decimal UnitPrice, decimal TaxRatePercent, decimal Net, decimal Tax, decimal Gross);

/// <summary>One immutable RESOLVE decision; <c>InvoiceVersion</c> is the version that was resolved.</summary>
public sealed record PurchaseInvoiceResolutionDetail(long Id, int InvoiceVersion, int LineNo,
    string Action, string Reason, decimal NetDifference, decimal TaxDifference, decimal GrossDifference,
    long DecidedBy, DateTime DecidedAt);

public sealed record PurchaseInvoiceDetail(long Id, long SupplierId, long PurchaseOrderId,
    string SupplierInvoiceNumber, DateOnly BusinessDate, string Currency, PurchaseInvoiceStatus Status,
    decimal TotalNet, decimal TotalTax, decimal TotalGross, DateTime CreatedAt, long CreatedBy,
    DateTime? UpdatedAt, int Version, IReadOnlyList<PurchaseInvoiceLineDetail> Lines,
    IReadOnlyList<PurchaseInvoiceResolutionDetail> Resolutions);

/// <summary>
/// Read-only invoice queries (REQ_F4114.8.1). Invoices carry no branch, department or owner, so only a
/// company-wide PUR.INVOICE.VIEW grant may read them, matching the invoice write actions.
/// </summary>
public sealed class PurchaseInvoiceQueryService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization)
{
    private static readonly string[] SortFields =
        ["businessDate", "supplierInvoiceNumber", "totalGross"];

    public async Task<PagedResult<PurchaseInvoiceSummary>> ListAsync(PurchaseInvoiceQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var companyId = access.RequireCompanyId();
        request.EnsureValid();
        if (request.From > request.To)
            throw new RequestValidationException("to", "End date must not precede start date.");
        if (request.SupplierId is <= 0)
            throw new RequestValidationException("supplierId", "Supplier must be positive.");
        if (request.PurchaseOrderId is <= 0)
            throw new RequestValidationException("purchaseOrderId", "Purchase order must be positive.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, SortFields, "-businessDate");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        await RequireViewAsync(companyId, token);

        var query = db.Set<PurchaseInvoice>().AsNoTracking().Where(x => x.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.SupplierInvoiceNumber.Contains(keyword));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } state) query = query.Where(x => x.Status == state);
        if (request.SupplierId is { } supplier) query = query.Where(x => x.SupplierId == supplier);
        if (request.PurchaseOrderId is { } order) query = query.Where(x => x.PurchaseOrderId == order);
        // Id is always the final tie-breaker so equal sort keys page deterministically.
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("supplierInvoiceNumber", false) => query.OrderBy(x => x.SupplierInvoiceNumber).ThenBy(x => x.Id),
            ("supplierInvoiceNumber", true) => query.OrderByDescending(x => x.SupplierInvoiceNumber).ThenByDescending(x => x.Id),
            ("totalGross", false) => query.OrderBy(x => x.TotalGross).ThenBy(x => x.Id),
            ("totalGross", true) => query.OrderByDescending(x => x.TotalGross).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new PurchaseInvoiceSummary(x.Id, x.SupplierId, x.PurchaseOrderId,
                x.SupplierInvoiceNumber, x.BusinessDate, x.Currency, x.Status, x.TotalNet, x.TotalTax,
                x.TotalGross, x.Lines.Count, x.Version)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<PurchaseInvoiceDetail> GetAsync(long id, CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        await RequireViewAsync(companyId, token);
        var invoice = await db.Set<PurchaseInvoice>().AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, token)
            ?? throw new NotFoundException("PUR.INVOICE.NOT_FOUND", "Purchase invoice", id);
        var resolutions = await db.Set<PurchaseInvoiceResolutionRecord>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.InvoiceId == id)
            .OrderBy(x => x.InvoiceVersion).ThenBy(x => x.LineNo).ThenBy(x => x.Id)
            .Select(x => new PurchaseInvoiceResolutionDetail(x.Id, x.InvoiceVersion, x.LineNo, x.Action,
                x.Reason, x.NetDifference, x.TaxDifference, x.GrossDifference, x.CreatedBy, x.CreatedAt))
            .ToListAsync(token);
        return new PurchaseInvoiceDetail(invoice.Id, invoice.SupplierId, invoice.PurchaseOrderId,
            invoice.SupplierInvoiceNumber, invoice.BusinessDate, invoice.Currency, invoice.Status,
            invoice.TotalNet, invoice.TotalTax, invoice.TotalGross, invoice.CreatedAt, invoice.CreatedBy,
            invoice.UpdatedAt, invoice.Version,
            [.. invoice.Lines.OrderBy(x => x.LineNo).Select(x => new PurchaseInvoiceLineDetail(x.Id,
                x.LineNo, x.ReceiptLineId, x.Quantity, x.UnitPrice, x.TaxRatePercent, x.Net, x.Tax, x.Gross))],
            resolutions);
    }

    private async Task RequireViewAsync(long companyId, CancellationToken token)
    {
        var grant = await authorization.GetAsync(token);
        if (grant is null || grant.CompanyId != companyId ||
            grant.FilterFor(PurchasingPermissions.InvoiceView)?.EffectiveScope != DataScope.Company)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Company-wide invoice view permission is required.");
    }
}
