using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-RETURN-SCHEMA-001～008 (WU-SD-008a1, ADR-184): real SQL proof of the <c>sal_return</c>,
/// <c>sal_return_line</c> and <c>sal_return_invoice_allocation</c> keys, checks, same-company source FKs, Draft-only
/// soft delete and the state triggers 51035/51036/51037 including the Posted invariants.
/// </summary>
public sealed class SalesReturnSchemaTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);
    private static readonly DateTime PostedTime = new(2028, 2, 29, 10, 0, 0);

    private sealed record Source(
        long OrderId, long OrderLineId, long DeliveryId, long DeliveryLineId, long InvoiceId, long InvoiceLineId);

    [Fact]
    public async Task DraftWithBilledAndUnbilledPortionsPostsAndIsThenImmutable()
    {
        // SD-RETURN-SCHEMA-001: BR-SD-043/048 — Draft → Pending → Posted with inbound movement and FI credit; then any
        // header/line/allocation change is refused and rows stay intact.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var ret = NewReturn(companyId, source, "A");
        db.Add(ret);
        await db.SaveChangesAsync(Ct);
        Assert.Equal((SalesReturnStatus.Draft, 1), (ret.Status, ret.Version));

        ret.Status = SalesReturnStatus.Pending;
        ret.ApprovalInstanceId = 77;
        await db.SaveChangesAsync(Ct);
        await MarkPostedAsync(db, ret, 9001);

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesReturn>().AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations)
            .SingleAsync(x => x.Id == ret.Id, Ct);
        var line = Assert.Single(saved.Lines);
        var allocation = Assert.Single(line.InvoiceAllocations);
        Assert.Equal((SalesReturnStatus.Posted, 3, 9001L, 3m, 1m, 2m, source.InvoiceLineId),
            (saved.Status, saved.Version, saved.FiCreditNoteId!.Value, line.Quantity, line.UnbilledQuantity,
                allocation.Quantity, allocation.InvoiceLineId));
        Assert.Equal(ret.Id * 10 + 1, line.InvMovementId);

        foreach (var (sql, number) in new[]
                 {
                     ("UPDATE dbo.sal_return SET remark = N'x' WHERE id = {0}", 51035),
                     ("UPDATE dbo.sal_return SET status = 'Draft', posted_at = NULL, fi_credit_note_id = NULL " +
                      "WHERE id = {0}", 51035),
                     ("UPDATE dbo.sal_return_line SET inv_movement_id = inv_movement_id + 1 WHERE return_id = {0}", 51036),
                     ("DELETE dbo.sal_return_invoice_allocation WHERE return_line_id IN " +
                      "(SELECT id FROM dbo.sal_return_line WHERE return_id = {0})", 51037),
                     ("UPDATE dbo.sal_return_invoice_allocation SET quantity = 1 WHERE return_line_id IN " +
                      "(SELECT id FROM dbo.sal_return_line WHERE return_id = {0})", 51037),
                     ("INSERT dbo.sal_return_line (company_id, return_id, line_no, delivery_id, delivery_line_id, " +
                      "order_line_id, item_id, original_outbound_movement_id, warehouse_id, quantity, unbilled_quantity, " +
                      "original_net, original_tax, original_gross, base_net, base_tax, base_gross, created_at, created_by, " +
                      "version) SELECT company_id, return_id, 2, delivery_id, delivery_line_id, order_line_id, item_id, " +
                      "original_outbound_movement_id, warehouse_id, 1, 1, 0, 0, 0, 0, 0, 0, created_at, created_by, 1 " +
                      "FROM dbo.sal_return_line WHERE return_id = {0}", 51036)
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() =>
                verify.Database.ExecuteSqlRawAsync(sql, [ret.Id], Ct));
            Assert.Equal(number, refused.Number);
        }

        await Assert.ThrowsAsync<SqlException>(() =>
            verify.Database.ExecuteSqlRawAsync("DELETE dbo.sal_return WHERE id = {0}", [ret.Id], Ct));
        Assert.Equal(1, await verify.Set<SalesReturnInvoiceAllocation>().CountAsync(Ct));
        Assert.Equal(SalesReturnStatus.Posted, (await verify.Set<SalesReturn>().AsNoTracking()
            .SingleAsync(x => x.Id == ret.Id, Ct)).Status);
    }

    [Fact]
    public async Task DraftLinesAreEditableButPendingAndRejectedLinesAreFrozen()
    {
        // SD-RETURN-SCHEMA-002: BR-SD-043 — Draft lines/allocations may be replaced; under Pending/Rejected the
        // lines are frozen except the inbound movement during posting, and saved identity never changes.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var otherOrder = await SeedSourceAsync(companyId, companyId * 10 + 5);
        var ret = NewReturn(companyId, source, "B");
        db.Add(ret);
        await db.SaveChangesAsync(Ct);

        // Draft edit: change quantities and drop the billed allocation (all unbilled).
        var line = ret.Lines[0];
        line.InvoiceAllocations.Clear();
        line.UnbilledQuantity = 3m;
        await db.SaveChangesAsync(Ct);
        Assert.Equal(0, await db.Set<SalesReturnInvoiceAllocation>().CountAsync(Ct));

        foreach (var sql in new[]
                 {
                     "UPDATE dbo.sal_return SET request_key = 'other' WHERE id = {0}",
                     "UPDATE dbo.sal_return SET order_id = {1} WHERE id = {0}",
                     "UPDATE dbo.sal_return SET customer_id = customer_id + 1 WHERE id = {0}",
                     "UPDATE dbo.sal_return SET document_no = 'RT-other' WHERE id = {0}"
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlRawAsync(sql, [ret.Id, otherOrder.OrderId], Ct));
            Assert.Equal(51035, refused.Number);
        }

        foreach (var status in new[] { "Pending", "Rejected" })
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_return SET status = {1}, approval_instance_id = 5 WHERE id = {0}", [ret.Id, status], Ct);
            foreach (var sql in new[]
                     {
                         "UPDATE dbo.sal_return_line SET quantity = 2, unbilled_quantity = 2 WHERE return_id = {0}",
                         "DELETE dbo.sal_return_line WHERE return_id = {0}",
                         "INSERT dbo.sal_return_invoice_allocation (company_id, return_line_id, invoice_id, invoice_line_id, " +
                         "quantity, original_net, original_tax, original_gross, base_net, base_tax, base_gross, created_at, " +
                         "created_by, version) SELECT l.company_id, l.id, {1}, {2}, 1, 0, 0, 0, 0, 0, 0, l.created_at, " +
                         "l.created_by, 1 FROM dbo.sal_return_line l WHERE l.return_id = {0}"
                     })
            {
                var refused = await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlRawAsync(sql, [ret.Id, source.InvoiceId, source.InvoiceLineId], Ct));
                Assert.True(refused.Number is 51036 or 51037, $"Unexpected SQL error {refused.Number}.");
            }
        }

        // Rejected cannot jump to Posted or Pending; revision returns to Draft and the lines become editable again.
        foreach (var status in new[] { "Posted", "Pending" })
        {
            var jump = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_return SET status = {1}, posted_at = '2028-02-29' WHERE id = {0}", [ret.Id, status], Ct));
            Assert.Equal(status == "Posted" ? 51035 : 547, jump.Number);
        }
        await db.Database.ExecuteSqlRawAsync("UPDATE dbo.sal_return SET status = 'Draft' WHERE id = {0}", [ret.Id], Ct);
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_return_line SET quantity = 2, unbilled_quantity = 2 WHERE return_id = {0}", [ret.Id], Ct));

        // Pending allows the posting step to stamp the inbound movement once (NULL → value) and nothing else.
        await db.Database.ExecuteSqlRawAsync("UPDATE dbo.sal_return SET status = 'Pending' WHERE id = {0}", [ret.Id], Ct);
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_return_line SET inv_movement_id = 4242 WHERE return_id = {0}", [ret.Id], Ct));
        var restamp = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_return_line SET inv_movement_id = 4243 WHERE return_id = {0}", [ret.Id], Ct));
        Assert.Equal(51036, restamp.Number);
    }

    public static TheoryData<string> InvalidPostedTransitions() =>
    [
        "missing-inbound", "portions-short", "billed-without-credit", "credit-without-billed", "no-lines"
    ];

    [Theory]
    [MemberData(nameof(InvalidPostedTransitions))]
    public async Task PostedTransitionRequiresCompletePostingEvidence(string shape)
    {
        // SD-RETURN-SCHEMA-003: BR-SD-044/046/047/049 — trigger 51035 refuses Posted without every inbound movement,
        // with billed + unbilled ≠ quantity, with billed portions but no FI credit, with an FI credit but no billed
        // portion, or without lines; the return stays Pending.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var ret = NewReturn(companyId, source, "C" + shape);
        var line = ret.Lines[0];
        switch (shape)
        {
            case "portions-short": line.UnbilledQuantity = 0.5m; break;
            case "credit-without-billed": line.InvoiceAllocations.Clear(); line.UnbilledQuantity = 3m; break;
            case "no-lines": ret.Lines.Clear(); break;
        }

        db.Add(ret);
        await db.SaveChangesAsync(Ct);
        ret.Status = SalesReturnStatus.Pending;
        await db.SaveChangesAsync(Ct);

        if (shape != "missing-inbound")
            foreach (var l in ret.Lines)
                l.InvMovementId = ret.Id * 10 + l.LineNo;
        await db.SaveChangesAsync(Ct);
        ret.Status = SalesReturnStatus.Posted;
        ret.PostedAt = PostedTime;
        ret.FiCreditNoteId = shape == "billed-without-credit" ? null : 9100;
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Equal(51035, Assert.IsType<SqlException>(refused.InnerException).Number);

        await using var verify = Context(companyId);
        Assert.Equal(SalesReturnStatus.Pending, (await verify.Set<SalesReturn>().AsNoTracking().SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task UnbilledOnlyReturnPostsWithoutFiCredit()
    {
        // SD-RETURN-SCHEMA-004: BR-SD-047 — a wholly unbilled return posts with inbound movement and no FI credit.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var ret = NewReturn(companyId, source, "D");
        ret.Lines[0].InvoiceAllocations.Clear();
        ret.Lines[0].UnbilledQuantity = 3m;
        db.Add(ret);
        await db.SaveChangesAsync(Ct);
        ret.Status = SalesReturnStatus.Pending;
        await db.SaveChangesAsync(Ct);
        await MarkPostedAsync(db, ret, null);

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesReturn>().AsNoTracking().SingleAsync(Ct);
        Assert.Equal((SalesReturnStatus.Posted, (long?)null), (saved.Status, saved.FiCreditNoteId));
    }

    [Fact]
    public async Task NumberRequestKeyCreditAndInboundAreUniqueEvenAfterSoftDelete()
    {
        // SD-RETURN-SCHEMA-005: BR-SD-048 — a soft-deleted Draft keeps its number and request key; a Posted return's
        // FI credit and inbound movement cannot be reused; another company may reuse all of them.
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        var otherSource = await SeedSourceAsync(otherCompanyId);
        await using (var db = Context(companyId))
        {
            var deleted = NewReturn(companyId, source, "E");
            db.Add(deleted);
            await db.SaveChangesAsync(Ct);
            deleted.IsDeleted = true;
            deleted.DeletedAt = PostedTime;
            await db.SaveChangesAsync(Ct);
            var undelete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_return SET is_deleted = 0, deleted_at = NULL WHERE id = {0}", [deleted.Id], Ct));
            Assert.Equal(51035, undelete.Number);
        }

        foreach (var mutate in new Action<SalesReturn>[] { x => x.DocumentNo = "RT-E", x => x.RequestKey = "key-E" })
        {
            await using var duplicate = Context(companyId);
            var candidate = NewReturn(companyId, source, "F");
            mutate(candidate);
            duplicate.Add(candidate);
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(Ct));
        }

        long postedInbound;
        await using (var db = Context(companyId))
        {
            var posted = NewReturn(companyId, source, "G");
            db.Add(posted);
            await db.SaveChangesAsync(Ct);
            posted.Status = SalesReturnStatus.Pending;
            await db.SaveChangesAsync(Ct);
            await MarkPostedAsync(db, posted, 7001);
            postedInbound = posted.Lines[0].InvMovementId!.Value;
        }

        foreach (var column in new[] { "fi_credit_note_id", "inv_movement_id" })
        {
            await using var duplicate = Context(companyId);
            await using var tx = await duplicate.Database.BeginTransactionAsync(Ct);
            var candidate = NewReturn(companyId, source, "H" + column[..2]);
            duplicate.Add(candidate);
            await duplicate.SaveChangesAsync(Ct);
            candidate.Status = SalesReturnStatus.Pending;
            await duplicate.SaveChangesAsync(Ct);
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => column == "fi_credit_note_id"
                ? MarkPostedAsync(duplicate, candidate, 7001)
                : MarkPostedAsync(duplicate, candidate, 7500, postedInbound));
            Assert.Contains(column, refused.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
            await tx.RollbackAsync(Ct);
        }

        await using (var other = Context(otherCompanyId))
        {
            var reused = NewReturn(otherCompanyId, otherSource, "E");
            other.Add(reused);
            await other.SaveChangesAsync(Ct);
            reused.Status = SalesReturnStatus.Pending;
            await other.SaveChangesAsync(Ct);
            await MarkPostedAsync(other, reused, 7001, postedInbound);
        }

        await using var verify = Context(companyId);
        Assert.Equal(1, await verify.Set<SalesReturn>().CountAsync(Ct));
        Assert.Equal(2, await verify.Set<SalesReturn>().IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, Ct));
    }

    public static TheoryData<string> InvalidShapes() =>
    [
        "header-gross", "header-negative", "base-rate", "blank-key", "blank-fingerprint", "pending-with-credit",
        "posted-without-time", "deleted-not-draft", "deleted-without-time", "line-quantity", "line-unbilled-negative",
        "line-unbilled-over", "line-gross", "line-no", "duplicate-line-no", "line-outbound", "line-location",
        "allocation-quantity", "allocation-gross", "duplicate-allocation"
    ];

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public async Task InvalidHeaderLineOrAllocationShapesSaveNothing(string shape)
    {
        // SD-RETURN-SCHEMA-006: BR-SD-044/049 — SQL CHECKs and unique keys back the service; nothing is saved.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var ret = NewReturn(companyId, source, "J");
        var line = ret.Lines[0];
        var allocation = line.InvoiceAllocations[0];
        switch (shape)
        {
            case "header-gross": ret.OriginalGross = 1m; ret.BaseGross = 1m; break;
            case "header-negative": ret.OriginalTax = -1m; ret.OriginalGross = ret.OriginalNet - 1m;
                ret.BaseTax = -1m; ret.BaseGross = ret.BaseNet - 1m; break;
            case "base-rate": ret.ExchangeRate = 1.5m; break;
            case "blank-key": ret.RequestKey = ""; break;
            case "blank-fingerprint": ret.RequestFingerprint = ""; break;
            case "pending-with-credit": ret.Status = SalesReturnStatus.Pending; ret.FiCreditNoteId = 3; break;
            case "posted-without-time": ret.Status = SalesReturnStatus.Posted; break;
            case "deleted-not-draft": ret.Status = SalesReturnStatus.Pending; ret.IsDeleted = true;
                ret.DeletedAt = PostedTime; break;
            case "deleted-without-time": ret.IsDeleted = true; break;
            case "line-quantity": line.Quantity = 0m; line.UnbilledQuantity = 0m; break;
            case "line-unbilled-negative": line.UnbilledQuantity = -1m; break;
            case "line-unbilled-over": line.UnbilledQuantity = 3.000001m; break;
            case "line-gross": line.OriginalGross = 1m; break;
            case "line-no": line.LineNo = 0; break;
            case "duplicate-line-no": ret.Lines.Add(NewLine(companyId, source, 1, withAllocation: false)); break;
            case "line-outbound": line.OriginalOutboundMovementId = 0; break;
            case "line-location": line.LocationId = 0; break;
            case "allocation-quantity": allocation.Quantity = 0m; break;
            case "allocation-gross": allocation.BaseGross = 1m; break;
            case "duplicate-allocation": line.InvoiceAllocations.Add(NewAllocation(companyId, source)); break;
        }

        db.Add(ret);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        await using var verify = Context(companyId);
        Assert.Equal(0, await verify.Set<SalesReturn>().IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, Ct));
        Assert.Equal(0, await verify.Set<SalesReturnLine>().CountAsync(Ct));
        Assert.Equal(0, await verify.Set<SalesReturnInvoiceAllocation>().CountAsync(Ct));
    }

    [Fact]
    public async Task SourceReferencesMustBeSameCompanyAndReturnedSourcesCannotBeRemoved()
    {
        // SD-RETURN-SCHEMA-007: BR-SD-043/049 — same-company FKs to order, delivery, delivery line, order line,
        // invoice and invoice line; a returned delivery line or credited invoice line is protected.
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        var foreign = await SeedSourceAsync(otherCompanyId);
        foreach (var crossed in new[]
                 {
                     source with { OrderId = foreign.OrderId },
                     source with { DeliveryId = foreign.DeliveryId },
                     source with { DeliveryLineId = foreign.DeliveryLineId },
                     source with { OrderLineId = foreign.OrderLineId },
                     source with { InvoiceId = foreign.InvoiceId },
                     source with { InvoiceLineId = foreign.InvoiceLineId }
                 })
        {
            await using var db = Context(companyId);
            db.Add(NewReturn(companyId, crossed, "K"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await using (var db = Context(companyId))
        {
            db.Add(NewReturn(companyId, source, "L"));
            await db.SaveChangesAsync(Ct);
        }

        await using var verify = Context(companyId);
        foreach (var sql in new[]
                 {
                     "DELETE dbo.sal_delivery_line WHERE id = {0}",
                     "DELETE dbo.sal_order WHERE id = {1}"
                 })
            await Assert.ThrowsAsync<SqlException>(() =>
                verify.Database.ExecuteSqlRawAsync(sql, [source.DeliveryLineId, source.OrderId], Ct));
        Assert.Equal(1, await verify.Set<SalesReturnLine>().CountAsync(Ct));
        Assert.Equal(1, await verify.Set<SalesReturnInvoiceAllocation>().CountAsync(Ct));
    }

    [Fact]
    public async Task ForeignCurrencyReturnKeepsSavedRateSixPlaceQuantityAndDraftDeleteCascades()
    {
        // SD-RETURN-SCHEMA-008: boundary — decimal(19,8) rate, decimal(19,4) base amounts and decimal(19,6)
        // quantities round-trip on a leap day; a Draft physically deleted (EF) removes its lines and allocations.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        long id;
        await using (var db = Context(companyId))
        {
            var ret = NewReturn(companyId, source, "M");
            ret.CurrencyCode = "USD";
            ret.ExchangeRate = 31.12345678m;
            ret.BaseNet = 2334.2593m;
            ret.BaseTax = 116.7130m;
            ret.BaseGross = 2450.9723m;
            ret.Lines[0].Quantity = 2.999999m;
            ret.Lines[0].UnbilledQuantity = 0.999999m;
            db.Add(ret);
            await db.SaveChangesAsync(Ct);
            id = ret.Id;
        }

        await using (var verify = Context(companyId))
        {
            var saved = await verify.Set<SalesReturn>().Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations)
                .SingleAsync(x => x.Id == id, Ct);
            Assert.Equal((31.12345678m, 2450.9723m, 2.999999m, 0.999999m, Day),
                (saved.ExchangeRate, saved.BaseGross, saved.Lines[0].Quantity, saved.Lines[0].UnbilledQuantity,
                    saved.BusinessDate));
            verify.Remove(saved);
            await verify.SaveChangesAsync(Ct);
        }

        await using var after = Context(companyId);
        Assert.Equal(0, await after.Set<SalesReturnLine>().CountAsync(Ct));
        Assert.Equal(0, await after.Set<SalesReturnInvoiceAllocation>().CountAsync(Ct));
    }

    /// <summary>3 units returned: 2 billed against the invoice line, 1 unbilled; 75 + 3.75 tax.</summary>
    private static SalesReturn NewReturn(long companyId, Source source, string suffix)
    {
        var ret = new SalesReturn
        {
            CompanyId = companyId, DocumentNo = "RT-" + suffix, RequestKey = "key-" + suffix,
            RequestFingerprint = "fp-" + suffix, CustomerId = 21, OrderId = source.OrderId, BusinessDate = Day,
            CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
            OriginalNet = 75m, OriginalTax = 3.75m, OriginalGross = 78.75m, BaseNet = 75m, BaseTax = 3.75m,
            BaseGross = 78.75m
        };
        ret.Lines.Add(NewLine(companyId, source, 1, withAllocation: true));
        return ret;
    }

    private static SalesReturnLine NewLine(long companyId, Source source, int lineNo, bool withAllocation)
    {
        var line = new SalesReturnLine
        {
            CompanyId = companyId, LineNo = lineNo, DeliveryId = source.DeliveryId,
            DeliveryLineId = source.DeliveryLineId, OrderLineId = source.OrderLineId, ItemId = 31,
            OriginalOutboundMovementId = companyId * 10, WarehouseId = 51, Quantity = 3m,
            UnbilledQuantity = withAllocation ? 1m : 3m, OriginalNet = 75m, OriginalTax = 3.75m,
            OriginalGross = 78.75m, BaseNet = 75m, BaseTax = 3.75m, BaseGross = 78.75m
        };
        if (withAllocation)
            line.InvoiceAllocations.Add(NewAllocation(companyId, source));
        return line;
    }

    private static SalesReturnInvoiceAllocation NewAllocation(long companyId, Source source) => new()
    {
        CompanyId = companyId, InvoiceId = source.InvoiceId, InvoiceLineId = source.InvoiceLineId, Quantity = 2m,
        OriginalNet = 50m, OriginalTax = 2.5m, OriginalGross = 52.5m, BaseNet = 50m, BaseTax = 2.5m,
        BaseGross = 52.5m
    };

    /// <summary>
    /// Posting order the 008a posting step must follow: stamp each line's INV inbound movement while the header is
    /// still Pending, then mark the header Posted (trigger 51035 verifies the evidence at that moment).
    /// </summary>
    private static async Task MarkPostedAsync(MyErpDbContext db, SalesReturn ret, long? fiCreditNoteId,
        long? inbound = null)
    {
        for (var i = 0; i < ret.Lines.Count; i++)
            ret.Lines[i].InvMovementId ??= inbound ?? ret.Id * 10 + i + 1;
        await db.SaveChangesAsync(Ct);
        ret.Status = SalesReturnStatus.Posted;
        ret.PostedAt = PostedTime;
        ret.FiCreditNoteId = fiCreditNoteId;
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>An Approved order line of 10 units, a Posted delivery of 10 and a Posted invoice of 4 (seeded directly).</summary>
    private async Task<Source> SeedSourceAsync(long companyId, long? outboundMovementId = null)
    {
        await using var db = Context(companyId);
        var order = new SalesOrder
        {
            CompanyId = companyId, OwnerUserId = 7, DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10],
            CustomerId = 21, CustomerCode = "C21", CustomerName = "Customer", BusinessDate = Day,
            CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m, OriginalNet = 250m, OriginalTax = 12.5m,
            OriginalGross = 262.5m, BaseNet = 250m, BaseTax = 12.5m, BaseGross = 262.5m
        };
        order.Lines.Add(new SalesOrderLine
        {
            CompanyId = companyId, LineNo = 1, ItemId = 31, ItemCode = "I31", ItemName = "Item", Quantity = 10m,
            UnitPrice = 25m, TaxRateId = 41, TaxRatePercent = 5m, OriginalNet = 250m, OriginalTax = 12.5m,
            OriginalGross = 262.5m, BaseNet = 250m, BaseTax = 12.5m, BaseGross = 262.5m
        });
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2028-02-29' WHERE id = {0}", [order.Id], Ct);

        var delivery = new SalesDelivery
        {
            CompanyId = companyId, DocumentNo = "DN" + Guid.NewGuid().ToString("N")[..10], OrderId = order.Id,
            BusinessDate = Day, Status = SalesDeliveryStatus.Posted, PostedAt = new DateTime(2028, 2, 29, 9, 0, 0)
        };
        delivery.Lines.Add(new SalesDeliveryLine
        {
            CompanyId = companyId, LineNo = 1, OrderLineId = order.Lines[0].Id, ItemId = 31, WarehouseId = 51,
            Quantity = 10m, InvMovementId = outboundMovementId ?? companyId * 10
        });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);

        var invoice = new SalesInvoice
        {
            CompanyId = companyId, DocumentNo = "INV" + Guid.NewGuid().ToString("N")[..10],
            RequestKey = Guid.NewGuid().ToString("N"), RequestFingerprint = "fp", CustomerId = 21, OrderId = order.Id,
            BusinessDate = Day, CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
            OriginalNet = 100m, OriginalTax = 5m, OriginalGross = 105m, BaseNet = 100m, BaseTax = 5m, BaseGross = 105m
        };
        invoice.Lines.Add(new SalesInvoiceLine
        {
            CompanyId = companyId, LineNo = 1, DeliveryId = delivery.Id, DeliveryLineId = delivery.Lines[0].Id,
            OrderLineId = order.Lines[0].Id, ItemId = 31, TaxRateId = 41, TaxRatePercent = 5m, Quantity = 4m,
            OriginalNet = 100m, OriginalTax = 5m, OriginalGross = 105m, BaseNet = 100m, BaseTax = 5m, BaseGross = 105m
        });
        db.Add(invoice);
        await db.SaveChangesAsync(Ct);
        invoice.Status = SalesInvoiceStatus.Posted;
        invoice.PostedAt = new DateTime(2028, 2, 29, 9, 30, 0);
        invoice.FiSalesSourceId = delivery.Lines[0].InvMovementId!.Value;
        invoice.FiReceivableId = companyId;
        invoice.FiJournalId = companyId;
        await db.SaveChangesAsync(Ct);
        return new Source(order.Id, order.Lines[0].Id, delivery.Id, delivery.Lines[0].Id, invoice.Id,
            invoice.Lines[0].Id);
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                    new FixedClock(new DateTime(2028, 2, 29, 9, 0, 0)), new FixedUser(7), company))).Options,
            [new SalesReturnDbModel(), new SalesInvoiceDbModel(), new SalesDeliveryDbModel(),
                new SalesOrderDbModel(), new SalesPriceRuleDbModel()], company);
    }
}
