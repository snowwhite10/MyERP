using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-RETURN-DRAFT-001～006 (WU-SD-008a2a, ADR-185): Draft return creation on real SQL — Posted-delivery and invoice-line
/// eligibility, billed/unbilled/returnable balances against committed Posted returns, remaining-proportional amount
/// snapshots, MD destination guard, request-key replay, delivery-line locking and rollback with no number consumed.
/// </summary>
public sealed class SalesReturnCreateServiceTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    /// <summary>Approved USD order (rate 31, TWD base), one Posted delivery of A 2 + B 1.5 and a Posted invoice of A 1.</summary>
    private sealed record Seeded(long CompanyId, SalesOrder Order, SalesDelivery Delivery, SalesInvoice Invoice)
    {
        public long DeliveryA => Delivery.Lines[0].Id;
        public long DeliveryB => Delivery.Lines[1].Id;
        public long InvoiceLineA => Invoice.Lines[0].Id;
    }

    [Fact]
    public async Task DraftSnapshotsBilledPortionsFromInvoiceAndUnbilledFromOrderLine()
    {
        // SD-RETURN-DRAFT-001: SD-RETURN-001/006/007/008 — one invoice line fully credited, unbilled share of what is
        // left of the order line, header = line sums, Draft with a permanent number and no INV/FI identity.
        var s = await SeedAsync("RA");
        var guard = new RecordingGuard();
        var input = new SalesReturnDraftInput("A-1", Day.AddDays(1), "  damaged  ",
        [
            new SalesReturnLineInput(2, s.DeliveryB, 52, 9, 1m, null),
            new SalesReturnLineInput(1, s.DeliveryA, 51, null, 0.5m, [new(s.InvoiceLineA, 1m)])
        ]);

        var outcome = await CreateAsync(s, input, guard);

        Assert.False(outcome.Replayed);
        await using var verify = Context(s.CompanyId);
        var saved = await verify.Set<SalesReturn>().AsNoTracking().Include(x => x.Lines)
            .ThenInclude(x => x.InvoiceAllocations).SingleAsync(x => x.Id == outcome.Return.Id, Ct);
        Assert.Equal(("RA001", SalesReturnStatus.Draft, 21L, s.Order.Id, "USD", "TWD", 31m, "damaged"),
            (saved.DocumentNo, saved.Status, saved.CustomerId, saved.OrderId, saved.CurrencyCode,
                saved.BaseCurrencyCode, saved.ExchangeRate, saved.Remark));
        Assert.Equal(("A-1", input.Fingerprint(), Day.AddDays(1)),
            (saved.RequestKey, saved.RequestFingerprint, saved.BusinessDate));
        Assert.Equal(((long?)null, (DateTime?)null, (long?)null, false),
            (saved.ApprovalInstanceId, saved.PostedAt, saved.FiCreditNoteId, saved.IsDeleted));
        Assert.Equal((60.00m, 3.00m, 63.00m, 1860m, 93m, 1953m), (saved.OriginalNet, saved.OriginalTax,
            saved.OriginalGross, saved.BaseNet, saved.BaseTax, saved.BaseGross));

        var lines = saved.Lines.OrderBy(x => x.LineNo).ToArray();
        // Line 1: billed 1 = the whole invoice line; unbilled 0.5 of (3 − 1 invoiced) → 66.67 × ¼, 3.33 × ¼, 2067 × ¼, 103 × ¼.
        AssertLine(lines[0], 1, s.Delivery.Id, s.DeliveryA, s.Order.Lines[0].Id, 31, 501, 51, null, 1.5m, 0.5m,
            50.00m, 2.50m, 1550m, 78m);
        var allocation = Assert.Single(lines[0].InvoiceAllocations);
        Assert.Equal((s.Invoice.Id, s.InvoiceLineA, 1m, 33.33m, 1.67m, 35.00m, 1033m, 52m, 1085m),
            (allocation.InvoiceId, allocation.InvoiceLineId, allocation.Quantity, allocation.OriginalNet,
                allocation.OriginalTax, allocation.OriginalGross, allocation.BaseNet, allocation.BaseTax,
                allocation.BaseGross));
        // Line 2: unbilled 1 of 1.5 uninvoiced on order line B (15.00 + 0.75; 465 + 23).
        AssertLine(lines[1], 2, s.Delivery.Id, s.DeliveryB, s.Order.Lines[1].Id, 32, 502, 52, 9, 1m, 1m,
            10.00m, 0.50m, 310m, 15m);
        Assert.Empty(lines[1].InvoiceAllocations);
        Assert.All(lines, x => Assert.Null(x.InvMovementId));

        Assert.Equal([(31L, 51L, (long?)null), (32L, 52L, (long?)9)], guard.Calls);
        Assert.Equal(1, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task BalancesCountOnlyPostedReturnsAndRefuseOneMicroUnitAbove()
    {
        // SD-RETURN-DRAFT-002: SD-RETURN-002/003/004/005 — returnable, unbilled and billed limits with the remaining
        // quantity; Draft, Pending and soft-deleted returns reserve nothing; Posted ones reduce every balance and the
        // later snapshot uses only what is left of the source amounts.
        var s = await SeedAsync("RB");
        var unbilled = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("B-1", s.DeliveryA, 1.000001m, lineNo: 4)));
        Assert.Equal(("SAL.RETURN.UNBILLED_EXCEEDED", 4, 1m),
            (unbilled.ErrorCode, unbilled.Extensions!["lineNo"], unbilled.Extensions["remaining"]));
        var billed = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("B-2", s.DeliveryA, 0m, (s.InvoiceLineA, 1.000001m))));
        Assert.Equal(("SAL.RETURN.BILLED_EXCEEDED", 1, s.InvoiceLineA, 1m), (billed.ErrorCode,
            billed.Extensions!["lineNo"], billed.Extensions["invoiceLineId"], billed.Extensions["remaining"]));

        // Drafts, a Pending return and a soft-deleted Draft reserve nothing: the full balance stays available.
        var first = await CreateAsync(s, Line("B-3", s.DeliveryA, 0.4m, (s.InvoiceLineA, 0.25m)));
        await CreateAsync(s, Line("B-4", s.DeliveryA, 1m, (s.InvoiceLineA, 1m)));
        var pending = await CreateAsync(s, Line("B-5", s.DeliveryB, 1.5m));
        await SetStatusAsync(s.CompanyId, pending.Return.Id, "Pending");
        var deleted = await CreateAsync(s, Line("B-6", s.DeliveryB, 1.5m));
        await using (var db = Context(s.CompanyId))
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_return SET is_deleted = 1, deleted_at = '2028-03-01' WHERE id = {0}",
                [deleted.Return.Id], Ct);
        await CreateAsync(s, Line("B-7", s.DeliveryB, 1.5m));

        // Posting B-3 (0.4 unbilled + 0.25 billed): returnable 2 − 0.65, unbilled 1 − 0.4, billed 1 − 0.25.
        await PostAsync(s.CompanyId, first.Return.Id, creditNoteId: 9001);
        var over = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("B-8", s.DeliveryA, 0.600001m)));
        Assert.Equal(("SAL.RETURN.UNBILLED_EXCEEDED", 0.6m), (over.ErrorCode, over.Extensions!["remaining"]));
        var overBilled = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("B-9", s.DeliveryA, 0m, (s.InvoiceLineA, 0.750001m))));
        Assert.Equal(("SAL.RETURN.BILLED_EXCEEDED", 0.75m), (overBilled.ErrorCode, overBilled.Extensions!["remaining"]));

        // Exactly the rest: billed 0.75 takes the invoice-line remainder, unbilled 0.6 the order-line share.
        var rest = await CreateAsync(s, Line("B-10", s.DeliveryA, 0.6m, (s.InvoiceLineA, 0.75m)));
        var restLine = Assert.Single(rest.Return.Lines);
        var b3 = first.Return.Lines[0];
        var b3Billed = Assert.Single(b3.InvoiceAllocations);
        var restBilled = Assert.Single(restLine.InvoiceAllocations);
        Assert.Equal((33.33m, 1.67m, 1033m, 52m), (b3Billed.OriginalNet + restBilled.OriginalNet,
            b3Billed.OriginalTax + restBilled.OriginalTax, b3Billed.BaseNet + restBilled.BaseNet,
            b3Billed.BaseTax + restBilled.BaseTax));
        await PostAsync(s.CompanyId, rest.Return.Id, creditNoteId: 9002);

        // Returnable is now 2 − 0.65 − 1.35 = 0 even though a Draft for the same line still exists.
        var none = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("B-11", s.DeliveryA, 0.000001m)));
        Assert.Equal(("SAL.RETURN.RETURN_EXCEEDED", 0m), (none.ErrorCode, none.Extensions!["remaining"]));

        // Order line A: invoiced 1 unit + unbilled 0.4 and 0.6 — the two unbilled snapshots plus the invoice equal
        // the saved line net exactly when 2 of 3 units are consumed... and never exceed it.
        await using var verify = Context(s.CompanyId);
        var unbilledNet = await verify.Database.SqlQuery<decimal>($"""
            SELECT SUM(rl.original_net - ISNULL(a.original_net, 0)) AS [Value]
            FROM dbo.sal_return_line rl JOIN dbo.sal_return r ON r.id = rl.return_id
            LEFT JOIN (SELECT return_line_id, SUM(original_net) AS original_net
                       FROM dbo.sal_return_invoice_allocation GROUP BY return_line_id) a ON a.return_line_id = rl.id
            WHERE r.status = 'Posted' AND rl.order_line_id = {s.Order.Lines[0].Id}
            """).SingleAsync(Ct);
        // 66.67 × 0.4 ÷ 2 = 13.33; then (66.67 − 13.33) × 0.6 ÷ 1.6 = 20.00.
        Assert.Equal(33.33m, unbilledNet);
        // Only the six saved Drafts (B-3～B-7, B-10) consumed numbers; every refused request rolled its number back.
        Assert.Equal(6L, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task IneligibleHiddenAndUnauthorisedSourcesFailWithoutRowsOrNumbers()
    {
        // SD-RETURN-DRAFT-003: SD-RETURN-009/010/011/012/013/017/018 — state, company, scope, permission, mixed order,
        // invoice line of another delivery line, dates, missing outbound movement, tracking and inactive destination.
        var s = await SeedAsync("RC");
        var foreign = await SeedAsync("RD");
        var sibling = await SeedAsync("RE", sameCompany: s.CompanyId);
        foreach (var status in new[] { SalesDeliveryStatus.Draft, SalesDeliveryStatus.Pending, SalesDeliveryStatus.Rejected })
        {
            var d = await SeedDeliveryAsync(s.CompanyId, s.Order, status, 1m);
            var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => CreateAsync(s,
                new SalesReturnDraftInput("C-" + status, Day, null,
                    [new(1, s.DeliveryB, 51, null, 1m, null), new(3, d.Lines[0].Id, 51, null, 1m, null)])));
            Assert.Equal(("SAL.RETURN.DELIVERY_NOT_POSTED", 3), (refused.ErrorCode, refused.Extensions!["lineNo"]));
        }
        var mixed = await Assert.ThrowsAsync<BusinessRuleException>(() => CreateAsync(s,
            new SalesReturnDraftInput("C-mix", Day, null,
                [new(1, s.DeliveryB, 51, null, 1m, null), new(2, sibling.DeliveryB, 51, null, 1m, null)])));
        Assert.Equal(("SAL.RETURN.SOURCE_MIXED", 2), (mixed.ErrorCode, mixed.Extensions!["lineNo"]));

        // The sibling order's invoice line belongs to another delivery line.
        var wrongInvoice = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("C-inv", s.DeliveryA, 0m, (sibling.InvoiceLineA, 1m))));
        Assert.Equal(("SAL.RETURN.INVOICE_LINE_INVALID", 1, sibling.InvoiceLineA), (wrongInvoice.ErrorCode,
            wrongInvoice.Extensions!["lineNo"], wrongInvoice.Extensions["invoiceLineId"]));
        Assert.Equal("SAL.RETURN.INVOICE_LINE_INVALID", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("C-inv2", s.DeliveryA, 0m, (long.MaxValue, 1m))))).ErrorCode);

        var early = await Assert.ThrowsAsync<BusinessRuleException>(() => CreateAsync(s,
            new SalesReturnDraftInput("C-date", Day.AddDays(-1), null, [new(5, s.DeliveryB, 51, null, 1m, null)])));
        Assert.Equal(("SAL.RETURN.DATE_BEFORE_DELIVERY", 5), (early.ErrorCode, early.Extensions!["lineNo"]));
        var beforeInvoice = await Assert.ThrowsAsync<BusinessRuleException>(() => CreateAsync(s,
            new SalesReturnDraftInput("C-idate", Day, null, [new(1, s.DeliveryA, 51, null, 0m, [new(s.InvoiceLineA, 1m)])])));
        Assert.Equal("SAL.RETURN.DATE_BEFORE_INVOICE", beforeInvoice.ErrorCode);

        var noMovement = await SeedDeliveryAsync(s.CompanyId, s.Order, SalesDeliveryStatus.Posted, 0.5m, movement: false);
        Assert.Equal("SAL.RETURN.SOURCE_INVALID", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("C-mv", noMovement.Lines[0].Id, 0.5m)))).ErrorCode);
        var lotLine = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("C-lot", s.DeliveryB, 1m, lineNo: 6), new RecordingGuard { TrackedItem = 32 }));
        Assert.Equal(("SAL.RETURN.TRACKING_UNSUPPORTED", 6), (lotLine.ErrorCode, lotLine.Extensions!["lineNo"]));
        Assert.Equal("MD.WAREHOUSE.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("C-wh", s.DeliveryB, 1m), new RecordingGuard { InactiveWarehouse = 51 }))).ErrorCode);

        // Another company's line, an unknown ID and an order outside the CREATE scope are hidden (404).
        Assert.Equal("SAL.RETURN.SOURCE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateAsync(s, Line("C-foreign", foreign.DeliveryB, 1m)))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => CreateAsync(s, Line("C-none", long.MaxValue, 1m)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateAsync(s, Line("C-scope", s.DeliveryB, 1m), userId: 8, scope: DataScope.Self));
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAsync(s, Line("C-perm", s.DeliveryB, 1m), allow: false));
        await Assert.ThrowsAsync<RequestValidationException>(() => CreateAsync(s, Line("C-zero", s.DeliveryB, 0m)));

        await using (var db = Context(s.CompanyId))
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [s.Order.Id], Ct);
        Assert.Equal("SAL.RETURN.ORDER_NOT_APPROVED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(s, Line("C-cancel", s.DeliveryB, 1m)))).ErrorCode);

        await using var verify = Context(s.CompanyId);
        Assert.False(await verify.Set<SalesReturn>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AnyAsync(Ct));
        Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
    }

    [Fact]
    public async Task SameRequestKeyReplaysIdenticalPayloadAndRejectsDifferentDeletedOrHiddenOnes()
    {
        // SD-RETURN-DRAFT-004: SD-RETURN-019/020 — replay saves nothing; changed payload, a soft-deleted Draft's key
        // and a user who cannot see the saved order all get 409 without the saved return.
        var s = await SeedAsync("RF");
        var input = Line("F-1", s.DeliveryA, 0.5m, (s.InvoiceLineA, 0.5m));
        var saved = await CreateAsync(s, input);

        var guard = new RecordingGuard();
        var replay = await CreateAsync(s, Line("F-1", s.DeliveryA, 0.500000m, (s.InvoiceLineA, 0.5m)), guard);
        Assert.True(replay.Replayed);
        Assert.Equal((saved.Return.Id, "RF001", 1m), (replay.Return.Id, replay.Return.DocumentNo,
            Assert.Single(replay.Return.Lines).Quantity));
        Assert.Single(replay.Return.Lines[0].InvoiceAllocations);
        Assert.Empty(guard.Calls);

        foreach (var changed in new[]
                 {
                     Line("F-1", s.DeliveryA, 0.4m, (s.InvoiceLineA, 0.5m)),
                     Line("F-1", s.DeliveryA, 0.5m, (s.InvoiceLineA, 0.5m), lineNo: 2),
                     input with { Remark = "x" }
                 })
            Assert.Equal("SAL.RETURN.REQUEST_CONFLICT",
                (await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(s, changed))).ErrorCode);
        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(s, input, userId: 8, scope: DataScope.Self));

        await using (var db = Context(s.CompanyId))
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_return SET is_deleted = 1, deleted_at = '2028-03-01' WHERE id = {0}",
                [saved.Return.Id], Ct);
        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(s, input));

        await using var verify = Context(s.CompanyId);
        Assert.Equal(1, await verify.Set<SalesReturn>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).CountAsync(Ct));
        Assert.Equal(1, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task CreatorsOfOneDeliveryLineSerializeWhileOtherOrdersProceed()
    {
        // SD-RETURN-DRAFT-005: BR-SD-044/047 — the delivery-line lock invoice issuance also takes blocks a return
        // create on that line; another order's return does not wait.
        var s = await SeedAsync("RG");
        var sibling = await SeedAsync("RH", sameCompany: s.CompanyId);
        await using var holder = Context(s.CompanyId);
        await using (var transaction = await holder.Database.BeginTransactionAsync(Ct))
        {
            await holder.Database.SqlQuery<long>($"""
                SELECT id AS [Value] FROM dbo.sal_delivery_line WITH (UPDLOCK, HOLDLOCK) WHERE id = {s.DeliveryB}
                """).ToListAsync(Ct);
            Assert.Equal("blocked", await TryCreateAsync(s, Line("G-1", s.DeliveryB, 1m)));
            Assert.Equal("created", await TryCreateAsync(sibling, Line("G-2", sibling.DeliveryB, 1m)));
            await transaction.RollbackAsync(Ct);
        }
        Assert.Equal("created", await TryCreateAsync(s, Line("G-1", s.DeliveryB, 1m)));
    }

    [Fact]
    public async Task FailureAfterNumberingRollsBackTheWholeDraft()
    {
        // SD-RETURN-DRAFT-006: SD-RETURN-021 — a failure inside the guarded save (after the number and the insert)
        // leaves no return, line, allocation or consumed number.
        var s = await SeedAsync("RI");
        var guard = new RecordingGuard { AfterSave = () => throw new InvalidOperationException("audit failed") };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAsync(s, Line("I-1", s.DeliveryA, 0.5m, (s.InvoiceLineA, 0.5m)), guard));

        await using var verify = Context(s.CompanyId);
        Assert.False(await verify.Set<SalesReturn>().AnyAsync(Ct));
        Assert.False(await verify.Set<SalesReturnLine>().AnyAsync(Ct));
        Assert.False(await verify.Set<SalesReturnInvoiceAllocation>().AnyAsync(Ct));
        Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
    }

    private static void AssertLine(SalesReturnLine x, int lineNo, long deliveryId, long deliveryLineId,
        long orderLineId, long itemId, long outbound, long warehouseId, long? locationId, decimal quantity,
        decimal unbilled, decimal net, decimal tax, decimal baseNet, decimal baseTax)
    {
        Assert.Equal((lineNo, deliveryId, deliveryLineId, orderLineId, itemId, outbound, warehouseId, locationId,
                quantity, unbilled),
            (x.LineNo, x.DeliveryId, x.DeliveryLineId, x.OrderLineId, x.ItemId, x.OriginalOutboundMovementId,
                x.WarehouseId, x.LocationId, x.Quantity, x.UnbilledQuantity));
        Assert.Equal((net, tax, net + tax, baseNet, baseTax, baseNet + baseTax),
            (x.OriginalNet, x.OriginalTax, x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross));
    }

    private static SalesReturnDraftInput Line(string key, long deliveryLineId, decimal unbilled,
        (long InvoiceLineId, decimal Quantity)? billed = null, int lineNo = 1) =>
        new(key, Day.AddDays(1), null,
        [
            new SalesReturnLineInput(lineNo, deliveryLineId, 51, null, unbilled,
                billed is { } b ? [new SalesReturnBilledInput(b.InvoiceLineId, b.Quantity)] : null)
        ]);

    private async Task<SalesReturnDraftOutcome> CreateAsync(Seeded s, SalesReturnDraftInput input,
        RecordingGuard? guard = null, bool allow = true, long userId = 7, DataScope? scope = null)
    {
        await using var db = Context(s.CompanyId);
        return await Service(db, s.CompanyId, guard ?? new RecordingGuard(), allow, userId, scope)
            .CreateAsync(input, Ct);
    }

    private async Task<string> TryCreateAsync(Seeded s, SalesReturnDraftInput input)
    {
        await using var db = Context(s.CompanyId);
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000", Ct);
        try
        {
            // SYS numbering has its own locking; a fixed unique number isolates the SD locks under test.
            await Service(db, s.CompanyId, new RecordingGuard(),
                numbers: new StaticNumber("P-" + Guid.NewGuid().ToString("N")[..12])).CreateAsync(input, Ct);
            return "created";
        }
        catch (Exception error) when (IsLockTimeout(error))
        {
            return "blocked";
        }
    }

    private static bool IsLockTimeout(Exception error)
    {
        for (var inner = error; inner is not null; inner = inner.InnerException)
            if (inner is Microsoft.Data.SqlClient.SqlException { Number: 1222 }) return true;
        return false;
    }

    private async Task SetStatusAsync(long companyId, long returnId, string status)
    {
        await using var db = Context(companyId);
        await db.Database.ExecuteSqlRawAsync("UPDATE dbo.sal_return SET status = {0} WHERE id = {1}",
            [status, returnId], Ct);
    }

    /// <summary>ADR-184 §3 posting order: Pending, stamp every line's inbound movement, then Posted.</summary>
    private async Task PostAsync(long companyId, long returnId, long? creditNoteId)
    {
        await SetStatusAsync(companyId, returnId, "Pending");
        await using var db = Context(companyId);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_return_line SET inv_movement_id = id + 800000 WHERE return_id = {0}", [returnId], Ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_return SET status = 'Posted', posted_at = '2028-03-01 10:00', fi_credit_note_id = {0} " +
            "WHERE id = {1}", [(object?)creditNoteId ?? DBNull.Value, returnId], Ct);
    }

    private sealed class StaticNumber(string number) : INumberGenerator
    {
        public Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default) => Task.FromResult(number);
    }

    private sealed class RecordingGuard : IInventoryReferenceGuard
    {
        public long? InactiveWarehouse { get; init; }
        public long? TrackedItem { get; init; }
        public Func<Task>? AfterSave { get; set; }
        public List<(long, long, long?)> Calls { get; } = [];

        public Task<T> RunAsync<T>(long itemId, long warehouseId, long? locationId, Func<Task<T>> action,
            CancellationToken token = default) =>
            RunWithCodesAsync(itemId, warehouseId, locationId, _ => action(), token);

        public async Task<T> RunWithCodesAsync<T>(long itemId, long warehouseId, long? locationId,
            Func<InventoryReferenceCodes, Task<T>> action, CancellationToken token = default)
        {
            Calls.Add((itemId, warehouseId, locationId));
            if (warehouseId == InactiveWarehouse)
                throw new BusinessRuleException("MD.WAREHOUSE.INACTIVE", "Warehouse is inactive.");
            var result = await action(new InventoryReferenceCodes("I" + itemId, "W" + warehouseId, null,
                itemId == TrackedItem ? ItemTrackingMode.Lot : ItemTrackingMode.None));
            if (AfterSave is { } callback)
            {
                AfterSave = null;
                await callback();
            }
            return result;
        }

        public Task RequireWarehousesAsync(IReadOnlyCollection<long> warehouseIds, CancellationToken token = default) =>
            Task.CompletedTask;
    }

    private async Task<Seeded> SeedAsync(string prefix, long? sameCompany = null)
    {
        var companyId = sameCompany ?? 0L;
        if (sameCompany is null)
        {
            await using (var setup = Context(null))
            {
                var company = new SystemCompany
                {
                    Code = prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Sales return test"
                };
                setup.Add(company);
                await setup.SaveChangesAsync(Ct);
                companyId = company.Id;
            }
            await using var rule = Context(companyId);
            rule.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.RETURN", Name = "Sales return",
                Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
                SequenceLength = 3 });
            await rule.SaveChangesAsync(Ct);
        }
        var order = await SeedOrderAsync(companyId);
        var delivery = await SeedDeliveryAsync(companyId, order, SalesDeliveryStatus.Posted, 2m, 1.5m);
        var invoice = await SeedInvoiceAsync(companyId, order, delivery);
        return new Seeded(companyId, order, delivery, invoice);
    }

    /// <summary>
    /// Approved USD order (rate 31, TWD base): line A 3 units net 100.00 + tax 5.00 (base 3,100 + 155); line B 1.5
    /// units net 15.00 + tax 0.75 (base 465 + 23).
    /// </summary>
    private async Task<SalesOrder> SeedOrderAsync(long companyId)
    {
        await using var db = Context(companyId);
        var order = new SalesOrder { CompanyId = companyId, OwnerUserId = 7,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10], CustomerId = 21, CustomerCode = "C21",
            CustomerName = "Customer", BusinessDate = Day, CurrencyCode = "USD", BaseCurrencyCode = "TWD",
            ExchangeRate = 31m, OriginalNet = 115m, OriginalTax = 5.75m, OriginalGross = 120.75m, BaseNet = 3565m,
            BaseTax = 178m, BaseGross = 3743m };
        order.Lines.Add(OrderLine(companyId, 1, 31, 3m, 100m, 5m, 3100m, 155m));
        order.Lines.Add(OrderLine(companyId, 2, 32, 1.5m, 15m, 0.75m, 465m, 23m));
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2028-02-29' WHERE id = {0}", [order.Id], Ct);
        return order;
    }

    private static SalesOrderLine OrderLine(long companyId, int lineNo, long itemId, decimal quantity, decimal net,
        decimal tax, decimal baseNet, decimal baseTax) => new()
    {
        CompanyId = companyId, LineNo = lineNo, ItemId = itemId, ItemCode = "I" + itemId, ItemName = "Item",
        Quantity = quantity, UnitPrice = decimal.Round(net / quantity, 6), TaxRateId = 41, TaxRatePercent = 5m,
        OriginalNet = net, OriginalTax = tax, OriginalGross = net + tax, BaseNet = baseNet, BaseTax = baseTax,
        BaseGross = baseNet + baseTax
    };

    /// <summary>Delivery of order line A (and B when a second quantity is given); Posted lines carry outbound movement 500 + line no.</summary>
    private async Task<SalesDelivery> SeedDeliveryAsync(long companyId, SalesOrder order, SalesDeliveryStatus status,
        decimal quantityA, decimal? quantityB = null, bool movement = true)
    {
        await using var db = Context(companyId);
        var posted = status == SalesDeliveryStatus.Posted;
        var delivery = new SalesDelivery { CompanyId = companyId, DocumentNo = "DN" + Guid.NewGuid().ToString("N")[..10],
            OrderId = order.Id, BusinessDate = Day, Status = status,
            PostedAt = posted ? new DateTime(2028, 2, 29, 9, 0, 0) : null };
        // Movement IDs are unique per company: the first delivery of a company gets 501/502, later ones 511/512, ….
        var movementBase = 501 + 10 * await db.Set<SalesDelivery>().IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId, Ct);
        var quantities = quantityB is { } b ? new[] { quantityA, b } : [quantityA];
        for (var i = 0; i < quantities.Length; i++)
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = i + 1,
                OrderLineId = order.Lines[i].Id, ItemId = order.Lines[i].ItemId, WarehouseId = 51,
                Quantity = quantities[i], InvMovementId = posted && movement ? movementBase + i : null });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
        return delivery;
    }

    /// <summary>Posted invoice of 1 unit of line A dated the day after delivery: 33.33 + 1.67 (base 1,033 + 52).</summary>
    private async Task<SalesInvoice> SeedInvoiceAsync(long companyId, SalesOrder order, SalesDelivery delivery)
    {
        await using var db = Context(companyId);
        var invoice = new SalesInvoice
        {
            CompanyId = companyId, DocumentNo = "INV" + Guid.NewGuid().ToString("N")[..10],
            RequestKey = Guid.NewGuid().ToString("N"), RequestFingerprint = "fp", CustomerId = 21, OrderId = order.Id,
            BusinessDate = Day.AddDays(1), CurrencyCode = "USD", BaseCurrencyCode = "TWD", ExchangeRate = 31m,
            OriginalNet = 33.33m, OriginalTax = 1.67m, OriginalGross = 35m, BaseNet = 1033m, BaseTax = 52m,
            BaseGross = 1085m
        };
        invoice.Lines.Add(new SalesInvoiceLine
        {
            CompanyId = companyId, LineNo = 1, DeliveryId = delivery.Id, DeliveryLineId = delivery.Lines[0].Id,
            OrderLineId = order.Lines[0].Id, ItemId = 31, TaxRateId = 41, TaxRatePercent = 5m, Quantity = 1m,
            OriginalNet = 33.33m, OriginalTax = 1.67m, OriginalGross = 35m, BaseNet = 1033m, BaseTax = 52m,
            BaseGross = 1085m
        });
        db.Add(invoice);
        await db.SaveChangesAsync(Ct);
        invoice.Status = SalesInvoiceStatus.Posted;
        invoice.PostedAt = new DateTime(2028, 3, 1, 9, 30, 0);
        invoice.FiSalesSourceId = invoice.Id + 7000;
        invoice.FiReceivableId = invoice.Id + 7000;
        invoice.FiJournalId = invoice.Id + 7000;
        await db.SaveChangesAsync(Ct);
        return invoice;
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2028, 2, 29, 9, 0, 0)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesReturnDbModel(), new SalesInvoiceDbModel(), new SalesDeliveryDbModel(), new SalesOrderDbModel(),
            new SalesQuotationDbModel(), new SalesPriceRuleDbModel(), new SystemOrganizationDbModel()],
        new FixedCompany(companyId));

    private static SalesReturnCreateService Service(MyErpDbContext db, long companyId, IInventoryReferenceGuard guard,
        bool allow = true, long userId = 7, DataScope? scope = null, INumberGenerator? numbers = null)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, allow && scope is null,
                allow && scope is { } value
                    ? new Dictionary<string, DataScope> { ["SAL.RETURN.CREATE"] = value }
                    : new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesReturnCreateService(db, new SalesActionAccess(company, auth.Object), company,
            numbers ?? new NumberGenerator(db, new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(7)), guard);
    }
}
