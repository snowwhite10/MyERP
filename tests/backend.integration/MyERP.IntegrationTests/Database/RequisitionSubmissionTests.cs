using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class RequisitionSubmissionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ApprovalStatus.Pending, RequisitionStatus.PendingApproval)]
    [InlineData(ApprovalStatus.Approved, RequisitionStatus.Approved)]
    public async Task SubmitPersistsWorkflowResultWithoutStockMovement(ApprovalStatus approval, RequisitionStatus expected)
    {
        var company = TestCompanies.Next();
        var id = await SeedAsync(company);
        await using (var db = Context(company))
        {
            var workflow = new Workflow(approval);
            var result = await Service(db, company, workflow).SubmitAsync(id, 1, Ct);
            Assert.Equal(expected, result.Requisition.Status);
            Assert.Equal(approval, result.Approval.Status);
            Assert.Equal("PUR.REQUISITION", workflow.Submission?.DocumentType);
            Assert.Equal(id, workflow.Submission?.DocumentId);
            Assert.Equal(0m, workflow.Submission?.Amount);
        }
        await using var check = Context(company);
        Assert.Equal(expected, (await check.Set<Requisition>().SingleAsync(x => x.Id == id, Ct)).Status);
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}", company).SingleAsync(Ct));
    }

    [Fact]
    public async Task StaleVersionInvalidItemAndWorkflowFailureLeaveDraft()
    {
        var company = TestCompanies.Next();
        var id = await SeedAsync(company);
        await using (var db = Context(company))
        {
            var service = Service(db, company, new Workflow(ApprovalStatus.Pending));
            await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(id, 2, Ct));
        }
        await using (var db = Context(company))
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, company, new Workflow(ApprovalStatus.Pending), invalidItem: true).SubmitAsync(id, 1, Ct));
        }
        await using (var db = Context(company))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Service(db, company, new Workflow(ApprovalStatus.Pending, fail: true)).SubmitAsync(id, 1, Ct));
        }
        await using var check = Context(company);
        var saved = await check.Set<Requisition>().SingleAsync(x => x.Id == id, Ct);
        Assert.Equal(RequisitionStatus.Draft, saved.Status);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task ScopeAndStatusRestrictSubmission()
    {
        var company = TestCompanies.Next();
        var id = await SeedAsync(company);
        await using (var db = Context(company))
        {
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Service(db, company, new Workflow(ApprovalStatus.Pending), permission: false).SubmitAsync(id, 1, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Service(db, company, new Workflow(ApprovalStatus.Pending), scope: DataScope.Self).SubmitAsync(id, 1, Ct));
        }
        await using (var db = Context(TestCompanies.Next()))
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Service(db, company, new Workflow(ApprovalStatus.Pending)).SubmitAsync(id, 1, Ct));
        await using (var db = Context(company))
        {
            var service = Service(db, company, new Workflow(ApprovalStatus.Pending));
            await service.SubmitAsync(id, 1, Ct);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(id, 2, Ct));
        }
    }

    private async Task<long> SeedAsync(long company)
    {
        await using var db = Context(company);
        var document = new Requisition { CompanyId = company, DocumentNo = $"PUR-SUB-{company}",
            BusinessDate = new(2028, 2, 29), RequesterId = 8 };
        document.Lines.Add(new RequisitionLine { CompanyId = company, LineNo = 1, ItemId = 11,
            RequiredDate = new(2028, 3, 1), Quantity = 1.123456m });
        db.Add(document);
        await db.SaveChangesAsync(Ct);
        return document.Id;
    }

    private MyErpDbContext Context(long company)
    {
        var current = new FixedCompany(company);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 20)), new FixedUser(7), current);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel()], current);
    }

    private static RequisitionSubmissionService Service(MyErpDbContext db, long company, Workflow workflow,
        bool invalidItem = false, bool permission = true, DataScope scope = DataScope.Company) =>
        new(db, new RequisitionCompanyAccess(new FixedCompany(company)),
            new Authorization(permission ? new UserCompanyAuthorization(7, company, null, null, false,
                new Dictionary<string, DataScope> { ["PUR.REQUISITION.SUBMIT"] = scope }) : null),
            new Items(invalidItem), workflow);

    private sealed class Authorization(UserCompanyAuthorization? value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) => Task.FromResult(value);
    }

    private sealed class Items(bool invalid) : IRequisitionItemGuard
    {
        public Task<T> RunAsync<T>(IReadOnlyCollection<long> ids, Func<Task<T>> action, CancellationToken token = default) =>
            invalid ? throw new BusinessRuleException("MD.ITEM.INACTIVE", "Item inactive") : action();
    }

    private sealed class Workflow(ApprovalStatus status, bool fail = false) : IApprovalWorkflowService
    {
        public ApprovalSubmission? Submission { get; private set; }
        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default)
        {
            Submission = submission;
            if (fail) throw new InvalidOperationException("SYS failure");
            return Task.FromResult(new ApprovalState(1, submission.DocumentType, submission.DocumentId,
                status, status == ApprovalStatus.Pending ? 1 : null, status == ApprovalStatus.Pending ? 1 : 0));
        }
        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
