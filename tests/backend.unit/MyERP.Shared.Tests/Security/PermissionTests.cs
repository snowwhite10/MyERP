using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;

namespace MyERP.Shared.Tests.Security;

/// <summary>SYS-PERM-001～006：權限代碼、資料範圍篩選、權限目錄與政策（WU-SYS-004）。</summary>
public sealed class PermissionTests
{
    [Theory]
    [InlineData("SYS.ROLE.VIEW", true)]
    [InlineData("SAL.PRICE_LIST.APPROVE", true)]
    [InlineData("GL.JOURNAL.POST", true)]
    [InlineData("sys.role.view", false)]
    [InlineData("SYS.ROLE", false)]
    [InlineData("SYS.ROLE.VIEW.ALL", false)]
    [InlineData("SYS..VIEW", false)]
    [InlineData("SYS.ROLE.VIEW ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PermissionCodeFormat(string? code, bool valid)
    {
        Assert.Equal(valid, PermissionCode.IsValid(code));
        if (!valid)
        {
            Assert.Throws<ArgumentException>(() => PermissionCode.Ensure(code));
        }
    }

    [Fact]
    public void RequirePermissionAttributeRejectsMalformedCodeAndBuildsPolicyName()
    {
        Assert.Throws<ArgumentException>(() => new RequirePermissionAttribute("sys.role.view"));
        var attribute = new RequirePermissionAttribute("SYS.ROLE.VIEW");
        Assert.Equal("perm:SYS.ROLE.VIEW", attribute.Policy);
        Assert.Equal("SYS.ROLE.VIEW", attribute.Code);
    }

    [Fact]
    public async Task PolicyProviderBuildsAuthenticatedPermissionPolicyAndDelegatesOthers()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
        var policy = await provider.GetPolicyAsync("perm:SYS.ROLE.EDIT");
        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, r => r is PermissionRequirement { Code: "SYS.ROLE.EDIT" });
        Assert.Contains(policy.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        Assert.Null(await provider.GetPolicyAsync("unknown-policy"));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetPolicyAsync("perm:bad"));
    }

    [Fact]
    public void AuthorizationTakesScopeFromGrantAndFullAccessCoversEverything()
    {
        var limited = new UserCompanyAuthorization(7, 1, 10, 20, false,
            new Dictionary<string, DataScope> { ["SYS.ROLE.VIEW"] = DataScope.Department });
        Assert.True(limited.Has("SYS.ROLE.VIEW"));
        Assert.False(limited.Has("SYS.ROLE.EDIT"));
        Assert.Equal(DataScope.Department, limited.ScopeOf("SYS.ROLE.VIEW"));
        Assert.Null(limited.FilterFor("SYS.ROLE.EDIT"));
        Assert.Equal(new DataScopeFilter(DataScope.Department, 7, 10, 20), limited.FilterFor("SYS.ROLE.VIEW"));

        var full = new UserCompanyAuthorization(7, 1, null, null, true, new Dictionary<string, DataScope>());
        Assert.Equal(DataScope.Company, full.ScopeOf("ANY.THING.AT_ALL"));
    }

    [Fact]
    public void DataScopeFilterRestrictsRowsAndFallsBackToSelfWithoutOrganization()
    {
        var rows = new[]
        {
            new Row(1, 10, 100, 7),
            new Row(2, 10, 101, 8),
            new Row(3, 11, 102, 7),
            new Row(4, null, null, 9),
        }.AsQueryable();

        Assert.Equal([1, 2, 3, 4], Ids(new DataScopeFilter(DataScope.Company, 7, 10, 100), rows));
        Assert.Equal([1, 2], Ids(new DataScopeFilter(DataScope.Branch, 7, 10, 100), rows));
        Assert.Equal([1], Ids(new DataScopeFilter(DataScope.Department, 7, 10, 100), rows));
        Assert.Equal([1, 3], Ids(new DataScopeFilter(DataScope.Self, 7, 10, 100), rows));

        // 沒有分店／部門歸屬時不可擴大為「分店為 null 的資料」，而是只看本人
        var noBranch = new DataScopeFilter(DataScope.Branch, 9, null, null);
        Assert.Equal(DataScope.Self, noBranch.EffectiveScope);
        Assert.Equal([4], Ids(noBranch, rows));
        Assert.Equal(DataScope.Self, new DataScopeFilter(DataScope.Department, 9, 10, null).EffectiveScope);

        var branch = new DataScopeFilter(DataScope.Branch, 7, 10, 100);
        Assert.True(branch.Allows(rows.First()));
        Assert.False(branch.Allows(rows.Last()));
        Assert.True(new DataScopeFilter(DataScope.Company, 7, null, null).Allows(rows.Last()));
        Assert.False(new DataScopeFilter(DataScope.Self, 7, null, null).Allows(rows.Last()));
        Assert.True(new DataScopeFilter(DataScope.Department, 7, 10, 101).Allows(rows.ElementAt(1)));
    }

    [Fact]
    public void CatalogValidatesPrefixDuplicatesAndFormat()
    {
        var catalog = new PermissionCatalog([Provider.Of(("SYS.ROLE.VIEW", "SYS"), ("GL.JOURNAL.POST", "FI"))]);
        Assert.True(catalog.Contains("GL.JOURNAL.POST"));
        Assert.False(catalog.Contains("GL.JOURNAL.VOID"));
        Assert.Equal(["GL.JOURNAL.POST", "SYS.ROLE.VIEW"], catalog.All.Select(p => p.Code));

        Assert.Throws<InvalidOperationException>(() => new PermissionCatalog([Provider.Of(("SAL.ORDER.VIEW", "SYS"))]));
        Assert.Throws<InvalidOperationException>(() => new PermissionCatalog([Provider.Of(("SYS.ROLE.VIEW", "SYS")), Provider.Of(("SYS.ROLE.VIEW", "SYS"))]));
        Assert.Throws<ArgumentException>(() => new PermissionCatalog([Provider.Of(("SYS.role.VIEW", "SYS"))]));
        Assert.Throws<KeyNotFoundException>(() => new PermissionCatalog([Provider.Of(("XX.ROLE.VIEW", "XX"))]));
    }

    [Fact]
    public async Task HandlerSucceedsOnlyWhenCurrentAuthorizationGrantsCode()
    {
        var requirement = new PermissionRequirement("SYS.ROLE.VIEW");
        var granted = new UserCompanyAuthorization(1, 1, null, null, false, new Dictionary<string, DataScope> { ["SYS.ROLE.VIEW"] = DataScope.Self });

        var ok = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), null);
        await new PermissionAuthorizationHandler(new FixedAuthorization(granted)).HandleAsync(ok);
        Assert.True(ok.HasSucceeded);

        var other = new AuthorizationHandlerContext([new PermissionRequirement("SYS.ROLE.EDIT")], new ClaimsPrincipal(), null);
        await new PermissionAuthorizationHandler(new FixedAuthorization(granted)).HandleAsync(other);
        Assert.False(other.HasSucceeded);

        var none = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), null);
        await new PermissionAuthorizationHandler(new FixedAuthorization(null)).HandleAsync(none);
        Assert.False(none.HasSucceeded);
    }

    [Fact]
    public async Task CurrentAuthorizationQueriesStoreOncePerRequestAndNeedsUserAndCompany()
    {
        var store = new CountingStore();
        var services = new SimpleServices(store);
        var current = new CurrentAuthorization(new User(5), new Company(3), services);
        var first = await current.GetAsync(CancellationToken.None);
        var second = await current.GetAsync(CancellationToken.None);
        Assert.Same(first, second);
        Assert.Equal(1, store.Calls);

        Assert.Null(await new CurrentAuthorization(new User(null), new Company(3), services).GetAsync(CancellationToken.None));
        Assert.Null(await new CurrentAuthorization(new User(5), new Company(null), services).GetAsync(CancellationToken.None));
        Assert.Null(await new CurrentAuthorization(new User(5), new Company(3), new SimpleServices(null)).GetAsync(CancellationToken.None));
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task CompanyAccessMiddlewareRejectsInaccessibleCompanyOnlyForAuthenticatedUsers()
    {
        var called = 0;
        var middleware = new CompanyAccessMiddleware(_ => { called++; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            middleware.InvokeAsync(context, new User(5), new Company(3), new FixedAuthorization(null)));
        Assert.Equal(PermissionErrorCodes.CompanyAccessDenied, ex.ErrorCode);

        await middleware.InvokeAsync(context, new User(null), new Company(3), new FixedAuthorization(null));
        await middleware.InvokeAsync(context, new User(5), new Company(null), new FixedAuthorization(null));
        var granted = new UserCompanyAuthorization(5, 3, null, null, false, new Dictionary<string, DataScope>());
        await middleware.InvokeAsync(context, new User(5), new Company(3), new FixedAuthorization(granted));
        Assert.Equal(3, called);
    }

    [Fact]
    public async Task ResultHandlerTurnsForbiddenIntoErrorCodeForAuthenticatedUser()
    {
        var handler = new ErpAuthorizationResultHandler();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "5")], "Bearer")) };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.HandleAsync(_ => Task.CompletedTask, context, policy, Microsoft.AspNetCore.Authorization.Policy.PolicyAuthorizationResult.Forbid()));
        Assert.Equal(PermissionErrorCodes.PermissionDenied, ex.ErrorCode);

        var passed = false;
        await handler.HandleAsync(_ => { passed = true; return Task.CompletedTask; }, context, policy,
            Microsoft.AspNetCore.Authorization.Policy.PolicyAuthorizationResult.Success());
        Assert.True(passed);
    }

    private static int[] Ids(DataScopeFilter filter, IQueryable<Row> rows) => [.. filter.Apply(rows).Select(r => r.Id).Order()];

    private sealed record Row(int Id, long? BranchId, long? DepartmentId, long OwnerUserId) : IDataScopedEntity;

    private sealed class Provider(IReadOnlyList<PermissionDefinition> permissions) : IPermissionDefinitionProvider
    {
        public IReadOnlyList<PermissionDefinition> Permissions { get; } = permissions;

        public static Provider Of(params (string Code, string Module)[] permissions) =>
            new([.. permissions.Select(p => new PermissionDefinition(p.Code, p.Module))]);
    }

    private sealed class FixedAuthorization(UserCompanyAuthorization? value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class CountingStore : IUserAuthorizationStore
    {
        public int Calls { get; private set; }

        public Task<UserCompanyAuthorization?> GetAsync(long userId, long companyId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<UserCompanyAuthorization?>(new UserCompanyAuthorization(userId, companyId, null, null, false, new Dictionary<string, DataScope>()));
        }
    }

    private sealed class SimpleServices(IUserAuthorizationStore? store) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IUserAuthorizationStore) ? store : null;
    }

    private sealed class User(long? id) : ICurrentUser
    {
        public long? UserId => id;

        public bool IsAuthenticated => id is not null;
    }

    private sealed class Company(long? id) : ICurrentCompany
    {
        public long? CompanyId => id;
    }
}
