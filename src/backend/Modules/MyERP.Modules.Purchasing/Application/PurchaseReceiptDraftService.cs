using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseReceiptSaveLine(int LineNumber, long PurchaseOrderLineId,
    decimal Quantity, long WarehouseId, IReadOnlyList<PurchaseReceiptTrackingInput>? Tracking = null);
public sealed record PurchaseReceiptSaveInput(long PurchaseOrderId, DateOnly BusinessDate,
    IReadOnlyList<PurchaseReceiptSaveLine>? Lines);
public sealed record PurchaseReceiptReadLine(long Id, int LineNumber, long PurchaseOrderLineId,
    long MaterialId, decimal Quantity, long WarehouseId, decimal UnitCost,
    IReadOnlyList<PurchaseReceiptTrackingRead> Tracking);
public sealed record PurchaseReceiptReadDetail(long Id, string DocumentNo, string Status, int Version,
    long PurchaseOrderId, long SupplierId, string Currency, DateOnly BusinessDate,
    IReadOnlyList<PurchaseReceiptReadLine> Lines);

/// <summary>REQ_F4114.2.1–3: one locked transaction saves a receipt draft without reserving or posting stock.</summary>
public sealed class PurchaseReceiptDraftService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, INumberGenerator numbers, IInventoryReferenceGuard references,
    PurchaseReceiptTracking tracking)
{
    public const string DocumentType = "PUR.RECEIPT";

    public async Task<PurchaseReceiptReadDetail> CreateAsync(PurchaseReceiptSaveInput input,
        CancellationToken token = default)
    {
        var validated = Validate(input);
        var companyId = access.RequireCompanyId();
        var user = await RequireScopeAsync(companyId, PurchasingPermissions.ReceiptCreate, token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var (order, lines, costs) = await ReadSourcesAsync(companyId, validated, token);
            await references.RequireWarehousesAsync(input.Lines!.Select(x => x.WarehouseId).Distinct().ToArray(), token);
            var receipt = new PurchaseReceiptSource
            {
                CompanyId = companyId, PurchaseOrderId = order.Id,
                SupplierId = order.SupplierId, Currency = order.Currency,
                DocumentNo = await numbers.NextAsync(DocumentType, validated.BusinessDate, token),
                BusinessDate = validated.BusinessDate, Status = "Draft",
                OwnerId = user.UserId, BranchId = user.BranchId, DepartmentId = user.DepartmentId
            };
            var saved = BuildLines(companyId, 0, input.Lines!, lines, costs);
            await CheckTrackingAsync(input.Lines!, saved, token);
            db.Add(receipt);
            await db.SaveChangesAsync(token);
            foreach (var row in saved) row.PurchaseReceiptId = receipt.Id;
            db.AddRange(saved);
            await db.SaveChangesAsync(token);
            var captured = await SaveTrackingAsync(companyId, input.Lines!, saved, token);
            await tx.CommitAsync(token);
            return Detail(receipt, saved, captured);
        }
        catch (Exception ex) { await RollbackAsync(tx, ex); throw; }
    }

    public async Task<PurchaseReceiptReadDetail> UpdateAsync(long id, int version,
        PurchaseReceiptSaveInput input, CancellationToken token = default)
    {
        if (id <= 0) throw new RequestValidationException("id", "Receipt ID must be positive.");
        var validated = Validate(input);
        var companyId = access.RequireCompanyId();
        var user = await RequireScopeAsync(companyId, PurchasingPermissions.ReceiptEdit, token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // Lock order first, matching cancellation and future SUBMIT. The receipt is then locked and re-read.
            await PurchaseReceiptLocks.LockOrderAsync(db, validated.PurchaseOrderId, companyId, token);
            await PurchaseReceiptLocks.LockReceiptAsync(db, id, companyId, token);
            var receipt = await ApplyScope(db.Set<PurchaseReceiptSource>()
                    .Where(x => x.CompanyId == companyId), user, PurchasingPermissions.ReceiptEdit)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw NotFound(id);
            if (version < 1 || receipt.Version != version) throw new ConflictException();
            PurchaseReceiptDraftInput.EnsureEditable(receipt.Status);
            if (receipt.PurchaseOrderId != validated.PurchaseOrderId)
                throw new BusinessRuleException("PUR.RECEIPT.SOURCE_LOCKED",
                    "The purchase order source cannot be changed.");
            var (order, lines, costs) = await ReadSourcesAsync(companyId, validated, token, orderLocked: true);
            if (receipt.SupplierId != order.SupplierId || receipt.Currency != order.Currency)
                throw new BusinessRuleException("PUR.RECEIPT.SOURCE_LOCKED",
                    "The saved supplier and currency must match the purchase order.");
            await references.RequireWarehousesAsync(input.Lines!.Select(x => x.WarehouseId).Distinct().ToArray(), token);
            var old = await db.Set<PurchaseReceiptSourceLine>()
                .Where(x => x.CompanyId == companyId && x.PurchaseReceiptId == id).ToArrayAsync(token);
            // Existing line IDs are retained for references. A changed line number is a new row.
            var incoming = input.Lines!.ToDictionary(x => x.LineNumber);
            var kept = old.Where(x => incoming.ContainsKey(x.LineNo)).ToDictionary(x => x.LineNo);
            db.RemoveRange(old.Where(x => !incoming.ContainsKey(x.LineNo)));
            foreach (var entry in old.Where(x => !incoming.ContainsKey(x.LineNo)))
                if (entry.PostedReceived != 0 || entry.PostedInvoiced != 0 || entry.MatchedReserved != 0)
                    throw new BusinessRuleException("PUR.RECEIPT.SOURCE_LOCKED", "Referenced receipt lines cannot be removed.");
            var saved = BuildLines(companyId, id, input.Lines!, lines, costs, kept);
            await CheckTrackingAsync(input.Lines!, saved, token);
            // Lot/serial capture is replaced as a whole (ADR-221); removed lines would cascade anyway.
            await tracking.DeleteAsync(companyId, id, token);
            await db.SaveChangesAsync(token);
            foreach (var row in saved.Where(x => x.Id == 0)) db.Add(row);
            receipt.BusinessDate = validated.BusinessDate;
            receipt.Status = "Draft";
            db.Entry(receipt).Property(x => x.BusinessDate).IsModified = true;
            await db.SaveChangesAsync(token);
            var captured = await SaveTrackingAsync(companyId, input.Lines!, saved, token);
            await tx.CommitAsync(token);
            return Detail(receipt, saved, captured);
        }
        catch (Exception ex) { await RollbackAsync(tx, ex); throw; }
    }

    public Task<PurchaseReceiptReadDetail> GetAsync(long id, CancellationToken token = default) =>
        GetAsync(id, PurchasingPermissions.ReceiptView, token);

    public Task<PurchaseReceiptReadDetail> GetForEditAsync(long id, CancellationToken token = default) =>
        GetAsync(id, PurchasingPermissions.ReceiptEdit, token);

    private async Task<PurchaseReceiptReadDetail> GetAsync(long id, string permission, CancellationToken token)
    {
        if (id <= 0) throw new RequestValidationException("id", "Receipt ID must be positive.");
        var companyId = access.RequireCompanyId();
        var user = await RequireScopeAsync(companyId, permission, token);
        var receipt = await ApplyScope(db.Set<PurchaseReceiptSource>().AsNoTracking()
                .Where(x => x.CompanyId == companyId), user, permission)
            .SingleOrDefaultAsync(x => x.Id == id, token) ?? throw NotFound(id);
        if (receipt.DocumentNo is null || receipt.BusinessDate is null)
            throw NotFound(id); // legacy invoice-only source rows are not user-facing inbound documents
        var rows = await db.Set<PurchaseReceiptSourceLine>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PurchaseReceiptId == id)
            .OrderBy(x => x.LineNo).ToArrayAsync(token);
        return Detail(receipt, rows, await tracking.LoadAsync(companyId, id, token));
    }

    /// <summary>MD tracking mode for lines that carry captures; a Draft line without captures needs no check.</summary>
    private Task CheckTrackingAsync(IReadOnlyList<PurchaseReceiptSaveLine> requested,
        IReadOnlyList<PurchaseReceiptSourceLine> rows, CancellationToken token)
    {
        var byNo = requested.ToDictionary(x => x.LineNumber);
        var tracked = rows.Where(x => byNo[x.LineNo].Tracking is { Count: > 0 })
            .Select(x => new PurchaseReceiptTrackedLine(x.LineNo, x.MaterialId, x.WarehouseId!.Value,
                x.Quantity!.Value, byNo[x.LineNo].Tracking!)).ToArray();
        return tracked.Length == 0 ? Task.CompletedTask : tracking.CheckAsync(tracked, false, token);
    }

    private async Task<Dictionary<long, PurchaseReceiptLineTracking[]>> SaveTrackingAsync(long companyId,
        IReadOnlyList<PurchaseReceiptSaveLine> requested, IReadOnlyList<PurchaseReceiptSourceLine> rows,
        CancellationToken token)
    {
        var byNo = requested.ToDictionary(x => x.LineNumber);
        var captured = rows.ToDictionary(x => x.Id,
            x => PurchaseReceiptTracking.Build(companyId, x.Id, byNo[x.LineNo].Tracking));
        var added = captured.Values.SelectMany(x => x).ToArray();
        if (added.Length > 0)
        {
            db.AddRange(added);
            await db.SaveChangesAsync(token);
        }
        return captured;
    }

    private async Task<(PurchaseOrderSource Order, Dictionary<long, PurchaseOrderSourceLine> Lines,
        Dictionary<long, decimal> Costs)> ReadSourcesAsync(long companyId,
        PurchaseReceiptValidatedInput validated, CancellationToken token, bool orderLocked = false)
    {
        if (!orderLocked) await PurchaseReceiptLocks.LockOrderAsync(db, validated.PurchaseOrderId, companyId, token);
        var order = await db.Set<PurchaseOrderSource>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == validated.PurchaseOrderId, token);
        if (order is null) throw new BusinessRuleException("PUR.RECEIPT.INVALID_SOURCE", "Purchase order does not exist.");
        var ids = validated.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Order().ToArray();
        foreach (var sourceId in ids) await PurchaseReceiptLocks.LockOrderLineAsync(db, sourceId, companyId, token);
        var sourceLines = await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id)).ToArrayAsync(token);
        PurchaseReceiptAvailability.Check(companyId, validated,
            new PurchaseReceiptOrderSnapshot(order.Id, order.CompanyId, order.Status, order.IsDeleted),
            sourceLines.Select(x => new PurchaseReceiptOrderLineSnapshot(x.Id, x.PurchaseOrderId,
                x.CompanyId, false, x.Quantity, x.PostedReceived, x.PendingReceiptReserved)).ToArray());
        var costs = new Dictionary<long, decimal>();
        foreach (var line in sourceLines)
        {
            if (line.BaseNet is null || line.Quantity <= 0 || line.BaseNet < 0)
                throw new BusinessRuleException("PUR.RECEIPT.COST_UNAVAILABLE",
                    $"Purchase order line {line.Id} has no saved base-currency cost.");
            decimal unitCost;
            try { unitCost = decimal.Round(line.BaseNet.Value / line.Quantity, 6,
                MidpointRounding.AwayFromZero); }
            catch (OverflowException) { throw new BusinessRuleException("PUR.RECEIPT.COST_UNAVAILABLE", "Cost exceeds decimal precision."); }
            if (unitCost >= 10_000_000_000_000m)
                throw new BusinessRuleException("PUR.RECEIPT.COST_UNAVAILABLE", "Cost exceeds SQL precision.");
            costs.Add(line.Id, unitCost);
        }
        return (order, sourceLines.ToDictionary(x => x.Id), costs);
    }

    private static PurchaseReceiptSourceLine[] BuildLines(long companyId, long receiptId,
        IReadOnlyList<PurchaseReceiptSaveLine> requested,
        Dictionary<long, PurchaseOrderSourceLine> sources, Dictionary<long, decimal> costs,
        Dictionary<int, PurchaseReceiptSourceLine>? existing = null) => requested.OrderBy(x => x.LineNumber)
        .Select(x =>
        {
            var row = existing is not null && existing.TryGetValue(x.LineNumber, out var saved)
                ? saved : new PurchaseReceiptSourceLine { CompanyId = companyId,
                    PurchaseReceiptId = receiptId, LineNo = x.LineNumber };
            if (row.Id != 0 && (row.PurchaseOrderLineId != x.PurchaseOrderLineId ||
                row.MaterialId != sources[x.PurchaseOrderLineId].MaterialId))
                throw new BusinessRuleException("PUR.RECEIPT.SOURCE_LOCKED",
                    $"Receipt line {x.LineNumber} cannot change its purchase order line or item.");
            row.PurchaseOrderLineId = x.PurchaseOrderLineId;
            row.MaterialId = sources[x.PurchaseOrderLineId].MaterialId;
            row.Quantity = x.Quantity;
            row.WarehouseId = x.WarehouseId;
            row.UnitCost = costs[x.PurchaseOrderLineId];
            return row;
        }).ToArray();

    private static PurchaseReceiptReadDetail Detail(PurchaseReceiptSource receipt,
        IReadOnlyList<PurchaseReceiptSourceLine> rows,
        IReadOnlyDictionary<long, PurchaseReceiptLineTracking[]> captured) => new(receipt.Id, receipt.DocumentNo!,
        receipt.Status, receipt.Version, receipt.PurchaseOrderId, receipt.SupplierId, receipt.Currency,
        receipt.BusinessDate!.Value, rows.OrderBy(x => x.LineNo)
            .Select(x => new PurchaseReceiptReadLine(x.Id, x.LineNo, x.PurchaseOrderLineId,
                x.MaterialId, x.Quantity!.Value, x.WarehouseId!.Value, x.UnitCost!.Value,
                PurchaseReceiptTracking.ToRead(captured.GetValueOrDefault(x.Id)))).ToArray());

    private static PurchaseReceiptValidatedInput Validate(PurchaseReceiptSaveInput input)
    {
        if (input is null) throw new RequestValidationException("body", "Receipt body is required.");
        if (input.Lines?.Any(x => x is null || x.WarehouseId <= 0) == true)
            throw new RequestValidationException("lines", "Every line needs a warehouse.");
        PurchaseReceiptTrackingRules.Validate(input.Lines);
        return PurchaseReceiptDraftInput.Validate(new(input.PurchaseOrderId, input.BusinessDate,
            input.Lines?.Select(x => new PurchaseReceiptDraftLine(x.LineNumber,
                x.PurchaseOrderLineId, x.Quantity)).ToArray()));
    }

    private async Task<UserCompanyAuthorization> RequireScopeAsync(long companyId, string permission,
        CancellationToken token)
    {
        var user = await authorization.GetAsync(token);
        if (user is null || user.CompanyId != companyId || !user.Has(permission) ||
            user.FilterFor(permission) is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Receipt permission is required.");
        return user;
    }

    private static IQueryable<PurchaseReceiptSource> ApplyScope(IQueryable<PurchaseReceiptSource> query,
        UserCompanyAuthorization user, string permission = PurchasingPermissions.ReceiptView)
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

    private static NotFoundException NotFound(long id) =>
        new("PUR.RECEIPT.NOT_FOUND", "Purchase receipt", id);

    private async Task RollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Exception ex)
    {
        await tx.RollbackAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        if (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: SqlException sql }
            && sql.Number is 2601 or 2627) throw new ConflictException();
    }
}
