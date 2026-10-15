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
public sealed record AssemblySummary(long Id, string DocumentNo, DateOnly BusinessDate,
    InventoryDocumentStatus Status, long FinishedItemId, int ComponentCount, int Version);

/// <summary>REQ_F4115.4.1–3/6：組裝草稿；核准與成本由 WU-INV-011b 實作。</summary>
public sealed class AssemblyService(MyErpDbContext db, INumberGenerator numbers,
    IInventoryReferenceGuard guard, IClock clock)
{
    public const string DocumentType = "INV.ASSEMBLY";
    public const string NotFound = "INV.ASSEMBLY.NOT_FOUND";
    public const string NotDraft = "INV.ASSEMBLY.NOT_DRAFT";
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

    private static IQueryable<Assembly> WithLines(IQueryable<Assembly> query) =>
        query.Include(x => x.Lines).ThenInclude(x => x.Tracking).AsSplitQuery();
    private static NotFoundException Missing(long id) => new(NotFound, "Assembly", id);
    private long RequireCompany() => db.CurrentCompanyId is long id && id > 0 ? id :
        throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private async Task<Assembly> LoadAsync(long id, int version, CancellationToken token)
    {
        var assembly = await WithLines(db.Set<Assembly>()).SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing(id);
        if (version < 1 || assembly.Version != version) throw new ConflictException();
        return assembly;
    }

    private async Task CheckReferencesAsync(List<AssemblyLine> lines, CancellationToken token)
    {
        var modes = new Dictionary<long, ItemTrackingMode>();
        foreach (var key in lines.Select(x => (x.ItemId, x.WarehouseId, x.LocationId)).Distinct()
            .OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId))
            modes[key.ItemId] = await guard.RunWithCodesAsync(key.ItemId, key.WarehouseId, key.LocationId,
                codes => Task.FromResult(codes.TrackingMode), token);
        foreach (var (line, index) in lines.OrderBy(x => x.LineNo).Select((line, index) => (line, index)))
            StockTrackingRules.CheckMode(line.LineNo, line.Quantity,
                [.. line.Tracking.OrderBy(x => x.SeqNo).Select(x => new InventoryTrackingLine(x.LotNo, x.SerialNo, x.Quantity))],
                $"lines[{index}]", modes[line.ItemId], false);
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
