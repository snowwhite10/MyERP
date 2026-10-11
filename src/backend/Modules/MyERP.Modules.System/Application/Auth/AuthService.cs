using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Auditing;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;
using IIpAccessPolicy = MyERP.Shared.Security.IIpAccessPolicy;
using IpAccessRequest = MyERP.Shared.Security.IpAccessRequest;

namespace MyERP.Modules.System.Application.Auth;

public sealed record LoginRequest(string? Username, string? Password, string? CaptchaId, string? CaptchaCode);

public sealed record RefreshRequest(string? RefreshToken);

/// <summary>登入／刷新回應。時間為 <see cref="IClock"/> 時區。</summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string TokenType = "Bearer");

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(1024);
        RuleFor(x => x.CaptchaId).MaximumLength(64);
        RuleFor(x => x.CaptchaCode).NotEmpty().Length(4, 6).Matches("^[0-9]+$");
    }
}

/// <summary>Captcha 錯誤或過期（400）。</summary>
public sealed class CaptchaInvalidException()
    : ErpException(AuthErrorCodes.CaptchaInvalid, "The captcha code is invalid or has expired.")
{
    public override ErpErrorKind Kind => ErpErrorKind.Validation;
}

public static class AuthErrorCodes
{
    public const string CaptchaInvalid = "SYS.AUTH.CAPTCHA_INVALID";
    public const string InvalidCredentials = "SYS.AUTH.INVALID_CREDENTIALS";
    public const string PasswordExpired = "SYS.AUTH.PASSWORD_EXPIRED";
    public const string RefreshInvalid = "SYS.AUTH.REFRESH_INVALID";
}

/// <summary>
/// 登入、刷新與登出（REQ_F4118.24.x）。Access Token 為無狀態 JWT；Refresh Token 只以 SHA-256 雜湊作 Redis 鍵，
/// 使用一次即輪替。帳密錯誤、帳號不存在、停用或鎖定一律回同一錯誤，不洩漏帳號狀態。
/// </summary>
public sealed class AuthService(
    MyErpDbContext db,
    SystemUserService users,
    IPasswordHasher<SystemUser> hasher,
    CaptchaService captcha,
    IOneTimeStore store,
    IOptions<JwtOptions> jwtOptions,
    IClock clock,
    IIpAccessPolicy ipPolicy,
    IAuditTrail audit,
    ILogger<AuthService> logger)
{
    private const string RefreshKeyPrefix = "sys:refresh:";
    private static readonly LoginRequestValidator LoginValidator = new();

    public async Task<AuthTokens> LoginAsync(LoginRequest request, string? remoteIp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = await LoginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new RequestValidationException(validation.Errors
                .GroupBy(e => char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }

        var username = request.Username!.Trim();
        var verification = await captcha.VerifyAsync(request.CaptchaId, request.CaptchaCode, username, remoteIp, cancellationToken);
        if (verification == CaptchaVerification.Invalid)
        {
            throw new CaptchaInvalidException();
        }

        var normalized = username.ToUpperInvariant();
        var user = await db.Set<SystemUser>().AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, cancellationToken);
        if (verification == CaptchaVerification.MasterCode)
        {
            // 萬用碼登入一律寫稽核（帳號、IP、時間；REQ_F4118.24.4），不論之後帳密是否正確
            audit.AddSecurityEvent(new SecurityAuditEvent(AuditActions.MasterCodeLogin, user?.Id, username));
        }

        if (user is null)
        {
            // 對不存在帳號也做一次雜湊驗證，使回應時間接近，避免以時間差探測帳號
            hasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password!);
            throw await LoginFailedAsync(username, null, remoteIp, cancellationToken);
        }
        if (!await users.VerifyPasswordAsync(user.Id, request.Password!, cancellationToken))
        {
            throw await LoginFailedAsync(username, user.Id, remoteIp, cancellationToken);
        }

        var verified = await db.Set<SystemUser>().AsNoTracking().SingleAsync(x => x.Id == user.Id, cancellationToken);
        if (users.IsPasswordExpired(verified))
        {
            AuthLog.PasswordExpired(logger, verified.Id);
            await WriteSecurityEventAsync(AuditActions.LoginFailed, verified.Id, verified.Username, PasswordExpiredReason, cancellationToken);
            throw new ForbiddenException(AuthErrorCodes.PasswordExpired, "The password has expired and must be changed.");
        }

        // 匿名登入請求的中介層只能檢查全系統層級；驗證身分後、發 Token 前再檢查用戶與角色層級。
        if (!await ipPolicy.IsAllowedAsync(new IpAccessRequest(ParseIp(remoteIp), verified.Id, verified.Username,
                "/api/v1/sys/auth/login"), cancellationToken))
            throw new ForbiddenException(CommonErrorCodes.IpNotAllowed, "Requests from this IP address are not allowed.");

        AuthLog.LoggedIn(logger, verified.Id, remoteIp ?? "unknown");
        await WriteSecurityEventAsync(AuditActions.Login, verified.Id, verified.Username, null, cancellationToken);
        return await IssueAsync(verified, cancellationToken);
    }

    public async Task<AuthTokens> RefreshAsync(RefreshRequest request, string? remoteIp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = RefreshKey(request.RefreshToken);
        var stored = key is null ? null : await store.TakeAsync(key, cancellationToken);
        if (stored is null || !long.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            throw new UnauthenticatedException(AuthErrorCodes.RefreshInvalid, "The refresh token is invalid, expired or already used.");
        }

        var now = clock.LocalNow;
        var user = await db.Set<SystemUser>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || user.LockoutEnd > now)
        {
            throw new UnauthenticatedException(AuthErrorCodes.RefreshInvalid, "The refresh token is invalid, expired or already used.");
        }
        if (!await ipPolicy.IsAllowedAsync(new IpAccessRequest(ParseIp(remoteIp), user.Id, user.Username,
                "/api/v1/sys/auth/refresh"), cancellationToken))
            throw new ForbiddenException(CommonErrorCodes.IpNotAllowed, "Requests from this IP address are not allowed.");
        return await IssueAsync(user, cancellationToken);
    }

    private static IPAddress? ParseIp(string? value) => IPAddress.TryParse(value, out var address) ? address : null;

    /// <summary>撤銷屬於 <paramref name="userId"/> 的 Refresh Token；他人或無效的 Token 靜默忽略。</summary>
    public async Task LogoutAsync(long userId, RefreshRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = RefreshKey(request.RefreshToken);
        if (key is not null)
        {
            await store.RemoveIfAsync(key, userId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        }
        AuthLog.LoggedOut(logger, userId);
        await WriteSecurityEventAsync(AuditActions.Logout, userId, null, null, cancellationToken);
    }

    /// <summary>
    /// 安全事件立即寫入 sys_audit_log。登入流程只以 AsNoTracking 或已 Reload 的方式讀取，ChangeTracker 沒有其他待存變更。
    /// </summary>
    private async Task WriteSecurityEventAsync(string action, long? userId, string? username, string? reason, CancellationToken cancellationToken)
    {
        audit.AddSecurityEvent(new SecurityAuditEvent(action, userId, username, null,
            reason is null ? null : new Dictionary<string, object?> { ["reason"] = reason }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthTokens> IssueAsync(SystemUser user, CancellationToken cancellationToken)
    {
        var settings = jwtOptions.Value;
        var now = clock.Now;
        var accessExpires = now.AddMinutes(settings.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = accessExpires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Name, user.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey(settings), SecurityAlgorithms.HmacSha256),
        };
        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);

        var refreshToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var refreshLifetime = TimeSpan.FromDays(settings.RefreshTokenDays);
        await store.SetAsync(RefreshKey(refreshToken)!, user.Id.ToString(CultureInfo.InvariantCulture), refreshLifetime, cancellationToken);
        return new AuthTokens(accessToken, accessExpires, refreshToken, now.Add(refreshLifetime));
    }

    /// <summary>JwtBearer 驗證與簽發共用的金鑰。</summary>
    public static SymmetricSecurityKey SigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey ?? throw new InvalidOperationException("Security:Jwt:SigningKey is not configured.")));

    private static string? RefreshKey(string? token) =>
        string.IsNullOrEmpty(token) || token.Length > 128
            ? null
            : RefreshKeyPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<UnauthenticatedException> LoginFailedAsync(string username, long? userId, string? remoteIp, CancellationToken cancellationToken)
    {
        AuthLog.LoginFailed(logger, username, remoteIp ?? "unknown");
        await WriteSecurityEventAsync(AuditActions.LoginFailed, userId, username, InvalidCredentialsReason, cancellationToken);
        return new UnauthenticatedException(AuthErrorCodes.InvalidCredentials, "The username or password is incorrect.");
    }

    private const string InvalidCredentialsReason = "INVALID_CREDENTIALS";
    private const string PasswordExpiredReason = "PASSWORD_EXPIRED";

    private static readonly SystemUser DummyUser = new() { Username = "", NormalizedUsername = "", PasswordHash = "" };
    private static readonly string DummyHash = new PasswordHasher<SystemUser>().HashPassword(DummyUser, "MyERP-dummy-password");
}
