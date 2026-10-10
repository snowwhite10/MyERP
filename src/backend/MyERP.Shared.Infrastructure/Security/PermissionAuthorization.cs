using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyERP.Shared.Errors;
using MyERP.Shared.Modules;
using MyERP.Shared.Security;

namespace MyERP.Shared.Infrastructure.Security;

/// <summary>
/// 標註 Controller / action 需要的權限代碼（<c>{PREFIX}.{RESOURCE}.{ACTION}</c>）。後端為準：
/// 用戶必須已登入、帶有可存取的 <c>X-Company-Id</c>，並在該公司獲授予此權限，否則 403。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public RequirePermissionAttribute(string code)
        : base(PolicyPrefix + PermissionCode.Ensure(code))
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed record PermissionRequirement(string Code) : IAuthorizationRequirement;

/// <summary>以 <c>perm:</c> 開頭的政策名稱動態產生政策，其他名稱交回預設提供者。</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName).ConfigureAwait(false);
        }

        var code = PermissionCode.Ensure(policyName[RequirePermissionAttribute.PolicyPrefix.Length..]);
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(code)).Build();
    }
}

public sealed class PermissionAuthorizationHandler(ICurrentAuthorization current) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        var authorization = await current.GetAsync(CancellationToken.None).ConfigureAwait(false);
        if (authorization?.Has(requirement.Code) == true)
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>同一請求只查一次授權；沒有 <see cref="IUserAuthorizationStore"/>（未註冊 SYS）時一律無授權。</summary>
public sealed class CurrentAuthorization(ICurrentUser user, ICurrentCompany company, IServiceProvider services) : ICurrentAuthorization
{
    private Task<UserCompanyAuthorization?>? cached;

    public Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken) =>
        cached ??= LoadAsync(cancellationToken);

    private async Task<UserCompanyAuthorization?> LoadAsync(CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId || company.CompanyId is not { } companyId)
        {
            return null;
        }

        var store = services.GetService<IUserAuthorizationStore>();
        return store is null ? null : await store.GetAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>授權失敗時拋出 <see cref="ForbiddenException"/>，由全域處理器轉為帶 errorCode 的 ProblemDetails；未登入仍回 401 challenge。</summary>
public sealed class ErpAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            throw new ForbiddenException(PermissionErrorCodes.PermissionDenied, "The current user does not have the required permission.");
        }

        return fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}

public static class PermissionErrorCodes
{
    /// <summary>缺少權限代碼。</summary>
    public const string PermissionDenied = "COMMON.PERMISSION.DENIED";

    /// <summary><c>X-Company-Id</c> 不是用戶可存取的公司（或公司已停用）。</summary>
    public const string CompanyAccessDenied = "COMMON.COMPANY.ACCESS_DENIED";
}

/// <summary>
/// 已登入請求若帶 <c>X-Company-Id</c>，必須是用戶可存取的公司，否則 403（api-conventions.md 第 7 節）。
/// 放在驗證之後；此檢查令公司篩選（Global Query Filter）不會被偽造的標頭繞過。
/// </summary>
public sealed class CompanyAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser user, ICurrentCompany company, ICurrentAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (user.IsAuthenticated && company.CompanyId is not null
            && await authorization.GetAsync(context.RequestAborted).ConfigureAwait(false) is null)
        {
            throw new ForbiddenException(PermissionErrorCodes.CompanyAccessDenied, "The current user cannot access the requested company.");
        }

        await next(context).ConfigureAwait(false);
    }
}

/// <summary>彙整各模組宣告的權限；啟動時驗證代碼格式、前綴屬於宣告模組、不重複。</summary>
public sealed class PermissionCatalog
{
    private readonly Dictionary<string, PermissionDefinition> byCode;

    public PermissionCatalog(IEnumerable<IPermissionDefinitionProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        byCode = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        foreach (var definition in providers.SelectMany(p => p.Permissions))
        {
            PermissionCode.Ensure(definition.Code);
            var module = ErpModuleCatalog.Get(definition.Module);
            var prefix = definition.Code[..definition.Code.IndexOf('.', StringComparison.Ordinal)];
            if (!module.Prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Permission '{definition.Code}' does not use a prefix of module '{module.Code}'.");
            }

            if (!byCode.TryAdd(definition.Code, definition))
            {
                throw new InvalidOperationException($"Permission '{definition.Code}' is declared more than once.");
            }
        }

        All = [.. byCode.Values.OrderBy(p => p.Code, StringComparer.Ordinal)];
    }

    public IReadOnlyList<PermissionDefinition> All { get; }

    public bool Contains(string code) => code is not null && byCode.ContainsKey(code);
}

public static class PermissionServiceCollectionExtensions
{
    /// <summary>權限政策、授權處理器、目前請求授權與權限目錄。各模組以 <see cref="IPermissionDefinitionProvider"/> 宣告權限。</summary>
    public static IServiceCollection AddMyErpPermissions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ErpAuthorizationResultHandler>();
        services.AddScoped<ICurrentAuthorization, CurrentAuthorization>();
        services.TryAddSingleton<PermissionCatalog>();
        return services;
    }

    /// <summary>在 <c>UseAuthentication</c> 之後、<c>UseAuthorization</c> 之前呼叫。</summary>
    public static IApplicationBuilder UseCompanyAccessCheck(this IApplicationBuilder app) =>
        app.UseMiddleware<CompanyAccessMiddleware>();
}
