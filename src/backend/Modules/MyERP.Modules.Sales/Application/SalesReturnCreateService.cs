using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Application;

/// <summary>Saved Draft return plus whether it was an identical replay of an earlier request (no new change).</summary>
public sealed record SalesReturnDraftOutcome(SalesReturn Return, bool Replayed);

/// <summary>
/// WU-SD-008a2a (REQ_F4112.7.1, BR-SD-043/044/046～049, ADR-185): creates one Draft sales return from Posted delivery
/// lines of one visible Approved order. In its own Read Committed transaction it replays an identical request key,
/// locks the order row, the delivery lines and the invoice lines in ascending ID order, checks the returnable, unbilled
/// and billed balances against committed Posted returns, snapshots the amounts (billed portions from the invoice line,
/// unbilled portions from the order line, remaining-proportional with exact final remainder), validates every
/// destination through MD under its shared lock and saves the Draft with a permanent number. A Draft reserves nothing
/// and never calls INV or FI; submission (WU-SD-008a5) rechecks every balance under the same locks.
/// </summary>
public sealed class SalesReturnCreateService(MyErpDbContext db, SalesActionAccess access, SalesCompanyAccess company,
    INumberGenerator numbers, IInventoryReferenceGuard references)
{
    public const string DocumentType = "SAL.RETURN";
    public const string CreatePermission = "SAL.RETURN.CREATE";
    public const string SourceNotFound = "SAL.RETURN.SOURCE_NOT_FOUND";
    public const string SourceMixed = "SAL.RETURN.SOURCE_MIXED";
    public const string OrderNotApproved = "SAL.RETURN.ORDER_NOT_APPROVED";
    public const string DeliveryNotPosted = "SAL.RETURN.DELIVERY_NOT_POSTED";
    public const string InvoiceLineInvalid = "SAL.RETURN.INVOICE_LINE_INVALID";
    public const string DateBeforeDelivery = "SAL.RETURN.DATE_BEFORE_DELIVERY";
    public const string DateBeforeInvoice = "SAL.RETURN.DATE_BEFORE_INVOICE";
    public const string TrackingUnsupported = "SAL.RETURN.TRACKING_UNSUPPORTED";
    public const string ReturnExceeded = "SAL.RETURN.RETURN_EXCEEDED";
    public const string UnbilledExceeded = "SAL.RETURN.UNBILLED_EXCEEDED";
    public const string BilledExceeded = "SAL.RETURN.BILLED_EXCEEDED";
    public const string RequestConflict = "SAL.RETURN.REQUEST_CONFLICT";

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

    public async Task<SalesReturnDraftOutcome> CreateAsync(SalesReturnDraftInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync(CreatePermission, token);
        var companyId = company.RequireCompanyId();
        var fingerprint = input.Fingerprint();
        var lines = input.Lines!.OrderBy(x => x.LineNo).ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var outcome = await ReplayAsync(input.RequestKey!, fingerprint, scope, token)
                ?? await SaveAsync(companyId, input, fingerprint, lines, scope, token);
            await transaction.CommitAsync(token);
            return outcome;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<SalesReturnDraftOutcome> SaveAsync(long companyId, SalesReturnDraftInput input,
        string fingerprint, SalesReturnLineInput[] lines, DataScopeFilter scope, CancellationToken token)
    {
        // Unlocked resolution: hidden or mixed sources are named before any lock is taken.
        var sources = await LoadSourcesAsync(companyId, lines, token);
        var orderId = sources[lines[0].DeliveryLineId].OrderId;
        if (lines.FirstOrDefault(x => sources[x.DeliveryLineId].OrderId != orderId) is { } mixed)
            throw LineError(SourceMixed, "All return lines must come from deliveries of one sales order.", mixed.LineNo);
        if (!await scope.Apply(db.Set<SalesOrder>()).AnyAsync(x => x.Id == orderId, token))
            throw new NotFoundException(SourceNotFound, "Sales return source", lines[0].DeliveryLineId);

        var invoiceLineIds = lines.SelectMany(x => x.Billed ?? []).Select(x => x.InvoiceLineId).Distinct().ToArray();
        await LockSourcesAsync(companyId, orderId, sources.Keys, invoiceLineIds, token);

        // A concurrent identical request may have committed while this one waited for the locks.
        if (await ReplayAsync(input.RequestKey!, fingerprint, scope, token) is { } late)
            return late;

        sources = await LoadSourcesAsync(companyId, lines, token);
        var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
            .Where(x => x.Id == orderId).Include(x => x.Lines).AsSplitQuery().SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(SourceNotFound, "Sales return source", lines[0].DeliveryLineId);
        var invoices = await LoadInvoiceLinesAsync(companyId, invoiceLineIds, token);
        await CheckSourcesAsync(companyId, input.BusinessDate, lines, sources, invoices, order, token);
        await CheckBalancesAsync(companyId, lines, sources, invoices, token);

        var saved = await BuildAsync(companyId, input, fingerprint, lines, sources, invoices, order, token);
        await GuardDestinationsAsync(saved.Lines, async () =>
        {
            saved.DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token);
            db.Add(saved);
            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException error) when (IsRequestKeyDuplicate(error))
            {
                // Same key raced in from another order's sources: the stored payload necessarily differs.
                throw new ConflictException(RequestConflict, "The request key was already used with different content.");
            }
        }, token);
        return new SalesReturnDraftOutcome(saved, false);
    }

    /// <summary>
    /// BR-SD-048: the key covers soft-deleted history. An identical payload on a live, visible return replays it; a
    /// different payload, a deleted Draft or a return whose order this user cannot see is 409 and reveals nothing.
    /// </summary>
    private async Task<SalesReturnDraftOutcome?> ReplayAsync(string requestKey, string fingerprint,
        DataScopeFilter scope, CancellationToken token)
    {
        var existing = await db.Set<SalesReturn>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.RequestKey == requestKey, token);
        if (existing is null)
            return null;
        var visible = await scope.Apply(db.Set<SalesOrder>()).AnyAsync(x => x.Id == existing.OrderId, token);
        if (existing.IsDeleted || !visible ||
            !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new ConflictException(RequestConflict, "The request key was already used with different content.");
        existing.Lines.Sort((a, b) => a.LineNo.CompareTo(b.LineNo));
        foreach (var line in existing.Lines)
            line.InvoiceAllocations.Sort((a, b) => a.InvoiceLineId.CompareTo(b.InvoiceLineId));
        return new SalesReturnDraftOutcome(existing, true);
    }

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
            throw new NotFoundException(SourceNotFound, "Sales return source", missing.DeliveryLineId);
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

    /// <summary>
    /// BR-SD-047 lock order (without the FI credit lock, which a Draft never takes): order row, then delivery lines,
    /// then invoice lines, each ascending by primary-key seek — the same delivery-line lock invoice issuance takes.
    /// </summary>
    private async Task LockSourcesAsync(long companyId, long orderId, IEnumerable<long> deliveryLineIds,
        IEnumerable<long> invoiceLineIds, CancellationToken token)
    {
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0", orderId, companyId).ToListAsync(token);
        foreach (var id in deliveryLineIds.Order())
            await db.Database.SqlQuery<long>($"""
                SELECT [id] AS [Value] FROM dbo.sal_delivery_line WITH (UPDLOCK, HOLDLOCK, FORCESEEK)
                WHERE [id] = {id} AND [company_id] = {companyId}
                """).ToListAsync(token);
        foreach (var id in invoiceLineIds.Order())
            await db.Database.SqlQuery<long>($"""
                SELECT [id] AS [Value] FROM dbo.sal_invoice_line WITH (UPDLOCK, HOLDLOCK, FORCESEEK)
                WHERE [id] = {id} AND [company_id] = {companyId}
                """).ToListAsync(token);
    }

    private async Task CheckSourcesAsync(long companyId, DateOnly businessDate, SalesReturnLineInput[] lines,
        IReadOnlyDictionary<long, SourceLine> sources, IReadOnlyDictionary<long, InvoiceSource> invoices,
        SalesOrder order, CancellationToken token)
    {
        if (order.Status != SalesOrderStatus.Approved)
            throw new BusinessRuleException(OrderNotApproved, "Only deliveries of an approved order can be returned.");
        var ids = sources.Keys.ToArray();
        var tracked = (await db.Set<SalesDeliveryLineTracking>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.LineId))
            .Select(x => x.LineId).Distinct().ToListAsync(token)).ToHashSet();
        foreach (var line in lines)
        {
            var source = sources[line.DeliveryLineId];
            if (source.OrderId != order.Id)
                throw LineError(SourceMixed, "All return lines must come from deliveries of one sales order.", line.LineNo);
            if (source.IsDeleted || source.Status != SalesDeliveryStatus.Posted)
                throw LineError(DeliveryNotPosted, "Only lines of a Posted delivery can be returned.", line.LineNo);
            if (source.OutboundMovementId is null || order.Lines.All(x => x.Id != source.OrderLineId))
                throw SalesReturnAmounts.Invalid(line.LineNo);
            if (tracked.Contains(source.DeliveryLineId))
                throw LineError(TrackingUnsupported, "Returns of lot or serial tracked deliveries are not supported yet.",
                    line.LineNo);
            if (businessDate < source.DeliveryDate)
                throw LineError(DateBeforeDelivery, "The return date cannot be earlier than the delivery date.",
                    line.LineNo);
            foreach (var portion in (line.Billed ?? []).OrderBy(x => x.InvoiceLineId))
            {
                if (!invoices.TryGetValue(portion.InvoiceLineId, out var invoice) ||
                    invoice.DeliveryLineId != source.DeliveryLineId || invoice.Status != SalesInvoiceStatus.Posted)
                    throw PortionError(InvoiceLineInvalid,
                        "The billed portion must reference a Posted invoice line of the same delivery line.",
                        line.LineNo, portion.InvoiceLineId);
                if (businessDate < invoice.InvoiceDate)
                    throw PortionError(DateBeforeInvoice, "The return date cannot be earlier than the invoice date.",
                        line.LineNo, portion.InvoiceLineId);
            }
        }
    }

    /// <summary>
    /// BR-SD-044, read under the source locks against committed Posted returns (soft-deleted Drafts and Pending returns
    /// reserve nothing): returned ≤ delivered − Posted returned; unbilled ≤ delivered − invoiced − Posted unbilled;
    /// each billed portion ≤ invoice-line quantity − Posted billed portions of that invoice line.
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
                throw Exceeded(ReturnExceeded, "Return quantity exceeds the returnable quantity of the delivery line.",
                    line.LineNo, returnable);
            var unbilled = source.Quantity - invoiced.GetValueOrDefault(line.DeliveryLineId) -
                (prior?.UnbilledQuantity ?? 0m);
            if (line.UnbilledQuantity > unbilled)
                throw Exceeded(UnbilledExceeded, "Unbilled return quantity exceeds the uninvoiced delivered quantity.",
                    line.LineNo, unbilled);
            foreach (var portion in (line.Billed ?? []).OrderBy(x => x.InvoiceLineId))
            {
                var remaining = invoices[portion.InvoiceLineId].Quantity -
                    (credited.GetValueOrDefault(portion.InvoiceLineId)?.Quantity ?? 0m);
                if (portion.Quantity > remaining)
                    throw new BusinessRuleException(BilledExceeded,
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

    private async Task<SalesReturn> BuildAsync(long companyId, SalesReturnDraftInput input, string fingerprint,
        SalesReturnLineInput[] lines, IReadOnlyDictionary<long, SourceLine> sources,
        IReadOnlyDictionary<long, InvoiceSource> invoices, SalesOrder order, CancellationToken token)
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

        var saved = new SalesReturn
        {
            CompanyId = companyId,
            DocumentNo = string.Empty,
            RequestKey = input.RequestKey!,
            RequestFingerprint = fingerprint,
            CustomerId = order.CustomerId,
            OrderId = order.Id,
            BusinessDate = input.BusinessDate,
            CurrencyCode = currency,
            BaseCurrencyCode = baseCurrency,
            ExchangeRate = order.ExchangeRate,
            Remark = input.NormalizedRemark
        };
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
            saved.Lines.Add(returnLine);
        }
        // Header totals are the sum of the saved lines (MASTER_PROMPT 6.3).
        saved.OriginalNet = saved.Lines.Sum(x => x.OriginalNet);
        saved.OriginalTax = saved.Lines.Sum(x => x.OriginalTax);
        saved.OriginalGross = saved.OriginalNet + saved.OriginalTax;
        saved.BaseNet = saved.Lines.Sum(x => x.BaseNet);
        saved.BaseTax = saved.Lines.Sum(x => x.BaseTax);
        saved.BaseGross = saved.BaseNet + saved.BaseTax;
        return saved;
    }

    /// <summary>
    /// Validates each distinct item/warehouse/location through MD (active warehouse, location of that warehouse),
    /// keeps them shared-locked and saves inside the innermost check. A lot/serial tracked item is refused until the
    /// tracked-return follow-up (ADR-184 §6).
    /// </summary>
    private Task GuardDestinationsAsync(IEnumerable<SalesReturnLine> lines, Func<Task> save, CancellationToken token)
    {
        var sets = lines
            .GroupBy(x => (x.ItemId, x.WarehouseId, x.LocationId))
            .OrderBy(x => x.Key.ItemId).ThenBy(x => x.Key.WarehouseId).ThenBy(x => x.Key.LocationId ?? 0)
            .ToArray();
        return GuardAsync(sets, 0, save, token);
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
                throw LineError(TrackingUnsupported, "Returns of lot or serial tracked items are not supported yet.",
                    set.Min(x => x.LineNo));
            await GuardAsync(sets, index + 1, save, token);
            return true;
        }, token);
    }

    private static bool IsRequestKeyDuplicate(DbUpdateException error) =>
        error.InnerException is SqlException { Number: 2601 or 2627 } sql &&
        sql.Message.Contains("request_key", StringComparison.OrdinalIgnoreCase);

    private static BusinessRuleException LineError(string code, string message, int lineNo) =>
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
