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
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesOrderCreateTransactionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    [Fact]
    public async Task ManualCommandPersistsSnapshotAndFailedSaveReturnsSysNumber()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SO" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales order test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SO", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        var command = Service(db, companyId);
        var input = new SalesOrderDraftInput(21, Day, "TWD", null,
            [new SalesOrderLineInput(1, 31, 2m, 10m, 0m, 41)]);

        var saved = await command.CreateAsync(input, Ct);
        Assert.Equal("SO001", saved.DocumentNo);
        Assert.Equal(21m, saved.OriginalGross);
        Assert.Equal(21m, Assert.Single(saved.Lines).OriginalGross);

        // The sequence advances, but a simulated downstream duplicate document insert fails.
        var failed = Service(db, companyId, new DuplicateAfterAllocation(
            new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7))));
        await Assert.ThrowsAsync<DbUpdateException>(() => failed.CreateAsync(input, Ct));
        await Assert.ThrowsAsync<MyERP.Shared.Errors.ForbiddenException>(() =>
            Service(db, companyId, allowCreate: false).CreateAsync(input, Ct));
        await using var verify = Context(companyId);
        Assert.Equal(1, await verify.Set<SalesOrder>().CountAsync(Ct));
        Assert.Equal(1, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesOrderDbModel(), new SystemOrganizationDbModel()], new FixedCompany(companyId));

    private static SalesOrderCreateService Service(MyErpDbContext db, long companyId,
        INumberGenerator? numbers = null, bool allowCreate = true)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, allowCreate,
                new Dictionary<string, DataScope>()));
        var customer = new Mock<ICustomerLookup>();
        customer.Setup(x => x.RequireApprovedAsync(21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerIdentity(21, "C21", "Customer"));
        var item = new Mock<ISalesItemLookup>();
        item.Setup(x => x.RequireActiveAsync(31, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesItemIdentity(31, "I31", "Item"));
        var currency = new Mock<ICurrencyLookup>();
        currency.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetRateSnapshotAsync("TWD", Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(1m, Day, null, null));
        currency.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrencyPrecision.Default);
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(41, TaxDirection.Output, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(41, 5m, Day, null));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesOrderCreateService(db, new SalesActionAccess(company, auth.Object), company,
            numbers ?? new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7)),
            new SalesOrderSnapshotBuilder(customer.Object, item.Object, currency.Object, tax.Object),
            new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27))));
    }

    private sealed class DuplicateAfterAllocation(INumberGenerator inner) : INumberGenerator
    {
        public async Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default)
        {
            await inner.NextAsync(documentType, documentDate, cancellationToken);
            return "SO001";
        }
    }
}
