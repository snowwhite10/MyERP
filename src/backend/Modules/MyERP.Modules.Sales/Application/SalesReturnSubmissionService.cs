using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesReturnSubmissionResult(SalesReturn Return, ApprovalState Approval);

/// <summary>
/// WU-SD-008a5a (REQ_F4112.7.1, BR-SD-043/044/047/049, ADR-190): submits a Draft sales return to SYS approval
/// (<c>SAL.RETURN.SUBMIT</c>, exact action scope on the source order; hidden returns are 404). Read Committed lock order
/// (BR-SD-047): FI customer credit key → order row → return row → delivery lines → invoice lines (ascending). The saved
/// lines are rechecked and their snapshot rebuilt by <see cref="SalesReturnPosting.RecheckAsync"/>; MD destinations are
/// revalidated under their shared locks; SYS saves its instance and the return becomes Pending. Zero-step approval
/// (BR-SD-043) runs the final posting path — INV inbound, FI credit of billed portions, SD commitment release, Posted —
/// in this same transaction; any refusal rolls everything back, including the SYS instance, and the return stays Draft.
/// </summary>
public sealed class SalesReturnSubmissionService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesReturnDraftBuilder builder, SalesReturnPosting posting,
    ICustomerCreditService credit, IApprovalWorkflowService workflow)
{
    public const string Permission = "SAL.RETURN.SUBMIT";
    public const string ApprovalResultInvalid = "SAL.RETURN.APPROVAL_RESULT_INVALID";

    public async Task<SalesReturnSubmissionResult> SubmitAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync(Permission, token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // Order and customer of a saved return never change (trigger 51035), so this unlocked read only picks locks.
            var source = (await db.Database.SqlQuery<ReturnSource>($"""
                SELECT [order_id], [customer_id] FROM dbo.sal_return WITH (FORCESEEK)
                WHERE [id] = {id} AND [company_id] = {companyId} AND [is_deleted] = 0
                """).ToListAsync(token)).SingleOrDefault() ?? throw NotFound(id);
            await credit.AcquireCreditLockAsync(companyId, source.CustomerId, transaction.GetDbTransaction(), token);
            await builder.LockOrderAsync(companyId, source.OrderId, token);
            await db.Database.SqlQuery<long>($"""
                SELECT [id] AS [Value] FROM dbo.sal_return WITH (UPDLOCK, HOLDLOCK, FORCESEEK)
                WHERE [id] = {id} AND [company_id] = {companyId}
                """).ToListAsync(token);

            var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
                .Where(x => x.Id == source.OrderId).Include(x => x.Lines).AsSplitQuery().SingleOrDefaultAsync(token)
                ?? throw NotFound(id);
            var saved = await db.Set<SalesReturn>().AsSplitQuery()
                .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations)
                .SingleOrDefaultAsync(x => x.Id == id, token) ?? throw NotFound(id);
            if (version < 1 || saved.Version != version) throw new ConflictException();
            if (saved.Status != SalesReturnStatus.Draft)
                throw new BusinessRuleException(SalesReturnDraftCommandService.InvalidStatus,
                    "Only a Draft sales return can be submitted.");

            await posting.RecheckAsync(saved, order, token);
            ApprovalState? approval = null;
            await builder.GuardDestinationsAsync(saved.Lines, async () =>
            {
                // SYS saves its own instance first; the header then becomes Pending in one version step.
                approval = await workflow.SubmitAsync(new ApprovalSubmission(SalesReturnCreateService.DocumentType,
                    saved.Id, saved.DocumentNo, saved.BaseGross,
                    saved.Remark is { Length: > 200 } ? saved.Remark[..200] : saved.Remark), token);
                Verify(approval, saved.Id);
                saved.ApprovalInstanceId = approval.InstanceId;
                saved.Status = SalesReturnStatus.Pending;
                await db.SaveChangesAsync(token);
                // No applicable step: SYS approved without calling a handler, so SD posts here (Draft→Pending→Posted).
                if (approval.Status == ApprovalStatus.Approved)
                    await posting.PostAsync(saved, token);
            }, token);

            await transaction.CommitAsync(token);
            db.ChangeTracker.Clear();
            var result = await db.Set<SalesReturn>().AsNoTracking().AsSplitQuery()
                .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations).SingleAsync(x => x.Id == id, token);
            SalesReturnCreateService.SortForOutput(result);
            return new SalesReturnSubmissionResult(result, approval!);
        }
        catch (Exception ex)
        {
            // An FI fault has already rolled the shared transaction back.
            if (db.Database.CurrentTransaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    private static void Verify(ApprovalState state, long documentId)
    {
        if (state.DocumentType != SalesReturnCreateService.DocumentType || state.DocumentId != documentId ||
            state.InstanceId <= 0 || state.Status is not (ApprovalStatus.Pending or ApprovalStatus.Approved))
            throw new ConflictException(ApprovalResultInvalid, "SYS returned an invalid sales return approval state.");
    }

    private static NotFoundException NotFound(long id) =>
        new(SalesReturnDraftCommandService.NotFoundCode, "Sales return", id);

    private sealed record ReturnSource(long OrderId, long CustomerId);
}
