using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.Notifications;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.System.Api;

/// <summary>目前登入用戶在目前公司的站內通知（REQ_F4118.33.2–3）。</summary>
[ApiController]
[Route("api/v1/sys/notifications")]
[Produces("application/json")]
[Tags("SYS - Notifications")]
public sealed class NotificationsController(NotificationInboxService inbox) : ControllerBase
{
    /// <summary>依建立時間新到舊分頁；可用 unreadOnly 篩選，過期通知不回傳。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.NotificationView)]
    [ProducesResponseType<PagedResult<NotificationItem>>(StatusCodes.Status200OK)]
    public Task<PagedResult<NotificationItem>> List([FromQuery] NotificationQuery query, CancellationToken ct) => inbox.ListAsync(query, ct);

    /// <summary>目前公司與用戶的未讀通知數。</summary>
    [HttpGet("unread-count")]
    [RequirePermission(SystemPermissions.NotificationView)]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK)]
    public async Task<UnreadCountResponse> UnreadCount(CancellationToken ct) => new(await inbox.UnreadCountAsync(ct));

    /// <summary>標記本人的單筆未到期通知；重複操作成功，其他通知回 404。</summary>
    [HttpPost("{id:long}/read")]
    [RequirePermission(SystemPermissions.NotificationRead)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        await inbox.MarkReadAsync(id, ct);
        return NoContent();
    }

    /// <summary>標記目前公司本人的所有未到期通知。</summary>
    [HttpPost("read-all")]
    [RequirePermission(SystemPermissions.NotificationRead)]
    [ProducesResponseType<ReadAllResponse>(StatusCodes.Status200OK)]
    public async Task<ReadAllResponse> MarkAllRead(CancellationToken ct) => new(await inbox.MarkAllReadAsync(ct));
}

public sealed record UnreadCountResponse(int Count);
public sealed record ReadAllResponse(int UpdatedCount);
