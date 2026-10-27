using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesDeliverySelectionStoreTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                    new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), company))).Options,
            [new SalesDeliveryDbModel()], company);
    }

    [Fact]
    public async Task SavedSelectionRequiresSerializableTransactionAndReadsAllCurrentLines()
    {
        var companyId = TestCompanies.Next();
        long deliveryId;
        await using (var setup = Context(companyId))
        {
            var delivery = new SalesDelivery { CompanyId = companyId, DocumentNo = $"D{companyId}",
                OrderId = 401, BusinessDate = new DateOnly(2026, 10, 27) };
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 1,
                OrderLineId = 12, ItemId = 20, WarehouseId = 30, Quantity = 1.25m });
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 2,
                OrderLineId = 11, ItemId = 21, WarehouseId = 30, Quantity = 2m });
            setup.Add(delivery);
            await setup.SaveChangesAsync(Ct);
            deliveryId = delivery.Id;
        }

        await using var db = Context(companyId);
        var store = new SalesDeliverySelectionStore(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SelectAsync(deliveryId, SalesDeliveryStatus.Draft, Ct));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
        var selected = await store.SelectAsync(deliveryId, SalesDeliveryStatus.Draft, Ct);
        Assert.Equal(2, selected.Lines.Count);
        Assert.Equal([11L, 12L], selected.OrderLineLockPlan);
        Assert.All(selected.Lines, line => Assert.True(line.LineId > 0));
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task SavedSelectionRefusesMissingAndOtherCompanyDocuments()
    {
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        long otherDeliveryId;
        await using (var other = Context(otherCompanyId))
        {
            var delivery = new SalesDelivery { CompanyId = otherCompanyId,
                DocumentNo = $"D{otherCompanyId}", OrderId = 402,
                BusinessDate = new DateOnly(2026, 10, 27) };
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = otherCompanyId,
                LineNo = 1, OrderLineId = 12, ItemId = 20, WarehouseId = 30, Quantity = 1m });
            other.Add(delivery);
            await other.SaveChangesAsync(Ct);
            otherDeliveryId = delivery.Id;
        }
        await using var db = Context(companyId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
        var store = new SalesDeliverySelectionStore(db);
        await Assert.ThrowsAnyAsync<Exception>(() => store.SelectAsync(long.MaxValue,
            SalesDeliveryStatus.Draft, Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => store.SelectAsync(otherDeliveryId,
            SalesDeliveryStatus.Draft, Ct));
        await transaction.RollbackAsync(Ct);
    }
}
