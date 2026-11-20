using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesDeliverySubmissionResult(SalesDelivery Delivery, ApprovalState Approval);

/// <summary>
/// REQ_F4112.3.4 / BR-SD-022 (ADR-139): submits a Draft delivery to SYS approval and withdraws a
/// Pending one. Both need <c>SAL.DELIVERY.SUBMIT</c>, scoped on the source order. Read Committed lock
/// order: source order primary key → delivery primary key → order lines in ascending ID. Submission
/// rereads every saved row after the locks and rechecks the full order-line balance; a Pending
/// delivery is the only reservation. Zero-step approval (BR-SD-024, ADR-144) posts the saved lines
/// through INV in this same transaction and the delivery becomes Posted; an INV refusal rolls back the
/// whole submission, including the SYS instance, and the delivery stays Draft.
/// </summary>
public sealed class SalesDeliverySubmissionService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesDeliveryDraftLines draftLines, SalesDeliveryPosting posting,
    IApprovalWorkflowService workflow)
{
    public const string Permission = "SAL.DELIVERY.SUBMIT";

    public Task<SalesDeliverySubmissionResult> SubmitAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, async (saved, order) =>
        {
            SalesDeliveryLifecycle.Next(saved.Status, SalesDeliveryAction.SubmitPending,
                order.Status == SalesOrderStatus.Approved);
            var orderLines = await posting.LockAndRecheckAsync(saved, SalesDeliveryStatus.Draft, token);

            await posting.LoadLinesAsync(saved, token);
            var byId = orderLines.ToDictionary(x => x.Id);
            // Approval thresholds use the order's base gross value of the delivered share.
            var amount = Money.Round(saved.Lines.Sum(line =>
                byId[line.OrderLineId].BaseGross * line.Quantity / byId[line.OrderLineId].Quantity),
                order.BaseCurrencyCode);
            ApprovalState? approval = null;
            await draftLines.GuardAsync(saved.Lines, async () =>
            {
                // SYS saves its own instance first; the header then changes once (one version step).
                approval = await workflow.SubmitAsync(new ApprovalSubmission(SalesDeliveryCreateService.DocumentType,
                    saved.Id, saved.DocumentNo, amount,
                    saved.Remark is { Length: > 200 } ? saved.Remark[..200] : saved.Remark), token);
                Verify(approval, saved.Id, ApprovalStatus.Pending, ApprovalStatus.Approved);
                saved.ApprovalInstanceId = approval.InstanceId;
                if (approval.Status == ApprovalStatus.Approved)
                {
                    // No applicable step: SYS approved without calling the handler, so SD posts here.
                    SalesDeliveryLifecycle.Next(saved.Status, SalesDeliveryAction.SubmitPosted, true);
                    await posting.PostAsync(saved, token);
                    return;
                }
                saved.Status = SalesDeliveryStatus.Pending;
                await db.SaveChangesAsync(token);
            }, token);
            return approval!;
        }, token);

    public Task<SalesDeliverySubmissionResult> WithdrawAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, async (saved, _) =>
        {
            // Withdrawal only releases the reservation, so the order state is irrelevant.
            var next = SalesDeliveryLifecycle.Next(saved.Status, SalesDeliveryAction.Withdraw, false);
            var approval = await workflow.CancelAsync(SalesDeliveryCreateService.DocumentType, saved.Id, token);
            Verify(approval, saved.Id, ApprovalStatus.Cancelled);
            saved.Status = next;
            await db.SaveChangesAsync(token);
            return approval;
        }, token);

    private async Task<SalesDeliverySubmissionResult> RunAsync(long id, int version,
        Func<SalesDelivery, OrderState, Task<ApprovalState>> action, CancellationToken token)
    {
        var scope = await access.RequireScopeAsync(Permission, token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // The source order of a saved delivery never changes, so this unlocked read is stable.
            var orderId = (await db.Database.SqlQueryRaw<long>(
                "SELECT [order_id] AS [Value] FROM dbo.sal_delivery WITH (FORCESEEK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token)).SingleOrDefault();
            if (orderId == 0) throw NotFound(id);
            await draftLines.LockOrderAsync(companyId, orderId, token);
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_delivery WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
                "WHERE [id] = {0} AND [company_id] = {1}",
                id, companyId).ToListAsync(token);

            var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
                .Where(x => x.Id == orderId)
                .Select(x => new OrderState(x.Status, x.BaseCurrencyCode)).SingleOrDefaultAsync(token)
                ?? throw NotFound(id);
            var saved = await db.Set<SalesDelivery>().SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw NotFound(id);
            if (version < 1 || saved.Version != version) throw new ConflictException();

            var approval = await action(saved, order);
            await transaction.CommitAsync(token);
            db.ChangeTracker.Clear();
            var result = await db.Set<SalesDelivery>().AsNoTracking().Include(x => x.Lines)
                .SingleAsync(x => x.Id == id, token);
            return new SalesDeliverySubmissionResult(result, approval);
        }
        catch (Exception ex)
        {
            if (db.Database.CurrentTransaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    private static void Verify(ApprovalState state, long documentId, params ApprovalStatus[] allowed)
    {
        if (state.DocumentType != SalesDeliveryCreateService.DocumentType || state.DocumentId != documentId ||
            state.InstanceId <= 0 || !allowed.Contains(state.Status))
            throw new ConflictException("SAL.DELIVERY.APPROVAL_RESULT_INVALID",
                "SYS returned an invalid delivery approval state.");
    }

    private static NotFoundException NotFound(long id) => new("SAL.DELIVERY.NOT_FOUND", "Sales delivery", id);

    private sealed record OrderState(SalesOrderStatus Status, string BaseCurrencyCode);
}
