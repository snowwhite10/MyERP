using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

/// <summary>每個 API 動作都要有權限代碼（api-conventions.md 第 6 節），代碼必須在權限目錄內。</summary>
public sealed class PermissionRulesTests
{
    /// <summary>只需登入、不需權限代碼的自助端點（本人資料、登出、本人授權）。新增項目須在 api.md 說明理由。</summary>
    private static readonly HashSet<string> SelfServiceActions =
    [
        "AuthController.Logout",
        "AuthController.Me",
        "AuthController.ChangePassword",
        "CurrentAuthorizationController.Get",
    ];

    /// <summary>可匿名的端點：登入、Captcha、Token 刷新。</summary>
    private static readonly HashSet<string> AnonymousActions =
    [
        "AuthController.GetCaptcha",
        "AuthController.Login",
        "AuthController.Refresh",
        "AuthController.ChangeExpiredPassword",
    ];

    private static IEnumerable<MethodInfo> Actions() =>
        SolutionAssemblies.ModuleImplementations
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

    private static string Name(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    private static IEnumerable<T> Attributes<T>(MethodInfo action)
        where T : Attribute =>
        action.GetCustomAttributes<T>().Concat(action.DeclaringType!.GetCustomAttributes<T>());

    [Fact]
    public void Discovery_FindsControllerActions()
    {
        Actions().Select(Name).Should().Contain(["AuthController.Login", "RolesController.List"]);
    }

    [Fact]
    public void EveryActionRequiresPermissionUnlessListedAsAnonymousOrSelfService()
    {
        var violations = new List<string>();
        foreach (var action in Actions())
        {
            var name = Name(action);
            var anonymous = Attributes<AllowAnonymousAttribute>(action).Any();
            var permissions = Attributes<RequirePermissionAttribute>(action).ToList();
            var authorize = Attributes<AuthorizeAttribute>(action).Any(a => a is not RequirePermissionAttribute);

            if (anonymous != AnonymousActions.Contains(name))
            {
                violations.Add($"{name}: [AllowAnonymous] only allowed on {string.Join(", ", AnonymousActions)}");
            }
            else if (!anonymous && permissions.Count == 0 && !(authorize && SelfServiceActions.Contains(name)))
            {
                violations.Add($"{name}: missing [RequirePermission]");
            }
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void EveryAnonymousActionDeclaresRateLimitPolicy()
    {
        // 匿名端點沒有用戶可分區，必須按 IP 限流（MASTER_PROMPT 第 7 節、WU-SYS-005）
        var missing = Actions()
            .Where(a => Attributes<AllowAnonymousAttribute>(a).Any())
            .Where(a => Attributes<EnableRateLimitingAttribute>(a).All(r => r.PolicyName is null || !RateLimitPolicies.PerIp.Contains(r.PolicyName)))
            .Select(Name)
            .ToList();

        missing.Should().BeEmpty();
    }

    [Fact]
    public void RequiredPermissionsAreDeclaredByModules()
    {
        var providers = SolutionAssemblies.ModuleImplementations
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IPermissionDefinitionProvider).IsAssignableFrom(t))
            .Select(t => (IPermissionDefinitionProvider)Activator.CreateInstance(t)!)
            .ToList();
        providers.Should().NotBeEmpty();
        var catalog = new PermissionCatalog(providers);

        var undeclared = Actions()
            .SelectMany(a => Attributes<RequirePermissionAttribute>(a).Select(p => $"{Name(a)}: {p.Code}"))
            .Where(x => !catalog.Contains(x[(x.IndexOf(": ", StringComparison.Ordinal) + 2)..]))
            .ToList();
        undeclared.Should().BeEmpty();
    }
}
