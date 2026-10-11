using MyERP.Shared.Security;

namespace MyERP.Modules.System.Application.Seeding;

/// <summary>種子帳號（<c>SeedUsers</c> 設定區段，只放在 appsettings.Development.json，MASTER_PROMPT 第 12 節）。</summary>
public sealed class SeedUserOptions
{
    public const string SectionName = "SeedUsers";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary><see cref="SystemSeedData.Roles"/> 的角色代碼。</summary>
    public string Role { get; set; } = "";
}

public sealed record SeedCompany(string Code, string Name, string BaseCurrency, IReadOnlyList<SeedOrganizationUnit> Branches);

public sealed record SeedOrganizationUnit(string Code, string Name);

/// <summary>
/// 種子角色。權限以規則表示，在種子執行時對照權限目錄展開：之後新模組宣告的權限，重新執行種子即補上（ADR-023）。
/// </summary>
public sealed record SeedRole(
    string Code,
    string Name,
    string Description,
    string DepartmentCode,
    bool IsFullAccess,
    Func<PermissionDefinition, bool> Grants);

/// <summary>示範組織與角色（MASTER_PROMPT 第 12 節）；帳號與密碼在設定檔。</summary>
public static class SystemSeedData
{
    public static IReadOnlyList<SeedCompany> Companies { get; } =
    [
        // 本位幣待 MD 幣別主檔與公司財務設定建立後寫入（ADR-023）；目前只作為示範資料的說明
        new("TW01", "MyERP 台灣股份有限公司", "TWD",
        [
            new("TPE", "台北總公司"),
            new("TXG", "台中分公司"),
            new("KHH", "高雄分公司"),
        ]),
        new("HK01", "MyERP 香港有限公司", "HKD",
        [
            new("HKG", "香港總公司"),
            new("KLN", "九龍分公司"),
        ]),
    ];

    /// <summary>每間公司都建立的部門；部門不指定分店（全公司共用）。</summary>
    public static IReadOnlyList<SeedOrganizationUnit> Departments { get; } =
    [
        new("ADM", "管理部"),
        new("FIN", "財務部"),
        new("SAL", "業務部"),
        new("PUR", "採購部"),
        new("WH", "倉儲部"),
        new("PRD", "生產部"),
        new("HR", "人資部"),
    ];

    public static IReadOnlyList<SeedRole> Roles { get; } =
    [
        new("ADMIN", "系統管理員", "全部權限", "ADM", IsFullAccess: true, _ => false),
        new("FINANCE", "財務", "FI、CO 全部；其他唯讀", "FIN", false,
            p => InModule(p, "FI", "CO") || IsView(p) || IsOwnApproval(p)),
        new("SALES", "銷售", "SD、CRM 全部；庫存唯讀", "SAL", false,
            p => InModule(p, "SD", "CRM") || (InModule(p, "INV") && IsView(p)) || IsOwnApproval(p)),
        new("PURCHASE", "採購", "PUR、供應商主檔", "PUR", false,
            p => InModule(p, "PUR") || OnResource(p, "MD", "SUPPLIER") || IsOwnApproval(p)),
        new("WAREHOUSE", "倉庫", "INV（含倉儲作業）", "WH", false,
            p => InModule(p, "INV") || IsOwnApproval(p)),
        new("PRODUCTION", "生產", "PP、QM、PM", "PRD", false,
            p => InModule(p, "PP", "QM", "PM") || IsOwnApproval(p)),
        new("HR", "人資", "HR", "HR", false,
            p => InModule(p, "HR") || IsOwnApproval(p)),
        new("MANAGER", "主管", "各模組審批權限 + 全部報表（含各模組查詢）", "ADM", false,
            p => IsOwnApproval(p) || HasAction(p, "APPROVE", "REJECT") || InModule(p, "BI") || IsView(p)),
        new("VIEWER", "唯讀", "全系統唯讀", "ADM", false, IsView),
    ];

    private static bool InModule(PermissionDefinition p, params string[] modules) =>
        modules.Contains(p.Module, StringComparer.OrdinalIgnoreCase);

    private static bool IsView(PermissionDefinition p) => HasAction(p, "VIEW");

    private static bool HasAction(PermissionDefinition p, params string[] actions) =>
        actions.Any(a => p.Code.EndsWith("." + a, StringComparison.Ordinal));

    /// <summary>模組內某資源（含其子資源，例如 SUPPLIER、SUPPLIER_CONTACT）。</summary>
    private static bool OnResource(PermissionDefinition p, string module, string resource) =>
        InModule(p, module) && p.Code.Split('.')[1].StartsWith(resource, StringComparison.Ordinal);

    /// <summary>本人的審批待辦（查詢與核准／駁回）：業務角色都可能被指定為審批人。</summary>
    private static bool IsOwnApproval(PermissionDefinition p) =>
        p.Code is "SYS.APPROVAL.VIEW" or "SYS.APPROVAL.PROCESS";
}
