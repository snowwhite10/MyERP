using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>SUBMIT result: saved receipt status/version and the SYS approval instance state.</summary>
public sealed record PurchaseReceiptSubmissionResult(long Id, string DocumentNo, string Status, int Version,
    ApprovalState Approval);

/// <summary>
/// REQ_F4114.2.4 (WU-PUR-007a9, ADR-218/219): SUBMIT reserves purchase order line capacity from saved receipt rows only.
/// One ReadCommitted transaction locks the order, then the receipt, then order lines by ascending ID (UPDLOCK, HOLDLOCK),
/// re-validates aggregated quantities, increases <c>pending_receipt_reserved</c> and creates the SYS instance through the
/// shared DbContext. Any failure leaves zero changes. Zero-step auto-approval is refused until INV posting (007b) exists.
/// </summary>
public sealed class PurchaseReceiptSubmissionService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IApprovalWorkflowService workflow)
{
    public const string DocumentType = PurchaseReceiptDraftService.DocumentType;

    public async Task<PurchaseReceiptSubmissionResult> SubmitAsync(long id, int version,
        CancellationToken token = default)
    {
        if (id <= 0) throw new RequestValidationException("id", "Receipt ID must be positive.");
        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        var scope = user is not null && user.CompanyId == companyId && user.Has(PurchasingPermissions.ReceiptSubmit)
            ? user.FilterFor(PurchasingPermissions.ReceiptSubmit) : null;
        if (scope is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Receipt submit permission is required.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // The order source of a saved receipt never changes (SOURCE_LOCKED), so it can be read before locking.
            var orderId = await db.Set<PurchaseReceiptSource>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == id)
                .Select(x => (long?)x.PurchaseOrderId).SingleOrDefaultAsync(token) ?? throw NotFound(id);
            await PurchaseReceiptLocks.LockOrderAsync(db, orderId, companyId, token);
            await PurchaseReceiptLocks.LockReceiptAsync(db, id, companyId, token);

            IQueryable<PurchaseReceiptSource> query = db.Set<PurchaseReceiptSource>()
                .Where(x => x.CompanyId == companyId);
            query = scope.EffectiveScope switch
            {
                DataScope.Company => query,
                DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
                DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
                _ => query.Where(x => x.OwnerId == scope.UserId)
            };
            var receipt = await query.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw NotFound(id);
            if (receipt.DocumentNo is null || receipt.BusinessDate is null) throw NotFound(id);
            if (version < 1 || receipt.Version != version) throw new ConflictException();
            if (receipt.Status != "Draft")
                throw new BusinessRuleException("PUR.RECEIPT.NOT_SUBMITTABLE",
                    "Only draft receipts can be submitted; revise a rejected receipt first.");

            var rows = await PurchaseReceiptLocks.SavedLinesAsync(db, companyId, id, token);
            var (order, sources) = await PurchaseReceiptLocks.LockSourcesAsync(db, companyId, receipt, rows, token);
            var changes = PurchaseReceiptReservationAssembly.ForSubmit(companyId, id,
                PurchaseReceiptLocks.Snapshot(receipt, rows), order,
                sources.Values.Select(x => new PurchaseReceiptReservationSource(
                    PurchaseReceiptLocks.LineSnapshot(x), 0m)).ToArray());
            foreach (var change in changes)
                sources[change.PurchaseOrderLineId].PendingReceiptReserved = change.TargetPending;
            receipt.Status = "PendingApproval";

            decimal amount;
            try
            {
                amount = rows.Sum(x => decimal.Round(x.Quantity!.Value * x.UnitCost!.Value, 4,
                    MidpointRounding.AwayFromZero));
            }
            catch (OverflowException)
            {
                throw new BusinessRuleException("PUR.RECEIPT.COST_UNAVAILABLE", "Receipt amount exceeds precision.");
            }
            // SYS saves through the shared DbContext, so reservation and status changes commit with the instance.
            var approval = await workflow.SubmitAsync(new ApprovalSubmission(DocumentType, receipt.Id,
                receipt.DocumentNo, amount, null), token);
            if (approval.Status == ApprovalStatus.Approved)
                throw new BusinessRuleException("PUR.RECEIPT.APPROVAL_FLOW_REQUIRED",
                    "Receipt approval needs an active SYS approval flow until inbound posting is available.");
            await tx.CommitAsync(token);
            return new PurchaseReceiptSubmissionResult(receipt.Id, receipt.DocumentNo, receipt.Status,
                receipt.Version, approval);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: SqlException sql }
                && sql.Number is 2601 or 2627) throw new ConflictException();
            throw;
        }
    }

    private static NotFoundException NotFound(long id) => new("PUR.RECEIPT.NOT_FOUND", "Purchase receipt", id);
}

/// <summary>
/// REQ_F4114.2.4～5 (WU-PUR-007a9): SYS final decision for a receipt in SYS's transaction. Rejection releases exactly this
/// receipt's saved quantities with the SUBMIT lock order; a stale or repeated decision returns 409 without a second release.
/// Final approval must post INV inbound (WU-PUR-007b); until then it is refused so stock is never approved unposted.
/// </summary>
public sealed class PurchaseReceiptApprovalResultHandler(MyErpDbContext db) : IApprovalResultHandler
{
    public const string StateChanged = "PUR.RECEIPT.APPROVAL_STATE_CHANGED";

    public string DocumentType => PurchaseReceiptSubmissionService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));

        var head = await db.Set<PurchaseReceiptSource>().AsNoTracking().Where(x => x.Id == result.DocumentId)
            .Select(x => new { x.CompanyId, x.PurchaseOrderId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("PUR.RECEIPT.NOT_FOUND", "Purchase receipt", result.DocumentId);
        await PurchaseReceiptLocks.LockOrderAsync(db, head.PurchaseOrderId, head.CompanyId, cancellationToken);
        await PurchaseReceiptLocks.LockReceiptAsync(db, result.DocumentId, head.CompanyId, cancellationToken);
        var receipt = await db.Set<PurchaseReceiptSource>()
            .SingleAsync(x => x.Id == result.DocumentId && x.CompanyId == head.CompanyId, cancellationToken);
        if (receipt.Status != "PendingApproval" || receipt.BusinessDate is null)
            throw new ConflictException(StateChanged, "The receipt is no longer waiting for approval.");

        if (result.Status == ApprovalStatus.Approved)
            throw new BusinessRuleException("PUR.RECEIPT.POSTING_NOT_AVAILABLE",
                "Receipt final approval with inbound stock posting is not available yet.");

        var rows = await PurchaseReceiptLocks.SavedLinesAsync(db, receipt.CompanyId, receipt.Id, cancellationToken);
        var (order, sources) = await PurchaseReceiptLocks.LockSourcesAsync(db, receipt.CompanyId, receipt, rows,
            cancellationToken);
        var own = rows.GroupBy(x => x.PurchaseOrderLineId).ToDictionary(x => x.Key, x => x.Sum(r => r.Quantity!.Value));
        var changes = PurchaseReceiptReservationAssembly.ForReject(receipt.CompanyId, receipt.Id,
            PurchaseReceiptLocks.Snapshot(receipt, rows), order,
            sources.Values.Select(x => new PurchaseReceiptReservationSource(PurchaseReceiptLocks.LineSnapshot(x),
                own.GetValueOrDefault(x.Id))).ToArray());
        foreach (var change in changes)
            sources[change.PurchaseOrderLineId].PendingReceiptReserved = change.TargetPending;
        receipt.Status = "Rejected";
    }
}

/// <summary>Shared lock order for receipt SUBMIT and SYS decisions: order → receipt → order lines ascending.</summary>
internal static class PurchaseReceiptLocks
{
    public static Task LockOrderAsync(MyErpDbContext db, long orderId, long companyId, CancellationToken token) =>
        db.Database.SqlQuery<long>($"SELECT [id] AS [Value] FROM dbo.pur_purchase_order WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {orderId} AND [company_id] = {companyId}").ToListAsync(token);

    public static Task LockReceiptAsync(MyErpDbContext db, long receiptId, long companyId, CancellationToken token) =>
        db.Database.SqlQuery<long>($"SELECT [id] AS [Value] FROM dbo.pur_purchase_receipt WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {receiptId} AND [company_id] = {companyId}").ToListAsync(token);

    public static Task LockOrderLineAsync(MyErpDbContext db, long lineId, long companyId, CancellationToken token) =>
        db.Database.SqlQuery<long>($"SELECT [id] AS [Value] FROM dbo.pur_purchase_order_line WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {lineId} AND [company_id] = {companyId}").ToListAsync(token);

    public static Task<PurchaseReceiptSourceLine[]> SavedLinesAsync(MyErpDbContext db, long companyId, long receiptId,
        CancellationToken token) => db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
        .Where(x => x.CompanyId == companyId && x.PurchaseReceiptId == receiptId)
        .OrderBy(x => x.LineNo).ToArrayAsync(token);

    /// <summary>Locks referenced order lines one by one in ascending ID order and loads them tracked.</summary>
    public static async Task<(PurchaseReceiptOrderSnapshot Order, Dictionary<long, PurchaseOrderSourceLine> Lines)>
        LockSourcesAsync(MyErpDbContext db, long companyId, PurchaseReceiptSource receipt,
            IReadOnlyList<PurchaseReceiptSourceLine> rows, CancellationToken token)
    {
        if (rows.Count == 0 || rows.Any(x => x.Quantity is null || x.UnitCost is null || x.WarehouseId is null))
            throw new BusinessRuleException("PUR.RECEIPT.INVALID_LINES", "Receipt lines have invalid numbers or quantities.");
        var order = await db.Set<PurchaseOrderSource>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Id == receipt.PurchaseOrderId)
            .Select(x => new PurchaseReceiptOrderSnapshot(x.Id, x.CompanyId, x.Status, x.IsDeleted))
            .SingleOrDefaultAsync(token)
            ?? throw new BusinessRuleException("PUR.RECEIPT.INVALID_SOURCE", "Purchase order does not exist.");
        var ids = rows.Select(x => x.PurchaseOrderLineId).Distinct().Order().ToArray();
        foreach (var sourceId in ids)
            await LockOrderLineAsync(db, sourceId, companyId, token);
        var lines = await db.Set<PurchaseOrderSourceLine>()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
        return (order, lines);
    }

    public static PurchaseReceiptApprovalSnapshot Snapshot(PurchaseReceiptSource receipt,
        IReadOnlyList<PurchaseReceiptSourceLine> rows) => new(receipt.Id, receipt.CompanyId,
        receipt.PurchaseOrderId, receipt.Status, receipt.BusinessDate!.Value,
        rows.Select(x => new PurchaseReceiptDraftLine(x.LineNo, x.PurchaseOrderLineId, x.Quantity!.Value)).ToArray());

    public static PurchaseReceiptOrderLineSnapshot LineSnapshot(PurchaseOrderSourceLine x) => new(x.Id,
        x.PurchaseOrderId, x.CompanyId, false, x.Quantity, x.PostedReceived, x.PendingReceiptReserved);
}
