using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using MyERP.Modules.System.Application.Auth;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Api;

public sealed record CaptchaResponse(string CaptchaId, string Image, int Length, int ExpiresInSeconds);

public sealed record CurrentUserResponse(long UserId, string Username);

/// <summary>登入、Captcha、Token 刷新與登出（api.md「登入與 Token」）。登入類端點限流於 WU-SYS-005 加入。</summary>
[ApiController]
[Route("api/v1/sys/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthService auth, CaptchaService captcha, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>取得數字 Captcha 圖片（SVG data URI）；答案只存在伺服器端。</summary>
    [HttpGet("captcha")]
    [AllowAnonymous]
    [ProducesResponseType<CaptchaResponse>(StatusCodes.Status200OK)]
    public async Task<CaptchaResponse> GetCaptcha(CancellationToken cancellationToken)
    {
        var challenge = await captcha.CreateAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return new CaptchaResponse(challenge.Id, CaptchaImage.ToDataUri(challenge.Code), challenge.Code.Length, challenge.ExpiresInSeconds);
    }

    /// <summary>帳密 + Captcha 登入，回傳 Access Token（15 分鐘）與 Refresh Token（7 天）。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<AuthTokens> Login(LoginRequest request, CancellationToken cancellationToken) =>
        auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

    /// <summary>以 Refresh Token 換發新的一組 Token；舊 Refresh Token 立即失效。</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthTokens> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        auth.RefreshAsync(request, cancellationToken);

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

    /// <summary>目前登入用戶。</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public CurrentUserResponse Me() =>
        new(currentUser.UserId!.Value, User.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? "");
}
