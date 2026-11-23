using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-INVOICE-SCHEMA-001～006 and SD-INVOICE-SOURCE-001 (WU-SD-007a1, ADR-176): real SQL proof of the
/// <c>sal_invoice</c>/<c>sal_invoice_line</c> keys, checks, same-company source FKs and immutability triggers, and of
/// SD's <see cref="IFinanceSalesInvoiceSource"/> reading the saved invoice inside the caller's transaction.
/// </summary>
public sealed class SalesInvoiceSchemaTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    private sealed record Source(long OrderId, long OrderLineId, long DeliveryId, long DeliveryLineId);

    [Fact]
    public async Task IssuingInvoiceBecomesPostedAndIsThenImmutable()
    {
        // SD-INVOICE-SCHEMA-001: BR-SD-040/042 — Issuing → Posted with FI references, then UPDATE/DELETE refused.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var invoice = NewInvoice(companyId, source, "A");
        db.Add(invoice);
        await db.SaveChangesAsync(Ct);
        Assert.Equal((SalesInvoiceStatus.Issuing, 1), (invoice.Status, invoice.Version));

        MarkPosted(invoice, 9001);
        await db.SaveChangesAsync(Ct);

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesInvoice>().AsNoTracking().Include(x => x.Lines)
            .SingleAsync(x => x.Id == invoice.Id, Ct);
        Assert.Equal((SalesInvoiceStatus.Posted, 2, 9001L, 105m), (saved.Status, saved.Version, saved.FiSalesSourceId!.Value,
            saved.BaseGross));
        Assert.Equal(4m, Assert.Single(saved.Lines).Quantity);

        foreach (var (sql, number) in new[]
                 {
                     ("UPDATE dbo.sal_invoice SET base_net = base_net WHERE id = {0}", 51032),
                     ("UPDATE dbo.sal_invoice SET status = 'Issuing', posted_at = NULL, fi_sales_source_id = NULL, " +
                      "fi_receivable_id = NULL, fi_journal_id = NULL WHERE id = {0}", 51032),
                     ("DELETE dbo.sal_invoice_line WHERE invoice_id = {0}", 51033),
                     ("UPDATE dbo.sal_invoice_line SET quantity = 1 WHERE invoice_id = {0}", 51033),
                     ("INSERT dbo.sal_invoice_line (company_id, invoice_id, line_no, delivery_id, delivery_line_id, " +
                      "order_line_id, item_id, tax_rate_id, tax_rate_percent, quantity, original_net, original_tax, " +
                      "original_gross, base_net, base_tax, base_gross, created_at, created_by, version) " +
                      $"SELECT company_id, invoice_id, 2, delivery_id, delivery_line_id, order_line_id, item_id, tax_rate_id, " +
                      "tax_rate_percent, 1, 0, 0, 0, 0, 0, 0, created_at, created_by, 1 FROM dbo.sal_invoice_line " +
                      "WHERE invoice_id = {0}", 51033)
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() =>
                verify.Database.ExecuteSqlRawAsync(sql, [invoice.Id], Ct));
            Assert.Equal(number, refused.Number);
        }

        // The header delete hits the trigger (or the Restrict line FK) and leaves both rows intact.
        await Assert.ThrowsAsync<SqlException>(() =>
            verify.Database.ExecuteSqlRawAsync("DELETE dbo.sal_invoice WHERE id = {0}", [invoice.Id], Ct));
        Assert.Equal(1, await verify.Set<SalesInvoiceLine>().CountAsync(x => x.InvoiceId == invoice.Id, Ct));
        Assert.Equal(SalesInvoiceStatus.Posted, (await verify.Set<SalesInvoice>().AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id, Ct)).Status);
    }

    [Fact]
    public async Task IssuingInvoiceContentAndLinesAreWriteOnceButRollbackCleanupIsAllowed()
    {
        // SD-INVOICE-SCHEMA-002: while Issuing only status/FI references may change; lines are never updated.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var invoice = NewInvoice(companyId, source, "B");
        db.Add(invoice);
        await db.SaveChangesAsync(Ct);

        foreach (var (sql, number) in new[]
                 {
                     ("UPDATE dbo.sal_invoice SET original_net = 99, original_gross = 104, base_net = 99, base_gross = 104 " +
                      "WHERE id = {0}", 51032),
                     ("UPDATE dbo.sal_invoice SET request_key = 'other' WHERE id = {0}", 51032),
                     ("UPDATE dbo.sal_invoice SET customer_id = customer_id + 1 WHERE id = {0}", 51032),
                     ("UPDATE dbo.sal_invoice SET business_date = DATEADD(day, 1, business_date) WHERE id = {0}", 51032),
                     ("UPDATE dbo.sal_invoice_line SET quantity = 1 WHERE invoice_id = {0}", 51033)
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlRawAsync(sql, [invoice.Id], Ct));
            Assert.Equal(number, refused.Number);
        }

        // A failed ISSUE that cleans up its own Issuing rows (lines first, then header) is permitted.
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
            "DELETE dbo.sal_invoice_line WHERE invoice_id = {0}", [invoice.Id], Ct));
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
            "DELETE dbo.sal_invoice WHERE id = {0}", [invoice.Id], Ct));
    }

    [Fact]
    public async Task NumberRequestKeyAndFiSourceAreUniquePerCompany()
    {
        // SD-INVOICE-SCHEMA-003: BR-SD-040 — permanent number, request key and FI source identity; other companies
        // may reuse the same number and key.
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        var otherSource = await SeedSourceAsync(otherCompanyId);
        await using (var db = Context(companyId))
        {
            var first = NewInvoice(companyId, source, "C");
            db.Add(first);
            await db.SaveChangesAsync(Ct);
            MarkPosted(first, 7001);
            await db.SaveChangesAsync(Ct);
        }

        foreach (var mutate in new Action<SalesInvoice>[]
                 {
                     x => x.DocumentNo = "INV-C",
                     x => x.RequestKey = "key-C"
                 })
        {
            await using var duplicate = Context(companyId);
            var candidate = NewInvoice(companyId, source, "D");
            mutate(candidate);
            duplicate.Add(candidate);
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(Ct));
        }

        // A committed invoice only reaches Posted through Issuing (lines may not be added to a Posted header), so
        // the FI source identity collision is proved on the Issuing → Posted update; the attempt rolls back.
        await using (var duplicate = Context(companyId))
        {
            await using var tx = await duplicate.Database.BeginTransactionAsync(Ct);
            var candidate = NewInvoice(companyId, source, "D");
            duplicate.Add(candidate);
            await duplicate.SaveChangesAsync(Ct);
            MarkPosted(candidate, 7001);
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(Ct));
            Assert.Contains("fi_sales_source_id", refused.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
            await tx.RollbackAsync(Ct);
        }

        await using (var other = Context(otherCompanyId))
        {
            var reused = NewInvoice(otherCompanyId, otherSource, "C");
            other.Add(reused);
            await other.SaveChangesAsync(Ct);
            MarkPosted(reused, 7001);
            await other.SaveChangesAsync(Ct);
        }

        await using var verify = Context(companyId);
        Assert.Equal(1, await verify.Set<SalesInvoice>().CountAsync(Ct));
        Assert.Equal(1, await verify.Set<SalesInvoiceLine>().CountAsync(Ct));
        await using var verifyOther = Context(otherCompanyId);
        Assert.Equal(SalesInvoiceStatus.Posted, (await verifyOther.Set<SalesInvoice>().AsNoTracking()
            .SingleAsync(Ct)).Status);
    }

    public static TheoryData<string> InvalidShapes() =>
    [
        "header-gross", "header-negative", "base-rate", "base-amounts", "posted-without-fi", "issuing-with-fi",
        "blank-key", "blank-fingerprint", "status", "line-quantity", "line-gross", "line-tax-rate", "line-no",
        "duplicate-line-no", "line-refs"
    ];

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public async Task InvalidHeaderOrLineShapesSaveNothing(string shape)
    {
        // SD-INVOICE-SCHEMA-004: SD-INVOICE-004/005 — SQL CHECKs back the service; a rejected row leaves zero rows.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var invoice = NewInvoice(companyId, source, "E");
        var line = invoice.Lines[0];
        switch (shape)
        {
            case "header-gross": invoice.OriginalGross = 104m; invoice.BaseGross = 104m; break;
            case "header-negative": invoice.OriginalTax = -1m; invoice.OriginalGross = 99m; invoice.BaseTax = -1m;
                invoice.BaseGross = 99m; break;
            case "base-rate": invoice.ExchangeRate = 1.5m; break;
            case "base-amounts": invoice.BaseNet = 101m; invoice.BaseGross = 106m; break;
            case "posted-without-fi": invoice.Status = SalesInvoiceStatus.Posted;
                invoice.PostedAt = new DateTime(2028, 2, 29, 10, 0, 0); break;
            case "issuing-with-fi": invoice.FiSalesSourceId = 1; break;
            case "blank-key": invoice.RequestKey = ""; break;
            case "blank-fingerprint": invoice.RequestFingerprint = ""; break;
            case "status": break;
            case "line-quantity": line.Quantity = 0m; break;
            case "line-gross": line.OriginalGross = 1m; break;
            case "line-tax-rate": line.TaxRatePercent = 100.0001m; break;
            case "line-no": line.LineNo = 0; break;
            case "duplicate-line-no": invoice.Lines.Add(NewLine(companyId, source, 1, 1m, 25m, 1.25m)); break;
            case "line-refs": line.TaxRateId = 0; break;
        }

        db.Add(invoice);
        if (shape == "status")
        {
            await db.SaveChangesAsync(Ct);
            var refused = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_invoice SET status = 'Voided' WHERE id = {0}", [invoice.Id], Ct));
            Assert.Equal(547, refused.Number);
            return;
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        await using var verify = Context(companyId);
        Assert.Equal(0, await verify.Set<SalesInvoice>().CountAsync(Ct));
        Assert.Equal(0, await verify.Set<SalesInvoiceLine>().CountAsync(Ct));
    }

    [Fact]
    public async Task SourceReferencesMustBeSameCompanyAndInvoicedDeliveryLinesCannotBeRemoved()
    {
        // SD-INVOICE-SCHEMA-005: SD-INVOICE-014/024 — same-company FKs; an invoiced delivery or order line is protected.
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        var foreign = await SeedSourceAsync(otherCompanyId);
        foreach (var crossed in new[]
                 {
                     source with { DeliveryLineId = foreign.DeliveryLineId },
                     source with { OrderLineId = foreign.OrderLineId },
                     source with { DeliveryId = foreign.DeliveryId },
                     source with { OrderId = foreign.OrderId }
                 })
        {
            await using var db = Context(companyId);
            db.Add(NewInvoice(companyId, crossed, "F"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await using (var db = Context(companyId))
        {
            db.Add(NewInvoice(companyId, source, "G"));
            await db.SaveChangesAsync(Ct);
        }

        await using var verify = Context(companyId);
        foreach (var sql in new[]
                 {
                     "DELETE dbo.sal_delivery_line WHERE id = {0}",
                     "DELETE dbo.sal_order_line WHERE id = {1}"
                 })
            await Assert.ThrowsAsync<SqlException>(() =>
                verify.Database.ExecuteSqlRawAsync(sql, [source.DeliveryLineId, source.OrderLineId], Ct));
        Assert.Equal(1, await verify.Set<SalesInvoiceLine>().CountAsync(Ct));
    }

    [Fact]
    public async Task ForeignCurrencyInvoiceKeepsSavedRateAndIndependentBaseAmounts()
    {
        // SD-INVOICE-SCHEMA-006: SD-INVOICE-006 — the saved decimal(19,8) rate and decimal(19,4) base amounts round-trip.
        var companyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using (var db = Context(companyId))
        {
            var invoice = NewInvoice(companyId, source, "H");
            invoice.CurrencyCode = "USD";
            invoice.ExchangeRate = 31.12345678m;
            invoice.BaseNet = 3112.3457m;
            invoice.BaseTax = 155.6173m;
            invoice.BaseGross = 3267.9630m;
            invoice.Lines[0].BaseNet = 3112.3457m;
            invoice.Lines[0].BaseTax = 155.6173m;
            invoice.Lines[0].BaseGross = 3267.9630m;
            invoice.Lines[0].Quantity = 3.999999m;
            db.Add(invoice);
            await db.SaveChangesAsync(Ct);
        }

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesInvoice>().AsNoTracking().Include(x => x.Lines).SingleAsync(Ct);
        Assert.Equal((31.12345678m, 3267.9630m, 3.999999m, Day),
            (saved.ExchangeRate, saved.BaseGross, saved.Lines[0].Quantity, saved.BusinessDate));
    }

    [Fact]
    public async Task FinanceSourcePortReadsTheSavedInvoiceInsideTheCallersTransactionOnly()
    {
        // SD-INVOICE-SOURCE-001: BR-SD-040 — FI reads Issuing then Posted with the saved fingerprint and totals;
        // missing or foreign-company IDs are null; a read outside the shared transaction is a programming error.
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        var source = await SeedSourceAsync(companyId);
        await using var db = Context(companyId);
        var port = new SalesInvoiceSource(db);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var invoice = NewInvoice(companyId, source, "J");
        db.Add(invoice);
        await db.SaveChangesAsync(Ct);
        var dbTransaction = transaction.GetDbTransaction();

        var issuing = await port.ReadInvoiceAsync(companyId, invoice.Id, dbTransaction, Ct);
        Assert.Equal(new FinanceSalesInvoiceSnapshot(companyId, invoice.Id, 1, "fp-J", 21,
            FinanceSalesSourceState.Issuing, Day, "TWD", "TWD", 1m, 100m, 5m, 105m, 100m, 5m, 105m), issuing);

        MarkPosted(invoice, 8001);
        await db.SaveChangesAsync(Ct);
        var posted = await port.ReadInvoiceAsync(companyId, invoice.Id, dbTransaction, Ct);
        Assert.Equal((FinanceSalesSourceState.Posted, 1), (posted!.State, posted.SourceVersion));

        Assert.Null(await port.ReadInvoiceAsync(companyId, invoice.Id + 1_000_000, dbTransaction, Ct));
        Assert.Null(await port.ReadInvoiceAsync(otherCompanyId, invoice.Id, dbTransaction, Ct));
        Assert.Null(await port.ReadInvoiceAsync(companyId, 0, dbTransaction, Ct));

        await using var unrelated = new SqlConnection(database.ConnectionString);
        await unrelated.OpenAsync(Ct);
        await using var unrelatedTransaction = await unrelated.BeginTransactionAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            port.ReadInvoiceAsync(companyId, invoice.Id, unrelatedTransaction, Ct));
        await transaction.RollbackAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            port.ReadInvoiceAsync(companyId, invoice.Id, dbTransaction, Ct));
    }

    private static SalesInvoice NewInvoice(long companyId, Source source, string suffix)
    {
        var invoice = new SalesInvoice
        {
            CompanyId = companyId, DocumentNo = "INV-" + suffix, RequestKey = "key-" + suffix,
            RequestFingerprint = "fp-" + suffix, CustomerId = 21, OrderId = source.OrderId, BusinessDate = Day,
            CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
            OriginalNet = 100m, OriginalTax = 5m, OriginalGross = 105m, BaseNet = 100m, BaseTax = 5m, BaseGross = 105m
        };
        invoice.Lines.Add(NewLine(companyId, source, 1, 4m, 100m, 5m));
        return invoice;
    }

    private static SalesInvoiceLine NewLine(long companyId, Source source, int lineNo, decimal quantity, decimal net,
        decimal tax) => new()
    {
        CompanyId = companyId, LineNo = lineNo, DeliveryId = source.DeliveryId, DeliveryLineId = source.DeliveryLineId,
        OrderLineId = source.OrderLineId, ItemId = 31, TaxRateId = 41, TaxRatePercent = 5m, Quantity = quantity,
        OriginalNet = net, OriginalTax = tax, OriginalGross = net + tax, BaseNet = net, BaseTax = tax,
        BaseGross = net + tax
    };

    private static void MarkPosted(SalesInvoice invoice, long fiSourceId)
    {
        invoice.Status = SalesInvoiceStatus.Posted;
        invoice.PostedAt = new DateTime(2028, 2, 29, 10, 0, 0);
        invoice.FiSalesSourceId = fiSourceId;
        invoice.FiReceivableId = fiSourceId + 1;
        invoice.FiJournalId = fiSourceId + 2;
    }

    /// <summary>An Approved order with one line and a Posted delivery of 10 units (seeded directly).</summary>
    private async Task<Source> SeedSourceAsync(long companyId)
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
        // Lines are inserted under Draft (trigger 51004); the header then moves to Approved (51005).
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
            Quantity = 10m, InvMovementId = companyId * 10
        });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
        return new Source(order.Id, order.Lines[0].Id, delivery.Id, delivery.Lines[0].Id);
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                    new FixedClock(new DateTime(2028, 2, 29, 9, 0, 0)), new FixedUser(7), company))).Options,
            [new SalesInvoiceDbModel(), new SalesDeliveryDbModel(), new SalesOrderDbModel(),
                new SalesPriceRuleDbModel()], company);
    }
}
