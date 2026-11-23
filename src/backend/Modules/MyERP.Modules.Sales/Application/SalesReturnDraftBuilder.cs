using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// WU-SD-008a2b1 (ADR-185/186, BR-SD-044/046/047): the Draft checks shared by create and edit — source resolution, the
/// delivery-line and invoice-line locks, eligibility and balance checks against committed Posted returns, the
/// remaining-proportional amount snapshot and the MD destination guard. Callers own the transaction, the order and
/// return-row locks, the data scope and the request-key rules.
/// </summary>
public sealed class SalesReturnDraftBuilder(MyErpDbContext db, IInventoryReferenceGuard references)
{
    private sealed record SourceLine(long DeliveryLineId, long DeliveryId, long OrderLineId, long ItemId,
        decimal Quantity, long? OutboundMovementId, long OrderId, SalesDeliveryStatus Status, bool IsDeleted,
        DateOnly DeliveryDate);

    private sealed record InvoiceSource(long InvoiceLineId, long InvoiceId, long DeliveryLineId, decimal Quantity,
        decimal OriginalNet, decimal OriginalTax, decimal BaseNet, decimal BaseTax, SalesInvoiceStatus Status,
        DateOnly InvoiceDate);

    private sealed record ReturnedQuantity(long DeliveryLineId, decimal Quantity, decimal UnbilledQuantity);

    private sealed record KeyedQuantity(long Id, decimal Quantity);

    private sealed record Consumed(long Id, decimal Quantity, decimal OriginalNet, decimal OriginalTax,
        decimal BaseNet, decimal BaseTax);

    /// <summary>
    /// Unlocked resolution of the single source order. Unknown or other-company lines are 404; a line of another order
    /// than the first line (or than <paramref name="expectedOrderId"/> on edit) is <c>SOURCE_MIXED</c> naming it.
    /// </summary>
    public async Task<long> ResolveOrderAsync(long companyId, IReadOnlyList<SalesReturnLineInput> lines,
        long? expectedOrderId, CancellationToken token)
    {
        var sources = await LoadSourcesAsync(companyId, lines, token);
        var orderId = expectedOrderId ?? sources[lines[0].DeliveryLineId].OrderId;
        if (lines.FirstOrDefault(x => sources[x.DeliveryLineId].OrderId != orderId) is { } mixed)
            throw LineError(SalesReturnCreateService.SourceMixed,
                "All return lines must come from deliveries of one sales order.", mixed.LineNo);
        return orderId;
    }

    /// <summary>BR-SD-047 lock order starts with the order row (the FI credit lock is taken only on submission).</summary>
    public Task LockOrderAsync(long companyId, long orderId, CancellationToken token) =>
        db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0", orderId, companyId).ToListAsync(token);

    /// <summary>
    /// Delivery lines, then invoice lines, each ascending by primary-key seek — the same delivery-line lock invoice
    /// issuance takes. Called after the order (and, on edit, the return) row lock.
    /// </summary>
    public async Task LockLinesAsync(long companyId, IReadOnlyList<SalesReturnLineInput> lines, CancellationToken token)
    {
        foreach (var id in lines.Select(x => x.DeliveryLineId).Distinct().Order())
            await db.Database.SqlQuery<long>($"""
                SELECT [id] AS [Value] FROM dbo.sal_delivery_line WITH (UPDLOCK, HOLDLOCK, FORCESEEK)
                WHERE [id] = {id} AND [company_id] = {companyId}
                """).ToListAsync(token);
        foreach (var id in InvoiceLineIds(lines).Order())
            await db.Database.SqlQuery<long>($"""
                SELECT [id] AS [Value] FROM dbo.sal_invoice_line WITH (UPDLOCK, HOLDLOCK, FORCESEEK)
                WHERE [id] = {id} AND [company_id] = {companyId}
                """).ToListAsync(token);
    }

    /// <summary>
    /// Under the locks: re-reads the sources, checks eligibility and balances, and returns the snapshot lines ordered by
    /// line number. <paramref name="order"/> is the scoped source order with its lines.
    /// </summary>
    public async Task<List<SalesReturnLine>> BuildLinesAsync(long companyId, SalesOrder order, DateOnly businessDate,
        IReadOnlyList<SalesReturnLineInput> requested, CancellationToken token)
    {
        var lines = requested.OrderBy(x => x.LineNo).ToArray();
        var sources = await LoadSourcesAsync(companyId, lines, token);
        var invoices = await LoadInvoiceLinesAsync(companyId, InvoiceLineIds(lines), token);
        await CheckSourcesAsync(companyId, businessDate, lines, sources, invoices, order, token);
        await CheckBalancesAsync(companyId, lines, sources, invoices, token);
        return await SnapshotAsync(companyId, lines, sources, invoices, order, token);
    }

    /// <summary>Header totals are the sum of the saved lines (MASTER_PROMPT 6.3).</summary>
    public static void ApplyTotals(SalesReturn saved)
    {
        saved.OriginalNet = saved.Lines.Sum(x => x.OriginalNet);
        saved.OriginalTax = saved.Lines.Sum(x => x.OriginalTax);
        saved.OriginalGross = saved.OriginalNet + saved.OriginalTax;
        saved.BaseNet = saved.Lines.Sum(x => x.BaseNet);
        saved.BaseTax = saved.Lines.Sum(x => x.BaseTax);
        saved.BaseGross = saved.BaseNet + saved.BaseTax;
    }

    /// <summary>
    /// Validates each distinct item/warehouse/location through MD (active warehouse, location of that warehouse),
    /// keeps them shared-locked and saves inside the innermost check. A lot/serial tracked item is refused until the
    /// tracked-return follow-up (ADR-184 §6).
    /// </summary>
    public Task GuardDestinationsAsync(IEnumerable<SalesReturnLine> lines, Func<Task> save, CancellationToken token)
    {
        var sets = lines
            .GroupBy(x => (x.ItemId, x.WarehouseId, x.LocationId))
            .OrderBy(x => x.Key.ItemId).ThenBy(x => x.Key.WarehouseId).ThenBy(x => x.Key.LocationId ?? 0)
            .ToArray();
        return GuardAsync(sets, 0, save, token);
    }

    private static long[] InvoiceLineIds(IEnumerable<SalesReturnLineInput> lines) =>
        lines.SelectMany(x => x.Billed ?? []).Select(x => x.InvoiceLineId).Distinct().ToArray();

    /// <summary>
    /// Requested delivery lines with their headers, including soft-deleted headers so they are named as ineligible
    /// rather than hidden. A missing or foreign-company line is a 404 naming no detail.
    /// </summary>
    private async Task<Dictionary<long, SourceLine>> LoadSourcesAsync(long companyId,
        IReadOnlyList<SalesReturnLineInput> lines, CancellationToken token)
    {
        var ids = lines.Select(x => x.DeliveryLineId).ToArray();
        var found = await (
            from line in db.Set<SalesDeliveryLine>().AsNoTracking()
            join delivery in db.Set<SalesDelivery>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AsNoTracking()
                on new { line.CompanyId, Id = line.DeliveryId } equals new { delivery.CompanyId, delivery.Id }
            where line.CompanyId == companyId && ids.Contains(line.Id)
            select new SourceLine(line.Id, line.DeliveryId, line.OrderLineId, line.ItemId, line.Quantity,
                line.InvMovementId, delivery.OrderId, delivery.Status, delivery.IsDeleted, delivery.BusinessDate))
            .ToDictionaryAsync(x => x.DeliveryLineId, token);
        if (lines.FirstOrDefault(x => !found.ContainsKey(x.DeliveryLineId)) is { } missing)
            throw new NotFoundException(SalesReturnCreateService.SourceNotFound, "Sales return source",
                missing.DeliveryLineId);
        return found;
    }

    private async Task<Dictionary<long, InvoiceSource>> LoadInvoiceLinesAsync(long companyId,
        long[] invoiceLineIds, CancellationToken token)
    {
        if (invoiceLineIds.Length == 0)
            return [];
        return await (
            from line in db.Set<SalesInvoiceLine>().AsNoTracking()
            join invoice in db.Set<SalesInvoice>().AsNoTracking()
                on new { line.CompanyId, Id = line.InvoiceId } equals new { invoice.CompanyId, invoice.Id }
            where line.CompanyId == companyId && invoiceLineIds.Contains(line.Id)
            select new InvoiceSource(line.Id, line.InvoiceId, line.DeliveryLineId, line.Quantity, line.OriginalNet,
                line.OriginalTax, line.BaseNet, line.BaseTax, invoice.Status, invoice.BusinessDate))
            .ToDictionaryAsync(x => x.InvoiceLineId, token);
    }

    private async Task CheckSourcesAsync(long companyId, DateOnly businessDate, SalesReturnLineInput[] lines,
        IReadOnlyDictionary<long, SourceLine> sources, IReadOnlyDictionary<long, InvoiceSource> invoices,
        SalesOrder order, CancellationToken token)
    {
        if (order.Status != SalesOrderStatus.Approved)
            throw new BusinessRuleException(SalesReturnCreateService.OrderNotApproved,
                "Only deliveries of an approved order can be returned.");
        var ids = sources.Keys.ToArray();
        var tracked = (await db.Set<SalesDeliveryLineTracking>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.LineId))
            .Select(x => x.LineId).Distinct().ToListAsync(token)).ToHashSet();
        foreach (var line in lines)
        {
            var source = sources[line.DeliveryLineId];
            if (source.OrderId != order.Id)
                throw LineError(SalesReturnCreateService.SourceMixed,
                    "All return lines must come from deliveries of one sales order.", line.LineNo);
            if (source.IsDeleted || source.Status != SalesDeliveryStatus.Posted)
                throw LineError(SalesReturnCreateService.DeliveryNotPosted,
                    "Only lines of a Posted delivery can be returned.", line.LineNo);
            if (source.OutboundMovementId is null || order.Lines.All(x => x.Id != source.OrderLineId))
                throw SalesReturnAmounts.Invalid(line.LineNo);
            if (tracked.Contains(source.DeliveryLineId))
                throw LineError(SalesReturnCreateService.TrackingUnsupported,
                    "Returns of lot or serial tracked deliveries are not supported yet.", line.LineNo);
            if (businessDate < source.DeliveryDate)
                throw LineError(SalesReturnCreateService.DateBeforeDelivery,
                    "The return date cannot be earlier than the delivery date.", line.LineNo);
            foreach (var portion in (line.Billed ?? []).OrderBy(x => x.InvoiceLineId))
            {
                if (!invoices.TryGetValue(portion.InvoiceLineId, out var invoice) ||
                    invoice.DeliveryLineId != source.DeliveryLineId || invoice.Status != SalesInvoiceStatus.Posted)
                    throw PortionError(SalesReturnCreateService.InvoiceLineInvalid,
                        "The billed portion must reference a Posted invoice line of the same delivery line.",
                        line.LineNo, portion.InvoiceLineId);
                if (businessDate < invoice.InvoiceDate)
                    throw PortionError(SalesReturnCreateService.DateBeforeInvoice,
                        "The return date cannot be earlier than the invoice date.", line.LineNo, portion.InvoiceLineId);
            }
        }
    }

    /// <summary>
    /// BR-SD-044, read under the source locks against committed Posted returns (Drafts, soft-deleted Drafts and Pending
    /// returns reserve nothing): returned ≤ delivered − Posted returned; unbilled ≤ delivered − invoiced − Posted
    /// unbilled; each billed portion ≤ invoice-line quantity − Posted billed portions of that invoice line.
    /// </summary>
    private async Task CheckBalancesAsync(long companyId, SalesReturnLineInput[] lines,
        IReadOnlyDictionary<long, SourceLine> sources, IReadOnlyDictionary<long, InvoiceSource> invoices,
        CancellationToken token)
    {
        var deliveryJson = JsonSerializer.Serialize(sources.Keys);
        var returned = await db.Database.SqlQuery<ReturnedQuantity>($"""
            SELECT rl.delivery_line_id, SUM(rl.quantity) AS quantity, SUM(rl.unbilled_quantity) AS unbilled_quantity
            FROM OPENJSON({deliveryJson}) WITH ([id] bigint '$') j
            JOIN dbo.sal_return_line rl WITH (FORCESEEK)
              ON rl.company_id = {companyId} AND rl.delivery_line_id = j.[id]
            JOIN dbo.sal_return r ON r.company_id = rl.company_id AND r.id = rl.return_id
            WHERE r.status = 'Posted'
            GROUP BY rl.delivery_line_id
            """).ToDictionaryAsync(x => x.DeliveryLineId, token);
        var invoiced = await db.Database.SqlQuery<KeyedQuantity>($"""
            SELECT l.delivery_line_id AS id, SUM(l.quantity) AS quantity
            FROM OPENJSON({deliveryJson}) WITH ([id] bigint '$') j
            JOIN dbo.sal_invoice_line l WITH (FORCESEEK)
              ON l.company_id = {companyId} AND l.delivery_line_id = j.[id]
            GROUP BY l.delivery_line_id
            """).ToDictionaryAsync(x => x.Id, x => x.Quantity, token);
        var credited = await PostedAllocationsAsync(companyId, [.. invoices.Keys], token);

        foreach (var line in lines)
        {
            var source = sources[line.DeliveryLineId];
            var prior = returned.GetValueOrDefault(line.DeliveryLineId);
            var returnable = source.Quantity - (prior?.Quantity ?? 0m);
            if (line.Quantity > returnable)
                throw Exceeded(SalesReturnCreateService.ReturnExceeded,
                    "Return quantity exceeds the returnable quantity of the delivery line.", line.LineNo, returnable);
            var unbilled = source.Quantity - invoiced.GetValueOrDefault(line.DeliveryLineId) -
                (prior?.UnbilledQuantity ?? 0m);
            if (line.UnbilledQuantity > unbilled)
                throw Exceeded(SalesReturnCreateService.UnbilledExceeded,
                    "Unbilled return quantity exceeds the uninvoiced delivered quantity.", line.LineNo, unbilled);
            foreach (var portion in (line.Billed ?? []).OrderBy(x => x.InvoiceLineId))
            {
                var remaining = invoices[portion.InvoiceLineId].Quantity -
                    (credited.GetValueOrDefault(portion.InvoiceLineId)?.Quantity ?? 0m);
                if (portion.Quantity > remaining)
                    throw new BusinessRuleException(SalesReturnCreateService.BilledExceeded,
                        "Billed return quantity exceeds the uncredited quantity of the invoice line.")
                    {
                        Extensions = new Dictionary<string, object?>
                        {
                            ["lineNo"] = line.LineNo, ["invoiceLineId"] = portion.InvoiceLineId,
                            ["remaining"] = Math.Max(remaining, 0m)
                        }
                    };
            }
        }
    }

    private async Task<Dictionary<long, Consumed>> PostedAllocationsAsync(long companyId,
        long[] invoiceLineIds, CancellationToken token)
    {
        if (invoiceLineIds.Length == 0)
            return [];
        var json = JsonSerializer.Serialize(invoiceLineIds);
        return await db.Database.SqlQuery<Consumed>($"""
            SELECT a.invoice_line_id AS id, SUM(a.quantity) AS quantity, SUM(a.original_net) AS original_net,
                   SUM(a.original_tax) AS original_tax, SUM(a.base_net) AS base_net, SUM(a.base_tax) AS base_tax
            FROM OPENJSON({json}) WITH ([id] bigint '$') j
            JOIN dbo.sal_return_invoice_allocation a WITH (FORCESEEK)
              ON a.company_id = {companyId} AND a.invoice_line_id = j.[id]
            JOIN dbo.sal_return_line rl ON rl.company_id = a.company_id AND rl.id = a.return_line_id
            JOIN dbo.sal_return r ON r.company_id = rl.company_id AND r.id = rl.return_id
            WHERE r.status = 'Posted'
            GROUP BY a.invoice_line_id
            """).ToDictionaryAsync(x => x.Id, token);
    }

    /// <summary>
    /// Uninvoiced commitment already consumed per order line: every invoice line plus the unbilled part (line amount
    /// minus its billed allocations) of every Posted return line.
    /// </summary>
    private async Task<Dictionary<long, Consumed>> OrderLineConsumptionAsync(long companyId,
        long[] orderLineIds, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(orderLineIds);
        var rows = await db.Database.SqlQuery<Consumed>($"""
            SELECT l.order_line_id AS id, SUM(l.quantity) AS quantity, SUM(l.original_net) AS original_net,
                   SUM(l.original_tax) AS original_tax, SUM(l.base_net) AS base_net, SUM(l.base_tax) AS base_tax
            FROM OPENJSON({json}) WITH ([id] bigint '$') j
            JOIN dbo.sal_invoice_line l WITH (FORCESEEK)
              ON l.company_id = {companyId} AND l.order_line_id = j.[id]
            GROUP BY l.order_line_id
            UNION ALL
            SELECT rl.order_line_id AS id, SUM(rl.unbilled_quantity) AS quantity,
                   SUM(rl.original_net - ISNULL(a.original_net, 0)) AS original_net,
                   SUM(rl.original_tax - ISNULL(a.original_tax, 0)) AS original_tax,
                   SUM(rl.base_net - ISNULL(a.base_net, 0)) AS base_net,
                   SUM(rl.base_tax - ISNULL(a.base_tax, 0)) AS base_tax
            FROM OPENJSON({json}) WITH ([id] bigint '$') j
            JOIN dbo.sal_return_line rl WITH (FORCESEEK)
              ON rl.company_id = {companyId} AND rl.order_line_id = j.[id]
            JOIN dbo.sal_return r ON r.company_id = rl.company_id AND r.id = rl.return_id
            LEFT JOIN (
                -- Pre-aggregated: SQL Server cannot SUM over an applied aggregate (error 130).
                SELECT x.return_line_id, SUM(x.original_net) AS original_net, SUM(x.original_tax) AS original_tax,
                       SUM(x.base_net) AS base_net, SUM(x.base_tax) AS base_tax
                FROM dbo.sal_return_invoice_allocation x
                WHERE x.company_id = {companyId}
                GROUP BY x.return_line_id) a ON a.return_line_id = rl.id
            WHERE r.status = 'Posted'
            GROUP BY rl.order_line_id
            """).ToListAsync(token);
        return rows.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => new Consumed(g.Key, g.Sum(x => x.Quantity),
            g.Sum(x => x.OriginalNet), g.Sum(x => x.OriginalTax), g.Sum(x => x.BaseNet), g.Sum(x => x.BaseTax)));
    }

    private async Task<List<SalesReturnLine>> SnapshotAsync(long companyId, SalesReturnLineInput[] lines,
        IReadOnlyDictionary<long, SourceLine> sources, IReadOnlyDictionary<long, InvoiceSource> invoices,
        SalesOrder order, CancellationToken token)
    {
        var currency = order.CurrencyCode;
        var baseCurrency = order.BaseCurrencyCode;
        var orderLines = order.Lines.ToDictionary(x => x.Id);
        var credited = await PostedAllocationsAsync(companyId, [.. invoices.Keys], token);
        var consumed = await OrderLineConsumptionAsync(companyId,
            [.. lines.Select(x => sources[x.DeliveryLineId].OrderLineId).Distinct()], token);
        var invoiceRemaining = invoices.Values.ToDictionary(x => x.InvoiceLineId, x =>
        {
            var used = credited.GetValueOrDefault(x.InvoiceLineId);
            return new SalesReturnRemaining(x.Quantity - (used?.Quantity ?? 0m),
                x.OriginalNet - (used?.OriginalNet ?? 0m), x.OriginalTax - (used?.OriginalTax ?? 0m),
                x.BaseNet - (used?.BaseNet ?? 0m), x.BaseTax - (used?.BaseTax ?? 0m));
        });
        var orderRemaining = new Dictionary<long, SalesReturnRemaining>();
        var built = new List<SalesReturnLine>(lines.Length);

        foreach (var line in lines)
        {
            var source = sources[line.DeliveryLineId];
            var total = SalesReturnPortionAmounts.Zero;
            var returnLine = new SalesReturnLine
            {
                CompanyId = companyId, LineNo = line.LineNo, DeliveryId = source.DeliveryId,
                DeliveryLineId = source.DeliveryLineId, OrderLineId = source.OrderLineId, ItemId = source.ItemId,
                OriginalOutboundMovementId = source.OutboundMovementId!.Value, WarehouseId = line.WarehouseId,
                LocationId = line.LocationId, Quantity = line.Quantity, UnbilledQuantity = line.UnbilledQuantity
            };
            foreach (var portion in (line.Billed ?? []).OrderBy(x => x.InvoiceLineId))
            {
                var (amounts, after) = SalesReturnAmounts.Take(invoiceRemaining[portion.InvoiceLineId],
                    portion.Quantity, currency, baseCurrency, line.LineNo);
                invoiceRemaining[portion.InvoiceLineId] = after;
                total = total.Add(amounts);
                returnLine.InvoiceAllocations.Add(new SalesReturnInvoiceAllocation
                {
                    CompanyId = companyId, InvoiceId = invoices[portion.InvoiceLineId].InvoiceId,
                    InvoiceLineId = portion.InvoiceLineId, Quantity = portion.Quantity,
                    OriginalNet = amounts.OriginalNet, OriginalTax = amounts.OriginalTax,
                    OriginalGross = amounts.OriginalGross, BaseNet = amounts.BaseNet, BaseTax = amounts.BaseTax,
                    BaseGross = amounts.BaseGross
                });
            }
            if (line.UnbilledQuantity > 0m)
            {
                if (!orderRemaining.TryGetValue(source.OrderLineId, out var remaining))
                {
                    var orderLine = orderLines[source.OrderLineId];
                    var used = consumed.GetValueOrDefault(source.OrderLineId);
                    remaining = new SalesReturnRemaining(orderLine.Quantity - (used?.Quantity ?? 0m),
                        orderLine.OriginalNet - (used?.OriginalNet ?? 0m), orderLine.OriginalTax - (used?.OriginalTax ?? 0m),
                        orderLine.BaseNet - (used?.BaseNet ?? 0m), orderLine.BaseTax - (used?.BaseTax ?? 0m));
                }
                var (amounts, after) = SalesReturnAmounts.Take(remaining, line.UnbilledQuantity, currency,
                    baseCurrency, line.LineNo);
                orderRemaining[source.OrderLineId] = after;
                total = total.Add(amounts);
            }
            (returnLine.OriginalNet, returnLine.OriginalTax, returnLine.OriginalGross) =
                (total.OriginalNet, total.OriginalTax, total.OriginalGross);
            (returnLine.BaseNet, returnLine.BaseTax, returnLine.BaseGross) =
                (total.BaseNet, total.BaseTax, total.BaseGross);
            built.Add(returnLine);
        }
        return built;
    }

    private async Task GuardAsync(
        IReadOnlyList<IGrouping<(long ItemId, long WarehouseId, long? LocationId), SalesReturnLine>> sets, int index,
        Func<Task> save, CancellationToken token)
    {
        if (index == sets.Count)
        {
            await save();
            return;
        }
        var set = sets[index];
        await references.RunWithCodesAsync(set.Key.ItemId, set.Key.WarehouseId, set.Key.LocationId, async codes =>
        {
            if (codes.TrackingMode != ItemTrackingMode.None)
                throw LineError(SalesReturnCreateService.TrackingUnsupported,
                    "Returns of lot or serial tracked items are not supported yet.", set.Min(x => x.LineNo));
            await GuardAsync(sets, index + 1, save, token);
            return true;
        }, token);
    }

    internal static BusinessRuleException LineError(string code, string message, int lineNo) =>
        new(code, message) { Extensions = new Dictionary<string, object?> { ["lineNo"] = lineNo } };

    private static BusinessRuleException PortionError(string code, string message, int lineNo, long invoiceLineId) =>
        new(code, message)
        {
            Extensions = new Dictionary<string, object?> { ["lineNo"] = lineNo, ["invoiceLineId"] = invoiceLineId }
        };

    private static BusinessRuleException Exceeded(string code, string message, int lineNo, decimal remaining) =>
        new(code, message)
        {
            Extensions = new Dictionary<string, object?> { ["lineNo"] = lineNo, ["remaining"] = Math.Max(remaining, 0m) }
        };
}
