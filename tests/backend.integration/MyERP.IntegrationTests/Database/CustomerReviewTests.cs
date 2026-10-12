using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class CustomerReviewTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new CustomerCategoryDbModel(), new CustomerDbModel(), new CustomerContactDbModel()], company);
    }

    [Fact]
    public async Task NewCustomerRequiresApprovalAndDeleteReservesCode()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(4000000, 5000000));
        var service = new CustomerService(db);
        var code = "C" + Guid.NewGuid().ToString("N")[..12];
        var customer = await service.CreateAsync(code, "Review me", null, token);
        Assert.Equal(CustomerReviewStatus.Pending, customer.ReviewStatus);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RequireApprovedAsync(customer.Id, token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteAsync(customer.Id, customer.Version, token));
        var approval = new CustomerApprovalService(db, new ImmediateApproval());
        var state = await approval.SubmitAsync(customer.Id, customer.Version, token);
        Assert.Equal(ApprovalStatus.Approved, state.Status);
        Assert.Equal(customer.Id, (await service.RequireApprovedAsync(customer.Id, token)).Id);
        customer = await db.Set<Customer>().SingleAsync(x => x.Id == customer.Id, token);
        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(customer.Id, customer.Version - 1, token));
        await service.DeleteAsync(customer.Id, customer.Version, token);
        Assert.Equal(0, (await service.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(code, "Reused", null, token));
    }

    private sealed class ImmediateApproval : IApprovalWorkflowService
    {
        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalState(1, submission.DocumentType, submission.DocumentId, ApprovalStatus.Approved, null, 0));
        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task FinalDecisionUpdatesPendingCustomerAndDocumentUsagePreventsDeletion()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(5000000, 6000000));
        var customer = await new CustomerService(db).CreateAsync("C" + Guid.NewGuid().ToString("N")[..12], "Used", null, token);
        var handler = new CustomerApprovalResultHandler(db);
        await handler.HandleAsync(new ApprovalCompleted(7, CustomerApprovalService.DocumentType, customer.Id,
            ApprovalStatus.Approved, 8, null), token);
        await db.SaveChangesAsync(token);
        Assert.Equal(customer.Id, (await new CustomerService(db).RequireApprovedAsync(customer.Id, token)).Id);
        var service = new CustomerService(db, [new UsedCustomer()]);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(customer.Id, customer.Version, token));
        Assert.Equal("MD.CUSTOMER.IN_USE", error.ErrorCode);
        Assert.False(customer.IsDeleted);
    }

    private sealed class UsedCustomer : ICustomerUsage
    {
        public Task<bool> IsUsedAsync(long customerId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
