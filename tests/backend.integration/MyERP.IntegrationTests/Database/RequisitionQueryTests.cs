using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class RequisitionQueryTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 19)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel()], company);
    }

    [Fact]
    public async Task ListFiltersAndPagesSavedHeadersWithoutExposingOtherCompanies()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var first = new Requisition { CompanyId = companyId, DocumentNo = "PUR-A", BusinessDate = new DateOnly(2028, 2, 29), RequesterId = 7 };
        first.Lines.Add(new RequisitionLine { CompanyId = companyId, LineNo = 1, ItemId = 11, RequiredDate = new DateOnly(2028, 3, 1), Quantity = 1.123456m });
        var second = new Requisition { CompanyId = companyId, DocumentNo = "PUR-B", BusinessDate = new DateOnly(2028, 3, 1), RequesterId = 8 };
        db.AddRange(first, second);
        await db.SaveChangesAsync(Ct);
        var authorization = new FixedAuthorization(new UserCompanyAuthorization(7, companyId, null, null, false,
            new Dictionary<string, DataScope> { ["PUR.REQUISITION.VIEW"] = DataScope.Company }));
        var query = new RequisitionQueryService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)), authorization);
        var page = await query.ListAsync(new RequisitionQuery { PageSize = 1, Sort = "-documentNo" }, Ct);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal("PUR-B", Assert.Single(page.Items).DocumentNo);
        var filtered = await query.ListAsync(new RequisitionQuery { Keyword = "PUR-A", From = new DateOnly(2028, 2, 29), To = new DateOnly(2028, 2, 29), RequesterId = 7 }, Ct);
        Assert.Equal(first.Id, Assert.Single(filtered.Items).Id);
        var detail = await query.GetAsync(first.Id, Ct);
        Assert.Equal(1.123456m, Assert.Single(detail.Lines).Quantity);
        Assert.Equal(first.Version, detail.Version);
        var otherCompanyId = TestCompanies.Next();
        await using var other = Context(otherCompanyId);
        var otherQuery = new RequisitionQueryService(other, new RequisitionCompanyAccess(new FixedCompany(otherCompanyId)),
            new FixedAuthorization(new UserCompanyAuthorization(7, otherCompanyId, null, null, false,
                new Dictionary<string, DataScope> { ["PUR.REQUISITION.VIEW"] = DataScope.Company })));
        Assert.Empty((await otherQuery.ListAsync(new RequisitionQuery(), Ct)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => otherQuery.GetAsync(first.Id, Ct));
        foreach (var invalid in new[] { new RequisitionQuery { PageSize = 0 },
            new RequisitionQuery { Sort = "requesterId" },
            new RequisitionQuery { From = new DateOnly(2028, 3, 2), To = new DateOnly(2028, 3, 1) },
            new RequisitionQuery { RequesterId = 0 },
            new RequisitionQuery { Status = (RequisitionStatus)99 } })
            await Assert.ThrowsAsync<RequestValidationException>(() => query.ListAsync(invalid, Ct));
        first.IsDeleted = true;
        first.DeletedAt = new DateTime(2026, 10, 19);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(1, (await query.ListAsync(new RequisitionQuery(), Ct)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => query.GetAsync(first.Id, Ct));
    }

    [Fact]
    public async Task ViewScopeFiltersListAndDetailForSelfDepartmentAndBranch()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var self = new Requisition { CompanyId = companyId, DocumentNo = "PUR-SELF", BusinessDate = new DateOnly(2028, 1, 1),
            RequesterId = 7, BranchId = 10, DepartmentId = 20 };
        var sameDepartment = new Requisition { CompanyId = companyId, DocumentNo = "PUR-DEPT", BusinessDate = new DateOnly(2028, 1, 1),
            RequesterId = 8, BranchId = 10, DepartmentId = 20 };
        var sameBranch = new Requisition { CompanyId = companyId, DocumentNo = "PUR-BRANCH", BusinessDate = new DateOnly(2028, 1, 1),
            RequesterId = 9, BranchId = 10, DepartmentId = 21 };
        var otherBranch = new Requisition { CompanyId = companyId, DocumentNo = "PUR-OTHER", BusinessDate = new DateOnly(2028, 1, 1),
            RequesterId = 10, BranchId = 11, DepartmentId = 22 };
        db.AddRange(self, sameDepartment, sameBranch, otherBranch);
        await db.SaveChangesAsync(Ct);
        foreach (var (scope, expectedCount) in new[] { (DataScope.Self, 1L), (DataScope.Department, 2L),
            (DataScope.Branch, 3L), (DataScope.Company, 4L) })
        {
            var auth = new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
                new Dictionary<string, DataScope> { ["PUR.REQUISITION.VIEW"] = scope }));
            var query = new RequisitionQueryService(db, new RequisitionCompanyAccess(new FixedCompany(companyId)), auth);
            Assert.Equal(expectedCount, (await query.ListAsync(new RequisitionQuery(), Ct)).TotalCount);
            if (scope == DataScope.Company)
                Assert.Equal(otherBranch.Id, (await query.GetAsync(otherBranch.Id, Ct)).Id);
            else
                await Assert.ThrowsAsync<NotFoundException>(() => query.GetAsync(otherBranch.Id, Ct));
        }
        var missingPermission = new RequisitionQueryService(db,
            new RequisitionCompanyAccess(new FixedCompany(companyId)),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, 10, 20, false,
                new Dictionary<string, DataScope>())));
        await Assert.ThrowsAsync<ForbiddenException>(() => missingPermission.ListAsync(new RequisitionQuery(), Ct));
        var noOrganization = new RequisitionQueryService(db,
            new RequisitionCompanyAccess(new FixedCompany(companyId)),
            new FixedAuthorization(new UserCompanyAuthorization(7, companyId, null, null, false,
                new Dictionary<string, DataScope> { ["PUR.REQUISITION.VIEW"] = DataScope.Branch })));
        Assert.Equal(self.Id, Assert.Single((await noOrganization.ListAsync(new RequisitionQuery(), Ct)).Items).Id);
    }

    private sealed class FixedAuthorization(UserCompanyAuthorization? value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }
}
