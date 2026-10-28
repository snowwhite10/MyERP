using System.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesQuotationActionScopeTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ViewUsesItsOwnScopeAndNeverRevealsOtherCompanyOrOwner()
    {
        var number = "QV" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        await using var db = Context(1);
        SalesQuotationQueryService Query(long companyId, long userId, DataScope? scope) =>
            new(db, Access(companyId, userId, "SAL.QUOTATION.VIEW", scope),
                new SalesCompanyAccess(new FixedCompany(companyId)));

        var visible = await Query(1, 7, DataScope.Self).GetAsync(id, Ct);
        Assert.Equal(number, visible.DocumentNo);
        Assert.Single(visible.Lines);
        await Assert.ThrowsAsync<NotFoundException>(() => Query(1, 8, DataScope.Self).GetAsync(id, Ct));
        await using var otherDb = Context(2);
        var otherCompany = new SalesCompanyAccess(new FixedCompany(2));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesQuotationQueryService(otherDb,
                Access(2, 7, "SAL.QUOTATION.VIEW", DataScope.Company), otherCompany).GetAsync(id, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Query(1, 7, null).GetAsync(id, Ct));
    }

    [Fact]
    public async Task EditUsesItsOwnScopeAndRebuildsSnapshotWithoutChangingNumberOrOwner()
    {
        var number = "QE" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        var input = new SalesQuotationInput(22, new DateOnly(2028, 2, 29), new DateOnly(2028, 4, 1),
            " revised ", [new SalesQuotationLineInput(1, 32, 2m, 15m, 0m, "TWD", 42)]);
        await using var db = Context(1);
        SalesQuotationDraftCommandService Command(long userId, DataScope? scope) =>
            new(db, Access(1, userId, "SAL.QUOTATION.EDIT", scope),
                new SalesCompanyAccess(new FixedCompany(1)), Snapshots(),
                new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));

        await Assert.ThrowsAsync<ForbiddenException>(() => Command(7, null).ReplaceAsync(id, 1, input, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Command(8, DataScope.Self).ReplaceAsync(id, 1, input, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => Command(7, DataScope.Self).ReplaceAsync(id, 2, input, Ct));
        var edited = await Command(7, DataScope.Self).ReplaceAsync(id, 1, input, Ct);
        Assert.Equal(2, edited.Version);
        Assert.Equal(number, edited.DocumentNo);
        Assert.Equal(7, edited.OwnerUserId);
        Assert.Equal("Revised customer", edited.CustomerName);
        Assert.Equal("Revised item", Assert.Single(edited.Lines).ItemName);
        Assert.Equal(32m, edited.OriginalGross);
        await using var verify = Context(1);
        var saved = await verify.Set<SalesQuotation>().Include(x => x.Lines).SingleAsync(x => x.Id == id, Ct);
        Assert.Equal("revised", saved.Remark);
        Assert.Equal(32m, saved.OriginalGross);
        Assert.Equal(32, Assert.Single(saved.Lines).ItemId);
        await Assert.ThrowsAsync<ConflictException>(() => Command(7, DataScope.Self).ReplaceAsync(id, 1, input, Ct));

        saved.Status = SalesQuotationStatus.Rejected;
        await verify.SaveChangesAsync(Ct);
        Assert.Equal(3, saved.Version);
        var revised = await Command(7, DataScope.Self).ReplaceAsync(id, 3, input, Ct);
        Assert.Equal(SalesQuotationStatus.Draft, revised.Status);
        Assert.Equal(4, revised.Version);
    }

    [Fact]
    public async Task DeleteRequiresItsOwnScopeAndCurrentVersion()
    {
        var number = "QD" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        await using var db = Context(1);
        SalesQuotationDraftCommandService Command(long userId, DataScope? scope) =>
            new(db, Access(1, userId, "SAL.QUOTATION.DELETE", scope),
                new SalesCompanyAccess(new FixedCompany(1)), Snapshots(),
                new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));

        await Assert.ThrowsAsync<ForbiddenException>(() => Command(7, null).DeleteAsync(id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Command(8, DataScope.Self).DeleteAsync(id, 1, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => Command(7, DataScope.Self).DeleteAsync(id, 2, Ct));
        await Command(7, DataScope.Self).DeleteAsync(id, 1, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => Command(7, DataScope.Self).DeleteAsync(id, 1, Ct));
        await using var verify = Context(1);
        Assert.False(await verify.Set<SalesQuotation>().AnyAsync(x => x.Id == id, Ct));
        Assert.Equal(number, (await verify.Set<SalesQuotation>().IgnoreQueryFilters()
            .SingleAsync(x => x.Id == id, Ct)).DocumentNo);
    }

    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 29)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesQuotationDbModel()], new FixedCompany(companyId));

    private static SalesActionAccess Access(long companyId, long userId, string permission, DataScope? scope)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, false,
                scope is null ? new Dictionary<string, DataScope>() :
                    new Dictionary<string, DataScope> { [permission] = scope.Value }));
        return new SalesActionAccess(new SalesCompanyAccess(new FixedCompany(companyId)), auth.Object);
    }

    private static SalesQuotation Draft(string number)
    {
        var quote = new SalesQuotation
        {
            CompanyId = 1, OwnerUserId = 7, DocumentNo = number, CustomerId = 21,
            CustomerCode = "C21", CustomerName = "Customer", BusinessDate = new DateOnly(2028, 2, 29),
            ExpiryDate = new DateOnly(2028, 3, 30), CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
            ExchangeRate = 1, OriginalNet = 10, OriginalGross = 10, BaseNet = 10, BaseGross = 10
        };
        quote.Lines.Add(new SalesQuotationLine
        {
            CompanyId = 1, LineNo = 1, ItemId = 31, ItemCode = "I31", ItemName = "Item",
            TaxRateId = 41, Quantity = 1, UnitPrice = 10,
            OriginalNet = 10, OriginalGross = 10, BaseNet = 10, BaseGross = 10
        });
        return quote;
    }

    private static SalesQuotationSnapshotBuilder Snapshots()
    {
        var customer = new Mock<ICustomerLookup>();
        customer.Setup(x => x.RequireApprovedAsync(22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerIdentity(22, "C22", "Revised customer"));
        var item = new Mock<ISalesItemLookup>();
        item.Setup(x => x.RequireActiveAsync(32, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesItemIdentity(32, "I32", "Revised item"));
        var currency = new Mock<ICurrencyLookup>();
        currency.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetRateSnapshotAsync("TWD", new DateOnly(2028, 2, 29),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(1m, new DateOnly(2028, 2, 29), null, null));
        currency.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrencyPrecision.Default);
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(42, TaxDirection.Output,
                new DateOnly(2028, 2, 29), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(42, 5m, new DateOnly(2028, 2, 29), null));
        return new SalesQuotationSnapshotBuilder(customer.Object, item.Object, currency.Object, tax.Object);
    }
}
