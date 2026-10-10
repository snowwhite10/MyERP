namespace MyERP.Shared.Modules;

/// <summary>全部 ERP 模組清單（單一來源：architecture.md 2.3）。新增模組只可追加，不得重排。</summary>
public static class ErpModuleCatalog
{
    public static IReadOnlyList<ErpModuleInfo> All { get; } =
    [
        new("SYS", "MyERP.Modules.System", ["sys"], [1, 8], "P0"),
        new("MD", "MyERP.Modules.MasterData", ["md"], [8], "P1"),
        new("INV", "MyERP.Modules.Inventory", ["inv"], [5], "P1"),
        new("PUR", "MyERP.Modules.Purchasing", ["pur"], [4], "P1"),
        new("SD", "MyERP.Modules.Sales", ["sal"], [2], "P1"),
        new("FI", "MyERP.Modules.Finance", ["gl", "ar", "ap", "fa", "cm", "tax"], [6], "P2"),
        new("CO", "MyERP.Modules.Controlling", ["co"], [10], "P2"),
        new("BI", "MyERP.Modules.Reporting", ["bi"], [7], "P2"),
        new("PP", "MyERP.Modules.Production", ["pp"], [3], "P3"),
        new("QM", "MyERP.Modules.Quality", ["qm"], [11], "P3"),
        new("PM", "MyERP.Modules.Maintenance", ["pm"], [12], "P3"),
        new("CRM", "MyERP.Modules.Crm", ["crm"], [13], "P4"),
        new("HR", "MyERP.Modules.HumanResources", ["hr"], [14], "P4"),
        new("PS", "MyERP.Modules.Projects", ["ps"], [15], "P4"),
        new("SCM", "MyERP.Modules.SupplyChain", ["scm"], [16], "P4"),
    ];

    private static readonly Dictionary<string, ErpModuleInfo> ByCode =
        All.ToDictionary(m => m.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ErpModuleInfo> ByPrefix =
        All.SelectMany(m => m.Prefixes.Select(p => (Prefix: p, Module: m)))
            .ToDictionary(x => x.Prefix, x => x.Module, StringComparer.OrdinalIgnoreCase);

    /// <summary>以模組代碼取得模組（不分大小寫）；不存在時拋出 <see cref="KeyNotFoundException"/>。</summary>
    public static ErpModuleInfo Get(string code) =>
        ByCode.TryGetValue(code, out var module)
            ? module
            : throw new KeyNotFoundException($"Unknown ERP module code '{code}'.");

    /// <summary>以前綴（資料表 / 路由 / 權限）找出所屬模組（不分大小寫）；不存在時回傳 null。</summary>
    public static ErpModuleInfo? FindByPrefix(string prefix) =>
        ByPrefix.GetValueOrDefault(prefix);
}
