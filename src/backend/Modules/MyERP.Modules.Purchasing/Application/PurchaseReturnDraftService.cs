using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// REQ_F4114.7.1～2 (WU-PUR-007c3, ADR-224): purchase return drafts. One Read Committed transaction locks the purchase
/// order, then the referenced receipt lines in ascending ID (the order SUBMIT in 007c4 reuses), rechecks that each line
/// belongs to a Posted receipt of the same order with enough returnable quantity, resolves the original INV
/// <c>PURCHASE_INBOUND</c> movement from INV evidence and checks lots/serials against the saved receipt capture.
/// Drafts reserve nothing and never write INV; any refused line leaves zero changes.
/// </summary>
public sealed class PurchaseReturnDraftService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, INumberGenerator numbers, IInventoryReferenceGuard references,
    IPurchaseInboundEvidenceQuery evidence, IClock clock)
{
    public const string DocumentType = "PUR.RETURN";
    public const string InvalidSource = "PUR.RETURN.INVALID_SOURCE";
    public const string QuantityExceeded = "PUR.RETURN.QUANTITY_EXCEEDED";
    public const string EvidenceMissing = "PUR.RETURN.SOURCE_EVIDENCE_MISSING";
    public const string TrackingNotFromReceipt = "PUR.RETURN.TRACKING_NOT_FROM_RECEIPT";
    public const string SourceLocked = "PUR.RETURN.SOURCE_LOCKED";
    public const string NotDeletable = "PUR.RETURN.NOT_DELETABLE";
    public const string NotFoundCode = "PUR.RETURN.NOT_FOUND";

    public async Task<PurchaseReturnReadDetail> CreateAsync(PurchaseReturnSaveInput input,
        CancellationToken token = default)
    {
        var lines = PurchaseReturnDraftInput.Validate(input);
        var companyId = access.RequireCompanyId();
        var user = await RequireScopeAsync(companyId, PurchasingPermissions.ReturnCreate, token);
        long id;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token))
        {
            try
            {
                await PurchaseReturnLocks.LockOrderAsync(db, input.PurchaseOrderId, companyId, token);
                var plan = await PlanAsync(companyId, input.PurchaseOrderId, lines, token);
                var header = new PurchaseReturn
                {
                    CompanyId = companyId, PurchaseOrderId = input.PurchaseOrderId, Status = "Draft",
                    DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token),
                    BusinessDate = input.BusinessDate,
                    OwnerId = user.UserId, BranchId = user.BranchId, DepartmentId = user.DepartmentId
                };
                db.Add(header);
                await db.SaveChangesAsync(token);
                var rows = plan.Select(x => Apply(new PurchaseReturnLine
                    { CompanyId = companyId, PurchaseReturnId = header.Id, LineNo = x.Line.LineNumber }, x)).ToArray();
                db.AddRange(rows);
                await db.SaveChangesAsync(token);
                await SaveTrackingAsync(companyId, rows, plan, token);
                await tx.CommitAsync(token);
                id = header.Id;
            }
            catch (Exception ex) { await RollbackAsync(tx, ex); throw; }
        }
        return await LoadAsync(companyId, id, token);
    }

    public async Task<PurchaseReturnReadDetail> UpdateAsync(long id, int version, PurchaseReturnSaveInput input,
        CancellationToken token = default)
    {
        if (id <= 0) throw new RequestValidationException("id", "Return ID must be positive.");
        var lines = PurchaseReturnDraftInput.Validate(input);
        var companyId = access.RequireCompanyId();
        var user = await RequireScopeAsync(companyId, PurchasingPermissions.ReturnEdit, token);
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token))
        {
            try
            {
                // Order → return → receipt lines ascending, the same order as SUBMIT and SYS decisions (007c4).
                await PurchaseReturnLocks.LockOrderAsync(db, input.PurchaseOrderId, companyId, token);
                await PurchaseReturnLocks.LockReturnAsync(db, id, companyId, token);
                var header = await ApplyScope(db.Set<PurchaseReturn>().Where(x => x.CompanyId == companyId), user,
                        PurchasingPermissions.ReturnEdit)
                    .SingleOrDefaultAsync(x => x.Id == id, token) ?? throw NotFound(id);
                if (version < 1 || header.Version != version) throw new ConflictException();
                PurchaseReturnDraftInput.EnsureEditable(header.Status);
                if (header.PurchaseOrderId != input.PurchaseOrderId)
                    throw new BusinessRuleException(SourceLocked, "The purchase order source cannot be changed.");
                var plan = await PlanAsync(companyId, input.PurchaseOrderId, lines, token);

                var old = await db.Set<PurchaseReturnLine>()
                    .Where(x => x.CompanyId == companyId && x.PurchaseReturnId == id).ToArrayAsync(token);
                var oldIds = old.Select(x => x.Id).ToArray();
                // Lot/serial capture is replaced as a whole; kept lines retain their IDs by line number.
                await db.Set<PurchaseReturnLineTracking>()
                    .Where(x => x.CompanyId == companyId && oldIds.Contains(x.LineId)).ExecuteDeleteAsync(token);
                var incoming = plan.ToDictionary(x => x.Line.LineNumber);
                db.RemoveRange(old.Where(x => !incoming.ContainsKey(x.LineNo)));
                var kept = old.Where(x => incoming.ContainsKey(x.LineNo)).ToDictionary(x => x.LineNo);
                var rows = plan.Select(x => Apply(kept.GetValueOrDefault(x.Line.LineNumber) ?? new PurchaseReturnLine
                    { CompanyId = companyId, PurchaseReturnId = id, LineNo = x.Line.LineNumber }, x)).ToArray();
                await db.SaveChangesAsync(token);
                db.AddRange(rows.Where(x => x.Id == 0));
                header.BusinessDate = input.BusinessDate;
                header.Status = "Draft";
                db.Entry(header).Property(x => x.BusinessDate).IsModified = true; // always bumps the version
                await db.SaveChangesAsync(token);
                await SaveTrackingAsync(companyId, rows, plan, token);
                await tx.CommitAsync(token);
            }
            catch (Exception ex) { await RollbackAsync(tx, ex); throw; }
        }
        return await LoadAsync(companyId, id, token);
    }

    /// <summary>Version-checked soft delete of a Draft in the DELETE scope; the number is never reused.</summary>
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        if (id <= 0) throw new RequestValidationException("id", "Return ID must be positive.");
        var companyId = access.RequireCompanyId();
        var user = await RequireScopeAsync(companyId, PurchasingPermissions.ReturnDelete, token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await PurchaseReturnLocks.LockReturnAsync(db, id, companyId, token);
            var header = await ApplyScope(db.Set<PurchaseReturn>().Where(x => x.CompanyId == companyId), user,
                    PurchasingPermissions.ReturnDelete)
                .SingleOrDefaultAsync(x => x.Id == id, token) ?? throw NotFound(id);
            if (version < 1 || header.Version != version) throw new ConflictException();
            if (header.Status != "Draft")
                throw new BusinessRuleException(NotDeletable,
                    "Only draft returns can be deleted; revise a rejected return back to draft first.");
            header.IsDeleted = true;
            header.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }
        catch (Exception ex) { await RollbackAsync(tx, ex); throw; }
    }

    public Task<PurchaseReturnReadDetail> GetAsync(long id, CancellationToken token = default) =>
        GetAsync(id, PurchasingPermissions.ReturnView, token);

    public Task<PurchaseReturnReadDetail> GetForEditAsync(long id, CancellationToken token = default) =>
        GetAsync(id, PurchasingPermissions.ReturnEdit, token);

    /// <summary>
    /// Posted receipt lines of one purchase order with returnable quantity, for CREATE or EDIT; at most 1000 lines by
    /// receipt date, receipt and line number. Availability is advisory; saving rechecks it under lock.
    /// </summary>
    public async Task<IReadOnlyList<PurchaseReturnSourceLine>> SourceLinesAsync(long purchaseOrderId,
        CancellationToken token = default)
    {
        if (purchaseOrderId <= 0)
            throw new RequestValidationException("purchaseOrderId", "Purchase order ID must be positive.");
        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        if (user is null || user.CompanyId != companyId ||
            !(user.Has(PurchasingPermissions.ReturnCreate) || user.Has(PurchasingPermissions.ReturnEdit)))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Return create or edit permission is required.");
        var rows = await (from l in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
                join r in db.Set<PurchaseReceiptSource>().AsNoTracking()
                    on new { l.CompanyId, Id = l.PurchaseReceiptId } equals new { r.CompanyId, r.Id }
                where l.CompanyId == companyId && r.PurchaseOrderId == purchaseOrderId && r.Status == "Posted"
                    && r.DocumentNo != null && r.BusinessDate != null && l.Quantity != null && l.WarehouseId != null
                    && l.UnitCost != null && l.PostedReceived - l.PostedReturned - l.PendingReturnReserved > 0
                orderby r.BusinessDate, r.Id, l.LineNo
                select new { Line = l, ReceiptNo = r.DocumentNo!, ReceiptDate = r.BusinessDate!.Value })
            .Take(PurchaseReturnDraftInput.MaxLines).ToArrayAsync(token);
        var captured = await ReceiptTrackingAsync(companyId, rows.Select(x => x.Line.Id).ToArray(), token);
        return rows.Select(x => new PurchaseReturnSourceLine(x.Line.PurchaseReceiptId, x.ReceiptNo, x.ReceiptDate,
            x.Line.Id, x.Line.LineNo, x.Line.MaterialId, x.Line.WarehouseId!.Value, x.Line.PostedReceived,
            x.Line.PostedReturned, x.Line.PendingReturnReserved, Returnable(x.Line), x.Line.UnitCost!.Value,
            captured.GetValueOrDefault(x.Line.Id, []).Select((t, i) =>
                new PurchaseReturnTrackingRead(i + 1, t.LotNo, t.SerialNo, t.Quantity)).ToArray())).ToArray();
    }

    // ------------------------------------------------------------------ planning

    private sealed record PlannedLine(PurchaseReturnSaveLine Line, PurchaseReceiptSourceLine Source,
        long OriginalMovementId, long WarehouseId);

    /// <summary>Caller holds the order lock; locks receipt lines ascending and refuses the whole draft on any bad line.</summary>
    private async Task<PlannedLine[]> PlanAsync(long companyId, long orderId, IReadOnlyList<PurchaseReturnSaveLine> lines,
        CancellationToken token)
    {
        if (!await db.Set<PurchaseOrderSource>().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == orderId, token))
            throw new BusinessRuleException(InvalidSource, "Purchase order does not exist.");
        var ids = lines.Select(x => x.ReceiptLineId).Distinct().Order().ToArray();
        foreach (var lineId in ids) await PurchaseReturnLocks.LockReceiptLineAsync(db, lineId, companyId, token);
        var sources = await (from l in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
                join r in db.Set<PurchaseReceiptSource>().AsNoTracking()
                    on new { l.CompanyId, Id = l.PurchaseReceiptId } equals new { r.CompanyId, r.Id }
                where l.CompanyId == companyId && ids.Contains(l.Id) && r.Status == "Posted"
                    && r.PurchaseOrderId == orderId && r.DocumentNo != null
                    && l.Quantity != null && l.WarehouseId != null && l.UnitCost != null
                select l).ToDictionaryAsync(x => x.Id, token);
        Refuse(InvalidSource, "Return lines must reference posted receipt lines of the same purchase order.",
            lines.Where(x => !sources.ContainsKey(x.ReceiptLineId)).Select(x => x.LineNumber));

        var over = lines.GroupBy(x => x.ReceiptLineId)
            .Where(g => g.Sum(x => x.Quantity) > Returnable(sources[g.Key])).ToArray();
        if (over.Length > 0)
            throw new BusinessRuleException(QuantityExceeded,
                "Return quantity exceeds the returnable quantity of the receipt line: " + string.Join("; ",
                    over.Select(g => $"receipt line {sources[g.Key].LineNo} returnable {Returnable(sources[g.Key]):0.######}")))
            {
                Extensions = LineExtensions(over.SelectMany(g => g).Select(x => x.LineNumber),
                    over.Select(g => new Dictionary<string, object?>
                    {
                        ["receiptLineId"] = g.Key, ["requested"] = g.Sum(x => x.Quantity),
                        ["returnable"] = Returnable(sources[g.Key])
                    }).ToArray())
            };

        var receiptIds = sources.Values.Select(x => x.PurchaseReceiptId).Distinct().Order().ToArray();
        var moves = (await evidence.FindAsync(receiptIds, token)).GroupBy(x => (x.ReceiptId, x.LineNo))
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.Single() : null);
        PurchaseInboundEvidence? Move(PurchaseReceiptSourceLine s) =>
            moves.GetValueOrDefault((s.PurchaseReceiptId, s.LineNo));
        Refuse(EvidenceMissing, "The original inventory receipt of the receipt line was not found.",
            lines.Where(x => Move(sources[x.ReceiptLineId]) is not { UnitCost: not null } m
                || m.ItemId != sources[x.ReceiptLineId].MaterialId || m.Quantity != sources[x.ReceiptLineId].Quantity)
                .Select(x => x.LineNumber));

        var warehouses = lines.Where(x => x.WarehouseId is { } w && w != sources[x.ReceiptLineId].WarehouseId)
            .Select(x => x.WarehouseId!.Value).Distinct().Order().ToArray();
        if (warehouses.Length > 0) await references.RequireWarehousesAsync(warehouses, token);

        await CheckTrackingAsync(companyId, lines, token);
        return lines.Select(x => new PlannedLine(x, sources[x.ReceiptLineId], Move(sources[x.ReceiptLineId])!.MovementId,
            x.WarehouseId ?? sources[x.ReceiptLineId].WarehouseId!.Value)).ToArray();
    }

    /// <summary>
    /// Each returned lot/serial must have been captured on the referenced receipt line; per receipt line and number the
    /// draft total may not exceed the captured quantity. An untracked receipt line accepts no tracking. A Draft may be
    /// incomplete; SUBMIT requires the full quantity (007c4) and INV rechecks stock at posting.
    /// </summary>
    private async Task CheckTrackingAsync(long companyId, IReadOnlyList<PurchaseReturnSaveLine> lines, CancellationToken token)
    {
        var tracked = lines.Where(x => x.Tracking is { Count: > 0 }).ToArray();
        if (tracked.Length == 0) return;
        var captured = await ReceiptTrackingAsync(companyId, tracked.Select(x => x.ReceiptLineId).Distinct().ToArray(), token);
        var available = captured.SelectMany(c => c.Value.Select(t => (Key: (c.Key, t.SerialNo is null,
                PurchaseReturnDraftInput.Normalize(t.LotNo ?? t.SerialNo)), t.Quantity)))
            .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var entries = tracked.SelectMany(line => line.Tracking!.Select(t => (Line: line,
            Key: (line.ReceiptLineId, t.SerialNo is null, PurchaseReturnDraftInput.Normalize(t.LotNo ?? t.SerialNo)),
            t.Quantity))).ToArray();
        Refuse(TrackingNotFromReceipt, "Returned lot or serial numbers were not received on the receipt line.",
            entries.GroupBy(x => x.Key).Where(g => g.Sum(x => x.Quantity) > available.GetValueOrDefault(g.Key))
                .SelectMany(g => g).Select(x => x.Line.LineNumber));
    }

    private static PurchaseReturnLine Apply(PurchaseReturnLine row, PlannedLine plan)
    {
        row.ReceiptLineId = plan.Source.Id;
        row.OriginalMovementId = plan.OriginalMovementId;
        row.MaterialId = plan.Source.MaterialId;
        row.WarehouseId = plan.WarehouseId;
        row.LocationId = null;
        row.Quantity = plan.Line.Quantity;
        row.OriginalUnitCost = null; // written from INV at posting (007c4)
        return row;
    }

    private async Task SaveTrackingAsync(long companyId, IReadOnlyList<PurchaseReturnLine> rows,
        IReadOnlyList<PlannedLine> plan, CancellationToken token)
    {
        var byNo = plan.ToDictionary(x => x.Line.LineNumber);
        var added = rows.SelectMany(row => (byNo[row.LineNo].Line.Tracking ?? []).Select((t, i) =>
            new PurchaseReturnLineTracking
            {
                CompanyId = companyId, LineId = row.Id, SeqNo = i + 1, LotNo = t.LotNo?.Trim(),
                SerialNo = t.SerialNo?.Trim(), Quantity = t.Quantity
            })).ToArray();
        if (added.Length == 0) return;
        db.AddRange(added);
        await db.SaveChangesAsync(token);
    }

    // ------------------------------------------------------------------ reads

    private async Task<PurchaseReturnReadDetail> GetAsync(long id, string permission, CancellationToken token)
    {
        if (id <= 0) throw new RequestValidationException("id", "Return ID must be positive.");
        var companyId = access.RequireCompanyId();
        var user = await RequireScopeAsync(companyId, permission, token);
        if (!await ApplyScope(db.Set<PurchaseReturn>().AsNoTracking().Where(x => x.CompanyId == companyId), user, permission)
                .AnyAsync(x => x.Id == id, token))
            throw NotFound(id);
        return await LoadAsync(companyId, id, token);
    }

    private async Task<PurchaseReturnReadDetail> LoadAsync(long companyId, long id, CancellationToken token)
    {
        var header = await db.Set<PurchaseReturn>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, token) ?? throw NotFound(id);
        var order = await db.Set<PurchaseOrderSource>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Id == header.PurchaseOrderId)
            .Select(x => new { x.DocumentNo, x.SupplierId, x.Currency }).SingleAsync(token);
        var rows = await (from l in db.Set<PurchaseReturnLine>().AsNoTracking()
                join s in db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
                    on new { l.CompanyId, Id = l.ReceiptLineId } equals new { s.CompanyId, s.Id }
                join r in db.Set<PurchaseReceiptSource>().AsNoTracking().IgnoreQueryFilters()
                    on new { s.CompanyId, Id = s.PurchaseReceiptId } equals new { r.CompanyId, r.Id }
                where l.CompanyId == companyId && l.PurchaseReturnId == id
                orderby l.LineNo
                select new { Line = l, Source = s, ReceiptNo = r.DocumentNo! }).ToArrayAsync(token);
        var lineIds = rows.Select(x => x.Line.Id).ToArray();
        var tracking = (await db.Set<PurchaseReturnLineTracking>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)).ToArrayAsync(token))
            .GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SeqNo)
                .Select(x => new PurchaseReturnTrackingRead(x.SeqNo, x.LotNo, x.SerialNo, x.Quantity)).ToArray());
        return new PurchaseReturnReadDetail(header.Id, header.DocumentNo, header.Status, header.Version,
            header.PurchaseOrderId, order.DocumentNo, order.SupplierId, order.Currency, header.BusinessDate,
            rows.Select(x => new PurchaseReturnReadLine(x.Line.Id, x.Line.LineNo, x.Source.PurchaseReceiptId,
                x.ReceiptNo, x.Source.Id, x.Source.LineNo, x.Line.OriginalMovementId, x.Line.MaterialId,
                x.Line.WarehouseId, x.Line.LocationId, x.Line.Quantity, x.Source.UnitCost ?? 0m,
                x.Line.OriginalUnitCost, Returnable(x.Source), tracking.GetValueOrDefault(x.Line.Id, []))).ToArray());
    }

    private async Task<Dictionary<long, PurchaseReceiptLineTracking[]>> ReceiptTrackingAsync(long companyId,
        long[] receiptLineIds, CancellationToken token) =>
        (await db.Set<PurchaseReceiptLineTracking>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && receiptLineIds.Contains(x.LineId)).ToArrayAsync(token))
        .GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SeqNo).ToArray());

    // ------------------------------------------------------------------ helpers

    private static decimal Returnable(PurchaseReceiptSourceLine line) =>
        line.PostedReceived - line.PostedReturned - line.PendingReturnReserved;

    private static void Refuse(string code, string message, IEnumerable<int> lineNos)
    {
        var numbers = lineNos.Distinct().Order().ToArray();
        if (numbers.Length > 0)
            throw new BusinessRuleException(code, message) { Extensions = LineExtensions(numbers, null) };
    }

    private static Dictionary<string, object?> LineExtensions(IEnumerable<int> lineNos, object? details)
    {
        var numbers = lineNos.Distinct().Order().ToArray();
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
            { ["lineNo"] = numbers[0], ["lineNos"] = numbers };
        if (details is not null) result["receiptLines"] = details;
        return result;
    }

    private async Task<UserCompanyAuthorization> RequireScopeAsync(long companyId, string permission,
        CancellationToken token)
    {
        var user = await authorization.GetAsync(token);
        if (user is null || user.CompanyId != companyId || !user.Has(permission) || user.FilterFor(permission) is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Return permission is required.");
        return user;
    }

    private static IQueryable<PurchaseReturn> ApplyScope(IQueryable<PurchaseReturn> query,
        UserCompanyAuthorization user, string permission)
    {
        var scope = user.FilterFor(permission)!;
        return scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
    }

    private static NotFoundException NotFound(long id) => new(NotFoundCode, "Purchase return", id);

    private async Task RollbackAsync(IDbContextTransaction tx, Exception ex)
    {
        await tx.RollbackAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        if (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: SqlException sql }
            && sql.Number is 2601 or 2627) throw new ConflictException();
    }
}

/// <summary>Lock order for returns: purchase order → return → receipt lines ascending (007c3／c4).</summary>
internal static class PurchaseReturnLocks
{
    public static Task LockOrderAsync(MyErpDbContext db, long orderId, long companyId, CancellationToken token) =>
        PurchaseReceiptLocks.LockOrderAsync(db, orderId, companyId, token);

    public static Task LockReturnAsync(MyErpDbContext db, long returnId, long companyId, CancellationToken token) =>
        db.Database.SqlQuery<long>($"SELECT [id] AS [Value] FROM dbo.pur_purchase_return WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {returnId} AND [company_id] = {companyId}").ToListAsync(token);

    public static Task LockReceiptLineAsync(MyErpDbContext db, long lineId, long companyId, CancellationToken token) =>
        db.Database.SqlQuery<long>($"SELECT [id] AS [Value] FROM dbo.pur_purchase_receipt_line WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {lineId} AND [company_id] = {companyId}").ToListAsync(token);
}
