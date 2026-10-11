using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;
using MyERP.Shared.Time;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.Modules.System.Application.Security;

/// <summary>
/// 白名單規則快照（singleton）：啟用中的規則全部載入記憶體（表很小），每個請求不必查詢資料庫。
/// 本程序內修改時立即失效；多台主機時其他主機最遲 <see cref="Lifetime"/> 後生效。
/// 另記錄最近的拒絕，同一 IP＋用戶每 <see cref="DenialRecordInterval"/> 只寫一次日誌與稽核，避免被大量請求灌爆。
/// </summary>
public sealed class IpWhitelistCache(IClock clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DenialRecordInterval = TimeSpan.FromMinutes(1);
    private const int MaxTrackedDenials = 10_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> recentDenials = new(StringComparer.Ordinal);
    private readonly object snapshotGate = new();
    private Snapshot? snapshot;
    private long generation;

    internal sealed record Snapshot(IReadOnlyList<IpAccessRule> Rules, bool HasRoleRules, DateTimeOffset LoadedAt);

    /// <summary>規則已修改：下一個請求重新載入。</summary>
    public void Invalidate()
    {
        lock (snapshotGate)
        {
            generation++;
            Volatile.Write(ref snapshot, null);
        }
    }

    internal async Task<Snapshot> GetAsync(Func<CancellationToken, Task<IReadOnlyList<IpAccessRule>>> load, CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = clock.Now;
            var current = Volatile.Read(ref snapshot);
            if (current is not null && now - current.LoadedAt < Lifetime)
                return current;

            long loadingGeneration;
            lock (snapshotGate) loadingGeneration = generation;
            var rules = await load(cancellationToken).ConfigureAwait(false);
            var loaded = new Snapshot(rules, rules.Any(r => r.Scope == IpAccessScope.Role), clock.Now);
            // Invalidate and publishing a loaded snapshot share the same lock: a concurrent edit cannot
            // leave an old snapshot cached, and a request loading during an edit retries before evaluation.
            lock (snapshotGate)
            {
                if (generation == loadingGeneration)
                {
                    Volatile.Write(ref snapshot, loaded);
                    return loaded;
                }
            }
        }
    }

    internal bool ShouldRecordDenial(string key)
    {
        var now = clock.Now;
        if (recentDenials.TryGetValue(key, out var last) && now - last < DenialRecordInterval)
        {
            return false;
        }

        if (recentDenials.Count >= MaxTrackedDenials)
        {
            recentDenials.Clear();
        }

        recentDenials[key] = now;
        return true;
    }
}

/// <summary>
/// IP 白名單判斷（REQ_F4118.32.3–5，ADR-025）。總開關 <c>Security:IpWhitelist:Enabled</c> 關閉時一律允許；
/// 角色層級套用用戶在任何啟用公司擁有的啟用角色（Token 可切換公司，故不以目前公司為限）。
/// 拒絕時寫 Warning 日誌與 <c>IP_DENIED</c> 安全稽核（IP、帳號、路徑、層級）。
/// </summary>
public sealed partial class IpWhitelistPolicy(
    MyErpDbContext db,
    IpWhitelistCache cache,
    WebSecurityOptions options,
    IClock clock,
    IAuditTrail audit,
    ILogger<IpWhitelistPolicy> logger) : IIpAccessPolicy
{
    private const int MaxAuditedPathLength = 200;

    public async Task<bool> IsAllowedAsync(IpAccessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.IpWhitelistEnabled)
        {
            return true;
        }

        var snapshot = await cache.GetAsync(LoadRulesAsync, cancellationToken).ConfigureAwait(false);
        if (snapshot.Rules.Count == 0)
        {
            return true;
        }

        IReadOnlyCollection<long> roleIds = request.UserId is { } userId && snapshot.HasRoleRules
            ? await RoleIdsAsync(userId, cancellationToken).ConfigureAwait(false)
            : [];
        var denied = IpAccessRules.Evaluate(request.RemoteIp, snapshot.Rules, request.UserId, roleIds, clock.Today);
        if (denied is null)
        {
            return true;
        }

        await RecordDenialAsync(request, denied.Value, cancellationToken).ConfigureAwait(false);
        return false;
    }

    private async Task<IReadOnlyList<IpAccessRule>> LoadRulesAsync(CancellationToken cancellationToken)
    {
        var entries = await db.Set<IpWhitelistEntry>().AsNoTracking().Where(x => x.IsActive)
            .Select(x => new { x.Scope, x.RoleId, x.UserId, x.IpRange, x.ValidFrom, x.ValidTo })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rules = new List<IpAccessRule>(entries.Count);
        foreach (var entry in entries)
        {
            // 只經服務寫入（已驗證）；萬一有人直接改資料庫寫入無效值，略過該列並寫 Warning，而不是令全部請求失敗
            if (IpRange.TryParse(entry.IpRange, out var range))
            {
                var target = entry.Scope switch
                {
                    IpAccessScope.Role => entry.RoleId,
                    IpAccessScope.User => entry.UserId,
                    _ => null,
                };
                rules.Add(new IpAccessRule(entry.Scope, target, range.Value, entry.ValidFrom, entry.ValidTo));
            }
            else
            {
                LogInvalidRange(logger, entry.IpRange);
            }
        }

        return rules;
    }

    private async Task<IReadOnlyCollection<long>> RoleIdsAsync(long userId, CancellationToken cancellationToken) =>
        await (from userRole in db.Set<SystemUserRole>()
               join access in db.Set<SystemUserCompany>()
                   on new { userRole.UserId, userRole.CompanyId } equals new { access.UserId, access.CompanyId }
               join company in db.Set<SystemCompany>() on userRole.CompanyId equals company.Id
               join role in db.Set<SystemRole>() on userRole.RoleId equals role.Id
               where userRole.UserId == userId && access.IsActive && company.IsActive && role.IsActive
               select userRole.RoleId)
            .Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);

    private async Task RecordDenialAsync(IpAccessRequest request, IpAccessScope scope, CancellationToken cancellationToken)
    {
        var ip = request.RemoteIp is null ? "unknown" : IpRange.Normalize(request.RemoteIp).ToString();
        var userKey = request.UserId?.ToString(CultureInfo.InvariantCulture) ?? request.Username?.ToUpperInvariant() ?? "-";
        if (!cache.ShouldRecordDenial($"{ip}|{userKey}"))
        {
            return;
        }

        var username = request.Username;
        if (username is null && request.UserId is { } userId)
        {
            username = await db.Set<SystemUser>().AsNoTracking().Where(x => x.Id == userId).Select(x => x.Username)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        var path = request.Path.Length > MaxAuditedPathLength ? request.Path[..MaxAuditedPathLength] : request.Path;
        LogDenied(logger, ip, username ?? "-", path, scope);
        audit.AddSecurityEvent(new SecurityAuditEvent(AuditActions.IpDenied, request.UserId, username, null,
            new Dictionary<string, object?> { ["ip"] = ip, ["path"] = path, ["scope"] = scope.ToString() }));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "IP whitelist denied {RemoteIp} for user {Username} on {RequestPath} ({IpWhitelistScope} level)")]
    private static partial void LogDenied(ILogger logger, string remoteIp, string username, string requestPath, IpAccessScope ipWhitelistScope);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring invalid IP whitelist range {IpRange} stored in sys_ip_whitelist")]
    private static partial void LogInvalidRange(ILogger logger, string ipRange);
}
