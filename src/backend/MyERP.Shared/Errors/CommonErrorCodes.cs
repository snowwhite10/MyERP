namespace MyERP.Shared.Errors;

/// <summary>跨模組共用的錯誤代碼（前端 i18n key：<c>errors.COMMON.*</c>）。模組專屬代碼以模組前綴開頭。</summary>
public static class CommonErrorCodes
{
    public const string ValidationFailed = "COMMON.REQUEST.VALIDATION_FAILED";
    public const string ConcurrencyConflict = "COMMON.CONCURRENCY.CONFLICT";
    public const string NotFound = "COMMON.RESOURCE.NOT_FOUND";
    public const string InternalError = "COMMON.SERVER.INTERNAL_ERROR";
    public const string RateLimited = "COMMON.REQUEST.RATE_LIMITED";

    /// <summary>連線 IP 不在 IP 白名單內（403）。</summary>
    public const string IpNotAllowed = "COMMON.REQUEST.IP_NOT_ALLOWED";
}
