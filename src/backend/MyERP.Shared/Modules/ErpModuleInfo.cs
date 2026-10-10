namespace MyERP.Shared.Modules;

/// <summary>ERP 模組的固定識別資料（architecture.md 2.3、ADR-003 決定 4）。</summary>
/// <param name="Code">模組代碼（SYS、SD…），用於專案、文件、日誌與 state.json。</param>
/// <param name="ProjectName">實作專案名稱；Contracts 專案為其後加 <c>.Contracts</c>。</param>
/// <param name="Prefixes">資料表 / API 路由 / 權限代碼共用的前綴（小寫），第一個為主要前綴。</param>
/// <param name="SrsModuleNumbers">SRS 模組編號（MASTER_PROMPT 15.1，固定不變）。</param>
/// <param name="Phase">開發階段（P0–P4）。</param>
public sealed record ErpModuleInfo(
    string Code,
    string ProjectName,
    IReadOnlyList<string> Prefixes,
    IReadOnlyList<int> SrsModuleNumbers,
    string Phase);
