using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-INVOICE-POST-001～006 (WU-SD-007a3a, ADR-178): the ISSUE command on real SQL with the real FI/AR/GL posting
/// service, the real SD invoice source and the real FI credit lock. Only the FI account mapping (fixed 1131/4101/2171),
/// numbering and the caller's authorisation are test doubles.
/// </summary>
public sealed class SalesInvoiceIssueServiceTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2026, 11, 6);
    private static readonly DateTime Now = new(2026, 11, 6, 9, 30, 0);
    private const long Customer = 21;

    [Fact]
    public async Task IssuePostsInvoiceArAndJournalTogetherAndMovesTheCommitmentToOpenAr()
    {
        // SD-INVOICE-POST-001: SD-INVOICE-010/012, FI-SALES-001/003 — Posted invoice, FI IDs, balanced journal,
        // open AR = invoice base gross, total exposure unchanged, the commitment of a fully invoiced order is zero.
        var s = await SeedAsync(limit: 100_000m);
        var d = await SeedDeliveryAsync(s, (s.LineA, 1m), (s.LineB, 1.5m));
        var before = await AssessAsync(s);
        Assert.Equal((0m, 3743m), (before.PostedOpenAr, before.OtherCommitments));

        var first = await IssueAsync(s, Input("P-1", (1, d.Lines[0].Id, 1m)));

        Assert.False(first.Replayed);
        await using (var verify = s.Raw())
        {
            var invoice = await verify.Set<SalesInvoice>().AsNoTracking().SingleAsync(x => x.Id == first.Invoice.Id, Ct);
            Assert.Equal((SalesInvoiceStatus.Posted, (DateTime?)Now, 1033m + 52m),
                (invoice.Status, invoice.PostedAt, invoice.BaseGross));
            var fi = await verify.Database.SqlQuery<FiRow>($"""
                SELECT s.id AS source_id, r.id AS receivable_id, s.gl_journal_id AS journal_id,
                       s.gross_amount, s.base_gross_amount, r.open_base_amount, s.currency
                FROM dbo.ar_sales_source s
                JOIN dbo.ar_posted_receivable r ON r.company_id = s.company_id AND r.source_id = s.source_id
                     AND r.source_type = 'Invoice'
                WHERE s.company_id = {s.CompanyId} AND s.source_type = 'Invoice' AND s.source_id = {invoice.Id}
                """).SingleAsync(Ct);
            Assert.Equal((invoice.FiSalesSourceId, invoice.FiReceivableId, invoice.FiJournalId),
                ((long?)fi.SourceId, (long?)fi.ReceivableId, (long?)fi.JournalId));
            Assert.Equal((invoice.OriginalGross, 1085m, 1085m, "USD"),
                (fi.GrossAmount, fi.BaseGrossAmount, fi.OpenBaseAmount, fi.Currency));
            var lines = await verify.Database.SqlQuery<JournalRow>($"""
                SELECT account_code, debit, credit FROM dbo.gl_journal_line
                WHERE company_id = {s.CompanyId} AND journal_id = {fi.JournalId} ORDER BY account_code
                """).ToListAsync(Ct);
            Assert.Equal([new("1131", 1085m, 0m), new("2171", 0m, 52m), new("4101", 0m, 1033m)], lines);
        }
        var after = await AssessAsync(s);
        Assert.Equal((1085m, 3743m - 1085m), (after.PostedOpenAr, after.OtherCommitments));
        Assert.Equal(before.PostedOpenAr + before.OtherCommitments, after.PostedOpenAr + after.OtherCommitments);

        // Invoicing the rest of the order releases its whole commitment into open AR.
        var d2 = await SeedDeliveryAsync(s, (s.LineA, 2m));
        await IssueAsync(s, Input("P-2", (1, d.Lines[1].Id, 1.5m), (2, d2.Lines[0].Id, 2m)));
        var done = await AssessAsync(s);
        Assert.Equal((3743m, 0m), (done.PostedOpenAr, done.OtherCommitments));
    }

    [Fact]
    public async Task IdenticalRequestReplaysThePostedInvoiceWithoutAnotherFiPostingAndChangedPayloadIs409()
    {
        // SD-INVOICE-POST-002: SD-INVOICE-008/020, BR-SD-040 — replay writes nothing anywhere.
        var s = await SeedAsync();
        var d = await SeedDeliveryAsync(s, (s.LineA, 3m));
        var saved = await IssueAsync(s, Input("R-1", (1, d.Lines[0].Id, 1m)));
        var counts = await FiCountsAsync(s);

        var replay = await IssueAsync(s, Input("R-1", (1, d.Lines[0].Id, 1m)));
        Assert.True(replay.Replayed);
        Assert.Equal((saved.Invoice.Id, SalesInvoiceStatus.Posted, saved.Invoice.FiJournalId),
            (replay.Invoice.Id, replay.Invoice.Status, replay.Invoice.FiJournalId));
        Assert.Equal(counts, await FiCountsAsync(s));

        Assert.Equal("SAL.INVOICE.REQUEST_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            IssueAsync(s, Input("R-1", (1, d.Lines[0].Id, 2m))))).ErrorCode);
        Assert.Equal(counts, await FiCountsAsync(s));
        await using var verify = s.Raw();
        Assert.Equal(1, await verify.Set<SalesInvoice>().CountAsync(Ct));
    }

    [Fact]
    public async Task FiRejectionRollsBackTheIssuingInvoiceAndKeepsFiCode()
    {
        // SD-INVOICE-POST-003: SD-INVOICE-011 — FI mapping missing is 422 with FI's code; nothing survives.
        var s = await SeedAsync(withMap: false);
        var d = await SeedDeliveryAsync(s, (s.LineA, 1m));

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            IssueAsync(s, Input("J-1", (1, d.Lines[0].Id, 1m))));

        Assert.Equal(SalesPostingErrors.MappingMissing, refused.ErrorCode);
        await AssertNothingSavedAsync(s);
    }

    [Fact]
    public async Task ReceiptMismatchAndFiFaultRollBackEverythingIncludingFiRows()
    {
        // SD-INVOICE-POST-004: BR-SD-041 / BR-FI-010 — a receipt that does not prove the saved gross is never
        // committed, even though FI already wrote its rows in the transaction; a Fault (FI rolled back) is 500.
        var s = await SeedAsync();
        var d = await SeedDeliveryAsync(s, (s.LineA, 1m));

        s.Posting.Tamper = result => result with { Receipt = result.Receipt! with { GrossAmount = 1m } };
        var mismatch = await Assert.ThrowsAsync<SalesInvoicePostingException>(() =>
            IssueAsync(s, Input("M-1", (1, d.Lines[0].Id, 1m))));
        Assert.Equal(SalesInvoiceIssueService.ReceiptMismatch, mismatch.ErrorCode);
        await AssertNothingSavedAsync(s);

        s.Posting.Fault = true;
        s.Posting.Tamper = null;
        var fault = await Assert.ThrowsAsync<SalesInvoicePostingException>(() =>
            IssueAsync(s, Input("M-2", (1, d.Lines[0].Id, 1m))));
        Assert.Equal(SalesPostingErrors.WriteFailed, fault.ErrorCode);
        await AssertNothingSavedAsync(s);

        // The same request then succeeds: nothing of the failed attempts blocks the key or the balance.
        s.Posting.Fault = false;
        Assert.Equal(SalesInvoiceStatus.Posted, (await IssueAsync(s, Input("M-2", (1, d.Lines[0].Id, 1m)))).Invoice.Status);
    }

    [Fact]
    public async Task HeldCreditLockBlocksIssueBeforeAnySdLockAndLeavesNoRows()
    {
        // SD-INVOICE-POST-005: BR-SD-041 — ISSUE takes the FI customer credit lock first; a held lock is 409.
        var s = await SeedAsync(creditLockTimeout: 500);
        var d = await SeedDeliveryAsync(s, (s.LineA, 1m));

        await using (var holder = s.Raw())
        await using (var held = await holder.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct))
        {
            await new CustomerCreditService(new FixedClock(Now), new FixedUser(7))
                .AcquireCreditLockAsync(s.CompanyId, Customer, held.GetDbTransaction(), Ct);

            var blocked = await Assert.ThrowsAsync<ConflictException>(() =>
                IssueAsync(s, Input("L-1", (1, d.Lines[0].Id, 1m))));
            Assert.Equal(FinanceModuleContracts.CreditLockTimeout, blocked.ErrorCode);

            // The order row was never locked: another connection can still take it immediately.
            await using var probe = s.Raw();
            await probe.Database.OpenConnectionAsync(Ct);
            await probe.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 1000", Ct);
            await using var probeTransaction = await probe.Database.BeginTransactionAsync(Ct);
            Assert.Equal([s.Order.Id], await probe.Database.SqlQuery<long>(
                $"SELECT id AS [Value] FROM dbo.sal_order WITH (UPDLOCK) WHERE id = {s.Order.Id}").ToListAsync(Ct));
        }
        await AssertNothingSavedAsync(s);
        Assert.Equal(SalesInvoiceStatus.Posted, (await IssueAsync(s, Input("L-1", (1, d.Lines[0].Id, 1m)))).Invoice.Status);
    }

    [Fact]
    public async Task IssueRefusesAnOuterTransactionAndSdRuleFailuresReachNoFiCall()
    {
        // SD-INVOICE-POST-006: the command owns its transaction; an SD 422 never calls FI.
        var s = await SeedAsync();
        var d = await SeedDeliveryAsync(s, (s.LineA, 1m));
        await using (var scope = s.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            await using var outer = await db.Database.BeginTransactionAsync(Ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<SalesInvoiceIssueService>().IssueAsync(Input("O-1", (1, d.Lines[0].Id, 1m)), Ct));
        }

        var over = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            IssueAsync(s, Input("O-2", (1, d.Lines[0].Id, 2m))));
        Assert.Equal("SAL.INVOICE.BALANCE_EXCEEDED", over.ErrorCode);
        Assert.Equal(0, s.Posting.Calls);
        await AssertNothingSavedAsync(s);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record FiRow(long SourceId, long ReceivableId, long JournalId, decimal GrossAmount,
        decimal BaseGrossAmount, decimal OpenBaseAmount, string Currency);

    private sealed record JournalRow(string AccountCode, decimal Debit, decimal Credit);

    private sealed record Seeded(long CompanyId, SalesOrder Order, ServiceProvider Provider, PostingProbe Posting,
        Func<MyErpDbContext> Raw)
    {
        public long LineA => Order.Lines[0].Id;
        public long LineB => Order.Lines[1].Id;
    }

    /// <summary>Wraps the real FI posting service: counts calls, can tamper with the result or simulate a fault.</summary>
    private sealed class PostingProbe
    {
        public int Calls;
        public bool Fault;
        public Func<SalesInvoicePostingResult, SalesInvoicePostingResult>? Tamper;
    }

    private sealed class ProbedPosting(SalesInvoicePostingService real, PostingProbe probe, MyErpDbContext db)
        : ISalesInvoicePostingService
    {
        public async Task<SalesInvoicePostingResult> PostInCurrentTransactionAsync(SalesInvoicePostingCommand? command,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref probe.Calls);
            var result = await real.PostInCurrentTransactionAsync(command, cancellationToken);
            if (probe.Fault)
            {
                // FI's Fault contract: the caller's whole transaction has already been rolled back.
                await db.Database.RollbackTransactionAsync(CancellationToken.None);
                return new(SalesPostingOutcomeKind.Fault, false, 500, SalesPostingErrors.WriteFailed);
            }
            return probe.Tamper is { } tamper ? tamper(result) : result;
        }
    }

    private sealed class FixedMap : ISalesInvoiceAccountMapSource, IGlPostingMappingVersionSource
    {
        public string SourceModule => "SD";
        public string SourceType => "Invoice";

        public Task<SalesInvoiceAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken) =>
            Task.FromResult<SalesInvoiceAccountMap?>(new(3, "1131", "4101", "2171"));

        public Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
            Task.FromResult(3);
    }

    private sealed class CountingNumbers : INumberGenerator
    {
        private int next;

        public Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("SI" + Guid.NewGuid().ToString("N")[..8] + Interlocked.Increment(ref next));
    }

    private static SalesInvoiceIssueInput Input(string key, params (int LineNo, long DeliveryLineId, decimal Quantity)[] lines) =>
        new(key, Day, [.. lines.Select(x => new SalesInvoiceLineInput(x.LineNo, x.DeliveryLineId, x.Quantity))]);

    private static async Task<SalesInvoiceIssueOutcome> IssueAsync(Seeded s, SalesInvoiceIssueInput input)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SalesInvoiceIssueService>().IssueAsync(input, Ct);
    }

    /// <summary>Credit position of the customer exactly as order submit sees it (FI lock, rolled back).</summary>
    private static async Task<SalesCreditAssessment> AssessAsync(Seeded s)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);
        var credit = sp.GetRequiredService<SalesOrderCreditCheck>();
        await credit.LockAsync(s.CompanyId, Customer, transaction.GetDbTransaction(), Ct);
        var assessment = await credit.AssessAsync(db, s.CompanyId, Customer, 0, 0m, "TWD",
            transaction.GetDbTransaction(), Ct);
        await transaction.RollbackAsync(Ct);
        return assessment;
    }

    private static async Task<(int Sources, int Receivables, int Journals)> FiCountsAsync(Seeded s)
    {
        await using var db = s.Raw();
        var sources = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_sales_source WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        var receivables = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_posted_receivable WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        var journals = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        return (sources, receivables, journals);
    }

    private static async Task AssertNothingSavedAsync(Seeded s)
    {
        Assert.Equal((0, 0, 0), await FiCountsAsync(s));
        await using var db = s.Raw();
        Assert.False(await db.Set<SalesInvoice>().AnyAsync(Ct));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.sal_invoice_line WHERE company_id = {0}", s.CompanyId).SingleAsync(Ct));
    }

    private async Task<Seeded> SeedAsync(decimal? limit = null, bool withMap = true, int? creditLockTimeout = null)
    {
        long companyId;
        await using (var setup = Organization(null))
        {
            var company = new SystemCompany { Code = "IP" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales invoice posting test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }

        var probe = new PostingProbe();
        var provider = Provider(companyId, probe, withMap, creditLockTimeout);
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
        }
        if (limit is { } amount)
        {
            await using var raw = Raw(null);
            await raw.Database.ExecuteSqlRawAsync(
                "INSERT dbo.ar_credit_key (company_id, customer_id, created_at, created_by, version) " +
                "VALUES ({0}, {1}, '2026-11-01', 1, 1); " +
                "INSERT dbo.ar_credit_limit (company_id, customer_id, base_currency, effective_from, effective_to, " +
                "amount, status, created_at, created_by, version) VALUES ({0}, {1}, 'TWD', '2026-01-01', " +
                "'2026-12-31', {2}, 'Active', '2026-11-01', 1, 3)", [companyId, Customer, amount], Ct);
        }
        return new Seeded(companyId, await SeedOrderAsync(companyId), provider, probe, () => Raw(companyId));
    }

    /// <summary>
    /// Approved USD order (rate 31, TWD base) for customer 21: line A 3 units net 100.00 + tax 5.00 (base 3,100 + 155);
    /// line B 1.5 units net 15.00 + tax 0.75 (base 465 + 23). Base gross 3,743.
    /// </summary>
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

    private static async Task<SalesDelivery> SeedDeliveryAsync(Seeded s, params (long OrderLineId, decimal Quantity)[] lines)
    {
        await using var db = s.Raw();
        var delivery = new SalesDelivery { CompanyId = s.CompanyId, DocumentNo = "DN" + Guid.NewGuid().ToString("N")[..10],
            OrderId = s.Order.Id, BusinessDate = Day, Status = SalesDeliveryStatus.Posted, PostedAt = Now };
        var lineNo = 0;
        foreach (var (orderLineId, quantity) in lines)
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = s.CompanyId, LineNo = ++lineNo,
                OrderLineId = orderLineId, ItemId = s.Order.Lines.Single(x => x.Id == orderLineId).ItemId,
                WarehouseId = 51, Quantity = quantity });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
        return delivery;
    }

    private ServiceProvider Provider(long companyId, PostingProbe probe, bool withMap, int? creditLockTimeout)
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

        foreach (var real in services.Where(x => x.ImplementationType == typeof(SalesInvoiceAccountMapStore)).ToList())
            services.Remove(real);
        if (withMap)
        {
            services.AddScoped<ISalesInvoiceAccountMapSource, FixedMap>();
            services.AddScoped<IGlPostingMappingVersionSource, FixedMap>();
        }
        if (creditLockTimeout is { } timeout)
            services.AddScoped<ICustomerCreditService>(sp => new CustomerCreditService(
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<ICurrentUser>())
                { LockTimeoutMilliseconds = timeout });
        services.AddSingleton(probe);
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<SalesInvoicePostingService>(sp));
        services.AddScoped<ISalesInvoicePostingService, ProbedPosting>();
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
