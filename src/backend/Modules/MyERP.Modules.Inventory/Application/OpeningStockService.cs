using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.Inventory.Application;

/// <summary>期初明細輸入：數量為物料基本單位；數量與單價最多六位小數。</summary>
public sealed record OpeningStockLineInput(int LineNo, long ItemId, long WarehouseId, long? LocationId, decimal Quantity, decimal UnitCost);

/// <summary>一次期初提交；<paramref name="RequestKey"/> 由用戶端產生，同公司重送相同內容不會重複入庫。</summary>
public sealed record OpeningStockInput(string RequestKey, DateOnly OpeningDate, string? Remark, IReadOnlyList<OpeningStockLineInput>? Lines);

/// <summary>逐行錯誤：<paramref name="Field"/> 為 itemId／warehouseId／locationId／openingDate／line，<paramref name="Code"/> 為三語錯誤碼。</summary>
public sealed record OpeningStockLineError(string Field, string Code);

public sealed record OpeningStockLineResult(int LineNo, decimal Amount, IReadOnlyList<OpeningStockLineError> Errors);

/// <summary>預覽結果；合計包含有錯誤的行，方便使用者對照輸入。</summary>
public sealed record OpeningStockPreview(IReadOnlyList<OpeningStockLineResult> Lines, int ErrorCount, decimal TotalQuantity, decimal TotalAmount);

public sealed record OpeningStockSubmitResult(OpeningStock OpeningStock, bool Replayed);

/// <summary>批次列表篩選：日期為啟用日期（含頭尾）；物料／倉庫篩選含有該明細的批次；關鍵字比對備註與請求鍵。</summary>
public sealed record OpeningStockQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public long? ItemId { get; init; }
    public long? WarehouseId { get; init; }
}

public sealed record OpeningStockSummary(long Id, string RequestKey, DateOnly OpeningDate, string? Remark, int LineCount,
    decimal TotalQuantity, decimal TotalAmount, DateTime PostedAt, DateTime CreatedAt);

/// <summary>
/// 物料期初庫存（REQ_F4115.23、ADR-059）。預覽唯讀並逐行回報錯誤；提交在公司級排他鎖內重新驗證，
/// 同一交易保存批次並以 <see cref="SourceType"/> 入庫過帳，不經 SYS 審批。
/// </summary>
public sealed partial class OpeningStockService(MyErpDbContext db, IInventoryReferenceGuard guard,
    IInventoryPostingService posting, IClock clock)
{
    public const string SourceType = "OPENING_STOCK";
    public const string NotFound = "INV.OPENING_STOCK.NOT_FOUND";
    public const string HasErrors = "INV.OPENING_STOCK.HAS_ERRORS";
    public const string RequestConflict = "INV.OPENING_STOCK.REQUEST_CONFLICT";
    public const string Duplicate = "INV.OPENING_STOCK.DUPLICATE";
    public const string DuplicateInBatch = "INV.OPENING_STOCK.DUPLICATE_IN_BATCH";
    public const string AfterMovement = "INV.OPENING_STOCK.AFTER_MOVEMENT";
    public const int MaxLines = 1000;
    public const int MaxRemarkLength = 500;
    private const decimal MaxQuantity = 10_000_000_000_000m;
    private const decimal MaxAmount = 1_000_000_000_000_000m;

    public async Task<PagedResult<OpeningStockSummary>> ListAsync(OpeningStockQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        if (request.From > request.To) throw new RequestValidationException("to", "End date must not be earlier than start date.");
        if (request.ItemId is <= 0) throw new RequestValidationException("itemId", "Item id must be positive.");
        if (request.WarehouseId is <= 0) throw new RequestValidationException("warehouseId", "Warehouse id must be positive.");

        var query = db.Set<OpeningStock>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.RequestKey.Contains(keyword) || (x.Remark != null && x.Remark.Contains(keyword)));
        }
        if (request.From is { } from) query = query.Where(x => x.OpeningDate >= from);
        if (request.To is { } to) query = query.Where(x => x.OpeningDate <= to);
        // 同時指定時須同一明細符合物料與倉庫
        query = (request.ItemId, request.WarehouseId) switch
        {
            ({ } i, { } w) => query.Where(x => x.Lines.Any(l => l.ItemId == i && l.WarehouseId == w)),
            ({ } i, null) => query.Where(x => x.Lines.Any(l => l.ItemId == i)),
            (null, { } w) => query.Where(x => x.Lines.Any(l => l.WarehouseId == w)),
            _ => query,
        };

        var count = await query.LongCountAsync(token);
        var items = await query.OrderByDescending(x => x.OpeningDate).ThenByDescending(x => x.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new OpeningStockSummary(x.Id, x.RequestKey, x.OpeningDate, x.Remark, x.Lines.Count,
                x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.Amount), x.PostedAt, x.CreatedAt))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<OpeningStock> GetAsync(long id, CancellationToken token = default)
    {
        RequireCompany();
        return await db.Set<OpeningStock>().AsNoTracking().Include(x => x.Lines.OrderBy(l => l.LineNo))
            .SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(NotFound, "Opening stock", id);
    }

    /// <summary>唯讀檢查全部行；MD 共享鎖在每次檢查後即釋放，提交時會重新驗證。</summary>
    public async Task<OpeningStockPreview> PreviewAsync(OpeningStockInput input, CancellationToken token = default)
    {
        RequireCompany();
        var (_, lines) = Validate(input);
        return await EvaluateAsync(input.OpeningDate, lines, token);
    }

    /// <summary>
    /// 公司級排他鎖序列化期初提交：先處理請求鍵重送，再重新驗證全部行；任何一行有錯則整批拒絕（422），
    /// 否則保存批次並同交易入庫過帳。MD 共享鎖持有至提交（BR-INV-005）。
    /// </summary>
    public async Task<OpeningStockSubmitResult> SubmitAsync(OpeningStockInput input, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var (remark, lines) = Validate(input);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            await db.Database.ExecuteSqlAsync($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {"inv_opening_stock:" + companyId}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @result < 0 THROW 51000, 'Opening stock lock was not granted.', 1;
                """, token);
            var existing = await db.Set<OpeningStock>().AsNoTracking().Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.RequestKey == input.RequestKey, token);
            if (existing is not null)
            {
                if (!SameContent(existing, input.OpeningDate, remark, lines))
                    throw new ConflictException(RequestConflict, "The request key was already used with different content.");
                await transaction.CommitAsync(token);
                existing.Lines.Sort((a, b) => a.LineNo.CompareTo(b.LineNo));
                return new OpeningStockSubmitResult(existing, true);
            }

            var preview = await EvaluateAsync(input.OpeningDate, lines, token);
            if (preview.ErrorCount > 0)
                throw new BusinessRuleException(HasErrors, "Correct all opening stock line errors before submitting.");
            var batch = new OpeningStock
            {
                CompanyId = companyId, RequestKey = input.RequestKey, OpeningDate = input.OpeningDate, Remark = remark,
                PostedAt = clock.LocalNow,
            };
            batch.Lines.AddRange(lines.OrderBy(x => x.LineNo));
            db.Add(batch);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            { throw new BusinessRuleException(HasErrors, "An opening stock already exists for one of the lines."); }
            await posting.PostAsync(new InventoryPostingRequest(SourceType, batch.Id, batch.OpeningDate,
                [.. batch.Lines.Select(x => new InventoryPostingLine(x.LineNo, StockDirection.In, x.ItemId, x.WarehouseId,
                    x.LocationId, x.Quantity, x.UnitCost))]), token);
            await transaction.CommitAsync(token);
            return new OpeningStockSubmitResult(batch, false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>逐行收集 MD 引用、批次內重複、既有期初及最早異動日期錯誤；不寫資料。</summary>
    private async Task<OpeningStockPreview> EvaluateAsync(DateOnly openingDate, IReadOnlyList<OpeningStockLine> lines,
        CancellationToken token)
    {
        var errors = lines.ToDictionary(x => x.LineNo, _ => new List<OpeningStockLineError>());
        // 每個結存鍵只驗證一次 MD 引用；重複的後續行另報批次內重複
        foreach (var group in lines.GroupBy(Key))
        {
            var ordered = group.OrderBy(x => x.LineNo).ToList();
            try { await guard.RunAsync(group.Key.Item1, group.Key.Item2, group.Key.Item3, () => Task.FromResult(true), token); }
            catch (ErpException ex) when (ex is NotFoundException or BusinessRuleException)
            {
                foreach (var line in ordered) errors[line.LineNo].Add(new OpeningStockLineError(FieldOf(ex.ErrorCode), ex.ErrorCode));
            }
            foreach (var line in ordered.Skip(1)) errors[line.LineNo].Add(new OpeningStockLineError("line", DuplicateInBatch));
        }

        var itemIds = lines.Select(x => x.ItemId).Distinct().ToList();
        var opened = (await db.Set<OpeningStockLine>().AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
                .Select(x => new { x.ItemId, x.WarehouseId, x.LocationId }).ToListAsync(token))
            .Select(x => (x.ItemId, x.WarehouseId, x.LocationId)).ToHashSet();
        var earliest = (await db.Set<StockMovement>().AsNoTracking().Where(x => itemIds.Contains(x.ItemId))
                .GroupBy(x => new { x.ItemId, x.WarehouseId, x.LocationId })
                .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, g.Key.LocationId, First = g.Min(x => x.BusinessDate) })
                .ToListAsync(token))
            .ToDictionary(x => (x.ItemId, x.WarehouseId, x.LocationId), x => x.First);
        foreach (var line in lines)
        {
            if (opened.Contains(Key(line))) errors[line.LineNo].Add(new OpeningStockLineError("line", Duplicate));
            if (earliest.TryGetValue(Key(line), out var first) && first < openingDate)
                errors[line.LineNo].Add(new OpeningStockLineError("openingDate", AfterMovement));
        }

        return new OpeningStockPreview(
            [.. lines.OrderBy(x => x.LineNo).Select(x => new OpeningStockLineResult(x.LineNo, x.Amount, errors[x.LineNo]))],
            errors.Count(x => x.Value.Count > 0), lines.Sum(x => x.Quantity), lines.Sum(x => x.Amount));
    }

    private static (long, long, long?) Key(OpeningStockLine x) => (x.ItemId, x.WarehouseId, x.LocationId);

    private static string FieldOf(string code) => code switch
    {
        _ when code.StartsWith("MD.ITEM.", StringComparison.Ordinal) => "itemId",
        _ when code.StartsWith("MD.WAREHOUSE.", StringComparison.Ordinal) => "warehouseId",
        _ when code.StartsWith("MD.STORAGE_LOCATION.", StringComparison.Ordinal) => "locationId",
        _ => "line",
    };

    private static bool SameContent(OpeningStock existing, DateOnly date, string? remark, IReadOnlyList<OpeningStockLine> lines) =>
        existing.OpeningDate == date && existing.Remark == remark &&
        existing.Lines.OrderBy(x => x.LineNo).Select(Snapshot).SequenceEqual(lines.OrderBy(x => x.LineNo).Select(Snapshot));

    private static (int, long, long, long?, decimal, decimal) Snapshot(OpeningStockLine x) =>
        (x.LineNo, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity, x.UnitCost);

    private long RequireCompany() => db.CurrentCompanyId is long id && id > 0
        ? id : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private (string? Remark, List<OpeningStockLine> Lines) Validate(OpeningStockInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);

        if (input.RequestKey is null || !RequestKeyPattern().IsMatch(input.RequestKey))
            Fail("requestKey", "Request key must be 1 to 64 letters, digits, hyphens or underscores.");
        if (input.OpeningDate == default) Fail("openingDate", "Opening date is required.");
        else if (input.OpeningDate > clock.Today) Fail("openingDate", "Opening date must not be later than today.");
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (remark?.Length > MaxRemarkLength) Fail("remark", $"Remark must not exceed {MaxRemarkLength} characters.");
        var lines = new List<OpeningStockLine>();
        var total = 0m;
        if (input.Lines is not { Count: > 0 and <= MaxLines })
        {
            Fail("lines", $"Opening stock must contain 1 to {MaxLines} lines.");
        }
        else
        {
            for (var i = 0; i < input.Lines.Count; i++)
            {
                var line = input.Lines[i];
                var field = $"lines[{i}]";
                if (line is null) { Fail(field, "Line is required."); continue; }
                var valid = true;
                void LineFail(string name, string message) { valid = false; Fail($"{field}.{name}", message); }
                if (line.LineNo <= 0) LineFail("lineNo", "Line number must be positive.");
                if (line.ItemId <= 0) LineFail("itemId", "Item is required.");
                if (line.WarehouseId <= 0) LineFail("warehouseId", "Warehouse is required.");
                if (line.LocationId is <= 0) LineFail("locationId", "Location id must be positive.");
                if (!IsStorable(line.Quantity) || line.Quantity <= 0)
                    LineFail("quantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
                if (!IsStorable(line.UnitCost) || line.UnitCost < 0)
                    LineFail("unitCost", "Unit cost must be non-negative, below 10^13 and have at most 6 decimals.");
                if (!valid) continue;
                var amount = OtherReceiptLine.AmountOf(line.Quantity, line.UnitCost);
                total += amount;
                lines.Add(new OpeningStockLine
                {
                    LineNo = line.LineNo, ItemId = line.ItemId, WarehouseId = line.WarehouseId, LocationId = line.LocationId,
                    Quantity = line.Quantity, UnitCost = line.UnitCost, Amount = amount,
                });
                // 逐行累計並提早停止，千行極大金額不會 decimal 溢位（同 INV-ISS-005）
                if (total >= MaxAmount) { Fail("lines", "Opening stock amount must be below 10^15."); break; }
            }
            if (input.Lines.Where(x => x is not null).GroupBy(x => x.LineNo).Any(g => g.Count() > 1))
                Fail("lines", "Line numbers must be unique.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, lines);
    }

    private static bool IsStorable(decimal value) => value > -MaxQuantity && value < MaxQuantity && decimal.Round(value, 6) == value;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RequestKeyPattern();
}
