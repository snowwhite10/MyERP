using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Return SUBMIT result: saved status/version and the SYS approval instance state.</summary>
public sealed record PurchaseReturnSubmissionResult(long Id, string DocumentNo, string Status, int Version,
    ApprovalState Approval);

/// <summary>
/// REQ_F4114.7.3 (WU-PUR-007c4, ADR-225): SUBMIT of a Draft purchase return. One Read Committed transaction locks the
/// purchase order, the return, then the referenced receipt lines in ascending ID (ADR-224 order), rechecks the
/// returnable quantity (BR-PUR-032) and the uninvoiced limit with no own reservation (BR-PUR-044), requires complete
/// lot/serial capture for tracked items, adds this return's quantity to <c>pending_return_reserved</c> and creates the
/// SYS instance through the shared DbContext. A zero-step approval posts in the same transaction through
/// <see cref="PurchaseReturnPosting"/>. Any refusal leaves zero changes.
/// </summary>
public sealed class PurchaseReturnSubmissionService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IApprovalWorkflowService workflow, PurchaseReturnPosting posting,
    IInventoryReferenceGuard references)
{
    public const string NotSubmittable = "PUR.RETURN.NOT_SUBMITTABLE";
    public const string TrackingRequired = "PUR.RETURN.TRACKING_REQUIRED";

    public async Task<PurchaseReturnSubmissionResult> SubmitAsync(long id, int version,
        CancellationToken token = default)
    {
        if (id <= 0) throw new RequestValidationException("id", "Return ID must be positive.");
        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        var scope = user is not null && user.CompanyId == companyId && user.Has(PurchasingPermissions.ReturnSubmit)
            ? user.FilterFor(PurchasingPermissions.ReturnSubmit) : null;
        if (scope is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Return submit permission is required.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // The purchase order of a saved return never changes (SOURCE_LOCKED), so it can be read before locking.
            var orderId = await db.Set<PurchaseReturn>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == id)
                .Select(x => (long?)x.PurchaseOrderId).SingleOrDefaultAsync(token) ?? throw NotFound(id);
            await PurchaseReturnLocks.LockOrderAsync(db, orderId, companyId, token);
            await PurchaseReturnLocks.LockReturnAsync(db, id, companyId, token);

            IQueryable<PurchaseReturn> query = db.Set<PurchaseReturn>().Where(x => x.CompanyId == companyId);
            query = scope.EffectiveScope switch
            {
                DataScope.Company => query,
                DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
                DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
                _ => query.Where(x => x.OwnerId == scope.UserId)
            };
            var header = await query.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw NotFound(id);
            if (version < 1 || header.Version != version) throw new ConflictException();
            if (header.Status != "Draft")
                throw new BusinessRuleException(NotSubmittable,
                    "Only draft returns can be submitted; revise a rejected return first.");

            var rows = await PurchaseReturnSources.SavedLinesAsync(db, companyId, id, token);
            var sources = await PurchaseReturnSources.LockAsync(db, companyId, header.PurchaseOrderId, rows, token);
            PurchaseReturnSources.EnsureReturnable(rows, sources, ownReserved: false);
            await CheckTrackingCompleteAsync(companyId, rows, token);

            foreach (var group in rows.GroupBy(x => x.ReceiptLineId))
                sources[group.Key].PendingReturnReserved += group.Sum(x => x.Quantity);
            header.Status = "PendingApproval";

            decimal amount;
            try
            {
                amount = rows.Sum(x => decimal.Round(x.Quantity * sources[x.ReceiptLineId].UnitCost!.Value, 4,
                    MidpointRounding.AwayFromZero));
            }
            catch (OverflowException)
            {
                throw new BusinessRuleException("PUR.RETURN.COST_UNAVAILABLE", "Return amount exceeds precision.");
            }
            // SYS saves through the shared DbContext, so the reservation and status commit with the instance.
            var approval = await workflow.SubmitAsync(new ApprovalSubmission(PurchaseReturnDraftService.DocumentType,
                header.Id, header.DocumentNo, amount, null), token);
            // SYS does not call result handlers for a zero-step approval, so SUBMIT posts the return itself.
            if (approval.Status == ApprovalStatus.Approved)
                await posting.PostAsync(header, token);
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            return new PurchaseReturnSubmissionResult(header.Id, header.DocumentNo, header.Status, header.Version,
                approval);
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

    /// <summary>
    /// A tracked item's line must carry lot/serial entries totalling its quantity before approval starts (the Draft
    /// already proved each number came from the receipt capture). The MD shared lock is held to the commit.
    /// </summary>
    private async Task CheckTrackingCompleteAsync(long companyId, IReadOnlyList<PurchaseReturnLine> rows,
        CancellationToken token)
    {
        var lineIds = rows.Select(x => x.Id).ToArray();
        var captured = (await db.Set<PurchaseReturnLineTracking>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)).ToArrayAsync(token))
            .GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var incomplete = new List<int>();
        foreach (var set in rows.GroupBy(x => (x.MaterialId, x.WarehouseId))
                     .OrderBy(x => x.Key.MaterialId).ThenBy(x => x.Key.WarehouseId))
        {
            var mode = await references.RunWithCodesAsync(set.Key.MaterialId, set.Key.WarehouseId, null,
                codes => Task.FromResult(codes.TrackingMode), token);
            if (mode == ItemTrackingMode.None) continue;
            incomplete.AddRange(set.Where(x => captured.GetValueOrDefault(x.Id) != x.Quantity).Select(x => x.LineNo));
        }
        PurchaseReturnSources.Refuse(TrackingRequired,
            "Enter lot or serial numbers totalling the line quantity before submission.", incomplete);
    }

    private static NotFoundException NotFound(long id) => new(PurchaseReturnDraftService.NotFoundCode, "Purchase return", id);
}

/// <summary>
/// REQ_F4114.7.3 / BR-PUR-030／033 (WU-PUR-007c4, ADR-225): the final-approval posting step shared by zero-step SUBMIT
/// and the SYS result handler. The caller owns the transaction and holds order → return locks; this step locks the
/// receipt lines ascending, rechecks the limits excluding this return's own reservation, moves the reservation to
/// <c>posted_returned</c>, issues the saved lines through INV <c>PostPurchaseReturnAsync</c> with the saved original
/// movement and lots/serials, stores the INV <c>OriginalUnitCost</c> on each line and marks the return Posted.
/// Any refusal throws so SYS, PUR and INV roll back together.
/// </summary>
public sealed class PurchaseReturnPosting(MyErpDbContext db, IExternalInventoryPostingService inventory)
{
    public const string PostingConflict = "PUR.RETURN.POSTING_CONFLICT";

    public async Task PostAsync(PurchaseReturn header, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned return transaction is required.");
        if (header.Status != "PendingApproval")
            throw new ConflictException(PurchaseReturnApprovalResultHandler.StateChanged,
                "The return is no longer waiting for approval.");

        var rows = await db.Set<PurchaseReturnLine>()
            .Where(x => x.CompanyId == header.CompanyId && x.PurchaseReturnId == header.Id)
            .OrderBy(x => x.LineNo).ToListAsync(token);
        var sources = await PurchaseReturnSources.LockAsync(db, header.CompanyId, header.PurchaseOrderId, rows, token);
        PurchaseReturnSources.EnsureReturnable(rows, sources, ownReserved: true);
        foreach (var group in rows.GroupBy(x => x.ReceiptLineId))
        {
            var own = group.Sum(x => x.Quantity);
            sources[group.Key].PendingReturnReserved -= own;
            sources[group.Key].PostedReturned += own;
        }

        var lineIds = rows.Select(x => x.Id).ToArray();
        var tracking = (await db.Set<PurchaseReturnLineTracking>().AsNoTracking()
                .Where(x => x.CompanyId == header.CompanyId && lineIds.Contains(x.LineId)).ToArrayAsync(token))
            .GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SeqNo)
                .Select(x => x.SerialNo is { } serial ? InventoryTrackingLine.Serial(serial)
                    : InventoryTrackingLine.Lot(x.LotNo!, x.Quantity)).ToArray());
        var lines = rows.Select(x => new PurchaseReturnOutboundLine(x.LineNo, x.OriginalMovementId, x.MaterialId,
            x.WarehouseId, x.LocationId, x.Quantity, tracking.GetValueOrDefault(x.Id))).ToArray();

        var result = await inventory.PostPurchaseReturnAsync(header.Id, header.BusinessDate, lines, token);
        // A replay means INV already issued stock for this return while PUR never recorded it: refuse, never re-count.
        if (result is null || result.Replayed || result.Movements.Count != lines.Length)
            throw new ConflictException(PostingConflict,
                "Inventory already holds a posting for this return; it cannot be posted again.");
        var costs = result.Movements.ToDictionary(x => x.LineNo);
        foreach (var row in rows)
            row.OriginalUnitCost = costs[row.LineNo].OriginalUnitCost;
        // Lines first: the Posted-immutability trigger refuses line updates once the header is Posted.
        await db.SaveChangesAsync(token);
        header.Status = "Posted";
        await db.SaveChangesAsync(token);
    }
}

/// <summary>
/// REQ_F4114.7.3 (WU-PUR-007c4): SYS final decision for a purchase return in SYS's transaction. Approval posts through
/// <see cref="PurchaseReturnPosting"/> (resolved lazily: INV depends on MD services, ADR-220); rejection releases exactly
/// this return's reservation under the SUBMIT lock order. A stale or repeated decision is 409 without a second effect.
/// </summary>
public sealed class PurchaseReturnApprovalResultHandler(MyErpDbContext db, Func<PurchaseReturnPosting> postingFactory)
    : IApprovalResultHandler
{
    public const string StateChanged = "PUR.RETURN.APPROVAL_STATE_CHANGED";

    public string DocumentType => PurchaseReturnDraftService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));

        var head = await db.Set<PurchaseReturn>().AsNoTracking().Where(x => x.Id == result.DocumentId)
            .Select(x => new { x.CompanyId, x.PurchaseOrderId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(PurchaseReturnDraftService.NotFoundCode, "Purchase return", result.DocumentId);
        await PurchaseReturnLocks.LockOrderAsync(db, head.PurchaseOrderId, head.CompanyId, cancellationToken);
        await PurchaseReturnLocks.LockReturnAsync(db, result.DocumentId, head.CompanyId, cancellationToken);
        var header = await db.Set<PurchaseReturn>()
            .SingleAsync(x => x.Id == result.DocumentId && x.CompanyId == head.CompanyId, cancellationToken);
        if (header.Status != "PendingApproval")
            throw new ConflictException(StateChanged, "The return is no longer waiting for approval.");

        if (result.Status == ApprovalStatus.Approved)
        {
            await postingFactory().PostAsync(header, cancellationToken);
            return;
        }

        var rows = await PurchaseReturnSources.SavedLinesAsync(db, header.CompanyId, header.Id, cancellationToken);
        var sources = await PurchaseReturnSources.LockAsync(db, header.CompanyId, header.PurchaseOrderId, rows,
            cancellationToken);
        foreach (var group in rows.GroupBy(x => x.ReceiptLineId))
        {
            var own = group.Sum(x => x.Quantity);
            if (sources[group.Key].PendingReturnReserved < own)
                throw new BusinessRuleException(PurchaseReturnInvoicedLimit.InvalidSourceCode,
                    "Receipt quantity snapshot is inconsistent.");
            sources[group.Key].PendingReturnReserved -= own;
        }
        header.Status = "Rejected";
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Receipt-line locking and limit checks shared by return SUBMIT, posting and rejection (ADR-224/225).</summary>
internal static class PurchaseReturnSources
{
    public static async Task<PurchaseReturnLine[]> SavedLinesAsync(MyErpDbContext db, long companyId, long returnId,
        CancellationToken token)
    {
        var rows = await db.Set<PurchaseReturnLine>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PurchaseReturnId == returnId)
            .OrderBy(x => x.LineNo).ToArrayAsync(token);
        if (rows.Length == 0)
            throw new BusinessRuleException("PUR.RETURN.INVALID_LINES", "The return has no lines.");
        return rows;
    }

    /// <summary>
    /// Locks the referenced receipt lines one by one in ascending ID and loads them tracked; each must still belong to
    /// a Posted receipt of the return's purchase order (422 <c>PUR.RETURN.INVALID_SOURCE</c> with <c>lineNos</c>).
    /// </summary>
    public static async Task<Dictionary<long, PurchaseReceiptSourceLine>> LockAsync(MyErpDbContext db, long companyId,
        long orderId, IReadOnlyList<PurchaseReturnLine> rows, CancellationToken token)
    {
        var ids = rows.Select(x => x.ReceiptLineId).Distinct().Order().ToArray();
        foreach (var lineId in ids) await PurchaseReturnLocks.LockReceiptLineAsync(db, lineId, companyId, token);
        // Tracked lines only: AsNoTracking on any joined set would make the whole query untracked.
        var sources = await db.Set<PurchaseReceiptSourceLine>()
            .Where(l => l.CompanyId == companyId && ids.Contains(l.Id) && l.Quantity != null && l.UnitCost != null
                && db.Set<PurchaseReceiptSource>().Any(r => r.CompanyId == l.CompanyId && r.Id == l.PurchaseReceiptId
                    && r.Status == "Posted" && r.PurchaseOrderId == orderId))
            .ToDictionaryAsync(x => x.Id, token);
        Refuse(PurchaseReturnDraftService.InvalidSource,
            "Return lines must reference posted receipt lines of the same purchase order.",
            rows.Where(x => !sources.ContainsKey(x.ReceiptLineId)).Select(x => x.LineNo));
        return sources;
    }

    /// <summary>
    /// BR-PUR-032 then BR-PUR-044 per receipt line (lines aggregated). With <paramref name="ownReserved"/> (approval)
    /// this return's quantity is already inside <c>pending_return_reserved</c> and is excluded once.
    /// </summary>
    public static void EnsureReturnable(IReadOnlyList<PurchaseReturnLine> rows,
        IReadOnlyDictionary<long, PurchaseReceiptSourceLine> sources, bool ownReserved)
    {
        var groups = rows.GroupBy(x => x.ReceiptLineId).OrderBy(x => x.Key)
            .Select(g => (Id: g.Key, Lines: g.ToArray(), Quantity: g.Sum(x => x.Quantity), Source: sources[g.Key]))
            .ToArray();
        decimal Own((long Id, PurchaseReturnLine[] Lines, decimal Quantity, PurchaseReceiptSourceLine Source) g) =>
            ownReserved ? g.Quantity : 0m;

        var over = groups.Where(g => g.Quantity > g.Source.PostedReceived - g.Source.PostedReturned
            - (g.Source.PendingReturnReserved - Own(g))).ToArray();
        if (over.Length > 0)
            throw new BusinessRuleException(PurchaseReturnDraftService.QuantityExceeded,
                "Return quantity exceeds the returnable quantity of the receipt line: " + string.Join("; ", over.Select(g =>
                    $"receipt line {g.Source.LineNo} returnable {Returnable(g.Source, Own(g)):0.######}")))
            {
                Extensions = Extensions(over.SelectMany(g => g.Lines).Select(x => x.LineNo), over.Select(g =>
                    new Dictionary<string, object?>
                    {
                        ["receiptLineId"] = g.Id, ["requested"] = g.Quantity, ["returnable"] = Returnable(g.Source, Own(g))
                    }).ToArray())
            };

        var decision = PurchaseReturnInvoicedLimit.Check(
            groups.Select(g => new PurchaseReturnReceiptRequest(g.Id, g.Quantity)).ToArray(),
            groups.Select(g => new PurchaseReturnReceiptSource(g.Id, new PurchaseReturnSourceQuantities(
                g.Source.PostedReceived, g.Source.PostedReturned, g.Source.PendingReturnReserved, g.Source.PostedInvoiced,
                g.Source.MatchedReserved, Own(g)))).ToArray());
        if (decision.CanReturn) return;
        var shortIds = decision.Shortages.Select(x => x.ReceiptLineId).ToHashSet();
        throw new BusinessRuleException(PurchaseReturnInvoicedLimit.ExceedsUninvoiced,
            "Return exceeds the uninvoiced quantity; use an FI credit note for invoiced quantity (" + string.Join("; ",
                decision.Shortages.Select(x => $"receipt line {sources[x.ReceiptLineId].LineNo}: requested " +
                    $"{x.Requested:0.######}, returnable {x.Available:0.######}, invoiced or matched " +
                    $"{x.InvoicedOrMatched:0.######}")) + ").")
        {
            Extensions = Extensions(groups.Where(g => shortIds.Contains(g.Id)).SelectMany(g => g.Lines)
                .Select(x => x.LineNo), decision.Shortages.Select(x => new Dictionary<string, object?>
                {
                    ["receiptLineId"] = x.ReceiptLineId, ["requested"] = x.Requested, ["returnable"] = x.Available,
                    ["invoicedOrMatched"] = x.InvoicedOrMatched
                }).ToArray())
        };
    }

    public static void Refuse(string code, string message, IEnumerable<int> lineNos)
    {
        var numbers = lineNos.Distinct().Order().ToArray();
        if (numbers.Length > 0)
            throw new BusinessRuleException(code, message) { Extensions = Extensions(numbers, null) };
    }

    private static decimal Returnable(PurchaseReceiptSourceLine line, decimal own) =>
        line.PostedReceived - line.PostedReturned - (line.PendingReturnReserved - own);

    private static Dictionary<string, object?> Extensions(IEnumerable<int> lineNos, object? details)
    {
        var numbers = lineNos.Distinct().Order().ToArray();
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
            { ["lineNo"] = numbers[0], ["lineNos"] = numbers };
        if (details is not null) result["receiptLines"] = details;
        return result;
    }
}
