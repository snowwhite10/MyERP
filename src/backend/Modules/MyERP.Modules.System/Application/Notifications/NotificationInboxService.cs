using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.System.Application.Notifications;

public sealed record NotificationQuery : PagedRequest
{
    public bool UnreadOnly { get; init; }
}

public sealed record NotificationItem(long Id, string Category, string Title, string Body, string? TargetPath,
    DateTime CreatedAt, DateTime? ReadAt);

/// <summary>目前登入用戶與公司專屬的通知收件箱。</summary>
public sealed class NotificationInboxService(MyErpDbContext db, ICurrentUser user, IClock clock)
{
    private long UserId => user.UserId ?? throw new UnauthenticatedException("COMMON.UNAUTHENTICATED", "Authentication is required.");
    private IQueryable<SystemNotification> Active() => db.Set<SystemNotification>()
        .Where(x => x.RecipientUserId == UserId && x.ExpiresAt > clock.LocalNow);

    public async Task<PagedResult<NotificationItem>> ListAsync(NotificationQuery request, CancellationToken ct = default)
    {
        request.EnsureValid();
        if (!string.IsNullOrEmpty(request.Sort) || !string.IsNullOrEmpty(request.Keyword))
            throw new RequestValidationException("sort", "Notification sorting and keyword search are not supported.");
        var query = Active().AsNoTracking();
        if (request.UnreadOnly) query = query.Where(x => x.ReadAt == null);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new NotificationItem(x.Id, x.Category, x.Title, x.Body, x.TargetPath, x.CreatedAt, x.ReadAt))
            .ToListAsync(ct);
        return PagedResult.Create(items, request, total);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct = default) =>
        Active().CountAsync(x => x.ReadAt == null, ct);

    public async Task MarkReadAsync(long id, CancellationToken ct = default)
    {
        var item = await Active().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("SYS.NOTIFICATION.NOT_FOUND", "Notification", id);
        if (item.ReadAt != null) return;
        item.ReadAt = clock.LocalNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 標示全部未讀為已讀；回傳更新筆數。刻意逐筆追蹤而不用 ExecuteUpdate：通知是業務表，每筆變更須經稽核攔截器寫入資料稽核
    /// 並更新版本欄位（code-review-log SYS-B06）。數量受 90 天保留期限制。
    /// </summary>
    public async Task<int> MarkAllReadAsync(CancellationToken ct = default)
    {
        var items = await Active().Where(x => x.ReadAt == null).ToListAsync(ct);
        var readAt = clock.LocalNow;
        foreach (var item in items) item.ReadAt = readAt;
        if (items.Count > 0) await db.SaveChangesAsync(ct);
        return items.Count;
    }

    public Task<int> DeleteExpiredAsync(CancellationToken ct = default) =>
        db.Set<SystemNotification>().IgnoreQueryFilters().Where(x => x.ExpiresAt <= clock.LocalNow)
            .ExecuteDeleteAsync(ct);
}
