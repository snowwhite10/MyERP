# 進度總覽

> 每完成一個工作單元更新。完成度 = 已完成工作單元 / 該模組全部工作單元。

| 階段 | 模組 | 狀態 | 完成度 | 工作單元（完成 / 全部） | 測試（通過 / 全部） | Tag | 最後更新 |
|---|---|---|---|---|---|---|---|
| P0 | 基礎 | 進行中 | 54% | 7 / 13 | 243 / 243 | - | 2026-10-10 |
| P0 | SYS | 未開始 | 0% | 0 / 14 | - | - | 2026-10-09 |
| P1 | MD | 未開始 | 0% | 待拆分 | - | - | - |
| P1 | INV | 未開始 | 0% | 待拆分 | - | - | - |
| P1 | PUR | 未開始 | 0% | 待拆分 | - | - | - |
| P1 | SD | 未開始 | 0% | 待拆分 | - | - | - |
| P2 | FI | 未開始 | 0% | 待拆分 | - | - | - |
| P2 | CO | 未開始 | 0% | 待拆分 | - | - | - |
| P2 | BI | 未開始 | 0% | 待拆分 | - | - | - |
| P3 | PP | 未開始 | 0% | 待拆分 | - | - | - |
| P3 | QM | 未開始 | 0% | 待拆分 | - | - | - |
| P3 | PM | 未開始 | 0% | 待拆分 | - | - | - |
| P4 | CRM | 未開始 | 0% | 待拆分 | - | - | - |
| P4 | HR | 未開始 | 0% | 待拆分 | - | - | - |
| P4 | PS | 未開始 | 0% | 待拆分 | - | - | - |
| P4 | SCM | 未開始 | 0% | 待拆分 | - | - | - |

## 最近完成

（由 AI 追加，最新在上）

- 2026-10-10 WU-P0-007 共用核心完成：MyERP.Shared 加 IClock / SystemClock（UTC+8，`App:TimeZoneOffset`；唯一可讀系統時間的型別，TimeProvider 亦列禁用）、`Money.Round`（TWD/JPY 0 位、其他 2 位、AwayFromZero，可設定）、ICurrentUser / ICurrentCompany、PagedRequest / PagedResult / SortSpec 白名單、ErpException 系列 + errorCode、BusinessEntity / MasterEntity；Shared.Infrastructure 加 MyErpDbContext（IModuleDbModel、snake_case、型別慣例、具名 Company / SoftDelete 篩選、無公司時 `WHERE 0 = 1`、version concurrency token）、AuditStamper + SaveChanges interceptor、全域 ProblemDetails（三語標題、traceId、500 不洩漏細節、404 亦回 ProblemDetails）。243 測試全過，行覆蓋率 Shared 93.5% / Infrastructure 87%（`tools/scripts/Get-BackendCoverage.ps1`）；決策見 ADR-005、設計見 shared-core.md。

- 2026-10-10 WU-P0-006 後端骨架完成：`src/backend/MyERP.sln`（Api、Shared、Shared.Infrastructure、Migrations、Modules.System + Contracts 範本）、Directory.Build/Packages.props（net10.0、Nullable、TreatWarningsAsErrors、ADR-001 版本集中管理）、nuget.config（只用 nuget.org）、`/health/live`、`IErpModule` + `ErpModuleCatalog` 模組註冊；測試 MyERP.Shared.Tests、MyERP.ArchitectureTests（模組互不引用實作、Contracts 純淨、Domain 不依賴 EF/ASP.NET/其他層、禁 DateTime.Now 與 System.Data.SqlClient，以 IL 掃描且有自我測試）、MyERP.IntegrationTests（WebApplicationFactory）共 43 測試全過；xunit.v3 4.0.2 經 global.json 改走 Microsoft.Testing.Platform（ADR-004），測試指令改為 `dotnet test --solution ...`。

- 2026-10-10 WU-P0-005 架構與規範文件完成：architecture.md（全景圖、模組清單、Mermaid 依賴圖、跨模組交易內 / 交易後事件、安全架構、NFR 指標 REQ_NF421.1～425.6）、api-conventions.md（路由 `/api/v1/{prefix}/{複數資源}`、ProblemDetails + errorCode、分頁排序篩選、權限代碼）、db-conventions.md、ui-conventions.md（列表 / 單據頁範本、馬卡龍與 dark mode、i18n key）；ADR-003 決定每模組實作 + Contracts 兩專案、單一 MyErpDbContext、前綴統一於表 / 路由 / 權限。

- 2026-10-10 WU-P0-004 ADR-001 技術版本固定：本機實測 SDK 10.0.301（新增 global.json）、Node 22.16.0；固定 EF Core 10.0.12、EFCore.NamingConventions 10.0.1、SqlClient 6.1.7、Serilog 10、xunit.v3 4.0.2、FluentAssertions 7.2.2（8.x 商業授權）、Moq 4.20.72；前端 Angular 22.2 + ng-zorro 22.1.1 + G2 5.4.8 + TS 6.0，需 Node 24.21.0（新增風險 R-016、R-017）；docgen 用 .NET + OpenXML；查版本腳本 tools/versions/Get-LatestVersions.ps1。

- 2026-10-10 WU-P0-003 項目章程完成：目標 G1–G4、範圍（15 模組 + BlueWhale 基線 222 REQ_F / 150 介面按模組分佈）、不在範圍、成功標準 S1–S8、里程碑 M0–M6、角色；風險清單新增 R-008～015（BlueWhale 不一致、SRS/SDS 版面、無 Python、版本相容）；sprint-log 建立 Sprint 1 計劃。

- 2026-10-10 WU-P0-002 參考文件核對完成：用腳本逐項核對 SRS / SDS 原文（REQ_F 222、REQ_DS43 88、介面 150、REQ_IO_A 1,416），補齊章節規模、版面樣式、表格欄寬框線；對照表 460 行填上 MyERP 模組、需求 ID、介面編號、預計表名；編號規則寫入 ADR-002。
- 2026-10-10 WU-P0-001 Runner 檢查完成：符合 MASTER_PROMPT 2.1 全部要求；額度訊息解析實測 8 個案例正確；6 項改善建議寫入 change-requests.md（CR-001～006），詳見 runner-review.md。
