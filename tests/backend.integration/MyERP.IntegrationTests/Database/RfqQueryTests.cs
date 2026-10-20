using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class RfqQueryTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavedLinesInvitationsAndScopeAreReadFromSql()
    {
        var companyId = TestCompanies.Next();
        var company = new FixedCompany(companyId);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(7), company);
        await using var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel()], company);
        var self = new Rfq { CompanyId = companyId, DocumentNo = $"RFQ-A-{companyId}",
            BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1),
            OwnerId = 7, BranchId = 10, DepartmentId = 20 };
        self.Lines.Add(new RfqLine { CompanyId = companyId, LineNo = 1, ItemId = 11,
            RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1.123456m });
        self.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = 21 });
        var department = new Rfq { CompanyId = companyId, DocumentNo = $"RFQ-B-{companyId}",
            BusinessDate = new DateOnly(2028, 3, 1), ReplyDeadline = new DateOnly(2028, 3, 2),
            OwnerId = 8, BranchId = 10, DepartmentId = 20 };
        var branch = new Rfq { CompanyId = companyId, DocumentNo = $"RFQ-C-{companyId}",
            BusinessDate = new DateOnly(2028, 3, 1), ReplyDeadline = new DateOnly(2028, 3, 2),
            OwnerId = 9, BranchId = 10, DepartmentId = 21 };
        db.AddRange(self, department, branch);
        await db.SaveChangesAsync(Ct);

        foreach (var (scope, count) in new[] { (DataScope.Self, 1L), (DataScope.Department, 2L),
            (DataScope.Branch, 3L), (DataScope.Company, 3L) })
        {
            var auth = new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
                new Dictionary<string, DataScope> { [PurchasingPermissions.RfqView] = scope }));
            var query = new RfqQueryService(db, new RequisitionCompanyAccess(company), auth);
            Assert.Equal(count, (await query.ListAsync(new RfqQuery(), Ct)).TotalCount);
            if (scope == DataScope.Self)
            {
                var detail = await query.GetAsync(self.Id, Ct);
                Assert.Equal(1.123456m, Assert.Single(detail.Lines).Quantity);
                Assert.Equal(21, Assert.Single(detail.Invitations).SupplierId);
                Assert.Equal(self.Version, detail.Version);
                await Assert.ThrowsAsync<NotFoundException>(() => query.GetAsync(department.Id, Ct));
            }
        }
        var companyAuth = new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
            new Dictionary<string, DataScope> { [PurchasingPermissions.RfqView] = DataScope.Company }));
        var companyQuery = new RfqQueryService(db, new RequisitionCompanyAccess(company), companyAuth);
        Assert.Equal(self.Id, Assert.Single((await companyQuery.ListAsync(new RfqQuery
        { Keyword = self.DocumentNo, From = self.BusinessDate, To = self.BusinessDate }, Ct)).Items).Id);
        Assert.Equal(branch.Id, Assert.Single((await companyQuery.ListAsync(new RfqQuery
        { Sort = "-documentNo", PageSize = 1 }, Ct)).Items).Id);
        foreach (var invalid in new[] { new RfqQuery { PageSize = 0 }, new RfqQuery { Sort = "ownerId" },
            new RfqQuery { From = new DateOnly(2028, 3, 2), To = new DateOnly(2028, 3, 1) },
            new RfqQuery { Status = (RfqStatus)99 } })
            await Assert.ThrowsAsync<RequestValidationException>(() => companyQuery.ListAsync(invalid, Ct));
        self.IsDeleted = true;
        self.DeletedAt = new DateTime(2026, 10, 21);
        await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => companyQuery.GetAsync(self.Id, Ct));
        var deniedQuery = new RfqQueryService(db, new RequisitionCompanyAccess(company),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
                new Dictionary<string, DataScope>())));
        await Assert.ThrowsAsync<ForbiddenException>(() => deniedQuery.ListAsync(new RfqQuery(), Ct));
        var otherCompanyId = TestCompanies.Next();
        var otherCompany = new FixedCompany(otherCompanyId);
        await using var otherDb = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString).Options,
            [new RfqDbModel()], otherCompany);
        var otherQuery = new RfqQueryService(otherDb,
            new RequisitionCompanyAccess(otherCompany),
            new FixedAuthorization(new UserCompanyAuthorization(7, otherCompanyId, null, null, false,
                new Dictionary<string, DataScope> { [PurchasingPermissions.RfqView] = DataScope.Company })));
        await Assert.ThrowsAsync<NotFoundException>(() => otherQuery.GetAsync(branch.Id, Ct));
    }

    private sealed class FixedAuthorization(UserCompanyAuthorization? value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }
}
