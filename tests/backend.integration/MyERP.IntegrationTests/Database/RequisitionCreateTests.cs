using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Numbering;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.IntegrationTests.Database;

public sealed class RequisitionCreateTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavesDraftAtomicallyWithAuthorizedOwnerAndNoInventoryRows()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId, new FakeItems());
        var input = new RequisitionInput(new DateOnly(2028, 2, 29), " request ",
            [new(1, 11, new DateOnly(2028, 3, 1), 1.123456m, null),
             new(2, 12, new DateOnly(2028, 3, 2), 2m, " line ")]);
        var result = await service.CreateAsync(input, Ct);
        Assert.Equal("PUR-001", result.DocumentNo);
        await using var check = Context(companyId);
        var saved = await check.Set<Requisition>().Include(x => x.Lines).SingleAsync(Ct);
        Assert.Equal(7, saved.RequesterId);
        Assert.Equal(10, saved.BranchId);
        Assert.Equal(20, saved.DepartmentId);
        Assert.Equal(2, saved.Lines.Count);
        Assert.Equal(1.123456m, saved.Lines.Single(x => x.LineNo == 1).Quantity);
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}", companyId).SingleAsync(Ct));
    }

    [Fact]
    public async Task InvalidLineOrItemRejectsWholeDocument()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId, new FakeItems(12));
        var date = new DateOnly(2028, 2, 29);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new RequisitionInput(date, null, [new(1, 11, date, 1.0000001m, null)]), Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new RequisitionInput(date, null, [new(1, 11, date, 1m, null), new(2, 12, date, 1m, null)]), Ct));
        Assert.Empty(await db.Set<Requisition>().ToListAsync(Ct));
    }

    [Fact]
    public async Task RejectsDateLineCountDuplicateAndPrecisionBoundariesBeforeNumbering()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var service = Service(db, companyId, new FakeItems());
        var date = new DateOnly(2028, 2, 29);
        var valid = new RequisitionLineInput(1, 11, date, 0.000001m, null);
        var invalid = new[]
        {
            new RequisitionInput(default, null, [valid]),
            new RequisitionInput(date, null, []),
            new RequisitionInput(date, null, Enumerable.Range(1, 1001)
                .Select(i => new RequisitionLineInput(i, 11, date, 1m, null)).ToArray()),
            new RequisitionInput(date, null, [valid, valid]),
            new RequisitionInput(date, null, [valid with { LineNo = 0 }]),
            new RequisitionInput(date, null, [valid with { Quantity = 0 }]),
            new RequisitionInput(date, null, [valid with { Quantity = 10_000_000_000_000m }]),
            new RequisitionInput(date, null, [valid with { RequiredDate = date.AddDays(-1) }]),
            new RequisitionInput(date, new string('x', 501), [valid]),
            new RequisitionInput(date, null, [valid with { Remark = new string('x', 501) }])
        };
        foreach (var input in invalid)
            await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(input, Ct));
        Assert.Empty(await db.Set<Requisition>().ToListAsync(Ct));
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 19)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel()], company);
    }

    private static RequisitionCreateService Service(MyErpDbContext db, long companyId, FakeItems items) =>
        new(db, new RequisitionCompanyAccess(new FixedCompany(companyId)),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
                new Dictionary<string, DataScope> { ["PUR.REQUISITION.CREATE"] = DataScope.Self })),
            new FakeNumbers(), items);

    private sealed class FixedAuthorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) => Task.FromResult<UserCompanyAuthorization?>(value);
    }

    private sealed class FakeNumbers : INumberGenerator
    {
        public Task<string> NextAsync(string type, DateOnly date, CancellationToken token = default) => Task.FromResult("PUR-001");
    }

    private sealed class FakeItems(long inactiveId = 0) : IRequisitionItemGuard
    {
        public Task<T> RunAsync<T>(IReadOnlyCollection<long> ids, Func<Task<T>> action, CancellationToken token = default) =>
            ids.Contains(inactiveId) ? throw new BusinessRuleException("MD.ITEM.INACTIVE", "Item is inactive.") : action();
    }
}
