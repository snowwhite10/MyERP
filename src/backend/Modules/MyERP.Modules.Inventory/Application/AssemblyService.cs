using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.Inventory.Application;

public sealed record AssemblyLineInput(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, IReadOnlyList<InventoryTrackingLine>? Tracking = null);
public sealed record AssemblyInput(DateOnly BusinessDate, string? Remark, AssemblyLineInput? Finished,
    IReadOnlyList<AssemblyLineInput>? Components);
public sealed record AssemblyQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public InventoryDocumentStatus? Status { get; init; }
    public long? FinishedItemId { get; init; }
}

/// <summary>SYS 審批交易內處理核准或駁回；失敗由 SYS 回滾決定與異動。</summary>
public sealed class AssemblyApprovalResultHandler(MyErpDbContext db, IInventoryPostingService posting,
    AssemblyCostSnapshot snapshot, IClock clock) : IApprovalResultHandler
{
    public string DocumentType => AssemblyService.DocumentType;
    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        var assembly = await AssemblyService.WithLines(db.Set<Assembly>())
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw AssemblyService.Missing(result.DocumentId);
        if (assembly.Status != InventoryDocumentStatus.PendingApproval)
            throw new ConflictException(AssemblyService.ApprovalStateChanged, "Assembly is no longer waiting for approval.");
        if (result.Status == ApprovalStatus.Approved)
            await AssemblyService.PostAsync(assembly, posting, snapshot, clock, cancellationToken);
        else assembly.Status = InventoryDocumentStatus.Rejected;
    }
}
public sealed record AssemblySummary(long Id, string DocumentNo, DateOnly BusinessDate,
    InventoryDocumentStatus Status, long FinishedItemId, int ComponentCount, int Version);
public sealed record AssemblyDetail(Assembly Assembly, ApprovalState? Approval);

/// <summary>REQ_F4115.4.1–6：組裝草稿、審批與原子成本轉移。</summary>
public sealed class AssemblyService(MyErpDbContext db, INumberGenerator numbers,
    IInventoryReferenceGuard guard, IApprovalWorkflowService workflow, IInventoryPostingService posting,
    AssemblyCostSnapshot snapshot, IClock clock)
{
    public const string DocumentType = "INV.ASSEMBLY";
    public const string SourceType = "ASSEMBLY";
    public const string NotFound = "INV.ASSEMBLY.NOT_FOUND";
    public const string NotDraft = "INV.ASSEMBLY.NOT_DRAFT";
    public const string ApprovalStateChanged = "INV.ASSEMBLY.APPROVAL_STATE_CHANGED";
    private const decimal MaxQuantity = 10_000_000_000_000m;

    public async Task<PagedResult<AssemblySummary>> ListAsync(AssemblyQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        if (request.From > request.To) throw new RequestValidationException("to", "End date must not be earlier than start date.");
        if (request.Status is { } s && !Enum.IsDefined(s)) throw new RequestValidationException("status", "Unknown status.");
        if (request.FinishedItemId is <= 0) throw new RequestValidationException("finishedItemId", "Item must be positive.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<Assembly>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || (x.Remark != null && x.Remark.Contains(keyword)));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.FinishedItemId is { } itemId) query = query.Where(x => x.Lines.Any(l => l.Role == AssemblyLineRole.Finished && l.ItemId == itemId));
        var sort = sorts[0];
        query = (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new AssemblySummary(x.Id, x.DocumentNo, x.BusinessDate, x.Status,
                x.Lines.Where(l => l.Role == AssemblyLineRole.Finished).Select(l => l.ItemId).First(),
                x.Lines.Count(l => l.Role == AssemblyLineRole.Component), x.Version)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<Assembly> GetAsync(long id, CancellationToken token = default)
    {
        RequireCompany();
        return await WithLines(db.Set<Assembly>().AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw Missing(id);
    }

    public async Task<Assembly> CreateAsync(AssemblyInput input, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var (remark, lines) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            await CheckReferencesAsync(lines, token);
            var assembly = new Assembly { CompanyId = companyId,
                DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token),
                BusinessDate = input.BusinessDate, Remark = remark };
            assembly.Lines.AddRange(lines);
            db.Add(assembly);
            await db.SaveChangesAsync(token);
            return assembly;
        }, token);
    }

    public async Task<Assembly> UpdateAsync(long id, AssemblyInput input, int version, CancellationToken token = default)
    {
        RequireCompany();
        var (remark, lines) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            var assembly = await LoadAsync(id, version, token);
            if (assembly.Status is not (InventoryDocumentStatus.Draft or InventoryDocumentStatus.Rejected))
                throw new BusinessRuleException(NotDraft, "Only a draft or rejected assembly can be edited.");
            await CheckReferencesAsync(lines, token);
            assembly.BusinessDate = input.BusinessDate;
            assembly.Remark = remark;
            assembly.Status = InventoryDocumentStatus.Draft;
            assembly.Lines.Clear();
            assembly.Lines.AddRange(lines);
            db.Entry(assembly).Property(x => x.Status).IsModified = true;
            await db.SaveChangesAsync(token);
            return assembly;
        }, token);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        await InTransactionAsync(async () =>
        {
            var assembly = await LoadAsync(id, version, token);
            if (assembly.Status != InventoryDocumentStatus.Draft)
                throw new BusinessRuleException(NotDraft, "Only a draft assembly can be deleted.");
            assembly.IsDeleted = true;
            assembly.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            return assembly;
        }, token);
    }

    public async Task<AssemblyDetail> SubmitAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        ApprovalState? state = null;
        var assembly = await InTransactionAsync(async () =>
        {
            var current = await LoadAsync(id, version, token);
            if (current.Status != InventoryDocumentStatus.Draft)
                throw new BusinessRuleException(NotDraft, "Only a draft assembly can be submitted.");
            await CheckReferencesAsync(current.Lines, token, true);
            current.Status = InventoryDocumentStatus.PendingApproval;
            state = await workflow.SubmitAsync(new ApprovalSubmission(DocumentType, current.Id,
                current.DocumentNo, 0m, current.Remark), token);
            if (state.Status == ApprovalStatus.Approved)
                await PostAsync(current, posting, snapshot, clock, token);
            await db.SaveChangesAsync(token);
            return current;
        }, token);
        return new AssemblyDetail(assembly, state);
    }

    internal static async Task PostAsync(Assembly assembly, IInventoryPostingService posting,
        AssemblyCostSnapshot snapshot, IClock clock, CancellationToken token)
    {
        var components = assembly.Lines.Where(x => x.Role == AssemblyLineRole.Component).OrderBy(x => x.ItemId).ThenBy(x => x.LineNo).ToList();
        var finished = assembly.Lines.Single(x => x.Role == AssemblyLineRole.Finished);
        var costs = new Dictionary<long, decimal>();
        decimal total = 0m;
        try
        {
            await snapshot.LockItemsAsync(assembly.Lines.Select(x => x.ItemId), token);
            foreach (var itemId in components.Select(x => x.ItemId).Distinct())
            {
                var value = await snapshot.GetLockedAsync(itemId, token);
                if (value.AverageUnitCost is not decimal cost || value.UnpricedQuantity > 0)
                    throw new BusinessRuleException("INV.ASSEMBLY.COST_UNKNOWN", $"Reliable cost is unavailable for item {itemId}.");
                costs.Add(itemId, cost);
            }
            foreach (var line in components)
            {
                line.PostedUnitCost = costs[line.ItemId];
                total = checked(total + decimal.Round(line.Quantity * costs[line.ItemId], 4, MidpointRounding.AwayFromZero));
            }
            var unitCost = decimal.Round(total / finished.Quantity, 6, MidpointRounding.AwayFromZero);
            if (unitCost < 0 || unitCost >= MaxQuantity ||
                decimal.Round(unitCost * finished.Quantity, 4, MidpointRounding.AwayFromZero) != total)
                throw new BusinessRuleException("INV.ASSEMBLY.COST_PRECISION", "Finished unit cost cannot represent the transferred value.");
            finished.PostedUnitCost = unitCost;
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException("INV.ASSEMBLY.COST_OVERFLOW", "Assembly cost exceeds supported precision.");
        }
        var lines = components.OrderBy(x => x.LineNo)
            .Select(x => new InventoryPostingLine(x.LineNo, StockDirection.Out, x.ItemId, x.WarehouseId,
                x.LocationId, x.Quantity, null, TrackingOf(x))).ToList();
        lines.Add(new InventoryPostingLine(finished.LineNo, StockDirection.In, finished.ItemId,
            finished.WarehouseId, finished.LocationId, finished.Quantity, finished.PostedUnitCost, TrackingOf(finished)));
        await posting.PostAsync(new InventoryPostingRequest(SourceType, assembly.Id, assembly.BusinessDate, lines), token);
        assembly.Status = InventoryDocumentStatus.Posted;
        assembly.PostedAt = clock.LocalNow;
    }

    internal static List<InventoryTrackingLine> TrackingOf(AssemblyLine line) =>
        [.. line.Tracking.OrderBy(x => x.SeqNo).Select(x => new InventoryTrackingLine(x.LotNo, x.SerialNo, x.Quantity))];

    internal static IQueryable<Assembly> WithLines(IQueryable<Assembly> query) =>
        query.Include(x => x.Lines).ThenInclude(x => x.Tracking).AsSplitQuery();
    internal static NotFoundException Missing(long id) => new(NotFound, "Assembly", id);
    private long RequireCompany() => db.CurrentCompanyId is long id && id > 0 ? id :
        throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private async Task<Assembly> LoadAsync(long id, int version, CancellationToken token)
    {
        var assembly = await WithLines(db.Set<Assembly>()).SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing(id);
        if (version < 1 || assembly.Version != version) throw new ConflictException();
        return assembly;
    }

    private async Task CheckReferencesAsync(List<AssemblyLine> lines, CancellationToken token, bool requireComplete = false)
    {
        var modes = new Dictionary<long, ItemTrackingMode>();
        foreach (var key in lines.Select(x => (x.ItemId, x.WarehouseId, x.LocationId)).Distinct()
            .OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId))
            modes[key.ItemId] = await guard.RunWithCodesAsync(key.ItemId, key.WarehouseId, key.LocationId,
                codes => Task.FromResult(codes.TrackingMode), token);
        foreach (var (line, index) in lines.OrderBy(x => x.LineNo).Select((line, index) => (line, index)))
            StockTrackingRules.CheckMode(line.LineNo, line.Quantity,
                TrackingOf(line), $"lines[{index}]", modes[line.ItemId], requireComplete);
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            var result = await action();
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    private static (string? Remark, List<AssemblyLine> Lines) Validate(AssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (remark?.Length > 1000) Fail("remark", "Remark must not exceed 1000 characters.");
        if (input.Finished is null) Fail("finished", "Finished line is required.");
        if (input.Components is not { Count: > 0 and <= 999 }) Fail("components", "An assembly needs 1 to 999 component lines.");
        var source = new List<(AssemblyLineInput? Line, AssemblyLineRole Role, string Field)>();
        if (input.Finished is not null) source.Add((input.Finished, AssemblyLineRole.Finished, "finished"));
        if (input.Components is not null)
            source.AddRange(input.Components.Select((line, i) => ((AssemblyLineInput?)line, AssemblyLineRole.Component, $"components[{i}]")));
        var lines = new List<AssemblyLine>();
        foreach (var (line, role, field) in source)
        {
            if (line is null) { Fail(field, "Line is required."); continue; }
            if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
            if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
            if (line.WarehouseId <= 0) Fail($"{field}.warehouseId", "Warehouse is required.");
            if (line.LocationId is <= 0) Fail($"{field}.locationId", "Location must be positive.");
            if (line.Quantity <= 0 || line.Quantity >= MaxQuantity || decimal.Round(line.Quantity, 6) != line.Quantity)
                Fail($"{field}.quantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
            if (line.Tracking is { } tracking)
                StockTrackingRules.Validate(role == AssemblyLineRole.Finished ? StockDirection.In : StockDirection.Out,
                    tracking, $"{field}.tracking", Fail);
            var entity = new AssemblyLine { LineNo = line.LineNo, Role = role, ItemId = line.ItemId,
                WarehouseId = line.WarehouseId, LocationId = line.LocationId, Quantity = line.Quantity };
            entity.Tracking.AddRange((line.Tracking ?? []).Select((t, j) => new AssemblyLineTracking
            { SeqNo = j + 1, LotNo = t.LotNo?.Trim(), SerialNo = t.SerialNo?.Trim(), Quantity = t.Quantity }));
            lines.Add(entity);
        }
        if (lines.GroupBy(x => x.LineNo).Any(x => x.Count() > 1)) Fail("components", "Line numbers must be unique.");
        if (input.Finished is { } finished && input.Components?.Any(x => x?.ItemId == finished.ItemId) == true)
            Fail("components", "Finished item cannot be a component.");
        if (source.Sum(x => x.Line?.Tracking?.Count ?? 0) > StockPostingService.MaxTrackingEntries)
            Fail("components", "An assembly must contain at most 10000 tracking entries.");
        if (StockTrackingRules.HasDuplicateSerials(source.Where(x => x.Line is not null)
            .Select(x => (x.Line!.ItemId, x.Role == AssemblyLineRole.Finished ? StockDirection.In : StockDirection.Out,
                x.Line.Tracking)))) Fail("components", "A serial number can appear only once per item within an assembly.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, lines);
    }
}
