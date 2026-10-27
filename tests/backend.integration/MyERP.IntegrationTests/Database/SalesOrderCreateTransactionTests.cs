using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
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

    [Fact]
    public async Task ManualCommandHoldsRealMdIdentityStableUntilCommit()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SO" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales MD stability test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SO", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        var uom = await new UomService(db).CreateAsync("U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            "Original item", null, uom.Id, null, null, Ct);
        var realItem = new SalesItemLookup(db);
        var observedIsolation = IsolationLevel.Unspecified;
        var itemLookup = new Mock<ISalesItemLookup>();
        itemLookup.Setup(x => x.RequireActiveAsync(item.Id, It.IsAny<CancellationToken>()))
            .Returns(async (long id, CancellationToken token) =>
            {
                observedIsolation = db.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel;
                var identity = await realItem.RequireActiveAsync(id, token);
                await using var other = new SqlConnection(database.ConnectionString);
                await other.OpenAsync(token);
                await using var update = other.CreateCommand();
                update.CommandText = "SET LOCK_TIMEOUT 1000; UPDATE dbo.md_item SET name = N'Changed item' WHERE id = @id";
                update.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
                var blocked = await Assert.ThrowsAsync<SqlException>(() => update.ExecuteNonQueryAsync(token));
                Assert.Equal(1222, blocked.Number);
                return identity;
            });
        var saved = await Service(db, companyId, items: itemLookup.Object).CreateAsync(
            new SalesOrderDraftInput(21, Day, "TWD", null,
                [new SalesOrderLineInput(1, item.Id, 2m, 10m, 0m, 41)]), Ct);
        Assert.Equal(IsolationLevel.Serializable, observedIsolation);
        Assert.Equal("Original item", Assert.Single(saved.Lines).ItemName);
        await using var after = new SqlConnection(database.ConnectionString);
        await after.OpenAsync(Ct);
        await using var change = after.CreateCommand();
        change.CommandText = "UPDATE dbo.md_item SET name = N'Changed item' WHERE id = @id";
        change.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = item.Id });
        Assert.Equal(1, await change.ExecuteNonQueryAsync(Ct));
        await using var verify = Context(companyId);
        Assert.Equal("Original item", await verify.Set<SalesOrderLine>().Where(x => x.OrderId == saved.Id)
            .Select(x => x.ItemName).SingleAsync(Ct));
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesOrderDbModel(), new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel()], new FixedCompany(companyId));

    private static SalesOrderCreateService Service(MyErpDbContext db, long companyId,
        INumberGenerator? numbers = null, bool allowCreate = true, ISalesItemLookup? items = null)
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
            new SalesOrderSnapshotBuilder(customer.Object, items ?? item.Object, currency.Object, tax.Object),
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
