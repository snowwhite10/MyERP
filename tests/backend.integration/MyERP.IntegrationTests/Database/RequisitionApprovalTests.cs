using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class RequisitionApprovalTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly FixedClock Clock = new(new DateTime(2026, 10, 20, 15, 0, 0));

    [Fact]
    public async Task FinalDecisionUpdatesRequisitionAndHistoryAcrossResubmission()
    {
        long company;
        await using (var db = Context(null))
        {
            var entity = new SystemCompany { Code = "P" + Guid.NewGuid().ToString("N")[..12], Name = "Requisition approval" };
            db.Add(entity);
            await db.SaveChangesAsync(Ct);
            company = entity.Id;
        }
        long id;
        await using (var db = Context(company))
        {
            var requisition = new Requisition { CompanyId = company, DocumentNo = $"PUR-APR-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), RequesterId = 8 };
            requisition.Lines.Add(new RequisitionLine { CompanyId = company, LineNo = 1, ItemId = 11,
                RequiredDate = new DateOnly(2028, 3, 1), Quantity = 2m });
            db.Add(requisition);
            await db.SaveChangesAsync(Ct);
            id = requisition.Id;
        }
        // The engine checks active company membership for the approver.
        await using (var db = Context(null))
        {
            var user = new SystemUser { Username = $"purapr{company}", NormalizedUsername = $"PURAPR{company}", PasswordHash = "x" };
            db.Add(user);
            await db.SaveChangesAsync(Ct);
            db.Add(new SystemUserCompany { CompanyId = company, UserId = user.Id });
            await db.SaveChangesAsync(Ct);
            approver = user.Id;
        }
        await using (var db = Context(company))
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("PUR.REQUISITION", "Requisition", false,
                [new ApprovalStepInput("Manager", ApproverType.User, null, approver, null)]), Ct);

        async Task<ApprovalState> Submit()
        {
            await using var db = Context(company);
            var document = await db.Set<Requisition>().SingleAsync(x => x.Id == id, Ct);
            document.Status = RequisitionStatus.PendingApproval;
            var state = await Engine(db, 8).SubmitAsync(new ApprovalSubmission("PUR.REQUISITION", id,
                document.DocumentNo, 0m), Ct);
            await db.SaveChangesAsync(Ct);
            return state;
        }

        var first = await Submit();
        await using (var db = Context(company))
        {
            var document = await db.Set<Requisition>().SingleAsync(x => x.Id == id, Ct);
            document.Status = RequisitionStatus.Draft;
            await db.SaveChangesAsync(Ct);
        }
        await using (var db = Context(company))
            await Assert.ThrowsAsync<ConflictException>(() => Engine(db, approver).RejectAsync(first.InstanceId,
                new ApprovalDecision("Must roll back", 1), Ct));
        await using (var db = Context(company))
        {
            Assert.Equal(ApprovalStatus.Pending, (await db.Set<ApprovalInstance>()
                .SingleAsync(x => x.Id == first.InstanceId, Ct)).Status);
            var document = await db.Set<Requisition>().SingleAsync(x => x.Id == id, Ct);
            Assert.Equal(RequisitionStatus.Draft, document.Status);
            document.Status = RequisitionStatus.PendingApproval;
            await db.SaveChangesAsync(Ct);
        }
        await using (var db = Context(company))
        {
            var rejected = await Engine(db, approver).RejectAsync(first.InstanceId,
                new ApprovalDecision("Need correction", 1), Ct);
            Assert.Equal(ApprovalStatus.Rejected, rejected.Status);
        }
        await using (var db = Context(company))
        {
            var document = await db.Set<Requisition>().SingleAsync(x => x.Id == id, Ct);
            Assert.Equal(RequisitionStatus.Rejected, document.Status);
            document.Status = RequisitionStatus.Draft;
            await db.SaveChangesAsync(Ct);
        }
        var second = await Submit();
        async Task<string> DecideFromSeparateConnection()
        {
            await using var connection = Context(company);
            try
            {
                await Engine(connection, approver).ApproveAsync(second.InstanceId,
                    new ApprovalDecision("Approved", 1), Ct);
                return "approved";
            }
            catch (ConflictException)
            {
                return "conflict";
            }
            catch (BusinessRuleException)
            {
                return "not pending";
            }
        }
        var decisions = await Task.WhenAll(DecideFromSeparateConnection(), DecideFromSeparateConnection());
        Assert.Single(decisions, x => x == "approved");
        Assert.Single(decisions, x => x is "conflict" or "not pending");
        await using (var db = Context(company))
        {
            Assert.Equal(ApprovalStatus.Approved,
                (await db.Set<ApprovalInstance>().SingleAsync(x => x.Id == second.InstanceId, Ct)).Status);
            var history = await ((IApprovalHistoryReader)Engine(db, approver))
                .GetHistoryAsync("PUR.REQUISITION", id, Ct);
            Assert.Equal([second.InstanceId, first.InstanceId], history.Select(x => x.InstanceId));
            Assert.Equal("Approved", history[0].Steps[0].Comment);
            Assert.Equal("Need correction", history[1].Steps[0].Comment);
        }
        await using var check = Context(company);
        Assert.Equal(RequisitionStatus.Approved, (await check.Set<Requisition>().SingleAsync(x => x.Id == id, Ct)).Status);
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}", company).SingleAsync(Ct));
    }

    private long approver;

    private static ApprovalWorkflowService Engine(MyErpDbContext db, long user) =>
        new(db, Clock, new FixedUser(user), new AuditTrail(),
            [new RequisitionApprovalResultHandler(db)]);

    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(Clock, new FixedUser(8), company);
        return new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new SystemOrganizationDbModel(), new RequisitionDbModel()], company);
    }
}
