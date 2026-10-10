using Microsoft.Extensions.Logging;

namespace MyERP.Modules.System.Application.Auth;

/// <summary>登入相關日誌。不得記錄密碼、Captcha 答案或任何 Token（MASTER_PROMPT 8.1）。</summary>
internal static partial class AuthLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged in from {RemoteIp}")]
    public static partial void LoggedIn(ILogger logger, long userId, string remoteIp);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged out")]
    public static partial void LoggedOut(ILogger logger, long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login failed for {Username} from {RemoteIp}")]
    public static partial void LoginFailed(ILogger logger, string username, string remoteIp);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for user {UserId}: password expired")]
    public static partial void PasswordExpired(ILogger logger, long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Captcha master code used for login {Username} from {RemoteIp}")]
    public static partial void MasterCodeUsed(ILogger logger, string username, string remoteIp);

    [LoggerMessage(Level = LogLevel.Error, Message = "Captcha:MasterCode is configured in environment {Environment}; startup refused.")]
    public static partial void MasterCodeOutsideDevelopment(ILogger logger, string environment);

    [LoggerMessage(Level = LogLevel.Information, Message = "Security:Jwt:SigningKey is not configured; using an ephemeral development key.")]
    public static partial void EphemeralJwtKey(ILogger logger);
}
