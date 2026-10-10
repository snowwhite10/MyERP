# 進度總覽

> 每完成一個工作單元更新。完成度 = 已完成工作單元 / 該模組全部工作單元。

| 階段 | 模組 | 狀態 | 完成度 | 工作單元（完成 / 全部） | 測試（通過 / 全部） | Tag | 最後更新 |
|---|---|---|---|---|---|---|---|
| P0 | 基礎 | 完成 | 100% | 13 / 13 | 後端 346 / 346；前端 181 / 181；E2E 16 / 16 | - | 2026-10-10 |
| P0 | SYS | 進行中（WU-SYS-002） | 20% | 3 / 15 | 後端 420 / 420；前端 285 / 285；E2E 28 / 28 | - | 2026-10-11 |
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

## 進行中

- 2026-10-11 WU-SYS-002 第二段：測試先行開放用戶管理 API `/api/v1/sys/users`（GET 分頁＋關鍵字＋帳號排序、POST 建立、PUT 啟用／停用帶 version、POST `{id}/reset-password`、POST `{id}/unlock`），新增 `SYS.USER.RESET_PASSWORD`。範圍為目前公司有用戶公司關聯的用戶，範圍外 404；建立時同交易加入目前公司（預設、無角色）；重複帳號（含他公司）409；不可停用自己 422；DTO 只回 `isLocked`／`isPasswordExpired`（ADR-016，REQ_F4118.23.5–8）。新增 6 個整合測試（SYS-USER-010～015），後端 420 / 420、建置 0 警告；前端與 E2E 未改動。既有測試 `UnsafeSecurityConfigurationRefusesStartup` 首次執行出現 ObjectDisposedException，重跑通過，疑為偶發。三語用戶頁及 E2E 待下一段。
- 2026-10-10 WU-SYS-003 第一段（已於第二段完成，見「最近完成」）：測試先行完成後端登入。`/api/v1/sys/auth` 提供 captcha（SVG、答案只存 Redis、一次即作廢）、login、refresh、logout、me。Access Token 15 分鐘 HS256，`sub` 為用戶 Id；Refresh Token 7 天，Redis 只存雜湊，以 Lua 原子取出實現輪替，5 個並發重放只有 1 個成功；登出只撤銷本人 Token。帳密錯誤、帳號不存在及鎖定回同一 401；密碼到期回 403 不發 Token。萬用碼只在 Development 有效並寫 Warning；非 Development 設定萬用碼或缺 JWT 金鑰即拒絕啟動。`/health/ready` 加 Redis 檢查；共用核心加 401 `UnauthenticatedException`。新增 13 個整合測試，後端 381 / 381、建置 0 警告；前端與 E2E 本輪未改動。決策見 ADR-012。前端登入頁、Captcha 元件、Token 刷新與登入 E2E 待續。
- 2026-10-10 WU-SYS-002 第一段：接手上一輪未提交變更並驗證。新增 `sys_user`（REQ_DS43.82，`System_User` migration 已套用 `myerp`）、ASP.NET Core Identity `PasswordHasher`、可由 `Security:PasswordPolicy` 設定的密碼政策（不合法設定啟動即拒絕）、連續 5 次失敗鎖 15 分鐘（以 `ExecuteUpdate` 原子累計，並發不遺失）、鎖定到期重開計數、密碼到期判斷，以及帳號大小寫及刪除後仍唯一。補啟動設定驗證測試，用戶整合測試 5 個，後端 366 / 366、建置 0 警告。需求 REQ_F4118.23.1–4 與 SYS 文件已更新。管理 API、三語用戶頁、登入整合及 E2E 要等 WU-SYS-003／004；WU-SYS-002 維持進行中。
- 2026-10-10 WU-SYS-001 第七段：測試先行建立三語公司／分店／部門列表及表單骨架；列表有本地搜尋、代碼排序與分頁，分店／部門依公司 ID 篩選，表單驗證代碼與非空白名稱並輸出大寫代碼草稿。元件不接 API、不加路由或選單；隔離檢查及三語鍵測試通過。前端正式建置、189 / 189、後端 361 / 361、既有 E2E 16 / 16 通過。授權、HTTP API、正式頁面及 E2E 驗收待 WU-SYS-002～004 後續作；WU-SYS-001 維持進行中。下一輪先做 WU-SYS-002 的用戶、密碼政策與鎖定，以解開授權前置作業。
- 2026-10-10 WU-SYS-001 第六段：測試先行補公司列表的授權 ID 篩選、啟用與軟刪除篩選、分頁、關鍵字、排序及空授權邊界；內部服務已實作，不註冊 DI 或公開 API。後端建置 0 警告、361 / 361，前端 181 / 181、既有 E2E 16 / 16 通過；docgen 0 Error / 5 Warning。完整 HTTP 授權、前端組織頁與其 E2E 驗收仍待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第五段：測試先行補分店／部門列表的分頁、關鍵字、排序、公司隔離、軟刪除及無公司拒絕；內部服務已實作白名單排序與無追蹤查詢。後端建置 0 警告、360 / 360 測試通過；前端 181 / 181、E2E 16 / 16；docgen 首次 SQL 逾時，重跑 0 Error / 5 Warning。公開 API、授權、前端組織頁與完整驗收尚待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第四段：新增三個組織整合測試，驗證三表 JSON 約束、三種代碼唯一性、部門公司隔離及軟刪除、停用公司後歷史資料、公司更新審計、部門雙 DbContext 並發。組織測試 13 / 13、後端 359 / 359 通過；既有服務與 migration 無須改動。授權、公開 API、前端與 E2E 尚未完成，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第三段：測試先行補公司建立／修改／停用、停用部門不可作上級、代碼格式、公司唯一索引與分店 JSON 約束；加入公司內部服務並補部門停用，三種組織代碼統一轉大寫與校驗。組織整合測試 10 / 10、後端 356 / 356 通過，建置 0 警告；docgen 重生 0 Error / 5 Warning。代碼規則與追溯已更新。授權、公開 API、前端、E2E 與完整驗收仍待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第二段：新增未註冊 DI 的 `OrganizationService`，實作分店／部門建立及修改的啟用公司、代碼、參照、上級循環和版本檢查；重複代碼檢查含軟刪除資料，並處理唯一索引衝突。補 5 個整合測試，涵蓋複合 FK 違規寫入、跨公司上級、循環、停用公司、刪除代碼和樂觀鎖版本遞增。後端建置 0 警告，353 / 353 測試通過；docgen 0 Error / 5 Warning。授權完成前服務不開放 API（ADR-011）。公司服務、權限、API、前端與完整驗收待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第一段：新增 SYS 組織需求、資料結構 ER、API／UI 設計、業務規則與測試計畫；`sys_company`、`sys_branch`、`sys_department` 模型與 `System_Organization` migration 已建立並套用至 `myerp`。公司為隔離根，分店／部門以公司篩選、軟刪除篩選和複合外鍵保護跨公司參照。整合測試涵蓋資料表、模型篩選與分店跨公司查詢；後端 348 / 348 通過，docgen 0 Error / 5 Warning。應用服務、權限、API、前端與其完整驗收尚未完成，工作單元維持進行中。

## 最近完成

（由 AI 追加，最新在上）

- 2026-10-10 WU-SYS-001 公司／分店／部門完成（第八段）：開放組織 API `/api/v1/sys/companies|branches|departments`（GET 分頁、POST、PUT），以 `SYS.COMPANY/BRANCH/DEPARTMENT.VIEW/CREATE/EDIT` 保護，回應 DTO 不外露審計欄位。公司 API 只涵蓋用戶獲授權的公司：修改其他公司回 404；`includeInactive` 可列出停用公司以便重新啟用。建立公司時在同一交易內授權建立者，並沿用其目前公司的角色（ADR-015）。分店／部門沿用 `X-Company-Id` 公司隔離。前端新增 `OrganizationService` 與 `/sys/companies|branches|departments` 頁面（lazy 路由、`permissionGuard`、系統管理選單）：nz-table 伺服器端分頁與排序、關鍵字搜尋、狀態標籤、對話框新增／編輯（編輯時代碼唯讀、可停用），按鈕以 `*erpHasPermission` 控制；重複代碼、版本衝突等錯誤以三語顯示；切換公司後自動重新查詢。骨架列表的本地篩選測試改為伺服器端查詢測試（見 optimization-log）。後端 414 / 414（新增 4 個整合測試）、前端 285 / 285（新增 7 個）、E2E 28 / 28（新增 4 個），建置 0 警告，docgen 0 Error。

- 2026-10-10 WU-SYS-004 角色、權限與資料範圍完成：新增 `sys_role`、`sys_role_permission`、`sys_user_company`、`sys_user_role`（REQ_DS43.90–93，`System_Authorization` migration 已套用 `myerp`）。角色是全系統定義，按公司指派；`sys_user_role` 以複合外鍵指向用戶公司關聯，沒有公司存取權就不能指派角色；每位用戶最多一間預設公司。權限目錄由各模組以 `IPermissionDefinitionProvider` 宣告，啟動時檢查格式、前綴與重複，不另建權限表。後端以 `[RequirePermission("SYS.ROLE.VIEW")]` 動態政策檢查目前公司權限；已登入請求的 `X-Company-Id` 不可存取時，整個請求回 403 `COMMON.COMPANY.ACCESS_DENIED`（修正偽造標頭可繞過公司篩選的問題）；缺權限回 403 `COMMON.PERMISSION.DENIED`。資料範圍取各角色最寬者，全權角色為全公司，缺少分店／部門歸屬時退回本人；Application 層以 `DataScopeFilter` 套用。新增角色 CRUD、權限目錄、用戶公司授權、本人授權（`/api/v1/sys/auth/authorization`）API；ArchitectureTests 檢查每個 action 都有權限代碼，且代碼已宣告。前端新增 PermissionService（進入主框架前載入權限與公司清單，記住的公司已不可存取時改用預設公司，切換公司重新載入）、`*erpHasPermission`、`permissionGuard`，選單依權限過濾，登出時清除。後端 410 / 410（新增 29）、前端 278 / 278（新增 13）、E2E 24 / 24（新增 3），建置 0 警告；docgen 0 Error（順便修正 ui-spec 介面表頭與測試計畫範圍式引用造成的 13 個舊 Error）。決策見 ADR-014。角色與用戶權限頁面拆至 WU-SYS-015。

- 2026-10-10 WU-SYS-003 登入完成（第二段：前端）：測試先行新增 `AuthService`，Token 與帳號存 `localStorage`（`myerp.auth.session`），壞資料或 Refresh Token 過期即清除。攔截器只為非匿名 API 加 Bearer；收到 401 時刷新一次再重送，並發的 401 共用同一個 refresh（後端輪替後舊 Token 立即失效）。刷新失敗會清除 Session，導向 `/login?returnUrl=…`。`authGuard`／`guestGuard` 保護主框架；`safeReturnUrl` 擋外部、`//`、`/\` 路徑。共用 `erp-captcha`（ControlValueAccessor）只收 4–6 位數字、點擊換圖、載入失敗可重試。三語登入頁依 `errorCode` 顯示錯誤，失敗後清空密碼並換驗證碼，頁底可切換語言。頂列顯示帳號和登出按鈕：登出時撤銷 Refresh Token，伺服器失敗也清除本機 Session。E2E 以 fixture 寫入假 Session，讓既有測試維持已登入；另以 `page.route` 模擬 auth API，新增 5 個登入 E2E（萬用碼登入、重新整理仍保持登入、登出、密碼／驗證碼錯誤、必填、returnUrl、三語）。前端 265 / 265（新增 76 個）、E2E 21 / 21、後端 381 / 381，建置 0 警告。ADR-001 補記本機 Redis 為 Windows 移植版 3.2.100（新增 R-018）。決策見 ADR-013。萬用碼稽核移到 WU-SYS-007，登入限流移到 WU-SYS-005，連真實 API 的 E2E 移到 WU-SYS-013。

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
