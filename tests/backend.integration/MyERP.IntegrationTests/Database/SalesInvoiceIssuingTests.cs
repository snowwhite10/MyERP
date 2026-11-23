using Microsoft.EntityFrameworkCore;
using Moq;
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
/// SD-INVOICE-ISSUE-001～006 (WU-SD-007a2, ADR-177): the SD half of ISSUE on real SQL — Posted-delivery eligibility,
/// cumulative amount allocation, uninvoiced balance under source locks, request-key replay and rollback. The FI call
/// and the Issuing → Posted update belong to WU-SD-007a3, so these tests commit or roll back Issuing invoices directly.
/// </summary>
public sealed class SalesInvoiceIssuingTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    private sealed record Seeded(long CompanyId, SalesOrder Order)
    {
        public long LineA => Order.Lines[0].Id;
        public long LineB => Order.Lines[1].Id;
    }

    [Fact]
    public async Task PartialInvoicesAcrossDeliveriesAllocateSavedAmountsAndReconcileExactly()
    {
        // SD-INVOICE-ISSUE-001: SD-INVOICE-001/002/006/007 — USD order, TWD base, two Posted deliveries.
        var s = await SeedAsync("IA");
        var d1 = await SeedDeliveryAsync(s, SalesDeliveryStatus.Posted, (s.LineA, 1m), (s.LineB, 1.5m));
        var d2 = await SeedDeliveryAsync(s, SalesDeliveryStatus.Posted, (s.LineA, 2m));

        var first = await IssueAsync(s, new SalesInvoiceIssueInput("K-1", Day,
        [
            new(3, d1.Lines[1].Id, 0.5m), new(1, d1.Lines[0].Id, 1m), new(2, d2.Lines[0].Id, 1m)
        ]));

        Assert.False(first.Replayed);
        await using (var verify = Context(s.CompanyId))
        {
            var saved = await verify.Set<SalesInvoice>().AsNoTracking().Include(x => x.Lines)
                .SingleAsync(x => x.Id == first.Invoice.Id, Ct);
            Assert.Equal(("IA001", SalesInvoiceStatus.Issuing, 21L, s.Order.Id, "USD", "TWD", 31m),
                (saved.DocumentNo, saved.Status, saved.CustomerId, saved.OrderId, saved.CurrencyCode,
                    saved.BaseCurrencyCode, saved.ExchangeRate));
            Assert.Equal(("K-1", new SalesInvoiceIssueInput("K-1", Day, [new(1, d1.Lines[0].Id, 1m),
                new(2, d2.Lines[0].Id, 1m), new(3, d1.Lines[1].Id, 0.5m)]).Fingerprint()),
                (saved.RequestKey, saved.RequestFingerprint));
            Assert.Null(saved.PostedAt);
            Assert.Null(saved.FiSalesSourceId);
            Assert.Collection(saved.Lines.OrderBy(x => x.LineNo),
                x => AssertLine(x, 1, d1.Id, d1.Lines[0].Id, s.LineA, 31, 41, 1m, 33.33m, 1.67m, 1033m, 52m),
                x => AssertLine(x, 2, d2.Id, d2.Lines[0].Id, s.LineA, 31, 41, 1m, 33.34m, 1.66m, 1034m, 51m),
                x => AssertLine(x, 3, d1.Id, d1.Lines[1].Id, s.LineB, 32, 42, 0.5m, 5.00m, 0.25m, 155m, 8m));
            Assert.Equal((71.67m, 3.58m, 75.25m, 2222m, 111m, 2333m), (saved.OriginalNet, saved.OriginalTax,
                saved.OriginalGross, saved.BaseNet, saved.BaseTax, saved.BaseGross));
        }

        // The second invoice completes both order lines: the final portions take the exact saved remainder.
        var second = await IssueAsync(s, new SalesInvoiceIssueInput("K-2", Day.AddDays(1),
            [new(1, d2.Lines[0].Id, 1m), new(2, d1.Lines[1].Id, 1m)]));
        Assert.Equal("IA002", second.Invoice.DocumentNo);
        Assert.Collection(second.Invoice.Lines.OrderBy(x => x.LineNo),
            x => AssertLine(x, 1, d2.Id, d2.Lines[0].Id, s.LineA, 31, 41, 1m, 33.33m, 1.67m, 1033m, 52m),
            x => AssertLine(x, 2, d1.Id, d1.Lines[1].Id, s.LineB, 32, 42, 1m, 10.00m, 0.50m, 310m, 15m));

        await using var totals = Context(s.CompanyId);
        var byOrderLine = await totals.Set<SalesInvoiceLine>().AsNoTracking()
            .GroupBy(x => x.OrderLineId)
            .Select(g => new { g.Key, Q = g.Sum(x => x.Quantity), N = g.Sum(x => x.OriginalNet),
                T = g.Sum(x => x.OriginalTax), G = g.Sum(x => x.OriginalGross), BN = g.Sum(x => x.BaseNet),
                BT = g.Sum(x => x.BaseTax), BG = g.Sum(x => x.BaseGross) })
            .ToDictionaryAsync(x => x.Key, Ct);
        foreach (var line in s.Order.Lines)
            Assert.Equal((line.Quantity, line.OriginalNet, line.OriginalTax, line.OriginalGross, line.BaseNet,
                    line.BaseTax, line.BaseGross),
                (byOrderLine[line.Id].Q, byOrderLine[line.Id].N, byOrderLine[line.Id].T, byOrderLine[line.Id].G,
                    byOrderLine[line.Id].BN, byOrderLine[line.Id].BT, byOrderLine[line.Id].BG));
    }

    [Fact]
    public async Task ExactRemainingQuantitySucceedsAndOneMicroUnitAboveOrZeroBalanceFails()
    {
        // SD-INVOICE-ISSUE-002: SD-INVOICE-005 — balance per delivery line, failures consume no number.
        var s = await SeedAsync("IB");
        var d = await SeedDeliveryAsync(s, SalesDeliveryStatus.Posted, (s.LineA, 1m));
        await IssueAsync(s, Input("B-1", d.Lines[0].Id, 0.4m));

        var over = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            IssueAsync(s, Input("B-2", d.Lines[0].Id, 0.600001m, lineNo: 9)));
        Assert.Equal(("SAL.INVOICE.BALANCE_EXCEEDED", 9, 0.6m),
            (over.ErrorCode, over.Extensions!["lineNo"], over.Extensions["remaining"]));

        await IssueAsync(s, Input("B-3", d.Lines[0].Id, 0.6m));
        var none = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            IssueAsync(s, Input("B-4", d.Lines[0].Id, 0.000001m)));
        Assert.Equal(("SAL.INVOICE.BALANCE_EXCEEDED", 0m), (none.ErrorCode, none.Extensions!["remaining"]));

        await using var verify = Context(s.CompanyId);
        Assert.Equal(["B-1", "B-3"], await verify.Set<SalesInvoice>().OrderBy(x => x.Id)
            .Select(x => x.RequestKey).ToListAsync(Ct));
        Assert.Equal(2, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task IneligibleHiddenMixedAndUnauthorisedSourcesFailWithoutRows()
    {
        // SD-INVOICE-ISSUE-003: SD-INVOICE-013/014/015/016 — state, company, scope, permission, order and date.
        var s = await SeedAsync("IC");
        var posted = await SeedDeliveryAsync(s, SalesDeliveryStatus.Posted, (s.LineA, 1m));
        var sibling = await SeedOrderAsync(s.CompanyId);
        var siblingPosted = await SeedDeliveryAsync(new Seeded(s.CompanyId, sibling), SalesDeliveryStatus.Posted,
            (sibling.Lines[0].Id, 1m));
        var foreign = await SeedAsync("ID");
        var foreignPosted = await SeedDeliveryAsync(foreign, SalesDeliveryStatus.Posted, (foreign.LineA, 1m));

        foreach (var status in new[] { SalesDeliveryStatus.Draft, SalesDeliveryStatus.Pending, SalesDeliveryStatus.Rejected })
        {
            var d = await SeedDeliveryAsync(s, status, (s.LineA, 1m));
            var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => IssueAsync(s,
                new SalesInvoiceIssueInput("C-" + status, Day, [new(1, posted.Lines[0].Id, 1m), new(4, d.Lines[0].Id, 1m)])));
            Assert.Equal(("SAL.INVOICE.DELIVERY_NOT_POSTED", 4), (refused.ErrorCode, refused.Extensions!["lineNo"]));
        }
        var deleted = await SeedDeliveryAsync(s, SalesDeliveryStatus.Draft, (s.LineA, 1m), deleted: true);
        Assert.Equal("SAL.INVOICE.DELIVERY_NOT_POSTED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            IssueAsync(s, Input("C-del", deleted.Lines[0].Id, 1m)))).ErrorCode);

        var mixed = await Assert.ThrowsAsync<BusinessRuleException>(() => IssueAsync(s,
            new SalesInvoiceIssueInput("C-mix", Day, [new(1, posted.Lines[0].Id, 1m), new(2, siblingPosted.Lines[0].Id, 1m)])));
        Assert.Equal(("SAL.INVOICE.SOURCE_MIXED", 2), (mixed.ErrorCode, mixed.Extensions!["lineNo"]));

        var early = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            IssueAsync(s, new SalesInvoiceIssueInput("C-date", Day.AddDays(-1), [new(3, posted.Lines[0].Id, 1m)])));
        Assert.Equal(("SAL.INVOICE.DATE_BEFORE_DELIVERY", 3), (early.ErrorCode, early.Extensions!["lineNo"]));

        // Another company's line, an unknown ID and an order outside the user's ISSUE scope are all hidden (404).
        Assert.Equal("SAL.INVOICE.SOURCE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            IssueAsync(s, Input("C-foreign", foreignPosted.Lines[0].Id, 1m)))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => IssueAsync(s, Input("C-none", long.MaxValue, 1m)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            IssueAsync(s, Input("C-scope", posted.Lines[0].Id, 1m), userId: 8, scope: DataScope.Self));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            IssueAsync(s, Input("C-perm", posted.Lines[0].Id, 1m), allow: false));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            IssueAsync(s, Input("C-zero", posted.Lines[0].Id, 0m)));

        await using var verify = Context(s.CompanyId);
        Assert.False(await verify.Set<SalesInvoice>().AnyAsync(Ct));
        Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
    }

    [Fact]
    public async Task SameRequestKeyReplaysIdenticalPayloadAndRejectsDifferentPayload()
    {
        // SD-INVOICE-ISSUE-004: SD-INVOICE-008/020 — replay creates nothing; changed payload is 409.
        var s = await SeedAsync("IE");
        var d = await SeedDeliveryAsync(s, SalesDeliveryStatus.Posted, (s.LineA, 3m));
        var saved = await IssueAsync(s, Input("E-1", d.Lines[0].Id, 1m));

        var hookCalls = 0;
        var replay = await IssueAsync(s, Input("E-1", d.Lines[0].Id, 1.000000m),
            hook: (_, _) => { hookCalls++; return Task.CompletedTask; });
        Assert.True(replay.Replayed);
        Assert.Equal((saved.Invoice.Id, "IE001", 1m), (replay.Invoice.Id, replay.Invoice.DocumentNo,
            Assert.Single(replay.Invoice.Lines).Quantity));
        Assert.Equal(0, hookCalls);

        foreach (var changed in new[]
                 {
                     Input("E-1", d.Lines[0].Id, 2m),
                     new SalesInvoiceIssueInput("E-1", Day.AddDays(1), [new(1, d.Lines[0].Id, 1m)]),
                     Input("E-1", d.Lines[0].Id, 1m, lineNo: 2)
                 })
            Assert.Equal("SAL.INVOICE.REQUEST_CONFLICT",
                (await Assert.ThrowsAsync<ConflictException>(() => IssueAsync(s, changed))).ErrorCode);
        // A user who cannot see the saved invoice's order gets the same 409, not the invoice.
        await Assert.ThrowsAsync<ConflictException>(() =>
            IssueAsync(s, Input("E-1", d.Lines[0].Id, 1m), userId: 8, scope: DataScope.Self));

        // A new key with the same payload is a new invoice while balance remains.
        Assert.False((await IssueAsync(s, Input("E-2", d.Lines[0].Id, 1m))).Replayed);
        await using var verify = Context(s.CompanyId);
        Assert.Equal(2, await verify.Set<SalesInvoice>().CountAsync(Ct));
        Assert.Equal(2, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task ConcurrentIssuersOfOneDeliveryLineSerializeWhileOtherOrdersProceed()
    {
        // SD-INVOICE-ISSUE-005: SD-INVOICE-009 — the order/delivery-line locks serialize; the loser sees zero balance.
        var s = await SeedAsync("IF");
        var d = await SeedDeliveryAsync(s, SalesDeliveryStatus.Posted, (s.LineA, 1m));
        var sibling = await SeedOrderAsync(s.CompanyId);
        var siblingDelivery = await SeedDeliveryAsync(new Seeded(s.CompanyId, sibling), SalesDeliveryStatus.Posted,
            (sibling.Lines[0].Id, 1m));

        await using var first = Context(s.CompanyId);
        await using (var transaction = await first.Database.BeginTransactionAsync(Ct))
        {
            await Service(first, s.CompanyId, numbers: new StaticNumber("F-" + Guid.NewGuid().ToString("N")[..10]))
                .SaveIssuingAsync(Input("F-1", d.Lines[0].Id, 1m), token: Ct);

            Assert.Equal("blocked", await TryIssueAsync(s.CompanyId, Input("F-2", d.Lines[0].Id, 1m)));
            Assert.Equal("issued", await TryIssueAsync(s.CompanyId, Input("F-3", siblingDelivery.Lines[0].Id, 1m)));
            await transaction.CommitAsync(Ct);
        }

        var loser = await Assert.ThrowsAsync<BusinessRuleException>(() => IssueAsync(s, Input("F-2", d.Lines[0].Id, 1m)));
        Assert.Equal("SAL.INVOICE.BALANCE_EXCEEDED", loser.ErrorCode);
        await using var verify = Context(s.CompanyId);
        Assert.Equal(1m, await verify.Set<SalesInvoiceLine>().Where(x => x.DeliveryLineId == d.Lines[0].Id)
            .SumAsync(x => x.Quantity, Ct));
        Assert.Equal(["F-1", "F-3"], await verify.Set<SalesInvoice>().OrderBy(x => x.RequestKey)
            .Select(x => x.RequestKey).ToListAsync(Ct));
    }

    [Fact]
    public async Task CallerTransactionIsRequiredAndTheCreditHookRunsBeforeLocksAndCanAbort()
    {
        // SD-INVOICE-ISSUE-006: BR-SD-041 hook order; SD-INVOICE-011 — a failure leaves no invoice or number.
        var s = await SeedAsync("IG");
        var d = await SeedDeliveryAsync(s, SalesDeliveryStatus.Posted, (s.LineA, 1m));
        await using (var bare = Context(s.CompanyId))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Service(bare, s.CompanyId).SaveIssuingAsync(Input("G-0", d.Lines[0].Id, 1m), token: Ct));

        var seen = new List<long>();
        var aborted = await Assert.ThrowsAsync<BusinessRuleException>(() => IssueAsync(s,
            Input("G-1", d.Lines[0].Id, 1m), hook: async (customerId, token) =>
            {
                seen.Add(customerId);
                // Runs before SD locks: another connection can still lock the source order row.
                Assert.Equal("issued", await TryIssueAsync(s.CompanyId, Input("G-2", d.Lines[0].Id, 0.5m), rollback: true));
                throw new BusinessRuleException("FI.CREDIT.LOCK_FAILED", "Credit lock refused.");
            }));
        Assert.Equal("FI.CREDIT.LOCK_FAILED", aborted.ErrorCode);
        Assert.Equal([21L], seen);

        await using var verify = Context(s.CompanyId);
        Assert.False(await verify.Set<SalesInvoice>().AnyAsync(Ct));
        Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
    }

    private static void AssertLine(SalesInvoiceLine x, int lineNo, long deliveryId, long deliveryLineId,
        long orderLineId, long itemId, long taxRateId, decimal quantity, decimal net, decimal tax, decimal baseNet,
        decimal baseTax)
    {
        Assert.Equal((lineNo, deliveryId, deliveryLineId, orderLineId, itemId, taxRateId, 5m, quantity),
            (x.LineNo, x.DeliveryId, x.DeliveryLineId, x.OrderLineId, x.ItemId, x.TaxRateId, x.TaxRatePercent,
                x.Quantity));
        Assert.Equal((net, tax, net + tax, baseNet, baseTax, baseNet + baseTax),
            (x.OriginalNet, x.OriginalTax, x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross));
    }

    private static SalesInvoiceIssueInput Input(string key, long deliveryLineId, decimal quantity, int lineNo = 1) =>
        new(key, Day, [new SalesInvoiceLineInput(lineNo, deliveryLineId, quantity)]);

    /// <summary>Runs SaveIssuingAsync in its own transaction; commits on success, rolls back on any error.</summary>
    private async Task<SalesInvoiceIssueOutcome> IssueAsync(Seeded s, SalesInvoiceIssueInput input,
        Func<long, CancellationToken, Task>? hook = null, bool allow = true, long userId = 7, DataScope? scope = null)
    {
        await using var db = Context(s.CompanyId);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        try
        {
            var outcome = await Service(db, s.CompanyId, allow, userId, scope).SaveIssuingAsync(input, hook, Ct);
            await transaction.CommitAsync(Ct);
            return outcome;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<string> TryIssueAsync(long companyId, SalesInvoiceIssueInput input, bool rollback = false)
    {
        await using var db = Context(companyId);
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000", Ct);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        try
        {
            // SYS numbering has its own locking; a fixed unique number isolates the SD locks under test.
            await Service(db, companyId, numbers: new StaticNumber("P-" + Guid.NewGuid().ToString("N")[..12]))
                .SaveIssuingAsync(input, token: Ct);
            if (rollback) await transaction.RollbackAsync(Ct);
            else await transaction.CommitAsync(Ct);
            return "issued";
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

    private sealed class StaticNumber(string number) : INumberGenerator
    {
        public Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default) => Task.FromResult(number);
    }

    private async Task<Seeded> SeedAsync(string prefix)
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales invoice test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using (var rule = Context(companyId))
        {
            rule.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.INVOICE", Name = "Sales invoice",
                Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
                SequenceLength = 3 });
            await rule.SaveChangesAsync(Ct);
        }
        return new Seeded(companyId, await SeedOrderAsync(companyId));
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
        order.Lines.Add(OrderLine(companyId, 1, 31, 41, 3m, 100m, 5m, 3100m, 155m));
        order.Lines.Add(OrderLine(companyId, 2, 32, 42, 1.5m, 15m, 0.75m, 465m, 23m));
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        // Lines are inserted under Draft (trigger 51004); the header then moves to Approved.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2028-02-29' WHERE id = {0}", [order.Id], Ct);
        return order;
    }

    private static SalesOrderLine OrderLine(long companyId, int lineNo, long itemId, long taxRateId,
        decimal quantity, decimal net, decimal tax, decimal baseNet, decimal baseTax) => new()
    {
        CompanyId = companyId, LineNo = lineNo, ItemId = itemId, ItemCode = "I" + itemId, ItemName = "Item",
        Quantity = quantity, UnitPrice = decimal.Round(net / quantity, 6), TaxRateId = taxRateId, TaxRatePercent = 5m,
        OriginalNet = net, OriginalTax = tax, OriginalGross = net + tax, BaseNet = baseNet, BaseTax = baseTax,
        BaseGross = baseNet + baseTax
    };

    private async Task<SalesDelivery> SeedDeliveryAsync(Seeded s, SalesDeliveryStatus status,
        params (long OrderLineId, decimal Quantity)[] lines) => await SeedDeliveryAsync(s, status, false, lines);

    private async Task<SalesDelivery> SeedDeliveryAsync(Seeded s, SalesDeliveryStatus status, (long, decimal) line,
        bool deleted) => await SeedDeliveryAsync(s, status, deleted, [line]);

    private async Task<SalesDelivery> SeedDeliveryAsync(Seeded s, SalesDeliveryStatus status, bool deleted,
        (long OrderLineId, decimal Quantity)[] lines)
    {
        await using var db = Context(s.CompanyId);
        var delivery = new SalesDelivery { CompanyId = s.CompanyId, DocumentNo = "DN" + Guid.NewGuid().ToString("N")[..10],
            OrderId = s.Order.Id, BusinessDate = Day, Status = status, IsDeleted = deleted,
            DeletedAt = deleted ? new DateTime(2028, 2, 29) : null,
            PostedAt = status == SalesDeliveryStatus.Posted ? new DateTime(2028, 2, 29, 9, 0, 0) : null };
        var lineNo = 0;
        foreach (var (orderLineId, quantity) in lines)
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = s.CompanyId, LineNo = ++lineNo,
                OrderLineId = orderLineId, ItemId = s.Order.Lines.Single(x => x.Id == orderLineId).ItemId,
                WarehouseId = 51, Quantity = quantity });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
        return delivery;
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2028, 2, 29, 9, 0, 0)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesInvoiceDbModel(), new SalesDeliveryDbModel(), new SalesOrderDbModel(), new SalesQuotationDbModel(),
            new SalesPriceRuleDbModel(), new SystemOrganizationDbModel()],
        new FixedCompany(companyId));

    private static SalesInvoiceIssuing Service(MyErpDbContext db, long companyId, bool allow = true, long userId = 7,
        DataScope? scope = null, INumberGenerator? numbers = null)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, allow && scope is null,
                allow && scope is { } value
                    ? new Dictionary<string, DataScope> { ["SAL.INVOICE.ISSUE"] = value }
                    : new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesInvoiceIssuing(db, new SalesActionAccess(company, auth.Object), company,
            numbers ?? new NumberGenerator(db, new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(7)));
    }
}
