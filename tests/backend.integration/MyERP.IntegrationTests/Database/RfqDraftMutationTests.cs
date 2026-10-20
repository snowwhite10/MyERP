using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

public sealed class RfqDraftMutationTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    [Fact]
    public async Task CreatesReplacesAndSoftDeletesWholeDraft()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId);
        var created = await service.CreateAsync(Input(), Ct);
        Assert.Equal("RFQ-001", created.DocumentNo);
        Assert.Equal(7, created.OwnerId);
        Assert.Equal(10, created.BranchId);
        Assert.Equal(1.123456m, Assert.Single(created.Lines).Quantity);
        Assert.Equal(21, Assert.Single(created.Invitations).SupplierId);
        var updated = await service.UpdateAsync(created.Id, created.Version,
            Input() with { Lines = [new(2, 12, null, Day.AddDays(3), 2m, null)],
                SupplierIds = [22] }, Ct);
        Assert.Equal(2, Assert.Single(updated.Lines).LineNo);
        Assert.Equal(22, Assert.Single(updated.Invitations).SupplierId);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(created.Id, created.Version, Input(), Ct));
        await service.DeleteAsync(created.Id, updated.Version, Ct);
        await using var check = Context(companyId);
        var saved = await check.Set<Rfq>().IgnoreQueryFilters().Include(x => x.Lines)
            .Include(x => x.Invitations).SingleAsync(x => x.Id == created.Id, Ct);
        Assert.True(saved.IsDeleted);
        Assert.Single(saved.Lines);
        Assert.Single(saved.Invitations);
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}",
            companyId).SingleAsync(Ct));
    }

    [Fact]
    public async Task InvalidLineSupplierAndSourceRollBackEntireDraft()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            Input() with { SupplierIds = [21, 21] }, Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            Input() with { Lines = [new(1, 11, null, Day, 1.0000001m, null)] }, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(
            Input() with { Lines = [new(1, 11, long.MaxValue, Day, 1m, null)] }, Ct));
        Assert.Empty(await db.Set<Rfq>().ToListAsync(Ct));
    }

    [Fact]
    public async Task SourceMustBeApprovedSameItemAndWithinUnorderedQuantity()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var requisition = new Requisition { CompanyId = companyId, DocumentNo = "PUR-RFQ-SOURCE",
            BusinessDate = Day, RequesterId = 7, Status = RequisitionStatus.Approved };
        requisition.Lines.Add(new RequisitionLine { CompanyId = companyId, LineNo = 1,
            ItemId = 11, RequiredDate = Day.AddDays(1), Quantity = 5m, OrderedQuantity = 2m });
        db.Add(requisition);
        await db.SaveChangesAsync(Ct);
        var sourceId = Assert.Single(requisition.Lines).Id;
        var service = Service(db, companyId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(Input() with
        { Lines = [new(1, 12, sourceId, Day.AddDays(1), 1m, null)] }, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(Input() with
        { Lines = [new(1, 11, sourceId, Day.AddDays(1), 4m, null)] }, Ct));
        db.Attach(requisition);
        requisition.Status = RequisitionStatus.Rejected;
        await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(Input() with
        { Lines = [new(1, 11, sourceId, Day.AddDays(1), 1m, null)] }, Ct));
        db.Attach(requisition);
        requisition.Status = RequisitionStatus.Approved;
        await db.SaveChangesAsync(Ct);
        var created = await service.CreateAsync(Input() with
        { Lines = [new(1, 11, sourceId, Day.AddDays(1), 3m, null)] }, Ct);
        Assert.Equal(sourceId, Assert.Single(created.Lines).RequisitionLineId);
        Assert.Equal(2m, Assert.Single(requisition.Lines).OrderedQuantity);
        Assert.Single(await db.Set<Rfq>().ToListAsync(Ct));
    }

    private static RfqInput Input() => new(Day, Day.AddDays(2), " test ",
        [new(1, 11, null, Day.AddDays(1), 1.123456m, null)], [21]);

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RfqDbModel(), new RequisitionDbModel()], company);
    }

    private static RfqDraftService Service(MyErpDbContext db, long companyId) => new(db,
        new RequisitionCompanyAccess(new FixedCompany(companyId)),
        new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
            new Dictionary<string, DataScope>
            {
                [PurchasingPermissions.RfqCreate] = DataScope.Self,
                [PurchasingPermissions.RfqEdit] = DataScope.Self,
                [PurchasingPermissions.RfqDelete] = DataScope.Self
            })), new FakeNumbers(), new FakeItems(), new FakeSuppliers(),
        new FixedClock(new DateTime(2026, 10, 21)));

    private sealed class FixedAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) => Task.FromResult<UserCompanyAuthorization?>(value);
    }
    private sealed class FakeNumbers : INumberGenerator
    {
        public Task<string> NextAsync(string type, DateOnly date, CancellationToken token = default) => Task.FromResult("RFQ-001");
    }
    private sealed class FakeItems : IRequisitionItemGuard
    {
        public Task<T> RunAsync<T>(IReadOnlyCollection<long> ids, Func<Task<T>> action, CancellationToken token = default) => action();
    }
    private sealed class FakeSuppliers : ISupplierLookup
    {
        public Task<SupplierIdentity> RequireApprovedAsync(long id, CancellationToken token = default) =>
            Task.FromResult(new SupplierIdentity(id, $"SUP{id}", $"Supplier {id}"));
    }
}
