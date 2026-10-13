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

public sealed record TransferLineInput(int LineNo, long ItemId, long SourceWarehouseId, long? SourceLocationId,
    long DestinationWarehouseId, long? DestinationLocationId, decimal Quantity,
    IReadOnlyList<InventoryTrackingLine>? Tracking = null);
public sealed record TransferInput(DateOnly BusinessDate, string? Remark, IReadOnlyList<TransferLineInput>? Lines);
public sealed record TransferQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public InventoryDocumentStatus? Status { get; init; }
}
public sealed record TransferSummary(long Id, string DocumentNo, DateOnly BusinessDate,
    InventoryDocumentStatus Status, string? Remark, int LineCount, decimal TotalQuantity, int Version);
public sealed record TransferDetail(Transfer Transfer, ApprovalState? Approval);

/// <summary>REQ_F4115.6.1–6：草稿、SYS 審批與原子出入庫。</summary>
public sealed class TransferService(MyErpDbContext db, INumberGenerator numbers, IInventoryReferenceGuard guard,
    IApprovalWorkflowService workflow, IInventoryPostingService posting, IClock clock)
{
    public const string DocumentType = "INV.TRANSFER";
    public const string SourceType = "TRANSFER";
    public const string NotFound = "INV.TRANSFER.NOT_FOUND";
    public const string NotDraft = "INV.TRANSFER.NOT_DRAFT";
    public const string ApprovalStateChanged = "INV.TRANSFER.APPROVAL_STATE_CHANGED";
    private const int MaxLines = 1000;
    private const decimal MaxQuantity = 10_000_000_000_000m;

    public async Task<PagedResult<TransferSummary>> ListAsync(TransferQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        if (request.From > request.To) throw new RequestValidationException("to", "End date must not be earlier than start date.");
        if (request.Status is { } s && !Enum.IsDefined(s)) throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<Transfer>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || (x.Remark != null && x.Remark.Contains(keyword)));
        }
        if (request.From is { } from) query = query.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.BusinessDate <= to);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        var sort = sorts[0];
        query = (sort.Field, sort.Descending) switch
        {
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo).ThenBy(x => x.Id),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id),
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new TransferSummary(x.Id, x.DocumentNo, x.BusinessDate, x.Status, x.Remark,
                x.Lines.Count, x.Lines.Sum(l => l.Quantity), x.Version)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<TransferDetail> GetAsync(long id, CancellationToken token = default)
    {
        RequireCompany();
        var transfer = await WithLines(db.Set<Transfer>().AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw Missing(id);
        return new TransferDetail(transfer, await workflow.GetStateAsync(DocumentType, id, token));
    }

    public async Task<Transfer> CreateAsync(TransferInput input, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var (remark, lines) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            await CheckReferencesAsync(lines, false, token);
            var transfer = new Transfer { CompanyId = companyId,
                DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token),
                BusinessDate = input.BusinessDate, Remark = remark };
            transfer.Lines.AddRange(lines);
            db.Add(transfer);
            await db.SaveChangesAsync(token);
            return transfer;
        }, token);
    }

    public async Task<Transfer> UpdateAsync(long id, TransferInput input, int version, CancellationToken token = default)
    {
        RequireCompany();
        var (remark, lines) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            var transfer = await LoadAsync(id, version, token);
            if (transfer.Status is not (InventoryDocumentStatus.Draft or InventoryDocumentStatus.Rejected)) throw NotEditable();
            await CheckReferencesAsync(lines, false, token);
            transfer.BusinessDate = input.BusinessDate;
            transfer.Remark = remark;
            transfer.Status = InventoryDocumentStatus.Draft;
            transfer.Lines.Clear();
            transfer.Lines.AddRange(lines);
            db.Entry(transfer).Property(x => x.Status).IsModified = true;
            await db.SaveChangesAsync(token);
            return transfer;
        }, token);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        await InTransactionAsync(async () =>
        {
            var transfer = await LoadAsync(id, version, token);
            if (transfer.Status != InventoryDocumentStatus.Draft) throw NotEditable();
            transfer.IsDeleted = true;
            transfer.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            return transfer;
        }, token);
    }

    public async Task<TransferDetail> SubmitAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        ApprovalState? state = null;
        var transfer = await InTransactionAsync(async () =>
        {
            var current = await LoadAsync(id, version, token);
            if (current.Status != InventoryDocumentStatus.Draft) throw NotEditable();
            await CheckReferencesAsync(current.Lines, true, token);
            current.Status = InventoryDocumentStatus.PendingApproval;
            state = await workflow.SubmitAsync(new ApprovalSubmission(DocumentType, current.Id,
                current.DocumentNo, 0m, current.Remark), token);
            if (state.Status == ApprovalStatus.Approved) await PostAsync(current, posting, clock, token);
            await db.SaveChangesAsync(token);
            return current;
        }, token);
        return new TransferDetail(transfer, state);
    }

    internal static async Task PostAsync(Transfer transfer, IInventoryPostingService posting, IClock clock, CancellationToken token)
    {
        var lines = transfer.Lines.OrderBy(x => x.LineNo).SelectMany(x =>
        {
            var tracking = TrackingOf(x);
            return new[]
            {
                new InventoryPostingLine(x.LineNo, StockDirection.Out, x.ItemId, x.SourceWarehouseId,
                    x.SourceLocationId, x.Quantity, null, tracking),
                new InventoryPostingLine(x.LineNo, StockDirection.In, x.ItemId, x.DestinationWarehouseId,
                    x.DestinationLocationId, x.Quantity, null, tracking),
            };
        }).ToList();
        await posting.PostAsync(new InventoryPostingRequest(SourceType, transfer.Id, transfer.BusinessDate, lines), token);
        transfer.Status = InventoryDocumentStatus.Posted;
        transfer.PostedAt = clock.LocalNow;
    }

    internal static IQueryable<Transfer> WithLines(IQueryable<Transfer> query) =>
        query.Include(x => x.Lines).ThenInclude(x => x.Tracking).AsSplitQuery();
    internal static NotFoundException Missing(long id) => new(NotFound, "Transfer", id);
    private static BusinessRuleException NotEditable() =>
        new(NotDraft, "Only a draft transfer can be changed, submitted or deleted; a rejected transfer must be revised first.");
    private static List<InventoryTrackingLine> TrackingOf(TransferLine line) =>
        [.. line.Tracking.OrderBy(x => x.SeqNo).Select(x => new InventoryTrackingLine(x.LotNo, x.SerialNo, x.Quantity))];

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

    private async Task<Transfer> LoadAsync(long id, int version, CancellationToken token)
    {
        var transfer = await WithLines(db.Set<Transfer>()).SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing(id);
        if (version < 1 || transfer.Version != version) throw new ConflictException();
        return transfer;
    }

    private async Task CheckReferencesAsync(List<TransferLine> lines, bool requireComplete, CancellationToken token)
    {
        var modes = new Dictionary<long, ItemTrackingMode>();
        var keys = lines.Select(x => (x.ItemId, WarehouseId: x.SourceWarehouseId, LocationId: x.SourceLocationId))
            .Concat(lines.Select(x => (x.ItemId, WarehouseId: x.DestinationWarehouseId, LocationId: x.DestinationLocationId)))
            .Distinct().OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId);
        foreach (var key in keys)
            modes[key.ItemId] = await guard.RunWithCodesAsync(key.ItemId, key.WarehouseId, key.LocationId,
                codes => Task.FromResult(codes.TrackingMode), token);
        var ordered = lines.OrderBy(x => x.LineNo).ToList();
        for (var i = 0; i < ordered.Count; i++)
            StockTrackingRules.CheckMode(ordered[i].LineNo, ordered[i].Quantity, TrackingOf(ordered[i]),
                $"lines[{i}]", modes[ordered[i].ItemId], requireComplete);
    }

    private long RequireCompany() => db.CurrentCompanyId is long id && id > 0 ? id :
        throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static (string? Remark, List<TransferLine> Lines) Validate(TransferInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (remark?.Length > 500) Fail("remark", "Remark must not exceed 500 characters.");
        var lines = new List<TransferLine>();
        if (input.Lines is not { Count: > 0 and <= MaxLines }) Fail("lines", "A transfer must contain 1 to 1000 lines.");
        else
        {
            for (var i = 0; i < input.Lines.Count; i++)
            {
                var line = input.Lines[i];
                var field = $"lines[{i}]";
                if (line is null) { Fail(field, "Line is required."); continue; }
                if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
                if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
                if (line.SourceWarehouseId <= 0) Fail($"{field}.sourceWarehouseId", "Source warehouse is required.");
                if (line.DestinationWarehouseId <= 0) Fail($"{field}.destinationWarehouseId", "Destination warehouse is required.");
                if (line.SourceLocationId is <= 0) Fail($"{field}.sourceLocationId", "Source location must be positive.");
                if (line.DestinationLocationId is <= 0) Fail($"{field}.destinationLocationId", "Destination location must be positive.");
                if (line.SourceWarehouseId == line.DestinationWarehouseId && line.SourceLocationId == line.DestinationLocationId)
                    Fail($"{field}.destinationWarehouseId", "Source and destination stock keys must differ.");
                if (line.Quantity <= 0 || line.Quantity >= MaxQuantity || decimal.Round(line.Quantity, 6) != line.Quantity)
                    Fail($"{field}.quantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
                if (line.Tracking is { } tracking) StockTrackingRules.Validate(StockDirection.Out, tracking, $"{field}.tracking", Fail);
                var entity = new TransferLine { LineNo = line.LineNo, ItemId = line.ItemId,
                    SourceWarehouseId = line.SourceWarehouseId, SourceLocationId = line.SourceLocationId,
                    DestinationWarehouseId = line.DestinationWarehouseId, DestinationLocationId = line.DestinationLocationId,
                    Quantity = line.Quantity };
                entity.Tracking.AddRange((line.Tracking ?? []).Select((t, j) => new TransferLineTracking
                { SeqNo = j + 1, LotNo = t.LotNo?.Trim(), SerialNo = t.SerialNo?.Trim(), Quantity = t.Quantity }));
                lines.Add(entity);
            }
            var present = input.Lines.Where(x => x is not null).ToList();
            if (present.GroupBy(x => x.LineNo).Any(x => x.Count() > 1)) Fail("lines", "Line numbers must be unique.");
            if (present.Sum(x => x.Tracking?.Count ?? 0) > StockPostingService.MaxTrackingEntries)
                Fail("lines", "A transfer must contain at most 10000 tracking entries.");
            if (StockTrackingRules.HasDuplicateSerials(present.Select(x => (x.ItemId, StockDirection.Out, x.Tracking))))
                Fail("lines", "A serial number can appear only once per item within a transfer.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, lines);
    }
}

/// <summary>在 SYS 審批交易內完成出入庫或駁回。</summary>
public sealed class TransferApprovalResultHandler(MyErpDbContext db, IInventoryPostingService posting, IClock clock)
    : IApprovalResultHandler
{
    public string DocumentType => TransferService.DocumentType;
    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        var transfer = await TransferService.WithLines(db.Set<Transfer>())
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw TransferService.Missing(result.DocumentId);
        if (transfer.Status != InventoryDocumentStatus.PendingApproval)
            throw new ConflictException(TransferService.ApprovalStateChanged, "Transfer is no longer waiting for approval.");
        if (result.Status == ApprovalStatus.Approved)
            await TransferService.PostAsync(transfer, posting, clock, cancellationToken);
        else transfer.Status = InventoryDocumentStatus.Rejected;
    }
}
