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
/// delivery is the only reservation. Zero-step approval would post stock, which waits for WU-SD-004b,
/// so it is refused with 422 and the whole submission (including the SYS instance) rolls back.
/// </summary>
public sealed class SalesDeliverySubmissionService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesDeliveryDraftLines draftLines, SalesDeliverySelectionStore selections,
    SalesOrderDraftStore orders, IApprovalWorkflowService workflow)
{
    public const string Permission = "SAL.DELIVERY.SUBMIT";
    public const string PostingUnavailable = "SAL.DELIVERY.POSTING_UNAVAILABLE";

    public Task<SalesDeliverySubmissionResult> SubmitAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, async (saved, order) =>
        {
            SalesDeliveryLifecycle.Next(saved.Status, SalesDeliveryAction.SubmitPending,
                order.Status == SalesOrderStatus.Approved);
            var selection = await selections.SelectAsync(saved.Id, SalesDeliveryStatus.Draft, token);
            foreach (var orderLineId in selection.OrderLineLockPlan)
                await LockOrderLineAsync(saved.CompanyId, orderLineId, token);

            var orderLines = await orders.OrderLinesQuery(saved.CompanyId, saved.OrderId).AsNoTracking()
                .Select(x => new { x.Id, x.Quantity, x.BaseGross }).ToListAsync(token);
            var snapshot = await SnapshotAsync(saved, token);
            SalesDeliverySelection.CheckLockedDecision(selection.Lines,
                orderLines.Select(x => new SavedOrderLineQuantity(x.Id, x.Quantity)).ToArray(),
                snapshot, saved.Id, SalesDeliveryStatus.Draft);

            await LoadLinesAsync(saved, token);
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
                if (approval.Status == ApprovalStatus.Approved)
                    throw new BusinessRuleException(PostingUnavailable,
                        "This delivery needs no approval and would post stock immediately; " +
                        "delivery posting is not available yet.");
                saved.Status = SalesDeliveryStatus.Pending;
                saved.ApprovalInstanceId = approval.InstanceId;
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

    private async Task LockOrderLineAsync(long companyId, long orderLineId, CancellationToken token) =>
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_order_line WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1}",
            orderLineId, companyId).ToListAsync(token);

    /// <summary>Current delivery lines plus every Posted/Pending line of the same order, by seeks only.</summary>
    private async Task<IReadOnlyList<SavedDeliveryLineQuantity>> SnapshotAsync(SalesDelivery saved,
        CancellationToken token)
    {
        var rows = await db.Database.SqlQuery<SnapshotRow>($"""
            SELECT l.id AS line_id, l.delivery_id, l.order_line_id, l.quantity, d.status, d.is_deleted
            FROM dbo.sal_delivery d WITH (FORCESEEK)
            JOIN dbo.sal_delivery_line l WITH (FORCESEEK)
              ON l.company_id = d.company_id AND l.delivery_id = d.id
            WHERE d.company_id = {saved.CompanyId} AND d.order_id = {saved.OrderId}
              AND d.status IN ('Pending', 'Posted') AND d.is_deleted = 0 AND d.id <> {saved.Id}
            UNION ALL
            SELECT l.id, l.delivery_id, l.order_line_id, l.quantity, d.status, d.is_deleted
            FROM dbo.sal_delivery d WITH (FORCESEEK)
            JOIN dbo.sal_delivery_line l WITH (FORCESEEK)
              ON l.company_id = d.company_id AND l.delivery_id = d.id
            WHERE d.company_id = {saved.CompanyId} AND d.id = {saved.Id}
            """).ToListAsync(token);
        return rows.Select(x => new SavedDeliveryLineQuantity(x.DeliveryId, x.OrderLineId, x.Quantity,
            Enum.Parse<SalesDeliveryStatus>(x.Status), x.IsDeleted, x.LineId)).ToArray();
    }

    private Task LoadLinesAsync(SalesDelivery saved, CancellationToken token) =>
        db.Set<SalesDeliveryLine>().FromSqlInterpolated($"""
            SELECT * FROM dbo.sal_delivery_line
              WITH (INDEX(ix_sal_delivery_line_company_id_delivery_id_line_no), FORCESEEK)
            WHERE company_id = {saved.CompanyId} AND delivery_id = {saved.Id}
            """).LoadAsync(token);

    private static void Verify(ApprovalState state, long documentId, params ApprovalStatus[] allowed)
    {
        if (state.DocumentType != SalesDeliveryCreateService.DocumentType || state.DocumentId != documentId ||
            state.InstanceId <= 0 || !allowed.Contains(state.Status))
            throw new ConflictException("SAL.DELIVERY.APPROVAL_RESULT_INVALID",
                "SYS returned an invalid delivery approval state.");
    }

    private static NotFoundException NotFound(long id) => new("SAL.DELIVERY.NOT_FOUND", "Sales delivery", id);

    private sealed record OrderState(SalesOrderStatus Status, string BaseCurrencyCode);

    private sealed record SnapshotRow(long LineId, long DeliveryId, long OrderLineId, decimal Quantity,
        string Status, bool IsDeleted);
}
