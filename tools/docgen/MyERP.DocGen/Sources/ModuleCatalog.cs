namespace MyERP.DocGen.Sources;

/// <summary>SRS 模組（MASTER_PROMPT 15.1，編號固定不得重排）。</summary>
/// <param name="Number">SRS 模組編號，出現在需求 ID 中。</param>
/// <param name="Name">英文名稱（4.1.1 / 4.4.1 小節標題用 "{Name} Module"）。</param>
/// <param name="FeatureTitle">SRS 2.2 小節標題；null 表示 2.2 不設小節（User、General Interface）。</param>
/// <param name="Codes">對應的 MyERP 模組代碼。</param>
public sealed record SrsModule(int Number, string Name, string? FeatureTitle, IReadOnlyList<string> Codes);

public static class ModuleCatalog
{
    public static IReadOnlyList<SrsModule> All { get; } =
    [
        new(1, "User", null, ["SYS"]),
        new(2, "Sales", "Sales Management", ["SD"]),
        new(3, "Production", "Production Management", ["PP"]),
        new(4, "Purchase", "Purchase Management", ["PUR"]),
        new(5, "Inventory", "Inventory Management", ["INV"]),
        new(6, "Accounting", "Accounting Management", ["FI"]),
        new(7, "Report", "Report Management", ["BI"]),
        new(8, "Settings", "Setting Management", ["MD", "SYS"]),
        new(9, "General Interface", null, []),
        new(10, "Controlling", "Controlling Management", ["CO"]),
        new(11, "Quality", "Quality Management", ["QM"]),
        new(12, "Maintenance", "Maintenance Management", ["PM"]),
        new(13, "CRM", "Customer Relationship Management", ["CRM"]),
        new(14, "Human Resources", "Human Resources Management", ["HR"]),
        new(15, "Project", "Project Management", ["PS"]),
        new(16, "Supply Chain", "Supply Chain Management", ["SCM"]),
    ];

    public static SrsModule? Find(int number) => All.FirstOrDefault(m => m.Number == number);

    /// <summary>標題用名稱；未登錄的編號（17+ 創意擴充）以 "Module N" 代替，並由一致性檢查報錯。</summary>
    public static string NameOf(int number) => Find(number)?.Name ?? $"Module {number}";
}
