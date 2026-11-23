using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-RETURN-FI-001～003 (WU-SD-008a4, ADR-189, BR-SD-056): SD's real <see cref="IFinanceSalesReturnSource"/> on real
/// SQL, read by the real FI credit posting service (<see cref="ISalesCreditPostingService"/>) against an invoice that
/// was issued through the real SD ISSUE command and FI/AR/GL posting. Only numbering, the caller's authorisation and
/// the customer lookup are test doubles; the FI account mapping is the real <c>gl_sales_invoice_account_map</c> row.
/// Returns are saved directly (the Draft builder is covered by SD-RETURN-DRAFT-*), because the port only reports
/// what SD saved.
/// </summary>
public sealed class SalesReturnFinanceSourceTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2026, 11, 6);
    private static readonly DateTime Now = new(2026, 11, 6, 9, 30, 0);
    private const long Customer = 21;

    [Fact]
    public async Task SourceReportsOnlyBilledPortionsWithInvoiceLineCapsAndFiCreditsThemOnce()
    {
        // SD-RETURN-FI-001: SD-RETURN-014/015/019, FI-SALES-004 — billed allocations only, ordered by return line,
        // header = allocation sums, caps = saved invoice lines; Draft is not postable; Pending posts one credit
        // source, a balanced credit journal and reduces the original open AR; an identical call replays unchanged.
        var s = await SeedAsync();
        var invoiceLines = s.Invoice.Lines.OrderBy(x => x.LineNo).ToArray();
        var a = invoiceLines[0];
        var b = invoiceLines[1];
        var returnId = await SaveReturnAsync(s, "F-1",
            // Line 2 first in insertion order: the port must still order by line number.
            new ReturnLine(2, s.Delivery.Lines[1], 1.5m, 0m, [(b, 1.5m, b.OriginalNet, b.OriginalTax, b.BaseNet, b.BaseTax)]),
            new ReturnLine(1, s.Delivery.Lines[0], 1.5m, 0.5m, [(a, 1m, 30m, 1.5m, 930m, 46m)], UnbilledNet: 10m,
                UnbilledTax: 0.5m, UnbilledBaseNet: 310m, UnbilledBaseTax: 16m));

        var draft = await ReadAsync(s, returnId);
        Assert.NotNull(draft);
        Assert.Equal((s.CompanyId, returnId, 1, "fp-F-1", Customer, FinanceSalesSourceState.Draft, Day.AddDays(1),
                "USD", "TWD", 31m),
            (draft.CompanyId, draft.ReturnId, draft.SourceVersion, draft.Fingerprint, draft.CustomerId, draft.State,
                draft.BusinessDate, draft.Currency, draft.BaseCurrency, draft.ExchangeRate));
        Assert.Equal(
            [
                new FinanceSalesReturnLine(s.Invoice.Id, a.Id, 1m, 30m, 1.5m, 31.5m, 930m, 46m, 976m,
                    new FinanceSalesInvoiceLineCap(a.Quantity, a.OriginalNet, a.OriginalTax, a.BaseNet, a.BaseTax)),
                new FinanceSalesReturnLine(s.Invoice.Id, b.Id, 1.5m, b.OriginalNet, b.OriginalTax, b.OriginalGross,
                    b.BaseNet, b.BaseTax, b.BaseGross,
                    new FinanceSalesInvoiceLineCap(b.Quantity, b.OriginalNet, b.OriginalTax, b.BaseNet, b.BaseTax))
            ],
            draft.Lines);
        // Billed totals only: the unbilled 0.5 (10.00 + 0.50; 310 + 16) of line 1 is not FI's business.
        Assert.Equal((30m + b.OriginalNet, 1.5m + b.OriginalTax, 31.5m + b.OriginalGross,
                930m + b.BaseNet, 46m + b.BaseTax, 976m + b.BaseGross),
            (draft.NetAmount, draft.TaxAmount, draft.GrossAmount, draft.BaseNetAmount, draft.BaseTaxAmount,
                draft.BaseGrossAmount));

        var command = new SalesCreditPostingCommand(s.CompanyId, returnId, Customer, 1, "fp-F-1");
        AssertRejected(await PostCreditAsync(s, command), 422, SalesPostingErrors.SourceNotPostable);

        var version = await SetStatusAsync(s, returnId, SalesReturnStatus.Pending);
        Assert.Equal(2, version);
        Assert.Equal(FinanceSalesSourceState.Issuing, (await ReadAsync(s, returnId))!.State);
        // The command must carry the current row version: the Draft version is now stale.
        AssertRejected(await PostCreditAsync(s, command), 409, SalesPostingErrors.SourceMismatch);

        var openBefore = await OpenArAsync(s);
        var posted = await PostCreditAsync(s, command with { SourceVersion = version });
        Assert.Equal((SalesPostingOutcomeKind.Posted, true, 200), (posted.Kind, posted.Changed, posted.HttpStatus));
        var receipt = posted.Receipt!;
        Assert.Equal(("USD", draft.GrossAmount, "TWD", draft.BaseGrossAmount),
            (receipt.Currency, receipt.GrossAmount, receipt.BaseCurrency, receipt.BaseGrossAmount));
        Assert.Equal([(s.Invoice.Id, a.Id, 1m, 976m), (s.Invoice.Id, b.Id, 1.5m, b.BaseGross)],
            receipt.Allocations.Select(x => (x.InvoiceId, x.InvoiceLineId, x.Quantity, x.BaseGrossAmount)));
        Assert.Equal(openBefore - draft.BaseGrossAmount, await OpenArAsync(s));

        await using (var verify = s.Raw())
        {
            var journal = await verify.Database.SqlQuery<JournalRow>($"""
                SELECT account_code, debit, credit FROM dbo.gl_journal_line
                WHERE company_id = {s.CompanyId} AND journal_id = {receipt.JournalId} ORDER BY account_code
                """).ToListAsync(Ct);
            Assert.Equal(
                [
                    new("1131", 0m, draft.BaseGrossAmount), new("2171", draft.BaseTaxAmount, 0m),
                    new("4101", draft.BaseNetAmount, 0m)
                ],
                journal);
        }

        // Identical replay under the same key writes nothing more.
        var counts = await FiCountsAsync(s);
        var replay = await PostCreditAsync(s, command with { SourceVersion = version });
        Assert.Equal((SalesPostingOutcomeKind.Unchanged, false, receipt.JournalId),
            (replay.Kind, replay.Changed, replay.Receipt!.JournalId));
        Assert.Equal(counts, await FiCountsAsync(s));
    }

    [Fact]
    public async Task UnbilledDeletedRejectedAndOverCreditedReturnsAreRefusedWithoutFiRows()
    {
        // SD-RETURN-FI-002: SD-RETURN-016/020/021 — a wholly unbilled return has no FI amount (422), soft-deleted and
        // Rejected returns are not postable, and a second return of an already credited invoice line is refused by
        // FI's cumulative cap; none of them writes an FI source, allocation, journal or open-AR change.
        var s = await SeedAsync();
        var b = s.Invoice.Lines.Single(x => x.LineNo == 2);
        var credited = await SaveReturnAsync(s, "G-1",
            new ReturnLine(1, s.Delivery.Lines[1], 1.5m, 0m, [(b, 1.5m, b.OriginalNet, b.OriginalTax, b.BaseNet, b.BaseTax)]));
        var v = await SetStatusAsync(s, credited, SalesReturnStatus.Pending);
        Assert.Equal(SalesPostingOutcomeKind.Posted,
            (await PostCreditAsync(s, new(s.CompanyId, credited, Customer, v, "fp-G-1"))).Kind);
        var counts = await FiCountsAsync(s);
        var open = await OpenArAsync(s);

        var unbilled = await SaveReturnAsync(s, "G-2",
            new ReturnLine(1, s.Delivery.Lines[0], 0.5m, 0.5m, [], UnbilledNet: 10m, UnbilledTax: 0.5m, UnbilledBaseNet: 310m,
                UnbilledBaseTax: 16m));
        v = await SetStatusAsync(s, unbilled, SalesReturnStatus.Pending);
        var empty = await ReadAsync(s, unbilled);
        Assert.Equal((FinanceSalesSourceState.Issuing, 0, 0m, 0m), (empty!.State, empty.Lines.Count,
            empty.GrossAmount, empty.BaseGrossAmount));
        AssertRejected(await PostCreditAsync(s, new(s.CompanyId, unbilled, Customer, v, "fp-G-2")), 422,
            SalesPostingErrors.InvalidAmounts);

        var again = await SaveReturnAsync(s, "G-3",
            new ReturnLine(1, s.Delivery.Lines[1], 0.1m, 0m, [(b, 0.1m, 1m, 0.05m, 31m, 2m)]));
        v = await SetStatusAsync(s, again, SalesReturnStatus.Pending);
        AssertRejected(await PostCreditAsync(s, new(s.CompanyId, again, Customer, v, "fp-G-3")), 422,
            SalesCreditPostingErrors.CreditExceedsLine, line: 1);

        var rejected = await SaveReturnAsync(s, "G-4",
            new ReturnLine(1, s.Delivery.Lines[0], 0.1m, 0m, [(s.Invoice.Lines.Single(x => x.LineNo == 1), 0.1m, 1m, 0.05m, 31m, 2m)]));
        await SetStatusAsync(s, rejected, SalesReturnStatus.Pending);
        v = await SetStatusAsync(s, rejected, SalesReturnStatus.Rejected);
        Assert.Equal(FinanceSalesSourceState.Rejected, (await ReadAsync(s, rejected))!.State);
        AssertRejected(await PostCreditAsync(s, new(s.CompanyId, rejected, Customer, v, "fp-G-4")), 422,
            SalesPostingErrors.SourceNotPostable);

        var deleted = await SaveReturnAsync(s, "G-5",
            new ReturnLine(1, s.Delivery.Lines[0], 0.1m, 0m, [(s.Invoice.Lines.Single(x => x.LineNo == 1), 0.1m, 1m, 0.05m, 31m, 2m)]));
        await using (var db = s.Raw())
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_return SET is_deleted = 1, deleted_at = '2026-11-07' WHERE id = {0}", [deleted], Ct);
        Assert.Equal(FinanceSalesSourceState.Deleted, (await ReadAsync(s, deleted))!.State);
        AssertRejected(await PostCreditAsync(s, new(s.CompanyId, deleted, Customer, 1, "fp-G-5")), 422,
            SalesPostingErrors.SourceNotPostable);

        Assert.Equal(counts, await FiCountsAsync(s));
        Assert.Equal(open, await OpenArAsync(s));
    }

    [Fact]
    public async Task SourceNeedsTheCallersTransactionAndHidesUnknownAndForeignReturns()
    {
        // SD-RETURN-FI-003: the port reads only inside the caller's transaction on the shared DbContext; an unknown
        // ID, another company's return or a company that is not the DbContext's own is null (FI answers 404).
        var s = await SeedAsync();
        var other = await SeedAsync();
        var b = s.Invoice.Lines.Single(x => x.LineNo == 2);
        var returnId = await SaveReturnAsync(s, "H-1",
            new ReturnLine(1, s.Delivery.Lines[1], 1m, 0m, [(b, 1m, 10m, 0.5m, 310m, 15m)]));
        var foreign = await SaveReturnAsync(other, "H-1",
            new ReturnLine(1, other.Delivery.Lines[1], 1m, 0m,
                [(other.Invoice.Lines.Single(x => x.LineNo == 2), 1m, 10m, 0.5m, 310m, 15m)]));

        await using (var scope = s.Provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MyErpDbContext>();
            var port = sp.GetRequiredService<IFinanceSalesReturnSource>();
            await using var own = await db.Database.BeginTransactionAsync(Ct);
            await using var stranger = s.Raw();
            await using var strangerTransaction = await stranger.Database.BeginTransactionAsync(Ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                port.ReadReturnAsync(s.CompanyId, returnId, strangerTransaction.GetDbTransaction(), Ct));
            var tx = own.GetDbTransaction();
            Assert.NotNull(await port.ReadReturnAsync(s.CompanyId, returnId, tx, Ct));
            Assert.Null(await port.ReadReturnAsync(s.CompanyId, foreign, tx, Ct));
            Assert.Null(await port.ReadReturnAsync(other.CompanyId, foreign, tx, Ct));
            Assert.Null(await port.ReadReturnAsync(s.CompanyId, long.MaxValue, tx, Ct));
            Assert.Null(await port.ReadReturnAsync(s.CompanyId, 0, tx, Ct));
        }
        await SetStatusAsync(s, returnId, SalesReturnStatus.Pending);
        AssertRejected(await PostCreditAsync(s, new(s.CompanyId, foreign, Customer, 2, "fp-H-1")), 404,
            SalesPostingErrors.SourceNotFound);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record JournalRow(string AccountCode, decimal Debit, decimal Credit);

    private sealed record Seeded(long CompanyId, SalesDelivery Delivery, SalesInvoice Invoice, ServiceProvider Provider,
        Func<MyErpDbContext> Raw);

    /// <summary>One return line: billed portions are (invoice line, quantity, net, tax, base net, base tax).</summary>
    private sealed record ReturnLine(int LineNo, SalesDeliveryLine Source, decimal Quantity, decimal Unbilled,
        (SalesInvoiceLine Line, decimal Quantity, decimal Net, decimal Tax, decimal BaseNet, decimal BaseTax)[] Billed,
        decimal UnbilledNet = 0m, decimal UnbilledTax = 0m, decimal UnbilledBaseNet = 0m, decimal UnbilledBaseTax = 0m);

    private static void AssertRejected(SalesCreditPostingResult result, int status, string code, int? line = null)
    {
        Assert.Equal((SalesPostingOutcomeKind.Rejected, false, status, code, line),
            (result.Kind, result.Changed, result.HttpStatus, result.ErrorCode, result.InvalidLine));
        Assert.Null(result.Receipt);
    }

    /// <summary>Saves a Draft return with header = line sums and line = billed + unbilled amounts; returns its ID.</summary>
    private static async Task<long> SaveReturnAsync(Seeded s, string key, params ReturnLine[] lines)
    {
        await using var db = s.Raw();
        var header = new SalesReturn { CompanyId = s.CompanyId, DocumentNo = "SR" + Guid.NewGuid().ToString("N")[..10],
            RequestKey = key, RequestFingerprint = "fp-" + key, CustomerId = Customer, OrderId = s.Delivery.OrderId,
            BusinessDate = Day.AddDays(1), CurrencyCode = "USD", BaseCurrencyCode = "TWD", ExchangeRate = 31m };
        foreach (var l in lines)
        {
            var line = new SalesReturnLine { CompanyId = s.CompanyId, LineNo = l.LineNo, DeliveryId = s.Delivery.Id,
                DeliveryLineId = l.Source.Id, OrderLineId = l.Source.OrderLineId, ItemId = l.Source.ItemId,
                OriginalOutboundMovementId = 7000 + l.Source.Id, WarehouseId = l.Source.WarehouseId,
                Quantity = l.Quantity, UnbilledQuantity = l.Unbilled,
                OriginalNet = l.UnbilledNet + l.Billed.Sum(x => x.Net),
                OriginalTax = l.UnbilledTax + l.Billed.Sum(x => x.Tax),
                BaseNet = l.UnbilledBaseNet + l.Billed.Sum(x => x.BaseNet),
                BaseTax = l.UnbilledBaseTax + l.Billed.Sum(x => x.BaseTax) };
            line.OriginalGross = line.OriginalNet + line.OriginalTax;
            line.BaseGross = line.BaseNet + line.BaseTax;
            foreach (var p in l.Billed)
                line.InvoiceAllocations.Add(new SalesReturnInvoiceAllocation { CompanyId = s.CompanyId,
                    InvoiceId = p.Line.InvoiceId, InvoiceLineId = p.Line.Id, Quantity = p.Quantity,
                    OriginalNet = p.Net, OriginalTax = p.Tax, OriginalGross = p.Net + p.Tax,
                    BaseNet = p.BaseNet, BaseTax = p.BaseTax, BaseGross = p.BaseNet + p.BaseTax });
            header.Lines.Add(line);
        }
        header.OriginalNet = header.Lines.Sum(x => x.OriginalNet);
        header.OriginalTax = header.Lines.Sum(x => x.OriginalTax);
        header.OriginalGross = header.Lines.Sum(x => x.OriginalGross);
        header.BaseNet = header.Lines.Sum(x => x.BaseNet);
        header.BaseTax = header.Lines.Sum(x => x.BaseTax);
        header.BaseGross = header.Lines.Sum(x => x.BaseGross);
        db.Add(header);
        await db.SaveChangesAsync(Ct);
        return header.Id;
    }

    /// <summary>Moves the return through the SQL state machine with EF so the row version advances; returns it.</summary>
    private static async Task<int> SetStatusAsync(Seeded s, long returnId, SalesReturnStatus status)
    {
        await using var db = s.Raw();
        var row = await db.Set<SalesReturn>().SingleAsync(x => x.Id == returnId, Ct);
        row.Status = status;
        await db.SaveChangesAsync(Ct);
        return row.Version;
    }

    private static async Task<FinanceSalesReturnSnapshot?> ReadAsync(Seeded s, long returnId)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);
        return await scope.ServiceProvider.GetRequiredService<IFinanceSalesReturnSource>()
            .ReadReturnAsync(s.CompanyId, returnId, transaction.GetDbTransaction(), Ct);
    }

    private static async Task<SalesCreditPostingResult> PostCreditAsync(Seeded s, SalesCreditPostingCommand command)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);
        var result = await sp.GetRequiredService<ISalesCreditPostingService>().PostInCurrentTransactionAsync(command, Ct);
        if (db.Database.CurrentTransaction is not null)
            await transaction.CommitAsync(Ct);
        return result;
    }

    private static async Task<decimal> OpenArAsync(Seeded s)
    {
        await using var db = s.Raw();
        return await db.Database.SqlQuery<decimal>(
            $"SELECT open_base_amount AS [Value] FROM dbo.ar_posted_receivable WHERE company_id = {s.CompanyId}")
            .SingleAsync(Ct);
    }

    private static async Task<(int Sources, int Allocations, int Journals)> FiCountsAsync(Seeded s)
    {
        await using var db = s.Raw();
        var sources = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_sales_source WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        var allocations = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_credit_allocation WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        var journals = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        return (sources, allocations, journals);
    }

    /// <summary>
    /// Company with TWD, accounts 1131/2171/4101, an open November period and the real SD invoice map; an Approved USD
    /// order (rate 31) for customer 21, a Posted delivery of A 3 + B 1.5 and an invoice of A 2 + B 1.5 issued through
    /// the real SD ISSUE command (FI source, open AR and journal posted).
    /// </summary>
    private async Task<Seeded> SeedAsync()
    {
        long companyId;
        await using (var setup = Organization(null))
        {
            var company = new SystemCompany { Code = "RF" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales return FI source test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }

        var provider = Provider(companyId);
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<CurrencyService>().CreateAsync("TWD", "New Taiwan Dollar", "NT$", 2, true, Ct);
            var accounts = sp.GetRequiredService<AccountService>();
            var assets = await accounts.CreateAsync("1", "Assets", null, AccountType.Asset, null, Ct);
            await accounts.CreateAsync("1131", "Receivable", assets.Id, null, null, Ct);
            var liabilities = await accounts.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, Ct);
            await accounts.CreateAsync("2171", "Output tax", liabilities.Id, null, null, Ct);
            var revenue = await accounts.CreateAsync("4", "Revenue", null, AccountType.Revenue, null, Ct);
            await accounts.CreateAsync("4101", "Sales", revenue.Id, null, null, Ct);
            await sp.GetRequiredService<GlPeriodWriteService>().CreateAsync(companyId,
                new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), true, Ct);
            var db = sp.GetRequiredService<MyErpDbContext>();
            db.Add(new SalesInvoiceAccountMapping
            {
                ReceivableAccountCode = "1131", RevenueAccountCode = "4101", OutputTaxAccountCode = "2171"
            });
            await db.SaveChangesAsync(Ct);
        }

        Func<MyErpDbContext> raw = () => Raw(companyId);
        var order = await SeedOrderAsync(companyId);
        SalesDelivery delivery;
        await using (var db = raw())
        {
            delivery = new SalesDelivery { CompanyId = companyId, DocumentNo = "DN" + Guid.NewGuid().ToString("N")[..10],
                OrderId = order.Id, BusinessDate = Day, Status = SalesDeliveryStatus.Posted, PostedAt = Now };
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 1, OrderLineId = order.Lines[0].Id,
                ItemId = order.Lines[0].ItemId, WarehouseId = 51, Quantity = 3m });
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 2, OrderLineId = order.Lines[1].Id,
                ItemId = order.Lines[1].ItemId, WarehouseId = 51, Quantity = 1.5m });
            db.Add(delivery);
            await db.SaveChangesAsync(Ct);
        }

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SalesInvoiceIssueService>().IssueAsync(
                new SalesInvoiceIssueInput("INV-1", Day,
                    [new SalesInvoiceLineInput(1, delivery.Lines[0].Id, 2m), new SalesInvoiceLineInput(2, delivery.Lines[1].Id, 1.5m)]),
                Ct);
        SalesInvoice invoice;
        await using (var db = raw())
            invoice = await db.Set<SalesInvoice>().AsNoTracking().Include(x => x.Lines).SingleAsync(Ct);
        Assert.Equal(SalesInvoiceStatus.Posted, invoice.Status);
        return new Seeded(companyId, delivery, invoice, provider, raw);
    }

    /// <summary>Approved USD order: A 3 units net 100.00 + tax 5.00 (base 3,100 + 155); B 1.5 units 15.00 + 0.75 (465 + 23).</summary>
    private async Task<SalesOrder> SeedOrderAsync(long companyId)
    {
        await using var db = Raw(companyId);
        var order = new SalesOrder { CompanyId = companyId, OwnerUserId = 7,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10], CustomerId = Customer, CustomerCode = "C21",
            CustomerName = "Customer", BusinessDate = Day, CurrencyCode = "USD", BaseCurrencyCode = "TWD",
            ExchangeRate = 31m, OriginalNet = 115m, OriginalTax = 5.75m, OriginalGross = 120.75m, BaseNet = 3565m,
            BaseTax = 178m, BaseGross = 3743m };
        order.Lines.Add(OrderLine(companyId, 1, 31, 3m, 100m, 5m, 3100m, 155m));
        order.Lines.Add(OrderLine(companyId, 2, 32, 1.5m, 15m, 0.75m, 465m, 23m));
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2026-11-06' WHERE id = {0}", [order.Id], Ct);
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

    private sealed class CountingNumbers : INumberGenerator
    {
        private int next;

        public Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("SI" + Guid.NewGuid().ToString("N")[..8] + Interlocked.Increment(ref next));
    }

    private ServiceProvider Provider(long companyId)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new MasterDataModule().AddServices(services, configuration);
        new FinanceModule().AddServices(services, configuration);
        new SalesModule().AddServices(services, configuration);
        var company = new FixedCompany(companyId);
        services.AddSingleton<ICurrentCompany>(company);
        services.AddSingleton<ICurrentUser>(new FixedUser(7));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<INumberGenerator>(new CountingNumbers());
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, true, new Dictionary<string, DataScope>()));
        services.AddSingleton(auth.Object);
        var customers = new Mock<ICustomerLookup>();
        customers.Setup(x => x.RequireApprovedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) => new CustomerIdentity(id, "C" + id, "Customer"));
        services.AddSingleton(customers.Object);
        services.AddScoped(sp => new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(sp.GetRequiredService<IClock>(),
                    sp.GetRequiredService<ICurrentUser>(), company))).Options,
            sp.GetServices<IModuleDbModel>(), company));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private MyErpDbContext Raw(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new SalesModule().AddServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Now), new FixedUser(7),
                    company))).Options,
            provider.GetServices<IModuleDbModel>(), company);
    }

    private MyErpDbContext Organization(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Now), new FixedUser(7),
                new FixedCompany(companyId)))).Options,
        [new SystemOrganizationDbModel()], new FixedCompany(companyId));
}
