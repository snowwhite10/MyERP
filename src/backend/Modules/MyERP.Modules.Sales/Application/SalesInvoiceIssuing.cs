using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.Modules.Sales.Application;

/// <summary>Saved invoice plus whether it was an identical replay of an earlier request (no new change).</summary>
public sealed record SalesInvoiceIssueOutcome(SalesInvoice Invoice, bool Replayed);

/// <summary>
/// WU-SD-007a2 (REQ_F4112.5.1, BR-SD-037～040, ADR-177): the SD half of ISSUE. Inside the caller's open transaction it
/// resolves the requested Posted delivery lines, replays an identical request key, takes the source locks
/// (order row, then delivery lines in ascending ID), rechecks the uninvoiced balance of every line, allocates the saved
/// order-line amounts and saves one <see cref="SalesInvoiceStatus.Issuing"/> invoice with a permanent number.
/// It never calls FI and never commits: WU-SD-007a3 wraps it with the FI credit lock (through
/// <c>beforeSourceLocks</c>), the FI/AR posting call and the Issuing → Posted update in the same transaction.
/// </summary>
public sealed class SalesInvoiceIssuing(MyErpDbContext db, SalesActionAccess access, SalesCompanyAccess company,
    INumberGenerator numbers)
{
    public const string DocumentType = "SAL.INVOICE";
    public const string IssuePermission = "SAL.INVOICE.ISSUE";
    public const string SourceNotFound = "SAL.INVOICE.SOURCE_NOT_FOUND";
    public const string DeliveryNotPosted = "SAL.INVOICE.DELIVERY_NOT_POSTED";
    public const string SourceMixed = "SAL.INVOICE.SOURCE_MIXED";
    public const string OrderNotApproved = "SAL.INVOICE.ORDER_NOT_APPROVED";
    public const string DateBeforeDelivery = "SAL.INVOICE.DATE_BEFORE_DELIVERY";
    public const string BalanceExceeded = "SAL.INVOICE.BALANCE_EXCEEDED";
    public const string RequestConflict = "SAL.INVOICE.REQUEST_CONFLICT";

    private sealed record SourceLine(long DeliveryLineId, long DeliveryId, long OrderLineId, long ItemId,
        decimal Quantity, long OrderId, SalesDeliveryStatus Status, bool IsDeleted, DateOnly DeliveryDate);

    private sealed record BilledQuantity(long DeliveryLineId, decimal Quantity);

    private sealed record OrderLineBilling(long OrderLineId, decimal Quantity, decimal OriginalNet,
        decimal OriginalTax, decimal BaseNet, decimal BaseTax);

    /// <param name="input">Validated ISSUE request; amounts, customer and currency come from the saved sources.</param>
    /// <param name="beforeSourceLocks">
    /// Called once with the source customer ID after the unlocked source resolution and before any SD lock, so the
    /// caller can take the FI customer credit lock first (BR-SD-041). Not called on replay or validation failure.
    /// </param>
    /// <param name="token">Cancellation token.</param>
    public async Task<SalesInvoiceIssueOutcome> SaveIssuingAsync(SalesInvoiceIssueInput input,
        Func<long, CancellationToken, Task>? beforeSourceLocks = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Invoice issuing must run inside the caller's transaction.");
        input.Validate();
        var scope = await access.RequireScopeAsync(IssuePermission, token);
        var companyId = company.RequireCompanyId();
        var fingerprint = input.Fingerprint();
        var lines = input.Lines!.OrderBy(x => x.LineNo).ToArray();

        if (await ReplayAsync(input.RequestKey!, fingerprint, scope, token) is { } replay)
            return replay;

        // Unlocked resolution: names hidden or mixed sources before taking any lock.
        var sources = await LoadSourcesAsync(companyId, lines, token);
        var orderId = sources.Values.First().OrderId;
        var mixed = lines.FirstOrDefault(x => sources[x.DeliveryLineId].OrderId != orderId);
        if (mixed is not null)
            throw LineError(SourceMixed, "All invoice lines must come from deliveries of one sales order.", mixed.LineNo);
        var customerId = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
            .Where(x => x.Id == orderId).Select(x => (long?)x.CustomerId).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(SourceNotFound, "Sales invoice source", lines[0].DeliveryLineId);

        if (beforeSourceLocks is not null)
            await beforeSourceLocks(customerId, token);
        await LockSourcesAsync(companyId, orderId, sources.Keys, token);

        // A concurrent identical request may have committed while this one waited for the locks.
        if (await ReplayAsync(input.RequestKey!, fingerprint, scope, token) is { } late)
            return late;

        sources = await LoadSourcesAsync(companyId, lines, token);
        var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
            .Where(x => x.Id == orderId).Include(x => x.Lines).AsSplitQuery().SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(SourceNotFound, "Sales invoice source", lines[0].DeliveryLineId);
        Check(input.BusinessDate, lines, sources, order);
        await CheckBalancesAsync(companyId, lines, sources, token);

        var invoice = await BuildAsync(companyId, input, fingerprint, lines, sources, order, token);
        invoice.DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token);
        db.Add(invoice);
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateException error) when (IsRequestKeyDuplicate(error))
        {
            // Same key raced in from another order's sources: the stored payload necessarily differs.
            throw new ConflictException(RequestConflict, "The request key was already used with different content.");
        }
        return new SalesInvoiceIssueOutcome(invoice, false);
    }

    private async Task<SalesInvoiceIssueOutcome?> ReplayAsync(string requestKey, string fingerprint,
        MyERP.Shared.Security.DataScopeFilter scope, CancellationToken token)
    {
        var existing = await db.Set<SalesInvoice>().AsNoTracking().Include(x => x.Lines)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.RequestKey == requestKey, token);
        if (existing is null)
            return null;
        // A different payload, or an invoice whose order this user cannot see, never reveals the saved invoice.
        var visible = await scope.Apply(db.Set<SalesOrder>()).AnyAsync(x => x.Id == existing.OrderId, token);
        if (!visible || !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new ConflictException(RequestConflict, "The request key was already used with different content.");
        existing.Lines.Sort((a, b) => a.LineNo.CompareTo(b.LineNo));
        return new SalesInvoiceIssueOutcome(existing, true);
    }

    /// <summary>
    /// Reads the requested delivery lines with their headers, including soft-deleted headers so they are named as
    /// ineligible rather than hidden. A missing or foreign-company line is a 404 naming no detail.
    /// </summary>
    private async Task<Dictionary<long, SourceLine>> LoadSourcesAsync(long companyId,
        IReadOnlyList<SalesInvoiceLineInput> lines, CancellationToken token)
    {
        var ids = lines.Select(x => x.DeliveryLineId).ToArray();
        var found = await (
            from line in db.Set<SalesDeliveryLine>().AsNoTracking()
            join delivery in db.Set<SalesDelivery>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AsNoTracking()
                on new { line.CompanyId, Id = line.DeliveryId } equals new { delivery.CompanyId, delivery.Id }
            where line.CompanyId == companyId && ids.Contains(line.Id)
            select new SourceLine(line.Id, line.DeliveryId, line.OrderLineId, line.ItemId, line.Quantity,
                delivery.OrderId, delivery.Status, delivery.IsDeleted, delivery.BusinessDate))
            .ToDictionaryAsync(x => x.DeliveryLineId, token);
        var missing = lines.FirstOrDefault(x => !found.ContainsKey(x.DeliveryLineId));
        if (missing is not null)
            throw new NotFoundException(SourceNotFound, "Sales invoice source", missing.DeliveryLineId);
        return found;
    }

    /// <summary>Order row first (the lock delivery submission and cancellation also take), then lines ascending.</summary>
    private async Task LockSourcesAsync(long companyId, long orderId, IEnumerable<long> deliveryLineIds,
        CancellationToken token)
    {
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0", orderId, companyId).ToListAsync(token);
        // One primary-key equality seek per line, ascending: a set-based join under HOLDLOCK took a company-wide
        // key-range lock and blocked issuers of other orders' delivery lines (SD-INVOICE-ISSUE-005).
        foreach (var id in deliveryLineIds.Order())
            await db.Database.SqlQuery<long>($"""
                SELECT [id] AS [Value] FROM dbo.sal_delivery_line WITH (UPDLOCK, HOLDLOCK, FORCESEEK)
                WHERE [id] = {id} AND [company_id] = {companyId}
                """).ToListAsync(token);
    }

    private static void Check(DateOnly businessDate, IReadOnlyList<SalesInvoiceLineInput> lines,
        IReadOnlyDictionary<long, SourceLine> sources, SalesOrder order)
    {
        if (order.Status != SalesOrderStatus.Approved)
            throw new BusinessRuleException(OrderNotApproved, "Only deliveries of an approved order can be invoiced.");
        foreach (var line in lines)
        {
            var source = sources[line.DeliveryLineId];
            if (source.OrderId != order.Id)
                throw LineError(SourceMixed, "All invoice lines must come from deliveries of one sales order.", line.LineNo);
            if (source.IsDeleted || source.Status != SalesDeliveryStatus.Posted)
                throw LineError(DeliveryNotPosted, "Only lines of a Posted delivery can be invoiced.", line.LineNo);
            if (businessDate < source.DeliveryDate)
                throw LineError(DateBeforeDelivery, "The invoice date cannot be earlier than the delivery date.",
                    line.LineNo);
            if (order.Lines.All(x => x.Id != source.OrderLineId))
                throw LineError(SalesInvoiceAllocation.SourceInvalid,
                    "The delivery line does not reference a line of its order.", line.LineNo);
        }
    }

    /// <summary>
    /// BR-SD-038: requested quantity ≤ saved Posted quantity − quantity on every saved invoice line of that delivery
    /// line (invoices are never deleted). Read under the delivery-line locks, so the result is final.
    /// </summary>
    private async Task CheckBalancesAsync(long companyId, IReadOnlyList<SalesInvoiceLineInput> lines,
        IReadOnlyDictionary<long, SourceLine> sources, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(lines.Select(x => x.DeliveryLineId));
        var billed = await db.Database.SqlQuery<BilledQuantity>($"""
            SELECT l.delivery_line_id, SUM(l.quantity) AS quantity
            FROM OPENJSON({json}) WITH ([id] bigint '$') j
            JOIN dbo.sal_invoice_line l WITH (FORCESEEK)
              ON l.company_id = {companyId} AND l.delivery_line_id = j.[id]
            GROUP BY l.delivery_line_id
            """).ToDictionaryAsync(x => x.DeliveryLineId, x => x.Quantity, token);
        foreach (var line in lines)
        {
            var remaining = sources[line.DeliveryLineId].Quantity - billed.GetValueOrDefault(line.DeliveryLineId);
            if (line.Quantity > remaining)
                throw new BusinessRuleException(BalanceExceeded,
                    "Invoice quantity exceeds the uninvoiced quantity of the delivery line.")
                {
                    Extensions = new Dictionary<string, object?>
                    {
                        ["lineNo"] = line.LineNo, ["remaining"] = Math.Max(remaining, 0m)
                    }
                };
        }
    }

    private async Task<SalesInvoice> BuildAsync(long companyId, SalesInvoiceIssueInput input, string fingerprint,
        SalesInvoiceLineInput[] lines, IReadOnlyDictionary<long, SourceLine> sources, SalesOrder order,
        CancellationToken token)
    {
        var orderLineIds = lines.Select(x => sources[x.DeliveryLineId].OrderLineId).Distinct().ToArray();
        var json = JsonSerializer.Serialize(orderLineIds);
        var prior = await db.Database.SqlQuery<OrderLineBilling>($"""
            SELECT l.order_line_id, SUM(l.quantity) AS quantity, SUM(l.original_net) AS original_net,
                   SUM(l.original_tax) AS original_tax, SUM(l.base_net) AS base_net, SUM(l.base_tax) AS base_tax
            FROM OPENJSON({json}) WITH ([id] bigint '$') j
            JOIN dbo.sal_invoice_line l WITH (FORCESEEK)
              ON l.company_id = {companyId} AND l.order_line_id = j.[id]
            GROUP BY l.order_line_id
            """).ToListAsync(token);
        var orderLines = order.Lines.ToDictionary(x => x.Id);
        var amounts = SalesInvoiceAllocation.Allocate(order.CurrencyCode, order.BaseCurrencyCode,
            [.. orderLineIds.Select(id => orderLines[id]).Select(x => new SalesInvoiceOrderLineSnapshot(x.Id,
                x.Quantity, x.OriginalNet, x.OriginalTax, x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross))],
            [.. prior.Select(x => new SalesInvoicePriorBilling(x.OrderLineId, x.Quantity, x.OriginalNet,
                x.OriginalTax, x.BaseNet, x.BaseTax))],
            [.. lines.Select(x => new SalesInvoicePortion(x.LineNo, sources[x.DeliveryLineId].OrderLineId, x.Quantity))]);

        var invoice = new SalesInvoice
        {
            CompanyId = companyId,
            DocumentNo = string.Empty,
            RequestKey = input.RequestKey!,
            RequestFingerprint = fingerprint,
            CustomerId = order.CustomerId,
            OrderId = order.Id,
            BusinessDate = input.BusinessDate,
            CurrencyCode = order.CurrencyCode,
            BaseCurrencyCode = order.BaseCurrencyCode,
            ExchangeRate = order.ExchangeRate,
            Status = SalesInvoiceStatus.Issuing
        };
        for (var i = 0; i < lines.Length; i++)
        {
            var source = sources[lines[i].DeliveryLineId];
            var orderLine = orderLines[source.OrderLineId];
            var amount = amounts[i];
            invoice.Lines.Add(new SalesInvoiceLine
            {
                CompanyId = companyId, LineNo = lines[i].LineNo, DeliveryId = source.DeliveryId,
                DeliveryLineId = source.DeliveryLineId, OrderLineId = source.OrderLineId, ItemId = orderLine.ItemId,
                TaxRateId = orderLine.TaxRateId, TaxRatePercent = orderLine.TaxRatePercent,
                Quantity = lines[i].Quantity, OriginalNet = amount.OriginalNet, OriginalTax = amount.OriginalTax,
                OriginalGross = amount.OriginalGross, BaseNet = amount.BaseNet, BaseTax = amount.BaseTax,
                BaseGross = amount.BaseGross
            });
        }
        // Header totals are the sum of the saved lines (MASTER_PROMPT 6.3); they cannot exceed the saved order totals.
        invoice.OriginalNet = invoice.Lines.Sum(x => x.OriginalNet);
        invoice.OriginalTax = invoice.Lines.Sum(x => x.OriginalTax);
        invoice.OriginalGross = invoice.OriginalNet + invoice.OriginalTax;
        invoice.BaseNet = invoice.Lines.Sum(x => x.BaseNet);
        invoice.BaseTax = invoice.Lines.Sum(x => x.BaseTax);
        invoice.BaseGross = invoice.BaseNet + invoice.BaseTax;
        return invoice;
    }

    private static bool IsRequestKeyDuplicate(DbUpdateException error) =>
        error.InnerException is SqlException { Number: 2601 or 2627 } sql &&
        sql.Message.Contains("request_key", StringComparison.OrdinalIgnoreCase);

    private static BusinessRuleException LineError(string code, string message, int lineNo) =>
        new(code, message) { Extensions = new Dictionary<string, object?> { ["lineNo"] = lineNo } };
}
