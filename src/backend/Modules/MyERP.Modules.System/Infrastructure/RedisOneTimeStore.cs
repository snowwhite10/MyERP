using Microsoft.Extensions.Diagnostics.HealthChecks;
using MyERP.Modules.System.Application.Auth;
using StackExchange.Redis;

namespace MyERP.Modules.System.Infrastructure;

/// <summary>以 Lua 腳本保證取用及條件刪除的原子性（不依賴 Redis 6.2 的 GETDEL，Memurai 舊版亦可用）。</summary>
public sealed class RedisOneTimeStore(IConnectionMultiplexer redis) : IOneTimeStore
{
    public const string KeyPrefix = "myerp:";

    private const string TakeScript = "local v = redis.call('GET', KEYS[1]) if v then redis.call('DEL', KEYS[1]) end return v";
    private const string RemoveIfScript = "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) end return 0";

    public Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
        redis.GetDatabase().StringSetAsync(KeyPrefix + key, value, timeToLive);

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(KeyPrefix + key);
        return value.IsNull ? null : (string?)value;
    }

    public async Task<string?> TakeAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(TakeScript, [(RedisKey)(KeyPrefix + key)]);
        return result.IsNull ? null : (string?)result;
    }

    public async Task<bool> RemoveIfAsync(string key, string expectedValue, CancellationToken cancellationToken = default)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(RemoveIfScript, [(RedisKey)(KeyPrefix + key)], [(RedisValue)expectedValue]);
        return (long)result == 1;
    }
}

/// <summary>就緒檢查：Redis PING；失敗時 /health/ready 回 503（REQ_NF424.2）。</summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public const string Name = "redis";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (RedisException ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unavailable.", ex);
        }
    }
}
