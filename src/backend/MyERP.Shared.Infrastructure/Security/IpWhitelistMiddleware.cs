using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.Shared.Infrastructure.Security;

/// <summary>
/// IP 白名單（MASTER_PROMPT 第 7 節、ADR-025）：每個請求以連線 IP（經 ForwardedHeaders 處理後）檢查；未登入請求只套用全系統層級，
/// 已登入請求另套用用戶與角色層級。不符合時回 403 <c>COMMON.REQUEST.IP_NOT_ALLOWED</c>。健康檢查不受限制（負載平衡器探測）。
/// 規則與總開關由 <see cref="IIpAccessPolicy"/>（SYS 模組）提供；未註冊時不檢查。
/// </summary>
public sealed class IpWhitelistMiddleware(RequestDelegate next)
{
    private static readonly PathString HealthPath = new("/health");

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase)
            && context.RequestServices.GetService<IIpAccessPolicy>() is { } policy)
        {
            var user = context.RequestServices.GetRequiredService<ICurrentUser>();
            var request = new IpAccessRequest(context.Connection.RemoteIpAddress, user.UserId, null, context.Request.Path.Value ?? "/");
            if (!await policy.IsAllowedAsync(request, context.RequestAborted).ConfigureAwait(false))
            {
                throw new ForbiddenException(CommonErrorCodes.IpNotAllowed, "Requests from this IP address are not allowed.");
            }
        }

        await next(context).ConfigureAwait(false);
    }
}

public static class IpWhitelistApplicationBuilderExtensions
{
    /// <summary>在 <c>UseAuthentication</c> 與 <c>UseRateLimiter</c> 之後呼叫：白名單內的 IP 仍受限流，被拒絕的 IP 也計入限流。</summary>
    public static IApplicationBuilder UseIpWhitelist(this IApplicationBuilder app) => app.UseMiddleware<IpWhitelistMiddleware>();
}
