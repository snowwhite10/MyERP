using System.Globalization;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace MyERP.Shared.Infrastructure.Security;

/// <summary>固定視窗限流設定：每個分區在 <see cref="Window"/> 內最多 <see cref="PermitLimit"/> 個請求。</summary>
public sealed record RateLimitRule(int PermitLimit, TimeSpan Window);

/// <summary>
/// 安全中介層設定（<c>Security</c> 區段，architecture/security.md）。啟動時一次讀取並驗證，
/// 設定錯誤即拒絕啟動，例外訊息含設定鍵名稱。
/// </summary>
public sealed class WebSecurityOptions
{
    public const string CorsOriginsKey = "Security:Cors:AllowedOrigins";
    public const string KnownProxiesKey = "Security:ForwardedHeaders:KnownProxies";
    public const string KnownNetworksKey = "Security:ForwardedHeaders:KnownNetworks";
    public const string ForwardLimitKey = "Security:ForwardedHeaders:ForwardLimit";
    public const string RateLimitingKey = "Security:RateLimiting";
    public const string HstsMaxAgeDaysKey = "Security:Hsts:MaxAgeDays";
    public const string IpWhitelistEnabledKey = "Security:IpWhitelist:Enabled";

    public required IReadOnlyList<string> AllowedOrigins { get; init; }

    public required IReadOnlyList<IPAddress> KnownProxies { get; init; }

    public required IReadOnlyList<IPNetwork> KnownNetworks { get; init; }

    public required int ForwardLimit { get; init; }

    /// <summary>沒有設定任何 Proxy 時不啟用 ForwardedHeaders（框架在清單皆空時會信任所有來源）。</summary>
    public bool TrustsProxies => KnownProxies.Count > 0 || KnownNetworks.Count > 0;

    /// <summary>登入、Token 刷新：每 IP 每端點。</summary>
    public required RateLimitRule Auth { get; init; }

    /// <summary>Captcha：每 IP。每次登入嘗試失敗後前端會再取一題，故比登入寬鬆。</summary>
    public required RateLimitRule Captcha { get; init; }

    /// <summary>已登入的一般 API：每用戶。</summary>
    public required RateLimitRule User { get; init; }

    /// <summary>匯出報表：每用戶（未登入時每 IP）。</summary>
    public required RateLimitRule Export { get; init; }

    public required int HstsMaxAgeDays { get; init; }

    /// <summary>
    /// IP 白名單總開關（未設定時為開啟；appsettings.Development.json 關閉）。緊急解鎖只能改此設定（MASTER_PROMPT 第 7 節）。
    /// </summary>
    public bool IpWhitelistEnabled { get; init; } = true;

    public static WebSecurityOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new WebSecurityOptions
        {
            AllowedOrigins = Values(configuration, CorsOriginsKey).Select(ParseOrigin).ToArray(),
            KnownProxies = Values(configuration, KnownProxiesKey)
                .Select(v => IPAddress.TryParse(v, out var ip) ? ip : throw Invalid(KnownProxiesKey, v, "an IP address"))
                .ToArray(),
            KnownNetworks = Values(configuration, KnownNetworksKey)
                .Select(v => IPNetwork.TryParse(v, out var network) ? network : throw Invalid(KnownNetworksKey, v, "a CIDR network"))
                .ToArray(),
            ForwardLimit = PositiveInt(configuration, ForwardLimitKey, 1),
            Auth = Rule(configuration, "Auth", 5),
            Captcha = Rule(configuration, "Captcha", 10),
            User = Rule(configuration, "User", 300),
            Export = Rule(configuration, "Export", 10),
            HstsMaxAgeDays = PositiveInt(configuration, HstsMaxAgeDaysKey, 365),
            IpWhitelistEnabled = Boolean(configuration, IpWhitelistEnabledKey, true),
        };
    }

    private static bool Boolean(IConfiguration configuration, string key, bool defaultValue)
    {
        var text = configuration[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        return bool.TryParse(text.Trim(), out var value) ? value : throw Invalid(key, text, "true or false");
    }

    private static IEnumerable<string> Values(IConfiguration configuration, string key) =>
        configuration.GetSection(key).GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!);

    // 來源必須是 scheme://host[:port]，不得含路徑或萬用字元；比對時瀏覽器送出的 Origin 不含結尾斜線
    private static string ParseOrigin(string value)
    {
        if (value.Contains('*', StringComparison.Ordinal)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.PathAndQuery != "/"
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Invalid(CorsOriginsKey, value, "an origin such as https://erp.example.com (wildcards are not allowed)");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static RateLimitRule Rule(IConfiguration configuration, string name, int defaultLimit)
    {
        var key = $"{RateLimitingKey}:{name}";
        return new RateLimitRule(
            PositiveInt(configuration, $"{key}:PermitLimit", defaultLimit),
            TimeSpan.FromSeconds(PositiveInt(configuration, $"{key}:WindowSeconds", 60)));
    }

    private static int PositiveInt(IConfiguration configuration, string key, int defaultValue)
    {
        var text = configuration[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw Invalid(key, text, "a positive integer");
    }

    private static InvalidOperationException Invalid(string key, string value, string expected) =>
        new($"Configuration '{key}' value '{value}' is invalid: expected {expected}.");
}
