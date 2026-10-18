using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.Inventory.Application;

public sealed record DisassemblyLineInput(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, decimal? AllocationWeight = null, IReadOnlyList<InventoryTrackingLine>? Tracking = null);
public sealed record DisassemblyInput(DateOnly BusinessDate, string? Remark, DisassemblyLineInput? Finished,
    IReadOnlyList<DisassemblyLineInput>? Components);
public sealed record DisassemblyQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public InventoryDocumentStatus? Status { get; init; }
    public long? FinishedItemId { get; init; }
}

public sealed record DisassemblySummary(long Id, string DocumentNo, DateOnly BusinessDate,
    InventoryDocumentStatus Status, long FinishedItemId, decimal FinishedQuantity, int ComponentCount, int Version,
    string? FinishedItemCode = null, string? FinishedItemName = null);
/// <summary>REQ_F4115.5.1–3/6：拆卸草稿保存與查詢。</summary>
public sealed class DisassemblyService(MyErpDbContext db, INumberGenerator numbers,
    IInventoryReferenceGuard guard, IClock clock)
{
    public const string DocumentType = "INV.DISASSEMBLY";
    public const string NotFound = "INV.DISASSEMBLY.NOT_FOUND";
    public const string NotDraft = "INV.DISASSEMBLY.NOT_DRAFT";
    private const decimal MaxQuantity = 10_000_000_000_000m;

    public async Task<PagedResult<DisassemblySummary>> ListAsync(DisassemblyQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        if (request.From > request.To) throw new RequestValidationException("to", "End date must not be earlier than start date.");
        if (request.Status is { } s && !Enum.IsDefined(s)) throw new RequestValidationException("status", "Unknown status.");
        if (request.FinishedItemId is <= 0) throw new RequestValidationException("finishedItemId", "Item must be positive.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<Disassembly>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || (x.Remark != null && x.Remark.Contains(keyword)));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.FinishedItemId is { } itemId) query = query.Where(x => x.Lines.Any(l => l.Role == DisassemblyLineRole.Finished && l.ItemId == itemId));
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
            .Select(x => new DisassemblySummary(x.Id, x.DocumentNo, x.BusinessDate, x.Status,
                x.Lines.Where(l => l.Role == DisassemblyLineRole.Finished).Select(l => l.ItemId).First(),
                x.Lines.Where(l => l.Role == DisassemblyLineRole.Finished).Select(l => l.Quantity).First(),
                x.Lines.Count(l => l.Role == DisassemblyLineRole.Component), x.Version)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<Disassembly> GetAsync(long id, CancellationToken token = default)
    {
        RequireCompany();
        return await WithLines(db.Set<Disassembly>().AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw Missing(id);
    }

    public async Task<Disassembly> CreateAsync(DisassemblyInput input, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var (remark, lines) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            await CheckReferencesAsync(lines, token);
            var disassembly = new Disassembly { CompanyId = companyId,
                DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token),
                BusinessDate = input.BusinessDate, Remark = remark };
            disassembly.Lines.AddRange(lines);
            db.Add(disassembly);
            await db.SaveChangesAsync(token);
            return disassembly;
        }, token);
    }

    public async Task<Disassembly> UpdateAsync(long id, DisassemblyInput input, int version, CancellationToken token = default)
    {
        RequireCompany();
        var (remark, lines) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            var disassembly = await LoadAsync(id, version, token);
            if (disassembly.Status is not (InventoryDocumentStatus.Draft or InventoryDocumentStatus.Rejected))
                throw new BusinessRuleException(NotDraft, "Only a draft or rejected disassembly can be edited.");
            await CheckReferencesAsync(lines, token);
            disassembly.BusinessDate = input.BusinessDate;
            disassembly.Remark = remark;
            disassembly.Status = InventoryDocumentStatus.Draft;
            disassembly.Lines.Clear();
            disassembly.Lines.AddRange(lines);
            db.Entry(disassembly).Property(x => x.Status).IsModified = true;
            await db.SaveChangesAsync(token);
            return disassembly;
        }, token);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        await InTransactionAsync(async () =>
        {
            var disassembly = await LoadAsync(id, version, token);
            if (disassembly.Status != InventoryDocumentStatus.Draft)
                throw new BusinessRuleException(NotDraft, "Only a draft disassembly can be deleted.");
            disassembly.IsDeleted = true;
            disassembly.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            return disassembly;
        }, token);
    }

    internal static List<InventoryTrackingLine> TrackingOf(DisassemblyLine line) =>
        [.. line.Tracking.OrderBy(x => x.SeqNo).Select(x => new InventoryTrackingLine(x.LotNo, x.SerialNo, x.Quantity))];

    internal static IQueryable<Disassembly> WithLines(IQueryable<Disassembly> query) =>
        query.Include(x => x.Lines).ThenInclude(x => x.Tracking).AsSplitQuery();
    internal static NotFoundException Missing(long id) => new(NotFound, "Disassembly", id);
    private long RequireCompany() => db.CurrentCompanyId is long id && id > 0 ? id :
        throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private async Task<Disassembly> LoadAsync(long id, int version, CancellationToken token)
    {
        var disassembly = await WithLines(db.Set<Disassembly>()).SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing(id);
        if (version < 1 || disassembly.Version != version) throw new ConflictException();
        return disassembly;
    }

    private async Task CheckReferencesAsync(List<DisassemblyLine> lines, CancellationToken token)
    {
        var modes = new Dictionary<long, ItemTrackingMode>();
        foreach (var key in lines.Select(x => (x.ItemId, x.WarehouseId, x.LocationId)).Distinct()
            .OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId))
            modes[key.ItemId] = await guard.RunWithCodesAsync(key.ItemId, key.WarehouseId, key.LocationId,
                codes => Task.FromResult(codes.TrackingMode), token);
        foreach (var (line, index) in lines.OrderBy(x => x.LineNo).Select((line, index) => (line, index)))
            StockTrackingRules.CheckMode(line.LineNo, line.Quantity,
                TrackingOf(line), $"lines[{index}]", modes[line.ItemId], false);
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

    private static (string? Remark, List<DisassemblyLine> Lines) Validate(DisassemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (remark?.Length > 1000) Fail("remark", "Remark must not exceed 1000 characters.");
        if (input.Finished is null) Fail("finished", "Finished line is required.");
        if (input.Components is not { Count: > 0 and <= 999 }) Fail("components", "A disassembly needs 1 to 999 component lines.");
        var source = new List<(DisassemblyLineInput? Line, DisassemblyLineRole Role, string Field)>();
        if (input.Finished is not null) source.Add((input.Finished, DisassemblyLineRole.Finished, "finished"));
        if (input.Components is not null)
            source.AddRange(input.Components.Select((line, i) => ((DisassemblyLineInput?)line, DisassemblyLineRole.Component, $"components[{i}]")));
        var lines = new List<DisassemblyLine>();
        foreach (var (line, role, field) in source)
        {
            if (line is null) { Fail(field, "Line is required."); continue; }
            if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
            if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
            if (line.WarehouseId <= 0) Fail($"{field}.warehouseId", "Warehouse is required.");
            if (line.LocationId is <= 0) Fail($"{field}.locationId", "Location must be positive.");
            if (line.Quantity <= 0 || line.Quantity >= MaxQuantity || decimal.Round(line.Quantity, 6) != line.Quantity)
                Fail($"{field}.quantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
            if (role == DisassemblyLineRole.Finished && line.AllocationWeight is not null)
                Fail($"{field}.allocationWeight", "Finished line cannot have allocation weight.");
            if (role == DisassemblyLineRole.Component && (line.AllocationWeight is null || line.AllocationWeight <= 0 || line.AllocationWeight >= MaxQuantity || decimal.Round(line.AllocationWeight.Value, 6) != line.AllocationWeight))
                Fail($"{field}.allocationWeight", "Component allocation weight must be positive and have at most 6 decimals.");
            if (line.Tracking is { } tracking)
                StockTrackingRules.Validate(role == DisassemblyLineRole.Finished ? StockDirection.Out : StockDirection.In,
                    tracking, $"{field}.tracking", Fail);
            var entity = new DisassemblyLine { LineNo = line.LineNo, Role = role, AllocationWeight = line.AllocationWeight, ItemId = line.ItemId,
                WarehouseId = line.WarehouseId, LocationId = line.LocationId, Quantity = line.Quantity };
            entity.Tracking.AddRange((line.Tracking ?? []).Select((t, j) => new DisassemblyLineTracking
            { SeqNo = j + 1, LotNo = t.LotNo?.Trim(), SerialNo = t.SerialNo?.Trim(), Quantity = t.Quantity }));
            lines.Add(entity);
        }
        if (lines.GroupBy(x => x.LineNo).Any(x => x.Count() > 1)) Fail("components", "Line numbers must be unique.");
        if (input.Finished is { } finished && input.Components?.Any(x => x?.ItemId == finished.ItemId) == true)
            Fail("components", "Finished item cannot be a component.");
        if (source.Sum(x => x.Line?.Tracking?.Count ?? 0) > StockPostingService.MaxTrackingEntries)
            Fail("components", "A disassembly must contain at most 10000 tracking entries.");
        if (StockTrackingRules.HasDuplicateSerials(source.Where(x => x.Line is not null)
            .Select(x => (x.Line!.ItemId, x.Role == DisassemblyLineRole.Finished ? StockDirection.Out : StockDirection.In,
                x.Line.Tracking)))) Fail("components", "A serial number can appear only once per item within a disassembly.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, lines);
    }
}
