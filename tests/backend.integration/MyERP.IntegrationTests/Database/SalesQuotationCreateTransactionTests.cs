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
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesQuotationCreateTransactionTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2028, 2, 29);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateUsesSysNumberAndMdSnapshotsInOneTransaction()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "QT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Quotation company" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        var prefix = "QT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.QUOTATION", Name = "Quote",
            Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);

        var service = Service(db, companyId, true);
        var input = new SalesQuotationInput(21, Day, Day.AddDays(30), " first ",
            [new SalesQuotationLineInput(1, 31, 2m, 10m, 0m, "TWD", 41)]);
        var invalidCurrency = input with
        {
            Lines = [new SalesQuotationLineInput(1, 31, 2m, 10m, 0m, "USD", 41)]
        };
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            service.CreateAsync(invalidCurrency, Ct));
        Assert.False(await db.Set<SalesQuotation>().AnyAsync(Ct));
        var quote = await service.CreateAsync(input, Ct);
        Assert.Equal(prefix + "001", quote.DocumentNo);
        Assert.Equal(21m, quote.OriginalGross);
        Assert.Equal(1, quote.Version);
        Assert.Equal("first", quote.Remark);
        Assert.Equal(7, quote.OwnerUserId);
        db.ChangeTracker.Clear();
        var saved = await db.Set<SalesQuotation>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == quote.Id, Ct);
        Assert.Equal("Customer", saved.CustomerName);
        Assert.Equal("Item", Assert.Single(saved.Lines).ItemName);
        Assert.Equal(5m, saved.Lines[0].TaxRatePercent);
        Assert.Equal(21m, saved.Lines[0].OriginalGross);

        await Assert.ThrowsAsync<MyERP.Shared.Errors.ForbiddenException>(() =>
            Service(db, companyId, false).CreateAsync(input, Ct));
        Assert.Equal(1, await db.Set<SalesQuotation>().CountAsync(Ct));
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 29)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesQuotationDbModel(), new SystemOrganizationDbModel()],
        new FixedCompany(companyId));

    private static SalesQuotationCreateService Service(MyErpDbContext db, long companyId, bool allow)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, allow,
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
        return new SalesQuotationCreateService(db, new SalesActionAccess(company, auth.Object), company,
            new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 29)), new FixedUser(7)),
            new SalesQuotationSnapshotBuilder(customer.Object, item.Object, currency.Object, tax.Object),
            new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));
    }
}
