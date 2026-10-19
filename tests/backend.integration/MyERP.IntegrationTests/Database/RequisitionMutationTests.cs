using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class RequisitionMutationTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpdateReplacesLinesAndStaleVersionOrInvalidItemRollsBack()
    {
        var company = TestCompanies.Next();
        long id;
        await using (var db = Context(company))
        {
            var document = new Requisition { CompanyId = company, DocumentNo = "PUR-EDIT", BusinessDate = new(2028, 2, 29), RequesterId = 7 };
            document.Lines.Add(new RequisitionLine { CompanyId = company, LineNo = 1, ItemId = 11, RequiredDate = new(2028, 3, 1), Quantity = 1 });
            db.Add(document);
            await db.SaveChangesAsync(Ct);
            id = document.Id;
        }
        await using (var db = Context(company))
        {
            var service = Service(db, company);
            var changed = await service.UpdateAsync(id, 1, new(new(2028, 3, 1), " revised ",
                [new(2, 12, new(2028, 3, 2), 1.123456m, null)]), Ct);
            Assert.Equal("revised", changed.Remark);
            Assert.Single(changed.Lines);
            Assert.Equal(2, changed.Lines[0].LineNo);
            await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(id, 1,
                new(new(2028, 3, 1), null, [new(3, 13, new(2028, 3, 2), 1m, null)]), Ct));
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(id, changed.Version,
                new(new(2028, 3, 1), null, [new(3, 99, new(2028, 3, 2), 1m, null)]), Ct));
        }
        await using var check = Context(company);
        var saved = await check.Set<Requisition>().Include(x => x.Lines).SingleAsync(Ct);
        Assert.Equal(2, saved.Lines.Single().LineNo);
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}", company).SingleAsync(Ct));
    }

    [Fact]
    public async Task DeleteOnlyDraftAndHideFromQueries()
    {
        var company = TestCompanies.Next();
        long id;
        await using (var db = Context(company))
        {
            var document = new Requisition { CompanyId = company, DocumentNo = "PUR-DELETE", BusinessDate = new(2028, 2, 29), RequesterId = 7 };
            db.Add(document);
            await db.SaveChangesAsync(Ct);
            id = document.Id;
        }
        await using (var db = Context(company))
        {
            var service = Service(db, company);
            await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(id, 2, Ct));
            await service.DeleteAsync(id, 1, Ct);
            await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(id, 1, Ct));
        }
        await using var check = Context(company);
        Assert.Empty(await check.Set<Requisition>().ToListAsync(Ct));
        Assert.True((await check.Set<Requisition>().IgnoreQueryFilters().SingleAsync(x => x.Id == id, Ct)).IsDeleted);
    }

    [Fact]
    public async Task SelfScopeHidesOtherOwnerAndPendingStatusRejectsMutation()
    {
        var company = TestCompanies.Next();
        long otherId;
        long[] lockedIds;
        await using (var db = Context(company))
        {
            var other = new Requisition { CompanyId = company, DocumentNo = "PUR-OTHER", BusinessDate = new(2028, 2, 29), RequesterId = 8 };
            var locked = new[] { RequisitionStatus.PendingApproval, RequisitionStatus.Approved,
                RequisitionStatus.Closed }.Select(status => new Requisition { CompanyId = company,
                DocumentNo = "PUR-" + status, BusinessDate = new(2028, 2, 29), RequesterId = 7,
                Status = status }).ToArray();
            db.Add(other);
            db.AddRange(locked);
            await db.SaveChangesAsync(Ct);
            otherId = other.Id;
            lockedIds = locked.Select(x => x.Id).ToArray();
        }
        await using var check = Context(company);
        var service = Service(check, company, DataScope.Self);
        var input = new RequisitionInput(new(2028, 2, 29), null,
            [new(1, 11, new(2028, 3, 1), 1m, null)]);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(otherId, 1, input, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(otherId, 1, Ct));
        foreach (var id in lockedIds)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(id, 1, input, Ct));
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteAsync(id, 1, Ct));
        }
    }

    [Fact]
    public async Task RejectedRevisionReturnsDraftAndOtherCompanyCannotMutate()
    {
        var company = TestCompanies.Next();
        var otherCompany = TestCompanies.Next();
        long id;
        await using (var db = Context(company))
        {
            var rejected = new Requisition { CompanyId = company, DocumentNo = "PUR-REJECTED",
                BusinessDate = new(2028, 2, 29), RequesterId = 7, Status = RequisitionStatus.Rejected };
            db.Add(rejected);
            await db.SaveChangesAsync(Ct);
            id = rejected.Id;
        }
        var input = new RequisitionInput(new(2028, 2, 29), null,
            [new(1, 11, new(2028, 3, 1), 1m, null)]);
        await using (var db = Context(otherCompany))
        {
            var service = Service(db, otherCompany);
            await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(id, 1, input, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(id, 1, Ct));
        }
        await using (var db = Context(company))
        {
            var updated = await Service(db, company).UpdateAsync(id, 1, input, Ct);
            Assert.Equal(RequisitionStatus.Draft, updated.Status);
            Assert.Equal(2, updated.Version);
        }
    }

    private MyErpDbContext Context(long company)
    {
        var current = new FixedCompany(company);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 19)), new FixedUser(7), current);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel()], current);
    }

    private static RequisitionMutationService Service(MyErpDbContext db, long company, DataScope scope = DataScope.Company) =>
        new(db, new RequisitionCompanyAccess(new FixedCompany(company)),
            new Authorization(new UserCompanyAuthorization(7, company, null, null, false,
                new Dictionary<string, DataScope> { ["PUR.REQUISITION.EDIT"] = scope,
                    ["PUR.REQUISITION.DELETE"] = scope })), new Items(), new FixedClock(new DateTime(2026, 10, 19)));

    private sealed class Authorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) => Task.FromResult<UserCompanyAuthorization?>(value);
    }
    private sealed class Items : IRequisitionItemGuard
    {
        public Task<T> RunAsync<T>(IReadOnlyCollection<long> ids, Func<Task<T>> action, CancellationToken token = default) =>
            ids.Contains(99) ? throw new BusinessRuleException("MD.ITEM.INACTIVE", "Item inactive") : action();
    }
}
