using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>客戶送審。SYS 審批引擎和 MD 共用 scoped DbContext 與交易。</summary>
public sealed class CustomerApprovalService(MyErpDbContext db, IApprovalWorkflowService workflow)
{
    public const string DocumentType = "MD.CUSTOMER";

    public async Task<ApprovalState> SubmitAsync(long id, int version, CancellationToken token = default)
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var customer = await db.Set<Customer>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("MD.CUSTOMER.NOT_FOUND", "Customer", id);
        if (version < 1 || customer.Version != version) throw new ConflictException();
        if (customer.ReviewStatus == CustomerReviewStatus.Approved)
            throw new BusinessRuleException("MD.CUSTOMER.ALREADY_APPROVED", "Customer is already approved.");

        var state = await workflow.SubmitAsync(new ApprovalSubmission(DocumentType, id, customer.Code, 0m, customer.Name), token);
        customer.ReviewStatus = state.Status == ApprovalStatus.Approved
            ? CustomerReviewStatus.Approved : CustomerReviewStatus.Pending;
        db.Entry(customer).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        await transaction.CommitAsync(token);
        return state;
    }
}

/// <summary>SYS 最終審批結果在同一交易內同步到客戶主檔。</summary>
public sealed class CustomerApprovalResultHandler(MyErpDbContext db) : IApprovalResultHandler
{
    public string DocumentType => CustomerApprovalService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        var customer = await db.Set<Customer>().SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException("MD.CUSTOMER.NOT_FOUND", "Customer", result.DocumentId);
        if (customer.ReviewStatus != CustomerReviewStatus.Pending)
            throw new ConflictException("MD.CUSTOMER.APPROVAL_STATE_CHANGED", "Customer review state changed.");
        customer.ReviewStatus = result.Status == ApprovalStatus.Approved
            ? CustomerReviewStatus.Approved : CustomerReviewStatus.Rejected;
    }
}
