# 進度總覽

> 每完成一個工作單元更新。完成度 = 已完成工作單元 / 該模組全部工作單元。

| 階段 | 模組 | 狀態 | 完成度 | 工作單元（完成 / 全部） | 測試（通過 / 全部） | Tag | 最後更新 |
|---|---|---|---|---|---|---|---|
| P0 | 基礎 | 完成 | 100% | 13 / 13 | 後端 346 / 346；前端 181 / 181；E2E 16 / 16 | - | 2026-10-10 |
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

- 2026-10-10 WU-P0-013 文件產生器骨架完成：`tools/docgen` 使用 .NET 10 + OpenXML，從模組 Markdown、非功能需求、BlueWhale 對照表讀取來源，產出 SRS / SDS docx、九張工作表的需求 xlsx，以及一致性報告；可選 SQL Server 連線，以 `INFORMATION_SCHEMA` 產 SDS 4.1 欄位表並檢查資料表一致性。文件使用 `reference-notes.md` 的 BlueWhale 字型、欄寬、框線及章節順序；未完成敘述來源以 `[Pending]` 和警告標示。這次實際產出在 `docs/99-output/`，資料庫可連線，報告 0 Error / 5 Warning（後續內容待補）。新增 2 個 docgen 測試並納入 CI；後端 346 個測試通過。使用說明見 `tools/docgen/README.md`，決策見 ADR-010。

- 2026-10-10 WU-P0-012 Playwright 完成：`tests/e2e` 有獨立的 package.json，使用 @playwright/test 1.64.0，瀏覽器只裝 chromium，下載到 `.tools/ms-playwright`（由 `Invoke-Frontend.ps1` 設定 `PLAYWRIGHT_BROWSERS_PATH`）。webServer 先做前端 production 建置，再以 `serve-dist.mjs` 提供靜態檔：缺少的資源回 404，不以 index.html 掩蓋；路徑跳出根目錄時拒絕。共 16 個冒煙測試：首頁三張 G2 圖表加 KPI 迷你趨勢圖都以像素確認確實畫出；切換暗色時 theme-dark.css 回 200、CSSOM 有規則，body 色彩符合設計 token，重新整理後保留，切回亮色時移除；跟隨系統；切換三語時 `<html lang>` 與文字同步，並依瀏覽器語言決定預設語言；404 / 403 頁；靜態伺服器行為。自動 fixture 遇到任何 console error 即判定失敗。E2E 第一次執行就發現暗色模式下 body 背景被執行期插入的 ng-zorro 暗色 bundle 蓋成 `#000`，已改用 `html body` 提高權重修正。E2E 不加入 CI。後端 344、前端 181、E2E 16 個測試全過。說明見 e2e.md、ADR-009。

- 2026-10-10 WU-P0-011 CI 完成：新增 `.github/workflows/ci.yml`，有兩個 job，都跑在 `windows-latest`，權限只有 `contents: read`，不用任何機密。backend job 用 `global.json` 的 SDK 以 Release 建置，再逐一以 `dotnet test --project` 跑 `tests/backend.unit` 下的單元測試專案；不跑整合測試，因為需要 SQL Server。frontend job 用 `.nvmrc` 的 Node，執行 `npm ci`、`ng build`、`ng test --watch=false`（jsdom，不需瀏覽器）。新增 `CiWorkflowTests`（9 個測試），在本機檢查 workflow 結構：每個單元測試專案都要列入 CI、不得引用整合測試、沒有機密等。本機已用 Release 跑過 CI 的全部步驟，都通過。後端 344 個、前端 181 個測試全過。說明見 ci.md。

- 2026-10-10 WU-P0-009 應用程式日誌完成：`MyERP.Shared.Infrastructure/Logging` 以 Serilog 取代預設日誌提供者（`AddMyErpLogging` / `UseMyErpRequestLogging`）。日誌寫入滾動檔案 `logs/app-yyyyMMdd.log`，格式為 CLEF JSON，每天換檔，單檔上限 100 MB，保留 30 天。設定 `ApplicationInsights:ConnectionString` 或環境變數 `APPLICATIONINSIGHTS_CONNECTION_STRING` 後才啟用 Application Insights。實測 sink 5.0.1 相依 Microsoft.ApplicationInsights 2.23，和 AspNetCore SDK 3.2 衝突，所以只用 sink（ADR-008）。`ErpLogEnricher` 在每筆日誌加上 TraceId（和 ProblemDetails 的 traceId 對得上）、UserId、CompanyId，以及由路由前綴或命名空間判斷的 Module。請求耗時超過 `App:SlowRequestMilliseconds`（預設 1000）寫 Warning，其餘寫 Debug；請求路徑不含查詢字串。設定錯誤時啟動即失敗。appsettings 改用 `Serilog:MinimumLevel`。新增 53 個測試（單元 + WebApplicationFactory 整合，含檔案輸出），後端 335 測試全過；Infrastructure 行覆蓋率 84.8%。設計見 logging.md。

- 2026-10-10 WU-P0-010 前端骨架完成：`src/frontend` 為 Angular 22.2.2（standalone、zoneless、strict）+ ng-zorro-antd 22.1.1 + G2 5.4.8 + @ngx-translate 18。本機 Node 22 不符要求，改用 repo 內可攜版 Node 24.21.0，所有前端指令經 `tools/scripts/Invoke-Frontend.ps1` 執行（下載後驗證 SHA256，R-016 已緩解）。三語 i18n 按範圍分檔，模組語言檔以 `i18nScopeGuard` 延遲載入，`i18n-keys.spec.ts` 檢查三語 key 一致。主題有亮 / 暗 / 跟隨系統三種模式：用 less 編譯兩份 ng-zorro bundle 並換上馬卡龍主色，暗色 bundle 延遲載入；色值的 WCAG 對比由測試把關。Layout 有可收合側欄（平板寬度自動收合）與頂列（公司切換、語言、主題）；API 攔截器加上 Accept-Language 與 X-Company-Id。共用圖表元件有 erp-line-chart、erp-column-chart、erp-pie-chart（超過 7 類時合併「其他」）、erp-kpi-card（環比 + 迷你趨勢），主題或語言改變時重新渲染。另有首頁示範頁與 403 / 404 頁。前端 181 測試全過，覆蓋率 statements 97.4%、branches 96.6%；後端 282 測試全過。決策見 ADR-007。

- 2026-10-10 WU-P0-008 資料庫基線完成：`MyERP.Migrations` 第一個 migration `Core_Baseline`（`dotnet ef ... -p MyERP.Migrations -s MyERP.Api`，以 Api 主機 DI 建 DbContext）；定序改用 `Chinese_Traditional_Stroke_Count_100_CI_AS_SC`（MASTER_PROMPT 原名稱不存在，ADR-006、CR-007），既有庫定序不符時 migration 修正；SP 部署機制（`database/procedures/{前綴}/usp_*.sql` 嵌入 + `CreateOrAlterProcedure` / `DropProcedure`，範例 `usp_sys_database_info`）；EF 模型快取鍵含模組組合；`myerp_test` 每次測試執行重建（assembly fixture，拒絕連其他庫）；補 WU-P0-007 的真實資料庫測試（審計蓋章、樂觀鎖 409、公司 / 軟刪除篩選）；`/health/ready` 資料庫檢查（失敗 503）。本機 `myerp` 已建立並套用 migration。282 測試全過，Migrations 行覆蓋率 93.9%。

- 2026-10-10 WU-P0-007 共用核心完成：MyERP.Shared 加 IClock / SystemClock（UTC+8，`App:TimeZoneOffset`；唯一可讀系統時間的型別，TimeProvider 亦列禁用）、`Money.Round`（TWD/JPY 0 位、其他 2 位、AwayFromZero，可設定）、ICurrentUser / ICurrentCompany、PagedRequest / PagedResult / SortSpec 白名單、ErpException 系列 + errorCode、BusinessEntity / MasterEntity；Shared.Infrastructure 加 MyErpDbContext（IModuleDbModel、snake_case、型別慣例、具名 Company / SoftDelete 篩選、無公司時 `WHERE 0 = 1`、version concurrency token）、AuditStamper + SaveChanges interceptor、全域 ProblemDetails（三語標題、traceId、500 不洩漏細節、404 亦回 ProblemDetails）。243 測試全過，行覆蓋率 Shared 93.5% / Infrastructure 87%（`tools/scripts/Get-BackendCoverage.ps1`）；決策見 ADR-005、設計見 shared-core.md。

- 2026-10-10 WU-P0-006 後端骨架完成：`src/backend/MyERP.sln`（Api、Shared、Shared.Infrastructure、Migrations、Modules.System + Contracts 範本）、Directory.Build/Packages.props（net10.0、Nullable、TreatWarningsAsErrors、ADR-001 版本集中管理）、nuget.config（只用 nuget.org）、`/health/live`、`IErpModule` + `ErpModuleCatalog` 模組註冊；測試 MyERP.Shared.Tests、MyERP.ArchitectureTests（模組互不引用實作、Contracts 純淨、Domain 不依賴 EF/ASP.NET/其他層、禁 DateTime.Now 與 System.Data.SqlClient，以 IL 掃描且有自我測試）、MyERP.IntegrationTests（WebApplicationFactory）共 43 測試全過；xunit.v3 4.0.2 經 global.json 改走 Microsoft.Testing.Platform（ADR-004），測試指令改為 `dotnet test --solution ...`。

- 2026-10-10 WU-P0-005 架構與規範文件完成：architecture.md（全景圖、模組清單、Mermaid 依賴圖、跨模組交易內 / 交易後事件、安全架構、NFR 指標 REQ_NF421.1～425.6）、api-conventions.md（路由 `/api/v1/{prefix}/{複數資源}`、ProblemDetails + errorCode、分頁排序篩選、權限代碼）、db-conventions.md、ui-conventions.md（列表 / 單據頁範本、馬卡龍與 dark mode、i18n key）；ADR-003 決定每模組實作 + Contracts 兩專案、單一 MyErpDbContext、前綴統一於表 / 路由 / 權限。

- 2026-10-10 WU-P0-004 ADR-001 技術版本固定：本機實測 SDK 10.0.301（新增 global.json）、Node 22.16.0；固定 EF Core 10.0.12、EFCore.NamingConventions 10.0.1、SqlClient 6.1.7、Serilog 10、xunit.v3 4.0.2、FluentAssertions 7.2.2（8.x 商業授權）、Moq 4.20.72；前端 Angular 22.2 + ng-zorro 22.1.1 + G2 5.4.8 + TS 6.0，需 Node 24.21.0（新增風險 R-016、R-017）；docgen 用 .NET + OpenXML；查版本腳本 tools/versions/Get-LatestVersions.ps1。

- 2026-10-10 WU-P0-003 項目章程完成：目標 G1–G4、範圍（15 模組 + BlueWhale 基線 222 REQ_F / 150 介面按模組分佈）、不在範圍、成功標準 S1–S8、里程碑 M0–M6、角色；風險清單新增 R-008～015（BlueWhale 不一致、SRS/SDS 版面、無 Python、版本相容）；sprint-log 建立 Sprint 1 計劃。

- 2026-10-10 WU-P0-002 參考文件核對完成：用腳本逐項核對 SRS / SDS 原文（REQ_F 222、REQ_DS43 88、介面 150、REQ_IO_A 1,416），補齊章節規模、版面樣式、表格欄寬框線；對照表 460 行填上 MyERP 模組、需求 ID、介面編號、預計表名；編號規則寫入 ADR-002。
- 2026-10-10 WU-P0-001 Runner 檢查完成：符合 MASTER_PROMPT 2.1 全部要求；額度訊息解析實測 8 個案例正確；6 項改善建議寫入 change-requests.md（CR-001～006），詳見 runner-review.md。
