using System.Reflection;

namespace MyERP.ArchitectureTests.Infrastructure;

/// <summary>
/// 載入輸出目錄中的全部 MyERP 正式組件（不含測試組件）。
/// 本測試專案引用 MyERP.Api（組合根，引用全部模組）與 MyERP.Migrations，新模組加入 Api 後自動納入檢查。
/// </summary>
internal static class SolutionAssemblies
{
    public const string ModulePrefix = "MyERP.Modules.";
    public const string ContractsSuffix = ".Contracts";

    private static readonly Lazy<IReadOnlyList<Assembly>> LazyAll = new(Load);

    public static IReadOnlyList<Assembly> All => LazyAll.Value;

    public static IEnumerable<Assembly> ModuleImplementations =>
        All.Where(a => IsModuleImplementation(a.GetName().Name!));

    public static IEnumerable<Assembly> ModuleContracts =>
        All.Where(a => IsModuleContracts(a.GetName().Name!));

    public static bool IsModuleImplementation(string assemblyName) =>
        assemblyName.StartsWith(ModulePrefix, StringComparison.Ordinal)
        && !assemblyName.EndsWith(ContractsSuffix, StringComparison.Ordinal);

    public static bool IsModuleContracts(string assemblyName) =>
        assemblyName.StartsWith(ModulePrefix, StringComparison.Ordinal)
        && assemblyName.EndsWith(ContractsSuffix, StringComparison.Ordinal);

    /// <summary>模組根命名空間，例如 MyERP.Modules.Sales.Contracts → MyERP.Modules.Sales。</summary>
    public static string ModuleRoot(string assemblyName) =>
        assemblyName.EndsWith(ContractsSuffix, StringComparison.Ordinal)
            ? assemblyName[..^ContractsSuffix.Length]
            : assemblyName;

    private static List<Assembly> Load() =>
        Directory.GetFiles(AppContext.BaseDirectory, "MyERP.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !IsTestAssembly(name!))
            .Select(name => Assembly.Load(new AssemblyName(name!)))
            .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
            .ToList();

    private static bool IsTestAssembly(string name) =>
        name.EndsWith(".Tests", StringComparison.Ordinal)
        || name is "MyERP.ArchitectureTests" or "MyERP.IntegrationTests";
}
