using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;

namespace MyERP.Shared.Tests.Security;

/// <summary>SYS-IPW-001～003：IP／CIDR 解析、白名單層級判斷與中介層（WU-SYS-006、ADR-025）。</summary>
public sealed class IpAccessTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    [Theory]
    [InlineData("10.0.0.1", "10.0.0.1")]
    [InlineData(" 192.168.1.0/24 ", "192.168.1.0/24")]
    [InlineData("10.0.0.1/32", "10.0.0.1")]
    [InlineData("0.0.0.0/0", "0.0.0.0/0")]
    [InlineData("2001:DB8::1", "2001:db8::1")]
    [InlineData("2001:db8::/32", "2001:db8::/32")]
    [InlineData("2001:db8::1/128", "2001:db8::1")]
    [InlineData("::ffff:10.0.0.1", "10.0.0.1")]
    public void ParsesAndNormalizesAddressesAndNetworks(string input, string expected)
    {
        Assert.True(IpRange.TryParse(input, out var range));
        Assert.Equal(expected, range.Value.ToString());
        Assert.Equal(expected, IpRange.Parse(expected).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("10.0.0")]
    [InlineData("1")]
    [InlineData("010.0.0.1")]
    [InlineData("0x0A.0.0.1")]
    [InlineData("10.0.0.256")]
    [InlineData("10.1.2.3/8")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("2001:db8::1/64")]
    [InlineData("fe80::1%3")]
    [InlineData("example.com")]
    [InlineData("10.0.0.0/8,10.1.0.0/16")]
    public void RejectsInvalidOrAmbiguousRanges(string? input)
    {
        Assert.False(IpRange.TryParse(input, out var range));
        Assert.Null(range);
        Assert.Throws<FormatException>(() => IpRange.Parse(input));
    }

    [Fact]
    public void RangesMatchAddressesOfTheSameFamilyIncludingIpv4MappedAddresses()
    {
        var network = IpRange.Parse("192.168.1.0/24");
        Assert.True(network.Contains(IPAddress.Parse("192.168.1.200")));
        Assert.True(network.Contains(IPAddress.Parse("::ffff:192.168.1.7")));
        Assert.False(network.Contains(IPAddress.Parse("192.168.2.1")));
        Assert.False(network.Contains(IPAddress.Parse("2001:db8::1")));
        Assert.False(network.Contains(null));

        var v6 = IpRange.Parse("2001:db8::/32");
        Assert.True(v6.Contains(IPAddress.Parse("2001:db8:ffff::1")));
        Assert.False(v6.Contains(IPAddress.Parse("2001:db9::1")));
        Assert.True(IpRange.Parse("10.0.0.5").Contains(IPAddress.Parse("10.0.0.5")));
        Assert.False(IpRange.Parse("10.0.0.5").Contains(IPAddress.Parse("10.0.0.6")));
    }

    [Fact]
    public void NoEffectiveRulesAllowsEverythingAndLoopbackIsAlwaysAllowed()
    {
        Assert.Null(Evaluate("203.0.113.9", [], userId: 1));
        Assert.Null(Evaluate(null, [], userId: null));

        // 過期、尚未生效的規則視同沒有設定
        Assert.Null(Evaluate("203.0.113.9", [Rule(IpAccessScope.System, null, "10.0.0.0/8", to: Today.AddDays(-1))], 1));
        Assert.Null(Evaluate("203.0.113.9", [Rule(IpAccessScope.System, null, "10.0.0.0/8", from: Today.AddDays(1))], 1));

        IpAccessRule[] strict = [Rule(IpAccessScope.System, null, "10.0.0.0/8"), Rule(IpAccessScope.User, 1, "10.0.0.1")];
        Assert.Null(Evaluate("127.0.0.1", strict, 1));
        Assert.Null(Evaluate("127.10.0.1", strict, 1));
        Assert.Null(Evaluate("::1", strict, 1));
        Assert.Null(Evaluate("::ffff:127.0.0.1", strict, 1));
    }

    [Fact]
    public void SystemLevelAppliesToEveryRequestAndUnknownAddressesFailClosed()
    {
        IpAccessRule[] rules = [Rule(IpAccessScope.System, null, "10.0.0.0/8"), Rule(IpAccessScope.System, null, "2001:db8::/32")];
        Assert.Null(Evaluate("10.20.30.40", rules, null));
        Assert.Null(Evaluate("2001:db8::5", rules, 7));
        Assert.Equal(IpAccessScope.System, Evaluate("203.0.113.9", rules, null));
        Assert.Equal(IpAccessScope.System, Evaluate("203.0.113.9", rules, 7));
        Assert.Equal(IpAccessScope.System, Evaluate(null, rules, 7));

        // 有效期限含頭尾
        IpAccessRule[] bounded = [Rule(IpAccessScope.System, null, "10.0.0.0/8", Today, Today)];
        Assert.Equal(IpAccessScope.System, Evaluate("203.0.113.9", bounded, null));
    }

    [Fact]
    public void UserAndEveryRoleLevelMustAlsoBeSatisfied()
    {
        IpAccessRule[] rules =
        [
            Rule(IpAccessScope.System, null, "10.0.0.0/8"),
            Rule(IpAccessScope.User, 1, "10.1.0.0/16"),
            Rule(IpAccessScope.User, 1, "10.9.9.9"),
            Rule(IpAccessScope.Role, 100, "10.1.2.0/24"),
            Rule(IpAccessScope.Role, 200, "10.0.0.0/8"),
        ];

        // 用戶層級：符合任一條即可，但仍受全系統限制
        Assert.Null(Evaluate("10.9.9.9", rules, 1));
        Assert.Equal(IpAccessScope.User, Evaluate("10.2.0.1", rules, 1));
        Assert.Equal(IpAccessScope.System, Evaluate("11.1.0.1", rules, 1));

        // 沒有用戶設定的用戶只受全系統與角色限制
        Assert.Null(Evaluate("10.2.0.1", rules, 2));
        Assert.Null(Evaluate("10.2.0.1", rules, 2, 200));

        // 每個有設定的角色都要符合（較嚴格者為準）；沒有設定的角色不影響
        Assert.Equal(IpAccessScope.Role, Evaluate("10.2.0.1", rules, 2, 100, 200));
        Assert.Null(Evaluate("10.1.2.3", rules, 2, 100, 200, 300));
        Assert.Equal(IpAccessScope.Role, Evaluate("10.1.3.3", rules, 1, 100));
        Assert.Null(Evaluate("10.1.2.3", rules, 1, 100));

        // 未登入請求只檢查全系統
        Assert.Null(Evaluate("10.2.0.1", rules, null, 100));
    }

    [Fact]
    public async Task MiddlewareRejectsDeniedRequestsAndSkipsHealthChecks()
    {
        var policy = new FakePolicy(allowed: false);
        var reached = 0;
        var middleware = new IpWhitelistMiddleware(_ =>
        {
            reached++;
            return Task.CompletedTask;
        });

        var denied = Context(policy, "/api/v1/sys/users", "203.0.113.9");
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => middleware.InvokeAsync(denied));
        Assert.Equal(CommonErrorCodes.IpNotAllowed, error.ErrorCode);
        Assert.Equal(0, reached);
        var request = Assert.Single(policy.Requests);
        Assert.Equal(IPAddress.Parse("203.0.113.9"), request.RemoteIp);
        Assert.Equal(42, request.UserId);
        Assert.Equal("/api/v1/sys/users", request.Path);

        await middleware.InvokeAsync(Context(policy, "/health/live", "203.0.113.9"));
        await middleware.InvokeAsync(Context(policy, "/health/ready", "203.0.113.9"));
        Assert.Single(policy.Requests);
        Assert.Equal(2, reached);

        var allowing = new FakePolicy(allowed: true);
        await middleware.InvokeAsync(Context(allowing, "/api/v1/sys/auth/captcha", null, null));
        Assert.Equal(3, reached);
        Assert.Null(Assert.Single(allowing.Requests).UserId);

        // 沒有註冊白名單（未載入 SYS 模組）時直接放行
        await middleware.InvokeAsync(Context(null, "/api/v1/anything", "203.0.113.9"));
        Assert.Equal(4, reached);
    }

    private static IpAccessScope? Evaluate(string? ip, IpAccessRule[] rules, long? userId, params long[] roleIds) =>
        IpAccessRules.Evaluate(ip is null ? null : IPAddress.Parse(ip), rules, userId, roleIds, Today);

    private static IpAccessRule Rule(IpAccessScope scope, long? target, string range, DateOnly? from = null, DateOnly? to = null) =>
        new(scope, target, IpRange.Parse(range), from, to);

    private static DefaultHttpContext Context(IIpAccessPolicy? policy, string path, string? ip, long? userId = 42)
    {
        var services = new ServiceCollection();
        if (policy is not null)
        {
            services.AddSingleton(policy);
        }
        services.AddSingleton<ICurrentUser>(new FixedUser(userId));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
        return context;
    }

    private sealed class FakePolicy(bool allowed) : IIpAccessPolicy
    {
        public List<IpAccessRequest> Requests { get; } = [];

        public Task<bool> IsAllowedAsync(IpAccessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(allowed);
        }
    }

    private sealed class FixedUser(long? id) : ICurrentUser
    {
        public long? UserId => id;

        public bool IsAuthenticated => id is not null;
    }
}
