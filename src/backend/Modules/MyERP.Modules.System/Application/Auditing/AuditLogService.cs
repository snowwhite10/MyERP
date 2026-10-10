using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Auditing;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.System.Application.Auditing;

/// <summary>
/// 稽核查詢條件。日期為 <c>IClock</c> 時區的日期（含頭尾）；<c>tableName</c>＋<c>recordId</c> 用於查看單據的完整修改歷史。
/// 排序只支援 <c>occurredAt</c>／<c>-occurredAt</c>（預設新到舊）。
/// </summary>
public sealed record AuditLogQuery : PagedRequest
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    /// <summary><c>DATA</c> 或 <c>SECURITY</c>。</summary>
    public string? Category { get; init; }

    /// <summary>例如 <c>UPDATE</c>、<c>LOGIN_FAILED</c>。</summary>
    public string? Action { get; init; }

    public string? TableName { get; init; }

    public string? RecordId { get; init; }

    public long? UserId { get; init; }
}

/// <summary>稽核記錄。<see cref="Changes"/> 為欄位差異 <c>{"欄位":{"old":…,"new":…}}</c> 或安全事件明細；敏感欄位值為 <c>***</c>。</summary>
public sealed record AuditLogItem(
    long Id,
    DateTime OccurredAt,
    string Category,
    string Action,
    string? TableName,
    string? RecordId,
    long? UserId,
    string? Username,
    long? CompanyId,
    JsonElement? Changes,
    string? RemoteIp,
    string? TraceId);

/// <summary>
/// 稽核日誌查詢（REQ_F4118.27.4）。可見範圍（ADR-019）：屬於目前公司的記錄，以及沒有公司的安全事件中、用戶屬於目前公司者
/// （例如該公司用戶的登入、登出）。帳號不存在的登入失敗只留在資料庫與應用程式日誌。
/// </summary>
public sealed class AuditLogService(MyErpDbContext db)
{
    private static readonly string[] SortFields = ["occurredAt"];
    private static readonly string[] Categories = [AuditCategories.Data, AuditCategories.Security];

    public async Task<PagedResult<AuditLogItem>> ListAsync(AuditLogQuery query, long companyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.EnsureValid();
        Validate(query);
        var descending = SortSpec.Parse(query.Sort, SortFields, "-occurredAt") is [{ Descending: true }];

        var logs = db.Set<AuditLogEntry>().AsNoTracking().Where(e =>
            e.CompanyId == companyId
            || (e.CompanyId == null && e.UserId != null
                && db.Set<SystemUserCompany>().Any(uc => uc.UserId == e.UserId && uc.CompanyId == companyId)));
        if (query.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            logs = logs.Where(e => e.OccurredAt >= start);
        }

        if (query.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            logs = logs.Where(e => e.OccurredAt < end);
        }

        if (Normalize(query.Category) is { } category)
        {
            logs = logs.Where(e => e.Category == category);
        }

        if (Normalize(query.Action) is { } action)
        {
            logs = logs.Where(e => e.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.TableName))
        {
            var table = query.TableName.Trim().ToLowerInvariant();
            logs = logs.Where(e => e.TableName == table);
        }

        if (!string.IsNullOrWhiteSpace(query.RecordId))
        {
            var recordId = query.RecordId.Trim();
            logs = logs.Where(e => e.RecordId == recordId);
        }

        if (query.UserId is { } userId)
        {
            logs = logs.Where(e => e.UserId == userId);
        }

        var count = await logs.LongCountAsync(cancellationToken);
        var ordered = descending
            ? logs.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            : logs.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id);
        var rows = await ordered.Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);

        // 資料變更只存 user_id，帳號以第二次查詢補上（含已刪除用戶），固定 3 次查詢
        var userIds = rows.Where(e => e.Username is null && e.UserId is > 0).Select(e => e.UserId!.Value).Distinct().ToArray();
        var usernames = userIds.Length == 0
            ? new Dictionary<long, string>()
            : await db.Set<SystemUser>().IgnoreQueryFilters().AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);
        var items = rows
            .Select(e => ToItem(e, e.Username ?? (e.UserId is { } id && usernames.TryGetValue(id, out var name) ? name : null)))
            .ToList();
        return PagedResult.Create(items, query, count);
    }

    private static void Validate(AuditLogQuery query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.From is { } from && query.To is { } to && from > to)
        {
            errors["to"] = ["to must be on or after from."];
        }

        if (Normalize(query.Category) is { } category && !Categories.Contains(category, StringComparer.Ordinal))
        {
            errors["category"] = [$"category must be one of {string.Join(", ", Categories)}."];
        }

        if (query.Action is { Length: > 30 })
        {
            errors["action"] = ["action must be at most 30 characters."];
        }

        if (query.TableName is { Length: > 128 })
        {
            errors["tableName"] = ["tableName must be at most 128 characters."];
        }

        if (query.RecordId is { Length: > 100 })
        {
            errors["recordId"] = ["recordId must be at most 100 characters."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static AuditLogItem ToItem(AuditLogEntry entry, string? username) => new(
        entry.Id,
        entry.OccurredAt,
        entry.Category,
        entry.Action,
        entry.TableName,
        entry.RecordId,
        entry.UserId,
        username,
        entry.CompanyId,
        ParseJson(entry.Changes),
        entry.RemoteIp,
        entry.TraceId);

    private static JsonElement? ParseJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
