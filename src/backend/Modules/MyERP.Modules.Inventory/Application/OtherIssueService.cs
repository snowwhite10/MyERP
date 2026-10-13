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

/// <summary>明細輸入：數量為物料基本單位，單價只作審批估算；兩者最多六位小數。批序號草稿可先不填（ADR-060）。</summary>
public sealed record OtherIssueLineInput(int LineNo, long ItemId, long WarehouseId, long? LocationId, decimal Quantity, decimal UnitCost,
    IReadOnlyList<InventoryTrackingLine>? Tracking = null);

/// <summary>建立或整組替換草稿的內容。</summary>
public sealed record OtherIssueInput(DateOnly BusinessDate, string? Remark, IReadOnlyList<OtherIssueLineInput>? Lines);

/// <summary>列表篩選：關鍵字比對單號與備註；日期為業務日期（含頭尾）。</summary>
public sealed record OtherIssueQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public InventoryDocumentStatus? Status { get; init; }
}

public sealed record OtherIssueSummary(long Id, string DocumentNo, DateOnly BusinessDate, InventoryDocumentStatus Status,
    string? Remark, int LineCount, decimal TotalQuantity, decimal TotalAmount, int Version);

public sealed record OtherIssueDetail(OtherIssue Issue, ApprovalState? Approval);

/// <summary>
/// 其他出庫（REQ_F4115.3、ADR-057）。草稿保存不寫異動帳；送審在同一交易呼叫 SYS 審批，立即核准即過帳。
/// 待審單據由 <see cref="OtherIssueApprovalResultHandler"/> 在 SYS 審批交易內過帳或標為駁回。
/// </summary>
public sealed class OtherIssueService(MyErpDbContext db, INumberGenerator numbers, IInventoryReferenceGuard guard,
    IApprovalWorkflowService workflow, IInventoryPostingService posting, IClock clock)
{
    public const string DocumentType = "INV.OTHER_ISSUE";
    public const string SourceType = "OTHER_ISSUE";
    public const string NotFound = "INV.OTHER_ISSUE.NOT_FOUND";
    public const string NotDraft = "INV.OTHER_ISSUE.NOT_DRAFT";
    public const string ApprovalStateChanged = "INV.OTHER_ISSUE.APPROVAL_STATE_CHANGED";
    public const int MaxLines = 1000;
    public const int MaxRemarkLength = 500;
    private const decimal MaxQuantity = 10_000_000_000_000m;
    private const decimal MaxAmount = 1_000_000_000_000_000m;

    public async Task<PagedResult<OtherIssueSummary>> ListAsync(OtherIssueQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        if (request.From > request.To) throw new RequestValidationException("to", "End date must not be earlier than start date.");
        if (request.Status is { } s && !Enum.IsDefined(s)) throw new RequestValidationException("status", "Unknown status.");
        var sorts = SortSpec.Parse(request.Sort, ["documentNo", "businessDate"], "-businessDate");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");

        var query = db.Set<OtherIssue>().AsNoTracking();
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
            ("documentNo", false) => query.OrderBy(x => x.DocumentNo),
            ("documentNo", true) => query.OrderByDescending(x => x.DocumentNo),
            (_, false) => query.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id),
        };

        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new OtherIssueSummary(x.Id, x.DocumentNo, x.BusinessDate, x.Status, x.Remark, x.Lines.Count,
                x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.Amount), x.Version))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<OtherIssueDetail> GetAsync(long id, CancellationToken token = default)
    {
        RequireCompany();
        var issue = await db.Set<OtherIssue>().AsNoTracking().Include(x => x.Lines.OrderBy(l => l.LineNo)).ThenInclude(x => x.Tracking.OrderBy(t => t.SeqNo))
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing(id);
        return new OtherIssueDetail(issue, await workflow.GetStateAsync(DocumentType, id, token));
    }

    /// <summary>單號與單據同一交易：驗證或保存失敗時流水號一併回滾。</summary>
    public async Task<OtherIssue> CreateAsync(OtherIssueInput input, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var (remark, lines) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            await CheckReferencesAsync(lines, requireComplete: false, token);
            var issue = new OtherIssue
            {
                CompanyId = companyId, DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token),
                BusinessDate = input.BusinessDate, Remark = remark,
            };
            issue.Lines.AddRange(lines);
            db.Add(issue);
            await db.SaveChangesAsync(token);
            return issue;
        }, token);
    }

    /// <summary>整組替換草稿或已駁回單據的日期、備註與明細；已駁回單據回到草稿，審批歷史保留在 SYS。</summary>
    public async Task<OtherIssue> UpdateAsync(long id, OtherIssueInput input, int version, CancellationToken token = default)
    {
        RequireCompany();
        var (remark, lines) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            var issue = await LoadAsync(id, version, token);
            if (issue.Status is not (InventoryDocumentStatus.Draft or InventoryDocumentStatus.Rejected)) throw NotEditable();
            await CheckReferencesAsync(lines, requireComplete: false, token);
            issue.BusinessDate = input.BusinessDate;
            issue.Remark = remark;
            issue.Status = InventoryDocumentStatus.Draft;
            issue.Lines.Clear();
            issue.Lines.AddRange(lines);
            // 只改明細時表頭也要遞增版本，令並發送審以舊版本失敗
            db.Entry(issue).Property(x => x.Status).IsModified = true;
            await db.SaveChangesAsync(token);
            return issue;
        }, token);
    }

    /// <summary>只有草稿可軟刪除；單號保留不重用。</summary>
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        await InTransactionAsync(async () =>
        {
            var issue = await LoadAsync(id, version, token);
            if (issue.Status != InventoryDocumentStatus.Draft) throw NotEditable();
            issue.IsDeleted = true;
            issue.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            return issue;
        }, token);
    }

    /// <summary>草稿送審；SYS 沒有適用步驟而立即核准時，在同一交易過帳。</summary>
    public async Task<OtherIssueDetail> SubmitAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        ApprovalState? state = null;
        var issue = await InTransactionAsync(async () =>
        {
            var issue = await LoadAsync(id, version, token);
            if (issue.Status != InventoryDocumentStatus.Draft) throw NotEditable();
            await CheckReferencesAsync(issue.Lines, requireComplete: true, token);
            issue.Status = InventoryDocumentStatus.PendingApproval;
            state = await workflow.SubmitAsync(
                new ApprovalSubmission(DocumentType, issue.Id, issue.DocumentNo, issue.TotalAmount, issue.Remark), token);
            if (state.Status == ApprovalStatus.Approved)
                await PostAsync(issue, posting, clock, token);
            await db.SaveChangesAsync(token);
            return issue;
        }, token);
        return new OtherIssueDetail(issue, state);
    }

    /// <summary>核准後過帳：每行一筆出庫異動，來源為 <see cref="SourceType"/> + 單據 ID（ADR-053 防重放）。</summary>
    internal static async Task PostAsync(OtherIssue issue, IInventoryPostingService posting, IClock clock, CancellationToken token)
    {
        issue.Status = InventoryDocumentStatus.Posted;
        issue.PostedAt = clock.LocalNow;
        await posting.PostAsync(new InventoryPostingRequest(SourceType, issue.Id, issue.BusinessDate,
            [.. issue.Lines.OrderBy(x => x.LineNo).Select(x =>
                new InventoryPostingLine(x.LineNo, StockDirection.Out, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity, null,
                    TrackingOf(x)))]), token);
    }

    private static List<InventoryTrackingLine> TrackingOf(OtherIssueLine line) =>
        [.. line.Tracking.OrderBy(t => t.SeqNo).Select(t => new InventoryTrackingLine(t.LotNo, t.SerialNo, t.Quantity))];

    internal static IQueryable<OtherIssue> WithLines(IQueryable<OtherIssue> issues) =>
        issues.Include(x => x.Lines).ThenInclude(x => x.Tracking).AsSplitQuery();

    internal static NotFoundException Missing(long id) => new(NotFound, "Other issue", id);

    private static BusinessRuleException NotEditable() =>
        new(NotDraft, "Only a draft other issue can be changed, submitted or deleted; a rejected issue must be revised first.");

    /// <summary>
    /// 本服務自己的交易：失敗時回滾並清除追蹤，避免已回滾的單據、審批實例或異動被同一 DbContext 再次保存。
    /// 版本衝突（並發修改或送審）回 409。
    /// </summary>
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

    private async Task<OtherIssue> LoadAsync(long id, int version, CancellationToken token)
    {
        var issue = await WithLines(db.Set<OtherIssue>()).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw Missing(id);
        if (version < 1 || issue.Version != version) throw new ConflictException();
        return issue;
    }

    /// <summary>
    /// 每組不同的物料／倉庫／儲位驗證一次；MD 共享鎖持有至本交易提交（BR-INV-005）。
    /// 同時依物料追蹤方式檢查批序號：草稿只檢查種類，送審（<paramref name="requireComplete"/>）時合計必須等於數量；
    /// 批序號結存在過帳時核對。
    /// </summary>
    private async Task CheckReferencesAsync(List<OtherIssueLine> lines, bool requireComplete, CancellationToken token)
    {
        var modes = new Dictionary<long, ItemTrackingMode>();
        foreach (var key in lines.Select(x => (x.ItemId, x.WarehouseId, x.LocationId)).Distinct())
            modes[key.ItemId] = await guard.RunWithCodesAsync(key.ItemId, key.WarehouseId, key.LocationId,
                codes => Task.FromResult(codes.TrackingMode), token);
        var ordered = lines.OrderBy(x => x.LineNo).ToList();
        for (var i = 0; i < ordered.Count; i++)
            StockTrackingRules.CheckMode(ordered[i].LineNo, ordered[i].Quantity, TrackingOf(ordered[i]), $"lines[{i}]",
                modes[ordered[i].ItemId], requireComplete);
    }

    private long RequireCompany() => db.CurrentCompanyId is long id && id > 0
        ? id : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static (string? Remark, List<OtherIssueLine> Lines) Validate(OtherIssueInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);

        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (remark?.Length > MaxRemarkLength) Fail("remark", $"Remark must not exceed {MaxRemarkLength} characters.");
        var lines = new List<OtherIssueLine>();
        var totalAmount = 0m;
        if (input.Lines is not { Count: > 0 and <= MaxLines })
        {
            Fail("lines", $"A issue must contain 1 to {MaxLines} lines.");
        }
        else
        {
            for (var i = 0; i < input.Lines.Count; i++)
            {
                var line = input.Lines[i];
                var field = $"lines[{i}]";
                if (line is null) { Fail(field, "Line is required."); continue; }
                if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
                if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
                if (line.WarehouseId <= 0) Fail($"{field}.warehouseId", "Warehouse is required.");
                if (line.LocationId is <= 0) Fail($"{field}.locationId", "Location id must be positive.");
                var quantityValid = IsStorable(line.Quantity) && line.Quantity > 0;
                var costValid = IsStorable(line.UnitCost) && line.UnitCost >= 0;
                if (!quantityValid) Fail($"{field}.quantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
                if (!costValid) Fail($"{field}.unitCost", "Unit cost must be non-negative, below 10^13 and have at most 6 decimals.");
                var errorCount = errors.Count;
                if (line.Tracking is { } tracking) StockTrackingRules.Validate(StockDirection.Out, tracking, $"{field}.tracking", Fail);
                if (quantityValid && costValid && errors.Count == errorCount)
                {
                    var amount = OtherIssueLine.AmountOf(line.Quantity, line.UnitCost);
                    if (amount >= MaxAmount - totalAmount)
                    {
                        Fail("lines", "Issue amount must be below 10^15.");
                        continue;
                    }
                    totalAmount += amount;
                    var entity = new OtherIssueLine
                    {
                        LineNo = line.LineNo, ItemId = line.ItemId, WarehouseId = line.WarehouseId, LocationId = line.LocationId,
                        Quantity = line.Quantity, UnitCost = line.UnitCost, Amount = amount,
                    };
                    entity.Tracking.AddRange((line.Tracking ?? []).Select((t, j) => new OtherIssueLineTracking
                    {
                        SeqNo = j + 1, LotNo = t.LotNo?.Trim(), SerialNo = t.SerialNo?.Trim(), Quantity = t.Quantity,
                    }));
                    lines.Add(entity);
                }
            }
            var present = input.Lines.Where(x => x is not null).ToList();
            if (present.GroupBy(x => x.LineNo).Any(g => g.Count() > 1))
                Fail("lines", "Line numbers must be unique.");
            if (present.Sum(x => x.Tracking?.Count ?? 0) > StockPostingService.MaxTrackingEntries)
                Fail("lines", $"An issue must contain at most {StockPostingService.MaxTrackingEntries} lot or serial entries.");
            if (StockTrackingRules.HasDuplicateSerials(present.Select(x => (x.ItemId, StockDirection.Out, x.Tracking))))
                Fail("lines", "A serial number can appear only once per item within an issue.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, lines);
    }

    private static bool IsStorable(decimal value) => value > -MaxQuantity && value < MaxQuantity && decimal.Round(value, 6) == value;
}

/// <summary>
/// SYS 最終審批結果（INV.OTHER_ISSUE）：在 SYS 審批交易內核准即過帳、駁回只改狀態；
/// 不提交交易，任何失敗都令 SYS 決定、單據狀態與異動帳一起回滾。只接受待審單據，重複結果回 409。
/// </summary>
public sealed class OtherIssueApprovalResultHandler(MyErpDbContext db, IInventoryPostingService posting, IClock clock)
    : IApprovalResultHandler
{
    public string DocumentType => OtherIssueService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        var issue = await OtherIssueService.WithLines(db.Set<OtherIssue>())
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw OtherIssueService.Missing(result.DocumentId);
        if (issue.Status != InventoryDocumentStatus.PendingApproval)
            throw new ConflictException(OtherIssueService.ApprovalStateChanged, "Other issue is no longer waiting for approval.");
        if (result.Status == ApprovalStatus.Approved)
            await OtherIssueService.PostAsync(issue, posting, clock, cancellationToken);
        else
            issue.Status = InventoryDocumentStatus.Rejected;
    }
}
