using System.Data;
using System.Text;
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

/// <summary>
/// A saved invoice line. <c>Differences</c> is filled only while the invoice is Disputed: it is the current
/// BR-PUR-038 comparison with the saved order price and tax rate, i.e. exactly the lines RESOLVE must decide.
/// </summary>
public sealed record PurchaseInvoiceLineDetail(long Id, int LineNo, long ReceiptLineId, decimal Quantity,
    decimal UnitPrice, decimal TaxRatePercent, decimal Net, decimal Tax, decimal Gross,
    IReadOnlyList<PurchaseInvoiceAmountDifference> Differences);

/// <summary>One immutable RESOLVE decision; <c>InvoiceVersion</c> is the version that was resolved.</summary>
public sealed record PurchaseInvoiceResolutionDetail(long Id, int InvoiceVersion, int LineNo,
    string Action, string Reason, decimal NetDifference, decimal TaxDifference, decimal GrossDifference,
    long DecidedBy, DateTime DecidedAt);

/// <summary>Saved invoice detail; the posting time and FI source / payable / journal IDs are set only once Posted.</summary>
public sealed record PurchaseInvoiceDetail(long Id, long SupplierId, long PurchaseOrderId,
    string SupplierInvoiceNumber, DateOnly BusinessDate, string Currency, PurchaseInvoiceStatus Status,
    decimal TotalNet, decimal TotalTax, decimal TotalGross, DateTime CreatedAt, long CreatedBy,
    DateTime? UpdatedAt, int Version, IReadOnlyList<PurchaseInvoiceLineDetail> Lines,
    IReadOnlyList<PurchaseInvoiceResolutionDetail> Resolutions,
    DateTime? PostedAt, long? FiPurchaseSourceId, long? FiPayableId, long? FiJournalId);

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
        var (query, sort) = await FilterAsync(request, PurchasingPermissions.InvoiceView, token);
        // Id is always the final tie-breaker so equal sort keys page deterministically.
        query = (sort.Field, sort.Descending) switch
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

    /// <summary>
    /// Streams every saved line of the filtered invoices as CSV (REQ_F4114.8.1, 介面 4.19 匯出) under the company-wide
    /// PUR.INVOICE.EXPORT grant, inside one snapshot so the reported count equals the rows written. Paging is
    /// ignored; invoices keep the list order and their lines follow the saved line number. Over-limit fails
    /// with PUR.INVOICE.EXPORT_LIMIT before any byte is written.
    /// </summary>
    public async Task<long> ExportAsync(PurchaseInvoiceQuery request, string language, Stream output,
        Func<long, Task>? onReady = null, int maxRows = PurchaseInvoiceCsv.MaxRows,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var (invoices, sort) = await FilterAsync(request, PurchasingPermissions.InvoiceExport, token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var lines = from invoice in invoices
            join line in db.Set<PurchaseInvoiceLine>().AsNoTracking()
                on new { invoice.CompanyId, InvoiceId = invoice.Id } equals new { line.CompanyId, line.InvoiceId }
            select new { invoice, line };
        var count = await lines.LongCountAsync(token);
        PurchaseInvoiceCsv.EnsureWithinLimit(count, maxRows);
        if (onReady is not null) await onReady(count);
        lines = (sort.Field, sort.Descending) switch
        {
            ("supplierInvoiceNumber", false) => lines.OrderBy(x => x.invoice.SupplierInvoiceNumber).ThenBy(x => x.invoice.Id).ThenBy(x => x.line.LineNo),
            ("supplierInvoiceNumber", true) => lines.OrderByDescending(x => x.invoice.SupplierInvoiceNumber).ThenByDescending(x => x.invoice.Id).ThenBy(x => x.line.LineNo),
            ("totalGross", false) => lines.OrderBy(x => x.invoice.TotalGross).ThenBy(x => x.invoice.Id).ThenBy(x => x.line.LineNo),
            ("totalGross", true) => lines.OrderByDescending(x => x.invoice.TotalGross).ThenByDescending(x => x.invoice.Id).ThenBy(x => x.line.LineNo),
            (_, false) => lines.OrderBy(x => x.invoice.BusinessDate).ThenBy(x => x.invoice.Id).ThenBy(x => x.line.LineNo),
            _ => lines.OrderByDescending(x => x.invoice.BusinessDate).ThenByDescending(x => x.invoice.Id).ThenBy(x => x.line.LineNo)
        };
        var rows = lines.Select(x => new PurchaseInvoiceCsvRow(x.invoice.SupplierInvoiceNumber,
            x.invoice.BusinessDate, x.invoice.Status, x.invoice.SupplierId, x.invoice.PurchaseOrderId,
            x.invoice.Currency, x.line.LineNo, x.line.ReceiptLineId, x.line.Quantity, x.line.UnitPrice,
            x.line.TaxRatePercent, x.line.Net, x.line.Tax, x.line.Gross));
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), token);
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteAsync((PurchaseInvoiceCsv.HeaderLine(language) + CsvNewLine).AsMemory(), token);
        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(token))
            await writer.WriteAsync((PurchaseInvoiceCsv.Line(row, language) + CsvNewLine).AsMemory(), token);
        await writer.FlushAsync(token);
        await tx.CommitAsync(token);
        return count;
    }

    private const string CsvNewLine = "\r\n";

    /// <summary>Validates the list filters and applies them after the given company-wide grant is confirmed.</summary>
    private async Task<(IQueryable<PurchaseInvoice> Query, SortField Sort)> FilterAsync(
        PurchaseInvoiceQuery request, string permission, CancellationToken token)
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
        await RequireCompanyGrantAsync(companyId, permission, token);

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
        return (query, sorts[0]);
    }

    public async Task<PurchaseInvoiceDetail> GetAsync(long id, CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        await RequireCompanyGrantAsync(companyId, PurchasingPermissions.InvoiceView, token);
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
        var lines = invoice.Lines.OrderBy(x => x.LineNo).ToArray();
        var differences = invoice.Status == PurchaseInvoiceStatus.Disputed
            ? await DifferencesAsync(companyId, lines, token)
            : new Dictionary<int, IReadOnlyList<PurchaseInvoiceAmountDifference>>();
        return new PurchaseInvoiceDetail(invoice.Id, invoice.SupplierId, invoice.PurchaseOrderId,
            invoice.SupplierInvoiceNumber, invoice.BusinessDate, invoice.Currency, invoice.Status,
            invoice.TotalNet, invoice.TotalTax, invoice.TotalGross, invoice.CreatedAt, invoice.CreatedBy,
            invoice.UpdatedAt, invoice.Version,
            [.. lines.Select(x => new PurchaseInvoiceLineDetail(x.Id, x.LineNo, x.ReceiptLineId, x.Quantity,
                x.UnitPrice, x.TaxRatePercent, x.Net, x.Tax, x.Gross,
                differences.GetValueOrDefault(x.LineNo) ?? []))],
            resolutions, invoice.PostedAt, invoice.FiPurchaseSourceId, invoice.FiPayableId, invoice.FiJournalId);
    }

    /// <summary>
    /// Read-only BR-PUR-038 comparison against the saved order price and tax rate (no lock, no write).
    /// RESOLVE recomputes the same comparison under its transaction lock, so a stale or missing source here
    /// only means no differences are shown; RESOLVE then reports the authoritative 422 reason.
    /// </summary>
    private async Task<Dictionary<int, IReadOnlyList<PurchaseInvoiceAmountDifference>>> DifferencesAsync(
        long companyId, IReadOnlyList<PurchaseInvoiceLine> lines, CancellationToken token)
    {
        var ids = lines.Select(x => x.ReceiptLineId).Distinct().ToArray();
        var prices = await (from receipt in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
            join order in db.Set<PurchaseOrderSourceLine>().AsNoTracking()
                on new { receipt.CompanyId, Id = receipt.PurchaseOrderLineId }
                equals new { order.CompanyId, order.Id }
            where receipt.CompanyId == companyId && ids.Contains(receipt.Id)
            select new { ReceiptLineId = receipt.Id, order.UnitPrice, order.TaxRatePercent })
            .ToDictionaryAsync(x => x.ReceiptLineId, token);
        if (lines.Any(x => !prices.ContainsKey(x.ReceiptLineId)))
            return [];
        try
        {
            var decision = PurchaseInvoiceAmountMatch.Check([.. lines.Select(x =>
            {
                var price = prices[x.ReceiptLineId];
                return new PurchaseInvoiceAmountLine(x.LineNo, x.Quantity, price.UnitPrice,
                    price.TaxRatePercent, x.UnitPrice, x.TaxRatePercent, x.Net, x.Tax, x.Gross);
            })]);
            return decision.Lines.ToDictionary(x => x.LineNo, x => x.Differences);
        }
        catch (BusinessRuleException)
        {
            return [];
        }
    }

    private async Task RequireCompanyGrantAsync(long companyId, string permission, CancellationToken token)
    {
        var grant = await authorization.GetAsync(token);
        if (grant is null || grant.CompanyId != companyId ||
            grant.FilterFor(permission)?.EffectiveScope != DataScope.Company)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", $"Company-wide {permission} permission is required.");
    }
}
