using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace MyERP.Shared.Security;

/// <summary>IP 白名單層級（MASTER_PROMPT 第 7 節）：全系統、按角色、按用戶。API 與資料庫皆以名稱字串表示。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IpAccessScope>))]
public enum IpAccessScope
{
    System,
    Role,
    User,
}

/// <summary>
/// 單一 IP 或 CIDR 網段（IPv4／IPv6）。<see cref="ToString"/> 為正規化文字：單一 IP 不帶前綴長度（<c>10.0.0.1</c>、<c>2001:db8::1</c>），
/// 網段為 <c>10.0.0.0/8</c>；網段的主機位元必須為 0（避免 <c>10.1.2.3/8</c> 令人誤以為只允許一台主機）。
/// </summary>
public readonly record struct IpRange
{
    private IpRange(IPNetwork network) => Network = network;

    public IPNetwork Network { get; }

    /// <summary>正規化文字最長 43 字（IPv6 39 + <c>/128</c>）。</summary>
    public const int MaxLength = 43;

    public static bool TryParse(string? value, [NotNullWhen(true)] out IpRange? range)
    {
        range = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || text.Contains('%', StringComparison.Ordinal))
        {
            return false;
        }

        if (!text.Contains('/', StringComparison.Ordinal))
        {
            if (!IPAddress.TryParse(text, out var address) || !IsPlainAddress(text, address))
            {
                return false;
            }

            address = Normalize(address);
            range = new IpRange(new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128));
            return true;
        }

        var slash = text.IndexOf('/');
        var addressText = text[..slash];
        if (!IPAddress.TryParse(addressText, out var suppliedAddress)
            || !IsPlainAddress(addressText, suppliedAddress)
            || !IPNetwork.TryParse(text, out var network)
            || !suppliedAddress.Equals(network.BaseAddress))
        {
            return false;
        }

        var baseAddress = Normalize(network.BaseAddress);
        if (!baseAddress.Equals(network.BaseAddress))
        {
            return false;
        }

        range = new IpRange(network);
        return true;
    }

    /// <summary>解析並正規化；格式不符時拋出 <see cref="FormatException"/>。</summary>
    public static IpRange Parse(string? value) =>
        TryParse(value, out var range) ? range.Value : throw new FormatException($"'{value}' is not an IP address or CIDR network.");

    public bool Contains(IPAddress? address) => address is not null && Network.Contains(Normalize(address));

    public override string ToString() =>
        Network.PrefixLength == (Network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)
            ? Network.BaseAddress.ToString()
            : Network.ToString();

    /// <summary>IPv4 對映的 IPv6（<c>::ffff:10.0.0.1</c>）視為 IPv4，雙堆疊主機上的連線位址才能比對 IPv4 規則。</summary>
    public static IPAddress Normalize(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    // IPAddress.TryParse 接受 "1"、"1.2" 等簡寫與十六進位，白名單只接受完整寫法
    private static bool IsPlainAddress(string text, IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 || address.ToString() == text;
}

/// <summary>一條有效的白名單規則；<see cref="TargetId"/> 為角色或用戶 Id（全系統為 null）；有效期限為含頭尾的日期。</summary>
public sealed record IpAccessRule(IpAccessScope Scope, long? TargetId, IpRange Range, DateOnly? ValidFrom, DateOnly? ValidTo)
{
    public bool IsEffectiveOn(DateOnly today) => (ValidFrom is null || ValidFrom <= today) && (ValidTo is null || ValidTo >= today);
}

/// <summary>
/// 白名單判斷（ADR-025）：每個層級「有設定」（有今天有效的規則）時，IP 必須符合該層級其中一條規則；
/// 全系統、用戶、用戶擁有的每個角色各自獨立檢查，全部通過才允許——即以較嚴格者為準。
/// 沒有任何設定時允許；loopback（<c>127.0.0.0/8</c>、<c>::1</c>）永遠允許；沒有連線 IP 而有設定時拒絕（封閉失敗）。
/// </summary>
public static class IpAccessRules
{
    /// <summary>允許時回傳 null，否則回傳第一個不符合的層級。</summary>
    public static IpAccessScope? Evaluate(
        IPAddress? address,
        IEnumerable<IpAccessRule> rules,
        long? userId,
        IReadOnlyCollection<long> roleIds,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(roleIds);
        var ip = address is null ? null : IpRange.Normalize(address);
        if (ip is not null && IPAddress.IsLoopback(ip))
        {
            return null;
        }

        var effective = rules.Where(r => r.IsEffectiveOn(today)).ToList();
        if (Fails(effective.Where(r => r.Scope == IpAccessScope.System), ip))
        {
            return IpAccessScope.System;
        }

        if (userId is { } id && Fails(effective.Where(r => r.Scope == IpAccessScope.User && r.TargetId == id), ip))
        {
            return IpAccessScope.User;
        }

        foreach (var roleId in userId is null ? Array.Empty<long>() : roleIds)
        {
            if (Fails(effective.Where(r => r.Scope == IpAccessScope.Role && r.TargetId == roleId), ip))
            {
                return IpAccessScope.Role;
            }
        }

        return null;
    }

    private static bool Fails(IEnumerable<IpAccessRule> level, IPAddress? ip)
    {
        var configured = false;
        foreach (var rule in level)
        {
            if (rule.Range.Contains(ip))
            {
                return false;
            }

            configured = true;
        }

        return configured;
    }
}

/// <summary>白名單檢查的請求資訊；<see cref="Username"/> 只用於稽核（已登入請求由實作查出）。</summary>
public sealed record IpAccessRequest(IPAddress? RemoteIp, long? UserId, string? Username, string Path);

/// <summary>
/// 由 SYS 模組實作的 IP 白名單：讀取設定與規則，拒絕時寫 Warning 日誌與稽核（IP、帳號、路徑）。
/// 總開關關閉時一律允許。登入、Token 刷新與已登入請求都經此檢查。
/// </summary>
public interface IIpAccessPolicy
{
    /// <summary>允許時回傳 true；拒絕時已記錄，呼叫端回 403 <c>COMMON.REQUEST.IP_NOT_ALLOWED</c>。</summary>
    Task<bool> IsAllowedAsync(IpAccessRequest request, CancellationToken cancellationToken);
}
