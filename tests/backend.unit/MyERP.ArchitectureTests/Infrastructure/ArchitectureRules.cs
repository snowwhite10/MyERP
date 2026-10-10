using System.Reflection;

namespace MyERP.ArchitectureTests.Infrastructure;

/// <summary>架構規則的檢查邏輯，回傳違規描述；空集合代表通過。規則本身另有測試（ArchitectureRulesSelfTests）。</summary>
internal static class ArchitectureRules
{
    /// <summary>禁止的時間來源：一律改用注入的 IClock（MASTER_PROMPT 6.3）。</summary>
    public static readonly IReadOnlyList<MethodInfo> ForbiddenClockMethods =
    [
        typeof(DateTime).GetProperty(nameof(DateTime.Now))!.GetMethod!,
        typeof(DateTime).GetProperty(nameof(DateTime.UtcNow))!.GetMethod!,
        typeof(DateTime).GetProperty(nameof(DateTime.Today))!.GetMethod!,
        typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Now))!.GetMethod!,
        typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.UtcNow))!.GetMethod!,
    ];

    public static IEnumerable<string> FindForbiddenCalls(IEnumerable<Type> types, IReadOnlyCollection<MethodInfo> forbidden)
    {
        foreach (var type in types)
        {
            foreach (var method in TypeInspector.GetDeclaredMethods(type))
            {
                foreach (var called in IlReader.GetReferencedMembers(method).OfType<MethodInfo>())
                {
                    if (forbidden.Contains(called))
                    {
                        yield return $"{type.FullName}.{method.Name} calls {called.DeclaringType!.Name}.{called.Name}";
                    }
                }
            }
        }
    }

    public static IEnumerable<string> FindForbiddenTypeReferences(IEnumerable<Type> types, Func<Type, Type, bool> isForbidden)
    {
        foreach (var type in types)
        {
            foreach (var referenced in TypeInspector.GetReferencedTypes(type))
            {
                if (isForbidden(type, referenced))
                {
                    yield return $"{type.FullName} references {referenced.FullName}";
                }
            }
        }
    }

    public static IEnumerable<string> FindForbiddenAssemblyReferences(IEnumerable<Assembly> assemblies, Func<Assembly, string, bool> isForbidden)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (isForbidden(assembly, reference.Name!))
                {
                    yield return $"{assembly.GetName().Name} references {reference.Name}";
                }
            }
        }
    }

    public static bool IsInNamespace(Type type, string ns) =>
        type.Namespace is { } n && (n == ns || n.StartsWith(ns + ".", StringComparison.Ordinal));

    /// <summary>命名空間是否含有某一段，例如 MyERP.Modules.Sales.Domain.Orders 含 Domain 段。</summary>
    public static bool HasNamespaceSegment(Type type, string segment) =>
        type.Namespace is { } n && n.Split('.').Contains(segment, StringComparer.Ordinal);
}
