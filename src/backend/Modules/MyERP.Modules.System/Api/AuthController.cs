using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using MyERP.Modules.System.Application.Auth;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Api;

public sealed record CaptchaResponse(string CaptchaId, string Image, int Length, int ExpiresInSeconds);

public sealed record CurrentUserResponse(long UserId, string Username);

/// <summary>登入、Captcha、Token 刷新與登出（api.md「登入與 Token」）。匿名端點按 IP 限流，超過回 429（WU-SYS-005）。</summary>
[ApiController]
[Route("api/v1/sys/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthService auth, CaptchaService captcha, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>取得數字 Captcha 圖片（SVG data URI）；答案只存在伺服器端。</summary>
    [HttpGet("captcha")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Captcha)]
    [ProducesResponseType<CaptchaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<CaptchaResponse> GetCaptcha(CancellationToken cancellationToken)
    {
        var challenge = await captcha.CreateAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return new CaptchaResponse(challenge.Id, CaptchaImage.ToDataUri(challenge.Code), challenge.Code.Length, challenge.ExpiresInSeconds);
    }

    /// <summary>帳密 + Captcha 登入，回傳 Access Token（15 分鐘）與 Refresh Token（7 天）。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<AuthTokens> Login(LoginRequest request, CancellationToken cancellationToken) =>
        auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

    /// <summary>以 Refresh Token 換發新的一組 Token；舊 Refresh Token 立即失效。</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<AuthTokens> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        auth.RefreshAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

    /// <summary>登出：撤銷目前用戶的 Refresh Token。</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(currentUser.UserId!.Value, request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// 修改本人密碼（介面 4.4.1.1.3）：須輸入目前密碼；成功後之前簽發的 Refresh Token 全部失效，回傳新的一組 Token。
    /// 目前密碼錯誤回 400 <c>SYS.AUTH.CURRENT_PASSWORD_INVALID</c> 並計入失敗次數（5 次鎖定 15 分鐘）。
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AuthTokens> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        auth.ChangePasswordAsync(currentUser.UserId!.Value, request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

    /// <summary>
    /// 密碼到期（登入回 403 <c>SYS.AUTH.PASSWORD_EXPIRED</c>）時以帳號、目前密碼與 Captcha 修改密碼；成功即登入並回傳 Token。
    /// 帳密錯誤與登入相同回 401 <c>SYS.AUTH.INVALID_CREDENTIALS</c>。
    /// </summary>
    [HttpPost("change-expired-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<AuthTokens> ChangeExpiredPassword(ChangeExpiredPasswordRequest request, CancellationToken cancellationToken) =>
        auth.ChangeExpiredPasswordAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

    /// <summary>目前登入用戶。</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public CurrentUserResponse Me() =>
        new(currentUser.UserId!.Value, User.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? "");
}
