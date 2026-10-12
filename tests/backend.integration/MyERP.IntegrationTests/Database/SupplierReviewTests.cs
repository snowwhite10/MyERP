using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class SupplierReviewTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new PaymentSetupDbModel(), new SupplierCategoryDbModel(), new SupplierDbModel(), new SupplierContactDbModel()], company);
    }

    [Fact]
    public async Task NewSupplierRequiresApprovalAndDeleteReservesCode()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(6000000, 7000000));
        var service = new SupplierService(db);
        var code = "S" + Guid.NewGuid().ToString("N")[..12];
        var supplier = await service.CreateAsync(code, "Review me", null, token);
        Assert.Equal(PartnerReviewStatus.Pending, supplier.ReviewStatus);
        var notApproved = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RequireApprovedAsync(supplier.Id, token));
        Assert.Equal("MD.SUPPLIER.NOT_APPROVED", notApproved.ErrorCode);
        var pending = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteAsync(supplier.Id, supplier.Version, token));
        Assert.Equal("MD.SUPPLIER.APPROVAL_PENDING", pending.ErrorCode);

        var approval = new SupplierApprovalService(db, new ImmediateApproval());
        var state = await approval.SubmitAsync(supplier.Id, supplier.Version, token);
        Assert.Equal(ApprovalStatus.Approved, state.Status);
        Assert.Equal(SupplierApprovalService.DocumentType, state.DocumentType);
        Assert.Equal(supplier.Id, (await service.RequireApprovedAsync(supplier.Id, token)).Id);
        supplier = await db.Set<Supplier>().SingleAsync(x => x.Id == supplier.Id, token);
        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => approval.SubmitAsync(supplier.Id, supplier.Version, token));
        Assert.Equal("MD.SUPPLIER.ALREADY_APPROVED", again.ErrorCode);

        await service.UpdateAsync(supplier.Id, supplier.Name, false, null, supplier.Version, token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RequireApprovedAsync(supplier.Id, token));
        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(supplier.Id, supplier.Version - 1, token));
        await service.DeleteAsync(supplier.Id, supplier.Version, token);
        Assert.Equal(0, (await service.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => service.RequireApprovedAsync(supplier.Id, token));
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(code, "Reused", null, token));
    }

    [Fact]
    public async Task FinalDecisionUpdatesPendingSupplierAndDocumentUsagePreventsDeletion()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(7000000, 8000000));
        var supplier = await new SupplierService(db).CreateAsync("S" + Guid.NewGuid().ToString("N")[..12], "Used", null, token);
        var handler = new SupplierApprovalResultHandler(db);
        Assert.Equal("MD.SUPPLIER", handler.DocumentType);
        await handler.HandleAsync(new ApprovalCompleted(7, SupplierApprovalService.DocumentType, supplier.Id,
            ApprovalStatus.Approved, 8, null), token);
        await db.SaveChangesAsync(token);
        Assert.Equal(supplier.Id, (await new SupplierService(db).RequireApprovedAsync(supplier.Id, token)).Id);
        var changed = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new ApprovalCompleted(7,
            SupplierApprovalService.DocumentType, supplier.Id, ApprovalStatus.Rejected, 8, null), token));
        Assert.Equal("MD.SUPPLIER.APPROVAL_STATE_CHANGED", changed.ErrorCode);

        var service = new SupplierService(db, [new UsedSupplier()]);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(supplier.Id, supplier.Version, token));
        Assert.Equal("MD.SUPPLIER.IN_USE", error.ErrorCode);
        Assert.False(supplier.IsDeleted);
    }

    [Fact]
    public async Task RejectedSupplierCanBeResubmittedOrDeleted()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(8000000, 9000000));
        var service = new SupplierService(db);
        var supplier = await service.CreateAsync("S" + Guid.NewGuid().ToString("N")[..12], "Rejected", null, token);
        await new SupplierApprovalService(db, new PendingApproval()).SubmitAsync(supplier.Id, supplier.Version, token);
        Assert.Equal(PartnerReviewStatus.Pending, supplier.ReviewStatus);
        await new SupplierApprovalResultHandler(db).HandleAsync(new ApprovalCompleted(9, SupplierApprovalService.DocumentType,
            supplier.Id, ApprovalStatus.Rejected, 8, "missing tax id"), token);
        await db.SaveChangesAsync(token);
        Assert.Equal(PartnerReviewStatus.Rejected, supplier.ReviewStatus);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RequireApprovedAsync(supplier.Id, token));
        await service.DeleteAsync(supplier.Id, supplier.Version, token);
        Assert.True(supplier.IsDeleted);
    }

    [Fact]
    public async Task SupplierInOpenApprovalCannotBeEditedUntilDecided()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(9000000, 9500000));
        var workflow = new TrackedApproval();
        var service = new SupplierService(db, workflow: workflow);
        var supplier = await service.CreateAsync("S" + Guid.NewGuid().ToString("N")[..12], "Draft", null, token);
        supplier = await service.UpdateAsync(supplier.Id, "Draft renamed", true, null, supplier.Version, token);

        workflow.Status = ApprovalStatus.Pending;
        var pending = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(supplier.Id, "Swapped", true, null, supplier.Version, token));
        Assert.Equal("MD.SUPPLIER.APPROVAL_PENDING", pending.ErrorCode);
        Assert.Equal(SupplierApprovalService.DocumentType, workflow.LastDocumentType);
        Assert.Equal("Draft renamed", (await db.Set<Supplier>().AsNoTracking().SingleAsync(x => x.Id == supplier.Id, token)).Name);

        workflow.Status = ApprovalStatus.Cancelled;
        supplier = await service.UpdateAsync(supplier.Id, "After cancel", true, null, supplier.Version, token);
        Assert.Equal("After cancel", supplier.Name);
    }

    [Fact]
    public async Task CustomerInOpenApprovalCannotBeEditedButApprovedCustomerCan()
    {
        var token = TestContext.Current.CancellationToken;
        var company = Random.Shared.NextInt64(9500000, 9999999);
        var customerCompany = new FixedCompany(company);
        await using var db = new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                    new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), customerCompany))).Options,
            [new PaymentSetupDbModel(), new CustomerCategoryDbModel(), new CustomerDbModel(), new CustomerContactDbModel()],
            customerCompany);
        var workflow = new TrackedApproval { Status = ApprovalStatus.Pending };
        var service = new CustomerService(db, workflow: workflow);
        var customer = await service.CreateAsync("C" + Guid.NewGuid().ToString("N")[..12], "Customer", null, token);
        var pending = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(customer.Id, "Swapped", true, null, customer.Version, token));
        Assert.Equal("MD.CUSTOMER.APPROVAL_PENDING", pending.ErrorCode);
        Assert.Equal(CustomerApprovalService.DocumentType, workflow.LastDocumentType);

        await new CustomerApprovalResultHandler(db).HandleAsync(new ApprovalCompleted(3, CustomerApprovalService.DocumentType,
            customer.Id, ApprovalStatus.Approved, 8, null), token);
        await db.SaveChangesAsync(token);
        workflow.LastDocumentType = null;
        customer = await service.UpdateAsync(customer.Id, "Renamed", true, null, customer.Version, token);
        Assert.Equal("Renamed", customer.Name);
        Assert.Null(workflow.LastDocumentType);
    }

    private sealed class TrackedApproval : IApprovalWorkflowService
    {
        public ApprovalStatus? Status { get; set; }
        public string? LastDocumentType { get; set; }
        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default)
        {
            LastDocumentType = documentType;
            return Task.FromResult(Status is { } status ? new ApprovalState(5, documentType, documentId, status, null, 1) : null);
        }
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

    private sealed class PendingApproval : IApprovalWorkflowService
    {
        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalState(9, submission.DocumentType, submission.DocumentId, ApprovalStatus.Pending, 1, 1));
        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UsedSupplier : ISupplierUsage
    {
        public Task<bool> IsUsedAsync(long supplierId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
