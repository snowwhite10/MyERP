using MyERP.Modules.System.Application.Security;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-IPW-006：本機失效及跨主機 30 秒快照界線。</summary>
public sealed class IpWhitelistCacheTests
{
    [Fact]
    public async Task SnapshotExpiresAtThirtySecondsAndInvalidateReloadsImmediately()
    {
        var clock = new Clock();
        var cache = new IpWhitelistCache(clock);
        var loads = 0;
        Task<IReadOnlyList<IpAccessRule>> Load(CancellationToken _) { loads++; return Task.FromResult<IReadOnlyList<IpAccessRule>>([]); }

        await cache.GetAsync(Load, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(29));
        await cache.GetAsync(Load, CancellationToken.None);
        Assert.Equal(1, loads);
        clock.Advance(TimeSpan.FromSeconds(1));
        await cache.GetAsync(Load, CancellationToken.None);
        Assert.Equal(2, loads);
        cache.Invalidate();
        await cache.GetAsync(Load, CancellationToken.None);
        Assert.Equal(3, loads);
    }

    [Fact]
    public async Task InvalidationDuringLoadRetriesBeforeReturning()
    {
        var cache = new IpWhitelistCache(new Clock());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        async Task<IReadOnlyList<IpAccessRule>> Load(CancellationToken _)
        {
            if (++loads == 1)
            {
                started.SetResult();
                await release.Task;
                return [];
            }
            Assert.True(IpRange.TryParse("203.0.113.0/24", out var range));
            return [new IpAccessRule(IpAccessScope.System, null, range.Value, null, null)];
        }

        var pending = cache.GetAsync(Load, CancellationToken.None);
        await started.Task;
        cache.Invalidate();
        release.SetResult();
        var snapshot = await pending;
        Assert.Equal(2, loads);
        Assert.Single(snapshot.Rules);
        Assert.Same(snapshot, await cache.GetAsync(Load, CancellationToken.None));
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(8));
        public DateTime LocalNow => Now.DateTime;
        public DateOnly Today => DateOnly.FromDateTime(LocalNow);
        public TimeSpan Offset => TimeSpan.FromHours(8);
        public void Advance(TimeSpan duration) => Now = Now.Add(duration);
    }
}
