using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MyERP.Shared.Security;

namespace MyERP.Shared.Infrastructure.Web;

/// <summary>由已驗證的 JWT 取得用戶 Id（<c>sub</c>，或 <see cref="ClaimTypes.NameIdentifier"/>）。</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public long? UserId
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var value = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
        }
    }

    public bool IsAuthenticated => UserId is not null;
}

/// <summary>
/// 由請求標頭 <c>X-Company-Id</c> 取得目前公司。只做格式解析；用戶是否可存取該公司由 SYS 模組的授權中介層（WU-SYS-004）驗證，
/// 未驗證前不得把本值當作授權依據。
/// </summary>
public sealed class HttpCurrentCompany(IHttpContextAccessor accessor) : ICurrentCompany
{
    public const string HeaderName = "X-Company-Id";

    public long? CompanyId
    {
        get
        {
            var values = accessor.HttpContext?.Request.Headers[HeaderName];
            return values is { Count: 1 } && long.TryParse(values.Value[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                ? id
                : null;
        }
    }
}
