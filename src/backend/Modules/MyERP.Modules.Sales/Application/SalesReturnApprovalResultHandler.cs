using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// WU-SD-008a5b (REQ_F4112.7.1, BR-SD-043/044/047/049, ADR-191): applies the final SYS decision to a Pending sales return
/// inside the SYS approval transaction.
/// <list type="bullet">
/// <item>Rejected: Pending → Rejected; the saved lines and SYS link stay (nothing was reserved, INV and FI are never
/// called). The Draft command service revises a Rejected return back to Draft.</item>
/// <item>Approved: the same lock order as SUBMIT (BR-SD-047) — FI customer credit key → order row → return row → delivery
/// lines → invoice lines — then <see cref="SalesReturnPosting.RecheckAsync"/> (409 SNAPSHOT_STALE when balances moved;
/// the approver should reject so the user can revise) and, under the MD destination guards,
/// <see cref="SalesReturnPosting.PostAsync"/> (INV inbound, FI credit of billed portions, commitment release, Posted).</item>
/// </list>
/// Any refusal throws, so SYS rolls its decision back together with SD, INV and FI and the return stays Pending.
/// The posting collaborators are resolved only when a decision arrives: INV, FI and MD services must not be built with
/// the SYS workflow (DI cycle, ADR-142), as in <see cref="SalesDeliveryApprovalResultHandler"/>.
/// </summary>
public sealed class SalesReturnApprovalResultHandler(MyErpDbContext db, Func<SalesReturnPosting> postingFactory,
    Func<SalesReturnDraftBuilder> builderFactory, Func<ICustomerCreditService> creditFactory) : IApprovalResultHandler
{
    public const string ApprovalStateChanged = "SAL.RETURN.APPROVAL_STATE_CHANGED";

    public string DocumentType => SalesReturnCreateService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A caller-owned approval transaction is required.");

        if (result.Status == ApprovalStatus.Rejected)
        {
            var rejected = await LoadPendingAsync(result, cancellationToken);
            rejected.Status = SalesReturnStatus.Rejected;
            return;
        }

        var posting = postingFactory();
        var builder = builderFactory();
        // Order and customer of a saved return never change (trigger 51035), so this unlocked read only picks locks.
        var header = await db.Set<SalesReturn>().AsNoTracking().Where(x => x.Id == result.DocumentId)
            .Select(x => new { x.CompanyId, x.OrderId, x.CustomerId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound(result.DocumentId);
        await creditFactory().AcquireCreditLockAsync(header.CompanyId, header.CustomerId,
            transaction.GetDbTransaction(), cancellationToken);
        await builder.LockOrderAsync(header.CompanyId, header.OrderId, cancellationToken);
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_return WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1}",
            result.DocumentId, header.CompanyId).ToListAsync(cancellationToken);

        var saved = await LoadPendingAsync(result, cancellationToken);
        // The approver acts through SYS authority, so the order is read without the SD action scope.
        var order = await db.Set<SalesOrder>().AsNoTracking().Where(x => x.Id == saved.OrderId)
            .Include(x => x.Lines).AsSplitQuery().SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound(result.DocumentId);
        await posting.RecheckAsync(saved, order, cancellationToken);
        await builder.GuardDestinationsAsync(saved.Lines, () => posting.PostAsync(saved, cancellationToken),
            cancellationToken);
    }

    private async Task<SalesReturn> LoadPendingAsync(ApprovalCompleted result, CancellationToken token)
    {
        var document = await db.Set<SalesReturn>().AsSplitQuery()
            .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations)
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId && !x.IsDeleted, token)
            ?? throw NotFound(result.DocumentId);
        if (document.Status != SalesReturnStatus.Pending || document.ApprovalInstanceId != result.InstanceId)
            throw new ConflictException(ApprovalStateChanged,
                "The sales return is no longer waiting for this approval instance.");
        return document;
    }

    private static NotFoundException NotFound(long id) =>
        new(SalesReturnDraftCommandService.NotFoundCode, "Sales return", id);
}
