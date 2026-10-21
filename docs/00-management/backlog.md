# Backlog（產品待辦清單）

> 所有工作必須來自本表。新增項目附優先級與理由。狀態：`TODO` / `IN_PROGRESS` / `DONE` / `BLOCKED` / `WONT_DO`。
> 「待拆分」的 Epic：開始該模組前，先把它拆成多個工作單元（每個 30–90 分鐘可完成），再按次序執行。
> 優先級：P1 = 必須、阻擋後續；P2 = 必須；P3 = 應有；P4 = 可有。

## Phase 0 — 基礎

| ID | 模組 | 工作單元 | 優先級 | 狀態 | 依賴 | 備註 |
|---|---|---|---|---|---|---|
| WU-P0-001 | - | 檢查 Runner 是否符合 MASTER_PROMPT 2.1；問題寫入 change-requests.md（不得修改 Runner 核心檔） | P1 | DONE | - | Runner 已由人手提供；結果見 runner-review.md、CR-001～006 |
| WU-P0-002 | - | 閱讀 docs/reference 的 SRS / SDS（及 legacy-source 摘要），完成 reference-notes.md：章節大綱、表格欄位格式、功能 / 介面 / 資料表對照表 | P1 | DONE | - | 已核對原文並補 MyERP 側；分配規則見 ADR-002 |
| WU-P0-003 | - | project-charter.md、risk-register.md 補充、sprint-log.md（Sprint 計劃） | P1 | DONE | 002 | 章程含基線規模、成功標準 S1–S8、里程碑 M0–M6；新增風險 R-008～015；Sprint 1 計劃 |
| WU-P0-004 | - | ADR-001 技術版本固定（.NET 10 LTS、Angular / ng-zorro 版本、套件清單） | P1 | DONE | - | 已查證本機與 npm / NuGet 版本；global.json 10.0.301；Angular 22.2 + ng-zorro 22.1.1 需 Node 24（R-016）；FluentAssertions 固定 7.2.2（R-017） |
| WU-P0-005 | - | architecture.md（Mermaid 模組依賴圖）、api-conventions.md、db-conventions.md、ui-conventions.md、NFR 指標 | P1 | DONE | 004 | 四份文件 + NFR（REQ_NF421–425，architecture.md 第 8 節）；專案結構 / 單一 DbContext / 前綴統一見 ADR-003 |
| WU-P0-006 | - | 後端解決方案骨架：MyERP.Api、MyERP.Shared、模組專案範本、測試專案；建置與測試可跑 | P1 | DONE | 005 | 7 專案 + 3 測試專案，43 測試通過；xunit.v3 改走 Microsoft.Testing.Platform（ADR-004） |
| WU-P0-007 | - | 共用核心：IClock(UTC+8)、Money.Round、ICurrentUser/ICurrentCompany、分頁、ProblemDetails、審計 / 版本 SaveChanges interceptor、Global Query Filter | P1 | DONE | 006 | 243 測試通過；行覆蓋率 Shared 93.5%、Shared.Infrastructure 87%；設計見 shared-core.md、ADR-005 |
| WU-P0-008 | - | 資料庫基線：建庫 migration（定序 Chinese_Traditional_Stroke_Count_100_CI_AS_SC，ADR-006）、snake_case、myerp_test 重建機制、SP 部署機制（database/procedures + migrationBuilder.Sql） | P1 | DONE | 006 | Core_Baseline + 範例 SP usp_sys_database_info；TestDatabase assembly fixture；/health/ready；282 測試通過；原定序名稱不存在（ADR-006、CR-007） |
| WU-P0-009 | - | Serilog（檔案 + App Insights 可選）、TraceId/UserId/CompanyId enricher、慢 API Warning | P2 | DONE | 006 | CLEF 滾動檔案保留 30 天；App Insights 只用 sink（AspNetCore SDK 3.x 相依衝突，ADR-008）；設計見 logging.md；335 測試通過 |
| WU-P0-010 | - | 前端骨架：Angular standalone、ng-zorro、i18n（zh-Hant / zh-Hans / en）、dark mode、馬卡龍主題、Layout、G2 圖表共用元件 | P1 | DONE | 004 | Angular 22.2.2 + ng-zorro 22.1.1 + G2 5.4.8；可攜版 Node 24.21.0（Invoke-Frontend.ps1）；181 個前端測試通過，覆蓋率 97.4%；決策見 ADR-007 |
| WU-P0-011 | - | CI：GitHub Actions（windows-latest）建置 + 單元測試 + 前端測試 | P2 | DONE | 006, 010 | .github/workflows/ci.yml（backend + frontend 兩個 job）；CiWorkflowTests 本機檢查結構；整合測試 / E2E 在 VM 本機跑；見 ci.md |
| WU-P0-012 | - | Playwright 基礎設定 + 冒煙測試 | P2 | DONE | 010 | tests/e2e：production 建置 + serve-dist.mjs；16 個冒煙測試（G2 canvas 像素、暗色 bundle、三語、404/403）；修正暗色 body 背景被覆蓋；見 e2e.md、ADR-009 |
| WU-P0-013 | - | tools/docgen 骨架：讀取 Markdown 來源 → 產生 SRS / SDS / xlsx 空殼，套用 BlueWhale 樣式；一致性檢查骨架 | P2 | DONE | 002 | .NET + OpenXML CLI；輸出 2 份 docx、9 工作表 xlsx、一致性報告；ADR-010、tools/docgen/README.md；之後每模組重跑 |
| WU-P0-014 | - | 整合測試偶發 SQL 逾時：逾時時自動傾印 `dm_exec_requests`／阻擋源與執行緒集區狀態；評估測試連線關閉 MARS 或限制整合測試並行數；找到原因後修正 | P2 | DONE | - | 高並行重跑捕捉到十萬筆估值測試的大交易與回滾期間，其他 INSERT 鎖等待 21–22 秒；16 threads 有期初入庫鎖失敗。估值測試 collection 改為不與其他測試並行後，16 threads 510/510、最大等待 0.524 秒；後端全量 937/937。採樣補閒置 blocker 與失敗檢查，MARS=True 保留；原 35 秒逾時未逐字重現，見 log-analysis.md。 |
| WU-P0-015 | - | 補 MASTER_PROMPT 19.1–19.3 人手啟動路徑：`Start-Dev.ps1` 前置檢查／固定埠啟動、Angular proxy、README 手動與一鍵步驟 | P1 | BLOCKED | 010 | 腳本、代理、README 及既有建置／測試已完成；連續三輪因既有互動程序佔用預設埠，無法按 README 完整啟動驗收。4200 的健康頁為 Angular HTML，不能當作 API 健康；待埠釋放後重試，再由 016 建立 smoke 補驗。詳見 blocked.md、dev-start.md。 |
| WU-P0-016 | - | 建立 MASTER_PROMPT 19.4 真實啟動冒煙測試，走 README 的預設 `npm start`＋`dotnet run --launch-profile`，驗證驗證碼、登入、首頁、登出 | P1 | TODO | 015 | 011c 查驗發現 repo 無 `tests/smoke`；19.5 要求模組打 tag 前通過，INV-ASM-016 依此驗收 |
| WU-P0-017 | - | 補 MASTER_PROMPT 20.3 Playwright 共用守衛：意外 5xx、未處理 Promise rejection、未翻譯 i18n key 自動失敗 | P1 | DONE | 012 | fixture 獨立檢查 `/api/` 5xx、pageerror 及可見未翻譯 key；既有預期 500 明確登記 URL／狀態。守衛自測 5/5，E2E 全量 188 通過／5 略過，型別檢查通過；見 e2e.md 5.1。 |

## Phase 0 — SYS 系統管理（SRS 模組 1 與 8 的一部分）

| ID | 模組 | 工作單元 | 優先級 | 狀態 | 依賴 | 備註 |
|---|---|---|---|---|---|---|
| WU-SYS-001 | SYS | 公司 / 分店 / 部門（多公司隔離） | P1 | DONE | P0-007 | 三表模型／migration、內部服務；組織 API（`SYS.COMPANY/BRANCH/DEPARTMENT.*`，公司限獲授權範圍、建立者同交易獲授權，ADR-015）；`/sys/companies|branches|departments` 三語頁面（伺服器端分頁、對話框新增／編輯、權限按鈕）；4 個 API 整合測試、11 個前端單元、4 個 E2E |
| WU-SYS-002 | SYS | 用戶、ASP.NET Core Identity PasswordHasher、密碼政策、帳號鎖定 | P1 | DONE | 001 的資料模型基礎 | `sys_user` 模型／migration、PasswordHasher、可設定密碼政策、5 次鎖 15 分鐘（原子計數）、到期判斷；管理 API `/api/v1/sys/users`（ADR-016）及 6 個 API 整合測試；三語 `/sys/users` 頁（介面 8.61、8.63）、前端 10 個單元測試、E2E 5 個 |
| WU-SYS-003 | SYS | 登入 / 登出、JWT Access + Refresh（Redis、輪替）、Captcha（含 Development 萬用碼與啟動檢查） | P1 | DONE | 002 | 後端 API、Redis 輪替、萬用碼啟動檢查、13 個整合測試（ADR-012）；三語登入頁、`erp-captcha`、Token 保存／自動刷新、路由守衛、登出、5 個登入 E2E（模擬 API，ADR-013）。萬用碼稽核移 WU-SYS-007、登入限流移 WU-SYS-005、連真實 API 的 E2E 移 WU-SYS-013 |
| WU-SYS-004 | SYS | 角色、權限代碼（模組.資源.動作）、資料範圍、前端權限指令 | P1 | DONE | 003 | 4 張表（REQ_DS43.90–93）、`System_Authorization` migration；`[RequirePermission]` + 公司存取中介層 + 403 錯誤代碼；角色／權限目錄／用戶公司授權／本人授權 API；`DataScopeFilter`；ArchitectureTests 檢查每個 action；PermissionService、`*erpHasPermission`、`permissionGuard`、選單過濾；ADR-014。角色與用戶權限頁面拆至 WU-SYS-015 |
| WU-SYS-005 | SYS | 安全中介層：HTTPS/HSTS、CORS、Rate limiting、安全標頭、ForwardedHeaders | P1 | DONE | 003 | `AddMyErpWebSecurity`：ForwardedHeaders（未設 Proxy 不啟用）、安全標頭、非 Dev HSTS／HTTPS、CORS 白名單、限流（login／refresh 每 IP 每端點 5、captcha 10、每用戶 300、匯出政策 10）、429 `COMMON.REQUEST.RATE_LIMITED`；前端共用 `apiErrorKey` 三語 429；E2E 在前端 CSP 下執行；Token 維持 Bearer（ADR-018）；17 個整合／單元／架構測試、前端 11 個、E2E 1 個 |
| WU-SYS-006 | SYS | IP 白名單（全系統 / 角色 / 用戶、CIDR、有效期限、總開關） | P2 | DONE | 004 | 後端表／migration／管理 API／身分層級登入與 Refresh 檢查／稽核（ADR-025）；三語 `/sys/ip-whitelist`（介面 8.75）、共用列表範本與停用確認；可信代理、admin 拒絕與快取 30 秒測試、模擬 API E2E。 |
| WU-SYS-007 | SYS | 稽核日誌（sys_audit_log）+ 稽核查詢頁 + 操作日誌（BlueWhale Operation Log） | P1 | DONE | 001 | `sys_audit_log`、同交易稽核 interceptor、登入／登出／失敗／萬用碼與權限變更安全事件、查詢 API `GET /api/v1/sys/audit-logs`、13 個整合測試（ADR-019，REQ_F4118.27.1–5）；三語 `/sys/audit-logs` 頁（介面 8.66，REQ_F4118.27.6）：篩選與日期快選、展開欄位差異、`?tableName=&recordId=` 單據修改歷史（ADR-020）；前端 13 個單元測試、E2E 4 個 |
| WU-SYS-008 | SYS | 單號規則服務（併發不重號） | P1 | DONE | 001 | `sys_number_rule`／`sys_number_sequence`（REQ_DS43.95–96）、`INumberGenerator`（MERGE HOLDLOCK、同交易不跳號，ADR-021）、管理 API、9 個整合測試（REQ_F4118.28.1–5）；三語 `/sys/number-rules` 頁（介面 8.67，REQ_F4118.28.6）：即時樣式、週期檢查、已發號鎖定、預覽下一號；前端 14 個單元測試、E2E 5 個 |
| WU-SYS-009 | SYS | 審批流程引擎（多級、金額門檻、代理人） | P1 | DONE | 004 | 五張審批表（REQ_DS43.97–101）、`MyERP.Shared.Approvals` 送審介面與交易內結果處理器（ADR-022）、流程／代理／待辦／核准駁回 API、10 個整合測試（REQ_F4118.29.1–7）；三語流程、代理、待辦頁（介面 8.68–8.70，REQ_F4118.29.8–9）：前端 19 個單元測試、E2E 6 個 |
| WU-SYS-010 | SYS | 系統參數、列印設定、Logo 與印章 | P2 | DONE | 001 | BlueWhale 4.1.1.8.17–19；2026-10-11 拆為 010a–010d（每個可在一輪完成），全部完成 |
| WU-SYS-010a | SYS | 系統參數後端：模組宣告參數（`IParameterDefinitionProvider`）、`sys_parameter`、批次保存 API、`ISystemParameters` 讀取 | P2 | DONE | 001 | REQ_F4118.17.1–3、REQ_DS43.73、ADR-026；SYS-PARAM-001～004 |
| WU-SYS-010b | SYS | 系統參數頁（介面 4.4.1.8.55 Parameter Edit Interface）：依型別產生欄位、分組、三語參數名稱、批次保存與版本衝突、權限、E2E | P2 | DONE | 010a | `/sys/parameters`（REQ_F4118.17.4、介面 8.55）：分組卡片、依型別欄位、三語名稱、恢復預設、只送修改項目、409 重新載入、唯讀、離開確認；SYS-PARAM-005～007 |
| WU-SYS-010c | SYS | 列印設定（BlueWhale 4.1.1.8.18／介面 4.4.1.8.56）：列印抬頭（公司名稱、地址、電話、傳真、郵遞區號）、採購單／銷售出貨單頁尾備註、是否列印 Logo／印章；後端 + 三語頁 | P2 | DONE | 010a | 9 個 `SYS.PRINT_*` 參數（ADR-027，REQ_F4118.18.1–3）、`IPrintSettingsReader`（Contracts，公司名稱空白用主檔名稱）；三語 `/sys/print-settings`（介面 8.56）重用參數頁並加抬頭預覽、多行頁尾；SYS-PRINT-001～005 |
| WU-SYS-010d | SYS | Logo 與印章（BlueWhale 4.1.1.8.19／介面 4.4.1.8.57）：每間公司上傳 Logo／印章圖片（類型、大小、尺寸檢查）、預覽、替換；後端 + 三語頁 | P2 | DONE | 010c | `sys_company_image`（REQ_DS43.103，圖片存資料庫）、以內容判斷 PNG／JPEG、1 MB、16–2048 像素（ADR-028，REQ_F4118.19.1–3）；`ICompanyImageReader`（Contracts）；三語 `/sys/logo-stamp`（介面 8.57）預覽、上傳／替換、確認刪除；SYS-IMG-001～008 |
| WU-SYS-011 | SYS | 首頁、修改密碼、個人設定（語言、主題） | P2 | DONE | 003 | BlueWhale 4.1.1.1；2026-10-11 拆為 011a／011b，全部完成 |
| WU-SYS-011a | SYS | 本人修改密碼後端：舊密碼驗證、到期密碼入口、政策、刷新憑證撤銷、稽核 | P2 | DONE | 003 | REQ_F4111.1.2、ADR-029；SYS-AUTH-018～022 |
| WU-SYS-011b | SYS | 首頁、修改密碼介面（含密碼到期流程）、個人設定語言／主題保存；三語與 E2E | P2 | DONE | 011a | 介面 1.2–1.4；瀏覽器偏好見 ADR-030；前端 420/420、E2E 64/64（另 3 個真實 API 案例略過），後端 585 個分批通過 |
| WU-SYS-012 | SYS | 站內通知 | P3 | DONE | 001 | 拆為 012a 發布與儲存、012b 收件箱 API 與三語頁面；拆分理由：跨模組契約與收件箱介面可分別在 30–90 分鐘驗收 |
| WU-SYS-012a | SYS | 站內通知發布契約、收件人驗證、公司隔離、去重與保存 | P3 | DONE | 001 | REQ_F4118.33.1、REQ_DS43.104、ADR-031；3 個整合測試，並發去重 |
| WU-SYS-012b | SYS | 站內通知收件箱：列表、未讀數、已讀、三語頁面與 E2E | P3 | DONE | 012a | REQ_F4118.33.2–3、介面 8.76；`/api/v1/sys/notifications`（VIEW／READ）、每日清理過期通知、頂列徽章與收件箱、種子角色加本人通知權限；SYS-NOTIFY-004～007 |
| WU-SYS-013 | SYS | 種子資料：測試帳號（MASTER_PROMPT 第 12 節）、2 間公司、分店 | P1 | DONE | 004 | `--seed` 指令（只限 Development、冪等）、`IDataSeeder`；TW01／HK01、5 分店、14 部門、9 角色（規則對照權限目錄展開）、9 帳號（REQ_F4118.30.1–3，ADR-023）；`Invoke-RealApiE2E.ps1` + 3 個真實 API 登入 E2E（SYS-AUTH-017）；修正登出後列表頁送出 401 請求。本位幣欄位、示範審批流程與單號規則留給 MD／FI 與各業務模組種子 |
| WU-SYS-014 | - | 共用介面：確認框、提示框、選擇視窗（BlueWhale 4.4.1.9）、標準列表頁 / 單據頁範本 | P1 | DONE | P0-010 | `ErpDialogService`（確認框、提示框、選擇視窗、成功提示）、`erp-list-page` + `table-query`、`erp-document-page` + `adjacentIds` + `unsavedChangesGuard`、`erp-date-range`、`erp-status-tag`（介面 8.71–8.74，REQ_F4118.31.1–6，ADR-024）；角色頁改用列表範本與確認框；前端 20 個單元測試、E2E 確認框取消／確定。選擇視窗與單據頁的 E2E 待第一個主檔／單據頁採用時補 |
| WU-SYS-015 | SYS | 角色維護頁（列表、建立／編輯、權限勾選、資料範圍）與用戶權限頁（BlueWhale 4.4.1.8.26 User Permission Edit Interface：公司、分店／部門、預設公司、角色），三語、E2E | P2 | DONE | 004, 002 | `/sys/roles`（介面 8.64）與用戶權限對話框（介面 8.65）；授權指派限目前公司用戶與操作者可管理公司、新增指派查詢 API（ADR-017）；前端 17 個單元測試、E2E 7 個 |
| WU-SYS-016 | SYS | SYS 模組完成檢查：按 MASTER_PROMPT 自我代碼審查（可讀性、重複、效能／N+1、安全、錯誤處理）寫入 `code-review-log.md`，修正發現的問題，完整回歸（含真實 API E2E）後打 tag `v0.1.0-sys` | P1 | DONE | 001–015 | 審查發現 13 項後端、12 項前端問題，超過一輪，拆為 016a–d，全部完成；tag `v0.1.0-sys` |
| WU-SYS-016a | SYS | 審查結果寫入 code-review-log.md、log-analysis.md；修正後端安全與健壯性問題 SYS-B01、B02、B04、B05、B07、B08、B12、B13 | P1 | DONE | 016 | `AuthorityGuard`（ADR-032）；SYS-PERM-028～030；B06、B11 有理據不修改 |
| WU-SYS-016b | SYS | 修正安全與競態發現：SYS-B03（角色／IP 白名單是全系統資源）；前端 SYS-F02（列表過期回應保護）、F03、F05、F12 | P1 | DONE | 016a | ADR-033 按影響範圍檢查全權；共用 `latestRequest`；SYS-PERM-031～032、SYS-IPW-008～009、SYS-ORG-027 |
| WU-SYS-016c | SYS | 修正其餘審查發現：後端 SYS-B09（`ApprovalFlowService` 步驟驗證、`OrganizationService` 上層部門檢查改一次查詢）、B10（`ActiveCompanyIdAsync`、`UsernamesAsync`、代碼／名稱排序共用 helper）；前端 SYS-F04、F06、F07、F08、F09、F10、F11；F01 先抽共用 helper | P1 | DONE | 016b | `SystemQueries`；`shared/date.displayTime`；通知類別與步驟摘要 i18n；F01 部分修正並記錄理由；後端 599、前端 432、E2E 65 |
| WU-SYS-016d | SYS | SYS 完整回歸：後端、前端、模擬 E2E、`Invoke-RealApiE2E.ps1` 真實 API E2E；docgen（`--connection` 用 myerp）；全部通過後打 tag `v0.1.0-sys`、state.modules.SYS = DONE、重試 BLOCKED 項目，然後拆分 EP-MD | P1 | DONE | 016c | 後端 599/599（首次有 16 個偶發 SQL 逾時，重跑三次全部通過，見 WU-P0-014）、前端 432/432、模擬 E2E 65 通過 3 略過、真實 API E2E 3/3、docgen 0 Error；BlueWhale 對照表 SYS 列改為實際需求／介面／資料表編號；沒有 BLOCKED 項目；EP-MD 拆為 WU-MD-001～022 |

## Phase 1–4 — Epic（開始前先拆分）

| ID | 模組 | Epic | 優先級 | 狀態 | 依賴 | 備註 |
|---|---|---|---|---|---|---|
| EP-MD | MD | 主檔：客戶（含聯絡人、匯入、分類）、供應商（含聯絡人、匯入、分類）、物料（品牌、分類、圖片、期初庫存）、UoM 換算、倉庫儲位、幣別匯率、稅率、付款條件 / 結算方式、會計科目、收支類別、工序 / 工序分類 | P1 | 已拆分 | SYS | 2026-10-11 拆為下方 WU-MD-001～022；期初庫存（BlueWhale 4.4.1.8.14 Product Initial Stock）要寫庫存異動帳，MD 不得寫 INV 表，移入 EP-INV |
| EP-INV | INV | 入庫出庫（其他入庫 / 出庫）、調撥、盤點、組裝 / 拆卸、批號序號、安全庫存、庫存估值（SP）、物料期初庫存（自 EP-MD 移入，介面 4.4.1.8.14） | P1 | 已拆分 | MD | 2026-10-13 拆為 WU-INV-001～018；先建立異動帳與跨模組鎖，再逐項交付介面與交易 |
| EP-PUR | PUR | 請購、詢價比價、採購單、收貨（採購入庫）、退貨、三單比對 | P1 | 已拆分 | INV | 2026-10-19 拆為 WU-PUR-000～009；INV 跨模組交易契約已可供後續收貨使用，INV 模組 QA／真實 smoke 仍另行追蹤。 |
| EP-SD | SD | 報價、銷售訂單、定價折扣、出貨（銷售出庫及審批）、發票、信用管理、退貨 | P1 | 待拆分 | INV | |
| EP-FI | FI | 總帳與過帳引擎、應收（銷售收款、其他收款、收款核銷、客戶對帳單）、應付（採購付款、其他付款、付款核銷、供應商對帳單）、固定資產、銀行現金、稅務、匯差、月結年結、財務報表 | P1 | 待拆分 | SD, PUR | |
| EP-CO | CO | 成本中心、利潤中心、產品成本、預算 vs 實際 | P2 | 待拆分 | FI | |
| EP-BI1 | BI | 儀表板、BlueWhale 22 張報表（4.4.1.7）、比較 / 鑽取 / 匯出 / 排程 | P1 | 待拆分 | FI | |
| EP-PP | PP | BOM（分組）、工藝路線、MPS、MRP（SP）、生產計劃、工單、領料、完工入庫、產能、車間 | P2 | 待拆分 | INV, PUR | |
| EP-QM | QM | 來料 / 製程檢驗、不合格品、品質報告 | P3 | 待拆分 | PP | |
| EP-PM | PM | 設備、預防保養、維修工單、備件 | P3 | 待拆分 | INV | |
| EP-CRM | CRM | 潛在客戶、商機、互動、活動、客服工單 | P3 | 待拆分 | SD | |
| EP-HR | HR | 員工、組織、考勤排班、薪資、假期、招聘、績效、培訓 | P3 | 待拆分 | SYS | |
| EP-PS | PS | 項目預算、工時、資源、里程碑、按項目計費 | P3 | 待拆分 | FI | |
| EP-SCM | SCM | 需求預測、物流運輸、供應商協作入口 | P3 | 待拆分 | PUR | |

## Phase 1 — PUR 採購工作單元

| ID | 模組 | 工作單元 | 優先級 | 狀態 | 依賴 | 備註 |
|---|---|---|---|---|---|---|
| WU-PUR-000 | PUR | 拆分 EP-PUR，固定請購需求、資料／API／介面契約與完整測試情境 | P1 | DONE | INV 異動契約 | 設計切片；REQ_F4114.3.1–6、介面 4.8–4.10；不宣稱已有 API 或資料表。 |
| WU-PUR-001 | PUR | 模組骨架、Contracts、請購權限與隔離守衛 | P1 | DONE | 000 | 紅燈編譯測試後建立兩組件、六個權限與公司守衛；Release 後端 988/988。資料表與 HTTP 端點待 002。 |
| WU-PUR-002 | PUR | 請購草稿資料表、CRUD、單號與 MD 引用真實 SQL／HTTP 驗收 | P1 | DONE | 001 | 002a 資料表與 002b CRUD／引用、真實 SQL／HTTP 均完成；審批留 003。 |
| WU-PUR-002a | PUR | 請購表頭與明細資料模型、migration、真實 SQL 結構與隔離驗收 | P1 | DONE | 001 | REQ_DS43.161–162；`Purchasing_RequisitionDraft` 已套用 myerp；真實 SQL 閏日／六位數量、公司隔離、負數約束測試通過。 |
| WU-PUR-002b | PUR | 請購草稿讀寫 API、SYS 單號、MD 引用與真實 SQL／HTTP 驗收 | P1 | DONE | 002a | 002b1 列表／詳情與 002b2 寫入均完成；審批歷史留 003。 |
| WU-PUR-002b1 | PUR | 請購列表／詳情唯讀 API：篩選、排序、分頁、公司隔離及匿名 HTTP 守衛 | P1 | DONE | 002a | 真實 SQL 讀取與 HTTP 401 已驗；已保存明細／版本可讀。已登入 VIEW／資料範圍驗收與審批歷史分別待 002b2／003。 |
| WU-PUR-002b2 | PUR | 請購寫入、SYS 單號、MD 引用、版本、資料範圍與真實 SQL／HTTP 驗收 | P1 | DONE | 002b1 | 002b2a 資料範圍、002b2b 建立、002b2c 修改／軟刪完成。 |
| WU-PUR-002b2a | PUR | 請購分店／部門歸屬 migration、SYS VIEW 資料範圍與已登入 SQL／HTTP 驗收 | P1 | DONE | 002b1 | 真人登入 VIEW 200／無權 403；本人／部門／分店／全公司讀取及他公司 404。 |
| WU-PUR-002b2b | PUR | 請購建立草稿、SYS 同交易單號、MD 有效物料引用、原子保存與無 INV 異動 | P1 | DONE | 002b2a | 先紅燈真實 SQL／HTTP 測試；建立時分店／部門取 SYS 授權，400／403／422 與 1～1000 行、六位數量、閏日邊界。PUR 專屬併發及失敗重試加強測試留 002b2c。 |
| WU-PUR-002b2c | PUR | 請購修改／軟刪、版本 409、資料範圍及真實 SQL／HTTP 驗收 | P1 | DONE | 002b2b | 草稿／駁回整組替換、草稿軟刪、409／403／404／422、MD 停用／軟刪與回滾、無 INV 異動；PUR 併發取號及拒絕後重試已驗。 |
| WU-PUR-003 | PUR | 請購送審、SYS 核准／駁回、狀態與稽核 | P1 | DONE | 002 | 003a～003c 完成；最終核准後形成可轉採購單的已批准數量。 |
| WU-PUR-003a | PUR | 請購送審端點、SYS 送審契約、交易狀態與 SQL 測試 | P1 | DONE | 002 | 草稿送審、版本／權限／資料範圍、立即核准及回滾已驗；真實 SYS／MD 邊界留 003b／003c。 |
| WU-PUR-003b | PUR | SYS 最終核准／駁回處理器、審批歷史與真實 HTTP 測試 | P1 | DONE | 003a | `IApprovalResultHandler` 同交易更新 PUR；詳情經 VIEW 守衛後返回所有輪次與意見；真實 SQL／SYS HTTP 決定、狀態衝突回滾及修訂重送通過。Release 後端全量 1009/1009。 |
| WU-PUR-003c | PUR | 審批邊界、併發與端到端驗收 | P1 | DONE | 003b | 真實 MD 停用／軟刪、版本／狀態／範圍、雙連線送審與決定、PUR／SYS 稽核及回滾；Release 後端全量 1019/1019、DocGen 0 Error。前端請購頁留 004。 |
| WU-PUR-004 | PUR | 請購三語亮暗列表／表單、CSV／列印及 E2E／畫面 QA | P1 | DONE | 003 | 004a～d 全部完成；完整安全 CSV、三語亮暗與回歸已驗。真實預設埠啟動仍由 P0-015／016 追蹤。 |
| WU-PUR-004a | PUR | 請購前端 API 契約、十進位輸入守衛與真實 HTTP 精度驗收 | P1 | DONE | 003 | 列表／詳情／建立／修訂／送審／刪除契約；六位數量字串真實 HTTP 保存，未建立畫面。 |
| WU-PUR-004b | PUR | 請購三語亮暗列表／詳情、審批歷史、狀態動作及畫面 QA | P1 | DONE | 004a | 介面 4.8 三語亮暗、保存詳情與多輪歷史；權限／狀態、空／搜尋無結果、400／409／422／500／斷線／逾時、成功／失敗列印與 E2E。搜尋時關閉舊詳情；MD 停用原因可讀。CREATE／EDIT 表單留 004c，CSV 留 004d。 |
| WU-PUR-004c | PUR | 請購三語亮暗建立／修訂表單、MD 受權選項與畫面 QA | P1 | DONE | 004b | 介面 4.9～4.10、EDIT 範圍讀取、逐欄錯誤、未存離開、狀態／權限守衛；前端 683／683、後端 1022／1022、全量 E2E 264 通過／5 略過，12 個表單 E2E 通過。 |
| WU-PUR-004d | PUR | 請購完整安全 CSV、跨頁一致性與 4.8～4.10 回歸 QA | P1 | DONE | 004c | 201 張跨頁、10000 行上限、BOM／公式防護、取消與失敗零部分檔；CSV E2E 13/13，前端 685/685、後端 1022/1022、全量模擬 E2E 268 通過／5 略過、DocGen 0 Error／64 Warning。 |
| WU-PUR-005 | PUR | 詢價與供應商報價、比價及轉採購單設計與實作 | P1 | IN_PROGRESS | 004 | 拆為 005a～005f；只有子單元全部完成才標 DONE。 |
| WU-PUR-005a | PUR | 固定詢價、供應商報價、逐行比價及轉單需求／設計／完整測試情境 | P1 | DONE | 004 | ADR-070、REQ_F4114.4～6、介面 4.11～4.15、PUR-RFQ-001～018；設計切片不宣稱資料表或端點已交付。 |
| WU-PUR-005b | PUR | 詢價表頭／行與供應商邀請資料模型、migration、真實 SQL 約束及公司隔離 | P1 | DONE | 005a | REQ_DS43.163～165；`Purchasing_RfqDraft` 已套用 myerp，同公司複合 FK、索引、唯一號碼、六位數量及發送快照 CHECK；RfqSchemaTests 2/2、後端全量 1024/1024。MD／SYS 只有 ID 無跨模組 FK；草稿 API 留 005c。 |
| WU-PUR-005c | PUR | 詢價草稿 API、SYS 單號、MD 有效引用、發送／關閉與真實 SQL／HTTP 測試 | P1 | DONE | 005b | 005c1～c3 全部完成；發送快照及關閉受權 API、真實 MD／請購重驗與雙連線競態已驗。 |
| WU-PUR-005c1 | PUR | 詢價受權列表／詳情、保存行與邀請、公司及資料範圍、白名單分頁 | P1 | DONE | 005b | 真實 SQL／受權 HTTP 各 1/1；白名單排序、日期與來源篩選、軟刪隱藏、401／403／400／404。 |
| WU-PUR-005c2 | PUR | 詢價草稿建立／整組修訂／軟刪、SYS 單號、MD 與請購來源、版本及交易 | P1 | DONE | 005c1 | 真實 MD 停用／軟刪／未核准、SYS 同交易發號與注入故障回滾、受權 HTTP 201／200／204、範圍外 404、雙連線不重號；Release 全量 1033/1033。 |
| WU-PUR-005c3 | PUR | 詢價發送快照與關閉、重驗引用、狀態與版本競態 | P1 | DONE | 005c2 | 發送同交易保存供應商代碼／名稱及 sentAt；CLOSE 只接受已發送，真實 SQL／HTTP 驗權限、重驗、回滾及雙連線 409；Release 1036/1036。報價拒絕關閉狀態由 005d 的報價 API 驗。 |
| WU-PUR-005d | PUR | 供應商報價資料模型與 API：逐行單價／幣別／有效期、修訂及真實 SQL／HTTP 測試 | P1 | DONE | 005c | 005d1～d4 全部完成；報價讀寫、撤回、過期與競態已驗。 |
| WU-PUR-005d1 | PUR | 報價表頭、不可變修訂／行資料模型、migration 與真實 SQL 約束 | P1 | DONE | 005c | REQ_DS43.166～168、同公司邀請 FK、唯一與不可變歷史觸發器；QuotationSchemaTests 專項通過。 |
| WU-PUR-005d2 | PUR | 報價受權唯讀列表／詳情及版本歷史查詢 | P1 | DONE | 005d1 | `PUR.QUOTATION.VIEW` 公司／本人／部門／分店先驗詢價，撤回及全部修訂可見；真實 SQL 多供應商跨頁、篩選及 HTTP 401／403／404；整合全量 607/607。 |
| WU-PUR-005d3 | PUR | 首版報價與不可變修訂 API、MD 幣別／稅率重驗與交易競態 | P1 | DONE | 005d2 | 已完成 POST 首版／修訂、QUOTE 動作範圍、MD 公開契約重驗、整單原子保存、同版競態及受權 HTTP；後端整合 613/613、帶 DB DocGen 0 Error／67 Warning。過期／撤回／決標與故障注入由 005d4 驗。 |
| WU-PUR-005d4 | PUR | 報價撤回、過期／決標狀態、停用／軟刪引用與故障邊界 | P1 | DONE | 005d3 | 撤回受權 API、即時 `isExpired`、真實 MD 軟刪及故障回滾、雙連線撤回／修訂競態；Release 後端 1050/1050。 |
| WU-PUR-005e | PUR | 逐行比價、決標與轉採購單草稿的原子後端流程及競態測試 | P1 | IN_PROGRESS | 005d | 工作範圍超過 90 分鐘，拆成 005e1～e4；子單元全部完成後關閉父項。 |
| WU-PUR-005e1 | PUR | MD 匯率／稅率公開快照契約及真實 SQL 日期、公司、失效測試 | P1 | DONE | 005d | 契約及真實 SQL 專項 2/2；Release 整合專案單獨單執行緒全量 617/617 通過，SQL 採樣無持續阻擋；帶 DB DocGen 0 Error／67 Warning。 |
| WU-PUR-005e2 | PUR | 逐行跨幣別含稅比價 API、不可比原因、受權 SQL／HTTP 測試 | P1 | DONE | 005e1 | 唯讀 COMPARE 端點、MD 快照單請求快取與完整不可比原因；真實 SQL／HTTP 專項及 Release 整合全量 618/618，帶 DB DocGen 0 Error／67 Warning。 |
| WU-PUR-005e3 | PUR | 決標快照資料模型、migration、AWARD 端點及雙連線唯一性 | P1 | TODO | 005e2 | 保留價格、稅率、匯率與理由，不覆寫既有決標。 |
| WU-PUR-005e4 | PUR | 最小採購單草稿模型、CONVERT 端點、SYS 單號、原子回滾與全量驗收 | P1 | TODO | 005e3 | 同一供應商已決標未轉行一次轉單，請購餘量於 006 提交才消耗。 |
| WU-PUR-005f | PUR | 詢價／報價／比價及轉單三語亮暗頁面、E2E、CSV／列印與畫面 QA | P1 | TODO | 005e | 按介面 4.11 起驗畫面與持久資料一致；完成後關閉父 005。 |
| WU-PUR-006 | PUR | 採購單草稿、審批、來源請購與剩餘量；過大先拆子單元 | P1 | TODO | 005 | 對齊 BlueWhale REQ_F4114.1.1–6。 |
| WU-PUR-007 | PUR | 收貨／採購入庫與 INV 原子過帳、批序號、退貨；過大先拆子單元 | P1 | TODO | 006 | 對齊 BlueWhale REQ_F4114.2.1–6。 |
| WU-PUR-008 | PUR | 發票與訂單／收貨三單比對、差異處理；過大先拆子單元 | P1 | TODO | 007 | FI 應付只經模組契約。 |
| WU-PUR-009 | PUR | 模組 QA、完整回歸、DocGen、真實啟動 smoke、審查與 tag | P1 | TODO | 001–008, P0-015/016 | 依 MASTER_PROMPT 20.4 建立 QA 報告，重試 BLOCKED。 |

## Phase 1 — MD 主檔工作單元

| ID | 模組 | 工作單元 | 優先級 | 狀態 | 依賴 | 備註 |
|---|---|---|---|---|---|---|
| WU-MD-001 | MD | MD 模組骨架、權限目錄與公司隔離基線；共用主檔需求與 API 規格 | P1 | DONE | SYS | MD Contracts／模組、`MD.MASTER.VIEW`、三語 `/md` 路由及守衛；後端建置 0 警告，前端 432/432，E2E 67 通過 3 略過；本單元無資料表，首張主檔表起驗證公司隔離 |
| WU-MD-002 | MD | 客戶分類：資料模型、CRUD API、三語頁面與測試 | P1 | DONE | 001 | REQ_F4118.8.1–4／REQ_DS43.105；後端 603/603、前端 433/433、E2E 69 通過 3 略過；docgen 0 Error |
| WU-MD-003 | MD | 客戶主檔：基本資料、停用、搜尋與列表 | P1 | DONE | 002 | REQ_F4118.1.1–3／REQ_DS43.106；`md_customer` 同公司複合 FK 參照分類，分類被引用不可刪除；後端 605/605、前端 437/437、E2E 71 通過 3 略過；docgen 0 Error |
| WU-MD-004 | MD | 客戶聯絡人與地址：主從編輯、驗證與測試 | P2 | DONE | 003 | BlueWhale Client Linkman；2026-10-12 依 ADR-043 拆為 004a 後端與 004b 前端，兩者完成 |
| WU-MD-004a | MD | 客戶聯絡人與地址後端：ADR、需求、`md_customer_contact`／`md_customer_address`、子資源 API、唯一預設與整合測試 | P2 | DONE | 003 | ADR-043；REQ_F4118.41.1–4、42.1–4／REQ_DS43.128–129；`MasterData_CustomerContact` 已套用本機 `myerp`；共用 `PartnerChildService` 供 008 沿用；MD-CONT-001～004 |
| WU-MD-004b | MD | 客戶聯絡人與地址三語頁面：客戶對話框加聯絡人／地址子表（預設標記、停用、刪除）、權限、錯誤碼三語、ui-spec 介面 8.4、Vitest、E2E、docgen、reference-notes 狀態 | P2 | DONE | 004a | 客戶列「聯絡人」開啟獨立對話框 `CustomerContactDialog`（ADR-043 補充）；介面 8.4；MD-CONT-UI-001～002 |
| WU-MD-005 | MD | 客戶匯入：範本、預覽驗證、錯誤回報與冪等匯入 | P2 | DONE | 003 | BlueWhale Client Import；依 ADR-044 拆為 005a–c，均完成 |
| WU-MD-005a | MD | 共用 xlsx／CSV 讀取器、範本與單元測試 | P2 | DONE | 003 | ADR-044；MD-IMP-001～002；供 009／014 沿用 |
| WU-MD-005b | MD | 客戶匯入服務、API、獨立權限、整合測試 | P2 | DONE | 005a, 004b | 預覽只讀、整批交易、代碼冪等與公司鎖；4 個 SQL Server 整合案例通過；MD-IMP-003～004 |
| WU-MD-005c | MD | 客戶匯入三語對話框、預覽表、E2E 與 docgen | P2 | DONE | 005b | 介面 8.5；MD-IMP-005；前端 537/537、E2E 109 通過 3 略過、帶 DB docgen 0 Error |
| WU-MD-006 | MD | 供應商分類：資料模型、CRUD API、三語頁面與測試 | P1 | DONE | 001 | REQ_F4118.9.1–4／REQ_DS43.107；與客戶分類共用 `MasterCategoryService<T>` 及前端 `CategoryList`；`(company_id,id)` 唯一約束供 WU-MD-007 複合 FK；後端 609/609、前端 445/445、E2E 73 通過 3 略過；docgen 0 Error |
| WU-MD-007 | MD | 供應商主檔：基本資料、停用、搜尋與列表 | P1 | DONE | 006 | REQ_F4118.2.1–3／REQ_DS43.108；與客戶共用 `BusinessPartnerService<TPartner,TCategory>` 及前端 `PartnerList`；`md_supplier` 同公司複合 FK，分類被引用不可刪除；後端 612/612、前端 447/447、E2E 75 通過 3 略過；docgen 0 Error |
| WU-MD-008 | MD | 供應商聯絡人與地址：主從編輯、驗證與測試 | P2 | DONE | 007 | BlueWhale Vendor Linkman；依 ADR-043 只有聯絡人（無地址表）；REQ_F4118.43.1–4／REQ_DS43.130／介面 8.9；`MasterData_SupplierContact` 已套用本機 `myerp`；共用 `PartnerContactDialog`；MD-SUP-CONT-001～002、UI-001～002；後端 726/726、前端 540/540、E2E 111 通過 3 略過、docgen 0 Error |
| WU-MD-009 | MD | 供應商匯入：範本、預覽驗證、錯誤回報與冪等匯入 | P2 | DONE | 007 | ADR-044 補充；REQ_F4118.2.7／介面 8.10；與客戶共用 `PartnerImportService<TPartner,TCategory>` 及 `PartnerImportDialog`；`MD.SUPPLIER.IMPORT`；範本無地址欄；MD-SUP-IMP-001～003 |
| WU-MD-010 | MD | 計量單位與換算：單位主檔、換算規則與精度測試 | P1 | DONE | 001 | REQ_F4118.13.1–4／REQ_DS43.109–110；`md_uom` 繼承分類基底加小數位數，`md_uom_conversion` decimal(15,6)、兩單位最多一條路徑（applock 序列化）、`IUomConverter` 契約與試算 API（ADR-034）；後端 617/617、前端 452/452、E2E 77 通過 3 略過；docgen 0 Error |
| WU-MD-011 | MD | 物料品牌與分類：階層、代碼、停用與三語管理頁 | P1 | DONE | 001 | REQ_F4118.10.1–4、11.1–4／REQ_DS43.111–112；品牌沿用分類基底與前端 `CategoryList`；分類 5 層樹（同公司複合 FK、applock 序列化搬移與刪除），前端組樹與上層選項過濾；後端 626/626、前端 463/463、E2E 81 通過 3 略過；docgen 0 Error |
| WU-MD-012 | MD | 物料主檔：編碼、計量單位、狀態、查詢與三語頁面 | P1 | DONE | 010, 011 | 2026-10-12 拆為 012a–c，讓後端、頁面、物料專屬換算各可在單輪驗收；BlueWhale Goods；期初庫存交由 INV |
| WU-MD-012a | MD | 物料後端：`md_item`、公司內參照、CRUD API、權限與整合測試 | P1 | DONE | 010, 011 | REQ_F4118.3.1–3（012c 由誤用的 12.x 更正）／REQ_DS43.113；`MasterData_Item` migration 已套用本機 `myerp`；三語錯誤碼；後端 628 個測試（全量結果見 progress），前端 463/463 |
| WU-MD-012b | MD | 物料三語頁面：列表、表單、單位／品牌／分類選擇、權限、E2E | P1 | DONE | 012a | `/md/items`、介面 8.11–8.13、REQ_IO_A；三語、參照權限與跨頁選項；前端 466/466、E2E 83 通過 3 略過、後端 628/628、docgen 0 Error |
| WU-MD-012c | MD | 物料專屬換算：按 ADR-034 第 4 點決定覆寫／補充規則，契約、API、介面與測試 | P1 | DONE | 012b | ADR-035；REQ_F4118.3.5／REQ_DS43.114／介面 8.77；`MasterData_ItemUomConversion` 已套用本機 `myerp`；`IUomConverter.ConvertForItemAsync`；後端 631/631、前端 470/470、docgen 0 Error |
| WU-MD-013 | MD | 物料圖片與屬性：內容驗證、保存、預覽與測試 | P2 | DONE | 012 | BlueWhale Goods Picture／Goods Attribute；2026-10-12 依 ADR-045 拆為 013a–c，全部完成 |
| WU-MD-013a | MD | 物料圖片後端：ADR、需求、`md_item_image`、上傳／內容／主圖排序／刪除 API、共用 `ImageInspector`、整合測試 | P2 | DONE | 012 | ADR-045；REQ_F4118.44.1–4／REQ_DS43.131；`MasterData_ItemImage` 已套用本機 `myerp`；PNG／JPEG、2 MB、16–4096 像素、每物料 10 張、恰好一張主圖；MD-IMG-001～004 |
| WU-MD-013b | MD | 物料屬性後端：`md_item_attribute`（公司級定義，沿用分類基底加排序號）、`md_item_attribute_value`（物料 × 屬性 → 值）、定義 CRUD（帶使用數，被使用不可刪除）、物料屬性整批取代 API（最多 50 個、新設定的屬性須啟用）、權限、REQ_F4118.45.x、整合測試 | P2 | DONE | 013a | ADR-045 第 11–13 點；不做 SKU 變體；REQ_F4118.45.1–6／REQ_DS43.132–133；`MasterData_ItemAttributes` 已套用本機 `myerp`；權限 `MD.ITEM_ATTRIBUTE.VIEW/CREATE/EDIT/DELETE`；MD-ATTR-001～004 |
| WU-MD-013c | MD | 物料圖片與屬性三語頁面：物料列「圖片」對話框（上傳、預覽 data URL、設主圖、排序、刪除）、屬性定義頁與物料屬性編輯、錯誤碼三語、ui-spec 8.15／8.16、Vitest、E2E、docgen、reference-notes 狀態 | P2 | DONE | 013b | 前端以 Blob→data URL 顯示（ADR-028／045）；`ItemImageDialog`、`ItemAttributeDialog`、屬性定義頁沿用 `ProcessPage`（kind `attribute`）；`toDataUrl` 移到 `shared/images`；MD-IMG-UI-001～004、MD-ATTR-UI-001～003 |
| WU-MD-014a | MD | 物料匯入後端：範本、預覽驗證、錯誤回報、衝突重預覽與冪等提交；SQL Server 整合測試 | P2 | DONE | 012 | REQ_F4118.3.6、介面 8.14；`MD.ITEM.IMPORT`；ItemImportTests 5/5；不匯入期初庫存 |
| WU-MD-014b | MD | 物料匯入三語入口與對話框、Vitest、Playwright、文件生成與一致性檢查 | P2 | DONE | 014a | 共用匯入對話框擴充 `item`；前端 559/559、E2E 119 通過／3 略過、帶 DB docgen 0 Error／53 Warning |
| WU-MD-015 | MD | 倉庫與儲位：公司隔離、層級、停用與三語管理頁 | P1 | DONE | 001 | ADR-036；REQ_F4118.4.1–5／REQ_DS43.115–116／介面 8.17–8.19、8.78；`MasterData_Warehouse` 已套用本機 `myerp`；儲位一層、倉庫內代碼唯一，有儲位不可刪倉庫；後端 635/635、前端 478/478、E2E 87 通過 3 略過、docgen 0 Error |
| WU-MD-016 | MD | 幣別與匯率：有效日期、精度、重疊驗證與三語頁面 | P1 | DONE | 001 | ADR-037；REQ_F4118.35.1–4、36.1–5／REQ_DS43.117–118／介面 8.79–8.82；`MasterData_Currency` 已套用本機 `myerp`；本位幣為 MD 幣別旗標、有匯率後鎖定；匯率 1 外幣 = rate 本位幣、期間不重疊；契約 `ICurrencyLookup`；後端 640/640、前端 488/488、E2E 90 通過 3 略過、docgen 0 Error |
| WU-MD-017 | MD | 稅率：稅別、有效日期、適用範圍與三語頁面 | P1 | DONE | 001 | ADR-038；REQ_F4118.37.1–4、38.1–6／介面 8.83–8.86；`MasterData_Tax` 已套用本機 `myerp`；`Tax.Calculate` 以 Money 規則捨入、Net + Tax = Gross；契約 `ITaxRateLookup`；後端 656/656、前端 501/501、E2E 93 通過 3 略過、docgen 0 Error |
| WU-MD-018 | MD | 付款條件與結算方式：到期規則、停用與三語頁面 | P2 | DONE | 001 | 2026-10-12 拆為 018a–b；BlueWhale Settlement Method（4.1.1.8.14）＋追加付款條件；前後端與文件完成 |
| WU-MD-018a | MD | 結算方式與付款條件後端：ADR、需求、`md_settlement_method`／`md_payment_term`、CRUD 與到期日試算 API、`ISettlementMethodLookup`／`IPaymentTermLookup`／Usage 契約與測試 | P2 | DONE | 001 | ADR-040；REQ_F4118.14.1–5、40.1–5／REQ_DS43.122–123；`MasterData_PaymentSetup` 已套用本機 `myerp`；到期規則 NetDays／ClosingDays／ClosingMonthDay；MD-PAY-001～007 |
| WU-MD-018b | MD | 結算方式與付款條件三語頁面：`/md/settlement-methods`（介面 8.48–8.50）、`/md/payment-terms`（介面 8.89–8.91，依規則類型切換欄位、到期日試算）、權限、選單、錯誤碼三語、Vitest、E2E、docgen、reference-notes 狀態 | P2 | DONE | 018a | 前端 514/514、E2E 97 通過 3 略過、後端 700/700、帶資料庫 docgen 0 Error／48 Warning；MD-PAY-008～009 |
| WU-MD-019 | MD | 會計科目主檔：科目階層、啟停與三語頁面 | P1 | DONE | 001 | 2026-10-12 拆為 019a–b；FI 經 MD 契約讀取，不跨模組讀表 |
| WU-MD-019a | MD | 會計科目後端：ADR、需求、`md_account`、CRUD API、`IChartOfAccounts`／`IAccountUsage` 契約與整合測試 | P1 | DONE | 001 | ADR-039；REQ_F4118.39.1–5／REQ_DS43.121；`MasterData_Account` 已套用本機 `myerp`；數字代碼加上層前綴、類別同上層、末級才可過帳、使用檢查由 FI 實作 `IAccountUsage`；AccountTests 8 個案例 |
| WU-MD-019b | MD | 會計科目三語頁面：樹狀列表、新增子科目、修改／停用／刪除、權限、ui-spec 介面 8.87–8.88、Vitest 與 E2E、docgen | P1 | DONE | 019a | `/md/accounts`（介面 8.87–8.88）重用泛型樹函式；一級科目選類別帶出預設方向，子科目預填上層代碼、類別唯讀並先檢查前綴；`MD.ACCOUNT.*` 12 個錯誤碼三語；Vitest 11、E2E 2 |
| WU-MD-020 | MD | 收支類別與資金帳戶設定：分類、啟停與三語頁面 | P2 | DONE | 001 | 依 ADR-041 拆為 020a 後端及 020b 前端；資金交易與餘額歸 FI |
| WU-MD-020a | MD | 收支類別與資金帳戶後端：ADR、需求、資料表、CRUD API、契約及整合測試 | P2 | DONE | 001 | ADR-041；REQ_F4118.5.1–5／12.1–5；REQ_DS43.124–125；`MasterData_FundSetup` migration |
| WU-MD-020b | MD | 收支類別與資金帳戶三語頁面：介面 8.20–8.22／8.42–8.44、選單、錯誤碼、Vitest、E2E、docgen | P2 | DONE | 020a | `/md/fund-accounts`、`/md/income-expense-categories` 共用頁面；MD-FUND-UI-001～005；前端 517/517、E2E 101 通過 3 略過、帶資料庫 docgen 0 Error／60 Warning |
| WU-MD-021 | MD | 工序分類與工序：階層、排序、停用與三語頁面 | P2 | DONE | 001 | 依 ADR-042 拆為 021a 後端及 021b 前端；PP 經 MD 契約讀取 |
| WU-MD-021a | MD | 工序分類與工序後端：ADR、需求、`md_process_category`／`md_process`、CRUD API、`IProcessLookup`／`IProcessUsage` 契約與整合測試 | P2 | DONE | 001 | ADR-042；REQ_F4118.15.1–4、7.1–5；REQ_DS43.126–127；`MasterData_Process` migration 已套用本機 `myerp`；MD-PROC-001～004 |
| WU-MD-021b | MD | 工序分類與工序三語頁面：`/md/process-categories`（介面 8.51–8.53，排序號、工序數）、`/md/processes`（介面 8.27–8.29，類別篩選、類別／單位選項、標準工價）、選單、錯誤碼三語、Vitest、E2E、docgen、reference-notes 狀態 | P2 | DONE | 021a | 共用 `ProcessPage`；MD-PROC-UI-001～005；前端 526/526、E2E 105 通過 3 略過、帶資料庫 docgen 0 Error／51 Warning |
| WU-MD-023 | MD | 客戶核准／駁回（經 SYS 審批介面）與刪除（軟刪除、已被單據引用不可刪） | P2 | DONE | 003 | REQ_F4118.1.4–6；WU-MD-003 完成時發現 backlog 未涵蓋。客戶狀態需加「待審」，核准前不可用於交易 |
| WU-MD-024 | MD | 供應商核准／駁回（經 SYS 審批介面）與刪除（軟刪除、已被單據引用不可刪） | P2 | DONE | 007, 023 | REQ_F4118.2.4–6；WU-MD-007 完成時補入 backlog，沿用 WU-MD-023 的客戶設計 |
| WU-MD-025 | MD | 客戶／供應商預設付款條件與結算方式：主檔加可空欄位（以代碼經同公司驗證）、API 與頁面帶出 | P3 | DONE | 003, 007, 018 | ADR-047；REQ_F4118.1.8、2.8；`MasterData_PartnerPaymentDefaults` migration 已套用本機 `myerp`；`CustomerIdentity`／`SupplierIdentity` 帶出預設代碼；匯入不含預設值 |
| WU-MD-022 | MD | MD 完整回歸與模組收尾：審查、補測、真實 API E2E、docgen、對照表及 tag | P1 | DONE | 002–021、023、024、025 | 依 MASTER_PROMPT 第 14 節與模組完成規則驗收；拆為 022a–f，全部完成；P3 後續優化 026／027 保留於 backlog |
| WU-MD-022a | MD | MD 模組級代碼審查（後端、前端）與架構檢查：結果寫入 `code-review-log.md`；日誌分析寫入 `log-analysis.md`；修正 High 級發現 | P1 | DONE | 022 | 後端 12 項、前端 13 項發現；修正 MD-B01（審批中不可修改，ADR-048）與 MD-F01（`SYS.APPROVAL.ALREADY_PENDING` 缺翻譯）；架構檢查與日誌分析無問題 |
| WU-MD-022b | MD | 修正後端審查發現：MD-B02（待審＝從未送審或審批中，改以 SYS 實例判斷可否刪除）、B03（刪除與引用寫入用同一 `CompanyLock`）、B04（稅碼停用與幣別檢查）、B05（物料／倉庫／儲位／稅碼 Usage 契約）、B07–B12 | P1 | DONE | 022a | 見 code-review-log。B02、B03（022b-1，ADR-048 補充）、B04、B05（022b-2，ADR-049）、B07–B12（022b-3）已完成；B06 移到 WU-MD-026 |
| WU-MD-022c | MD | 修正前端審查發現：MD-F02（下拉只載一頁）、F03（共用 `fetchAllPages`）、F05（刪除防重複）、F06（409／404 後重新查詢）、F07（聯絡人對話框過期回應）、F08–F13；B02 前端（Pending 也顯示刪除鈕） | P1 | DONE | 022a | F02、F03、F05–F10、F12、F13 與 B02 前端已修正；F12 契約 `TaxCalculation` 加 `DecimalPlaces`。F04、F11 在 WU-MD-027 |
| WU-MD-022d | MD | MD 真實 API E2E（`Invoke-RealApiE2E.ps1`、`specs/real-api-*`）：客戶／供應商經 SYS 多步驟審批核准、審批中修改回 422（ADR-048）、預設付款條件與結算方式（ADR-047）；必要時補 MD 種子資料 | P1 | DONE | 022b | MD-REAL-001～002 真實 API 通過；審批中修改／刪除 422、引用付款主檔刪除 409；原有流程還原、新流程停用；種子帳號已足夠 |
| WU-MD-022e | MD | MD 完整回歸（後端、前端、模擬 E2E、真實 API E2E）、帶 DB docgen、reference-notes 對照表狀態、MD 的 SRS/SDS 章節草稿、重試 BLOCKED | P1 | DONE | 022c, 022d | 後端 761/761、前端 586/586、模擬 E2E 122 通過 5 略過、真實 API 5/5（登入 3、MD 2，分批）；docgen 0 Error／53 Warning（更正重複測試 ID MD-CUS-REV-004→005）；新增 SRS 2.2 Settings 敘述 `docs/05-doc-sources/srs/ch2-2-08-settings.md`；沒有 BLOCKED 項目。對照表核對發現 BlueWhale `REQ_F4118.3.4`（刪除物料）尚未實作，所以 tag 與模組完成移到 022f；期初庫存介面 4.4.1.8.14 改歸 INV |
| WU-MD-028 | MD | 物料刪除（REQ_F4118.3.4）：`DELETE /api/v1/md/items/{id}?version=`、`MD.ITEM.DELETE` 權限、軟刪除後代碼不可重用；有專屬換算、圖片、屬性時一併處理或擋下（ADR 決定）；任何使用方 `IItemUsage` 回報已有庫存、交易或單據時回 409；同一 `CompanyLock`；物料頁刪除鈕（popconfirm、防重複、409／404 重新查詢）、三語、Vitest、Playwright | P1 | DONE | 012, 022b | BlueWhale 基線缺口（022e 對照表核對發現）；ADR-050：換算軟刪除、圖片與屬性值一併刪除，`md_item`／屬性／圖片／換算排他鎖，圖片上傳與屬性取代在鎖內重新確認物料；`MD.ITEM.IN_USE`；確認對話框沿用 `ErpDialogService`；MD-ITEM-DEL-001～005、MD-ITEM-DEL-UI |
| WU-MD-022f | MD | MD 模組收尾：028 完成後確認全量回歸與帶 DB docgen（028 已全量跑過可沿用結果）、對照表 REQ_F4118.3.4 改完成；打 tag `v0.2.0-md`，state.modules.MD = DONE，拆分 EP-INV（含自 MD 移入的期初庫存介面 4.4.1.8.14） | P1 | DONE | 028 | 模組驗收通過；026、027 為 P3 後續優化，不阻擋 MD 交付 |
| WU-MD-026 | MD | MD 查詢契約批次版本：`IUomConverter`、`ITaxCalculator` 等加批次多行方法，或在請求範圍快取換算圖與幣別精度（審查發現 MD-B06） | P3 | TODO | 022e | 等 INV／SD／PUR 第一個逐行呼叫的使用方出現時一併設計，避免猜測介面 |
| WU-MD-027 | MD | MD 列表頁改用共用 `erp-list-page` 與 `table-query` helper（審查發現 MD-F04，12 頁重複的排序／分頁代碼、搜尋框 aria-label） | P3 | TODO | 022c | 純重構，以既有 Vitest 與 E2E 保護 |

## Phase 1 — INV 庫存工作單元

> 2026-10-13 拆分 EP-INV。每單元均按第 14 節更新需求、設計、測試、實作與文件；介面採三語、權限與 E2E。優先級：庫存異動帳及寫入安全性先於業務單據；批號／序號在啟用追蹤的單據前完成。MD 026 的批次查詢優化由第一個逐行呼叫的 INV 單元評估，MD 027 留作獨立 P3 重構。

| ID | 模組 | 工作單元 | 優先級 | 狀態 | 依賴 | 備註 |
|---|---|---|---|---|---|---|
| WU-INV-001 | INV | 模組骨架、Contracts、權限目錄、公司隔離與庫存需求基線；定義異動帳／單據狀態與數量精度的 ADR | P1 | DONE | MD-022f | ADR-051；模組／狀態守衛測試、全量後端 769/769；帶 DB docgen 0 Error／55 Warning。SQL 隔離隨首張表於 003 驗證 |
| WU-INV-002 | INV | 實作跨模組引用安全：INV 提供 `IItemUsage`／`IWarehouseUsage`／`IStorageLocationUsage`，寫入驗證至保存持有 `md_item` 共享鎖；倉庫／儲位刪除亦需互斥 | P1 | DONE | 001 | `inv_reference` 快照與三種 Usage；INV-REF-001～003 真實 SQL 並發測試通過；隔離 SYS 共用測試資料後全量後端 773/773、Release 建置 0 警告 |
| WU-INV-003 | INV | 庫存異動帳、結存查詢與過帳服務：來源單據唯一、防重放、交易內借出／借入、不可直接 UPDATE 彙總；migration 與整合測試 | P1 | DONE | 002 | ADR-053；`inv_stock_movement`（REQ_DS43.135）、trigger、過帳與結存契約；21+1 個專項整合案例；全量後端 795/795、帶 DB docgen 0 Error／59 Warning |
| WU-INV-004 | INV | 其他入庫：草稿建立／編輯／刪除、核准／駁回後寫異動帳、三語頁及列印／匯出 | P1 | 已拆分 | 003 | 2026-10-13 設計切片已提交（REQ_F4115.2.1–6、REQ_DS43.136–137、REQ_IO_A5.2–5.4、ADR-054）；超過單輪上限，拆為 004a／004b（ADR-055） |
| WU-INV-004a | INV | 其他入庫後端：migration、草稿 CRUD、單號、SYS 送審與審批結果處理器同交易過帳、HTTP API、SQL／API 整合測試 | P1 | DONE | 003 | INV-REC-001～005 通過（24 個新案例）；全量後端 819/819（Release）；`Inventory_OtherReceipt` 已套用 myerp；帶 DB docgen 0 Error／59 Warning |
| WU-INV-004b | INV | 其他入庫前端：三語建立／查看／編輯頁（介面 5.2–5.4）、權限按鈕、送審、列印、Excel 匯出、暗色模式、Playwright（模擬 API） | P1 | DONE | 004a | INV-REC-006；MD 名稱；列印及 Excel 可開啟的 UTF-8 CSV（ADR-056），原生 `.xlsx` 隨 WU-INV-017 評估；前端 593/593、E2E 全量 125 通過 5 略過 + 新增 2 專項、後端 819/819 + decimal 專項 1 |
| WU-INV-005 | INV | 其他出庫：草稿建立／編輯／刪除、核准／駁回、庫存不足與並發防超賣、三語頁及列印／匯出 | P1 | DONE | 003 | 拆為 005a／005b（ADR-057），兩段均完成；出庫金額為審批估算 |
| WU-INV-005a | INV | 其他出庫後端：migration、草稿 CRUD、SYS 審批同交易出庫、缺庫存及雙連線防超賣測試、API 與文件 | P1 | DONE | 003 | REQ_F4115.3.1–6、REQ_DS43.138–139；INV-ISS-001～003、005；後端 823/823，帶 DB docgen 0 Error／59 Warning |
| WU-INV-005b | INV | 其他出庫前端：三語介面 5.5–5.7、權限、MD 名稱、列印／CSV 匯出、庫存不足提示、Vitest 與 Playwright | P1 | DONE | 005a | INV-ISS-004；估算單價與金額標示；前端 595/595、後端 823/823、E2E 132 通過／5 略過、帶 DB docgen 0 Error／59 Warning；ADR-056 |
| WU-INV-006 | INV | 庫存查詢：依公司／倉庫／儲位／物料顯示現存與異動明細、篩選、分頁、Excel 匯出及三語頁 | P1 | DONE | 004, 005 | 拆為 006a／006b（ADR-058）；REQ_F4115.1.1–2 |
| WU-INV-006a | INV | 結存與異動明細的公司隔離、篩選、分頁及 VIEW 權限 API；真實 SQL 測試 | P1 | DONE | 003, 004, 005 | 後端 826/826、前端 595/595、E2E 132 通過／5 略過、帶 DB docgen 0 Error／61 Warning；ADR-058 |
| WU-INV-006b | INV | 庫存查詢三語頁、MD 名稱、列印與防公式注入 CSV 匯出、Vitest／Playwright | P1 | DONE | 006a | 介面 4.4.1.5.1（REQ_IO_A5.1.1–11）；供 Excel 開啟的 CSV（ADR-056）；INV-QRY-004 Vitest 2、Playwright 6 |
| WU-INV-007 | INV | 物料期初庫存：指定啟用日期、倉庫／儲位、數量與單價，預覽、冪等提交、錯誤回報，經異動帳過帳；三語頁 | P1 | DONE | 003 | 自 EP-MD 移入；BlueWhale Product Initial Stock Interface **4.4.1.8.14**；REQ_F4115.23.1–4；拆為 007a／007b（ADR-059），兩段均完成 |
| WU-INV-007a | INV | 期初庫存後端：`inv_opening_stock(_line)` migration、預覽逐行錯誤、冪等提交同交易 `OPENING_STOCK` 過帳、列表／詳情 API、真實 SQL／HTTP 測試 | P1 | DONE | 003 | REQ_DS43.140–141；INV-OPN-001～006 通過；`Inventory_OpeningStock` 已套用 myerp |
| WU-INV-007b | INV | 期初庫存前端：三語 lazy 頁（列表、批次輸入、預覽逐行錯誤、提交／重送、詳情）、`INV.OPENING_STOCK.VIEW/CREATE` 權限、MD 名稱、列印／CSV、暗色、Vitest 與 Playwright | P1 | DONE | 007a | INV-OPN-007 通過；介面 5.21／5.22（019 改編，對應 BlueWhale 4.4.1.8.14）；requestKey 由前端每批產生 GUID，重試沿用 |
| WU-INV-008 | INV | 批號與序號追蹤：物料追蹤設定、入庫採集／出庫核對、同公司唯一與追溯查詢 | P1 | DONE | 004, 005 | 依 ADR-060 拆為 008a／008b／008c，三段完成；先支援其他入出庫與期初，再供調撥／盤點等單據使用 |
| WU-INV-008a | INV | MD 物料追蹤方式 `tracking_mode`（None／Lot／Serial）：migration、API、使用後鎖定與 `md_item` 排他鎖、`InventoryReferenceCodes.TrackingMode` 契約、物料頁三語欄位、測試 | P1 | DONE | 004, 005 | REQ_F4118.3.7、REQ_IO_A8.12.7；MD-ITEM-TRK-001～006；`MasterData_ItemTrackingMode` 已套用 myerp |
| WU-INV-008b | INV | 批號／序號後端：`inv_lot`／`inv_serial`／`inv_stock_movement_tracking`（REQ_DS43.142–144）與單據 `*_line_tracking` 子表 migration；過帳契約 `Tracking`、其他入庫與期初採集、其他出庫核對、序號重複入庫／出庫防護與並發測試；追溯 API `INV.TRACKING.VIEW`；REQ_F4115.24.x | P1 | DONE | 008a | REQ_F4115.24.1–6、REQ_DS43.142–147；INV-LOT-001～015；`Inventory_LotSerial` 已套用 myerp |
| WU-INV-008c | INV | 批號／序號前端：其他入庫／出庫／期初頁的批序號輸入與檢查、追溯查詢三語頁、權限、Vitest 與 Playwright | P1 | DONE | 008b | 三單據採集與六位定點檢查、序號多行貼上；追溯分頁、位置／歷史；詳情／列印／CSV 帶批序號；INV-LOT-UI-001～003 |
| WU-INV-009 | INV | 調撥：來源／目的倉庫儲位、草稿與審批、同交易出入雙向異動及三語頁 | P1 | DONE | 006, 008 | ADR-061 拆為 009d 設計、009a 後端、009b 前端，三段完成 |
| WU-INV-009d | INV | 調撥需求、資料表／ER、API、介面與驗收案例設計 | P1 | DONE | 006, 008 | REQ_F4115.6.1–6、REQ_DS43.148–150、REQ_IO_A5.15.1–5.17.4、INV-TRF-001～009；ADR-061 |
| WU-INV-009a | INV | 調撥後端：`inv_transfer` 三表 migration、草稿／SYS 審批 API、相同來源雙向異動、批序號轉倉、真實 SQL／HTTP 驗收 | P1 | DONE | 009d | INV-TRF-001～008 真實 SQL／HTTP 通過；`Inventory_Transfer` 已套用 myerp；全量後端 892/892、帶 DB docgen 0 Error／59 Warning |
| WU-INV-009b | INV | 調撥三語頁：建立／列表詳情／編輯、兩側 MD 名稱及儲位、批序號、列印／安全 CSV、Vitest／Playwright | P1 | DONE | 009a | INV-TRF-009 通過（Vitest 11、Playwright 5）；介面 4.4.1.5.15–17；父單元結案 |
| WU-INV-010 | INV | 盤點：盤點單快照、實盤輸入、差異報告、審批後盈虧異動及三語頁 | P1 | DONE | 006, 008 | ADR-062 拆為 010d 設計、010a 後端、010b 前端；INV-STK-001～011 驗收完成 |
| WU-INV-010d | INV | 盤點需求、資料表／ER、API、介面與驗收案例設計；快照後異動規則 | P1 | DONE | 006, 008 | REQ_F4115.7.1–7、REQ_DS43.151–153、REQ_IO_A5.18.1–11、REQ_IO_A5.24.1–5、BR-INV-031～035、INV-STK-001～011；ADR-062 |
| WU-INV-010a | INV | 盤點後端：`inv_stocktake` 三表 migration、MD `IItemCategoryScope` 契約、快照／實盤／重新快照／送審過期檢查、SYS 審批盈虧過帳、差異報告 API、真實 SQL／HTTP 驗收 | P1 | DONE | 010d | 已拆 010a1（單據＋快照＋實盤＋重新快照）／010a2（送審＋過帳＋報告），兩段完成結案；INV-STK-001～010 通過 |
| WU-INV-010a1 | INV | 盤點後端（一）：三表 migration `Inventory_Stocktake`、MD `IItemCategoryScope` 與倉庫批次驗證、建立快照、範圍互斥、整組保存實盤／新增行、重新快照、軟刪除、列表／詳情 API 與 `INV.STOCKTAKE.VIEW/CREATE/EDIT/DELETE` | P1 | DONE | 010d | 接手上輪中斷的未提交變更並驗收；INV-STK-001／002 通過，003／004／007／009 的 010a1 部分通過；migration 已套用 myerp |
| WU-INV-010a2 | INV | 盤點後端（二）：`POST submit`（`COUNT_MISSING`、`SNAPSHOT_STALE` 帶 `changedKeys`）、`INV.STOCKTAKE.SUBMIT`、SYS `INV.STOCKTAKE` 審批處理器（逐批序號差異拆同行號 In／Out，來源 STOCKTAKE，成本空，不足全部回滾）、`GET variance` 報告、冪等 | P1 | DONE | 010a1 | INV-STK-003～010 通過；ProblemDetails 支援例外 extensions（`changedKeys`）；送審時預檢盈虧異動數不超過過帳服務上限（`TOO_MANY_LINES`） |
| WU-INV-010b | INV | 盤點三語頁：範圍查詢與實盤表格（5.18）、列表／差異報告（5.24）、重新快照、列印／安全 CSV、Vitest／Playwright | P1 | DONE | 010a | 010b1／010b2 均完成；前端 633/633、模擬 E2E 157 通過／5 略過 |
| WU-INV-010b1 | INV | 盤點服務、範圍建立／實盤頁、暫行草稿入口、三語與權限、Vitest／模擬 E2E | P1 | DONE | 010a | INV-STK-011 範圍建立、零實盤、差異、快照過期異動鍵；已通過前端建置及專項 E2E |
| WU-INV-010b2 | INV | 完成盤點列表／詳情／差異報告（5.24）：篩選、列印、防公式注入 CSV、權限與暗色；補批序號／重新快照 E2E，全量驗證 | P1 | DONE | 010b1 | INV-STK-011 通過：Vitest 4、模擬 E2E 7；全量後端 907/907、前端 633/633、模擬 E2E 157 通過／5 略過；帶 DB docgen 0 Error／59 Warning；父單元結案 |
| WU-INV-019 | INV | 介面編號修正：既有擴充介面 5.8／5.9（期初）與 5.10（批序號追溯）和 BlueWhale 5.8 Sales Outbound (Approval)、重排後 5.9／5.10 Product Assembly 衝突，改編 5.21–5.23，同步 ui-spec、REQ_IO_A、reference-notes 與 docgen | P1 | DONE | 010d | INV-DOC-001／002；docgen 保留號防線與排序測試；帶 DB docgen 0 Error／59 Warning，前端建置與 633/633 通過 |
| WU-INV-011 | INV | 組裝入庫：組件清單、耗料與成品同交易過帳、審批及三語頁 | P2 | 已拆分 | 008 | 011d 設計、011a 單據後端、011b 成本／SYS 原子過帳、011c 三語頁與 E2E；全部完成才結案 |
| WU-INV-011d | INV | 固定 INV 組裝單與 PP BOM 邊界，需求、資料、API、介面與完整測試情境 | P2 | DONE | 008 | ADR-066；REQ_F4115.4.1–6、REQ_IO_A5.9.1–5.11.4、INV-ASM-001～016；預留 REQ_DS43.154–156，部署後納入 docgen |
| WU-INV-011a | INV | 先寫真實 SQL／HTTP 失敗測試，再建三表 migration、組裝草稿 CRUD／列表／詳情／權限與批序號保存 | P2 | DONE | 011d | `Inventory_AssemblyDraft` 已套用 myerp；INV-ASM-002 通過，003／007～010 草稿部分通過；後端 930/930、前端 655/655、模擬 E2E 169 通過／5 略過；帶 DB docgen 0 Error／64 Warning；審批與過帳留 011b |
| WU-INV-011b | INV | 鎖定成本快照、SYS 審批與耗料 Out／成品 In 同交易過帳 | P2 | DONE | 011a | 011b1 估值鎖、011b2 審批及原子過帳均完成；前端留 011c |
| WU-INV-011b1 | INV | 寫真實 SQL 失敗測試，建立估值快照與所有 INV 過帳共用的公司／物料交易鎖，驗證雙連線等待及未知／明確零成本 | P2 | DONE | 011a | INV-ASM-017 真實 SQL 通過；來源重放不取估值鎖 |
| WU-INV-011b2 | INV | 先寫真實 SQL／HTTP 失敗測試，再做組裝送審、SYS 最終核准、鎖定耗料成本計算及耗料 Out／成品 In 同交易過帳 | P2 | DONE | 011b1 | 真實 SQL／HTTP 驗證即時與待審核准、未知／零成本、四位守恆與精度拒絕、批序號不足／衝突、雙連線爭用、冪等及整單回滾；test-plan 列出模組收尾仍需補的專項邊界 |
| WU-INV-011c | INV | 三語亮暗列表／建立／編輯、選擇框、錯誤與停用原因、列印安全 CSV、E2E 與 QA 截圖 | P2 | 進行中 | 011b | 列表／表單／選擇框、安全 CSV、逐行三語錯誤、審批歷史及選項 CREATE／EDIT 權限已驗證；INV-ASM-009～012、015 通過。013 真實 SQL 結存已核對，真實瀏覽器與 SQL 同資料聯查待 WU-P0-015／016 預設啟動路徑；不結案。 |
| WU-INV-012 | INV | 拆卸入庫：成品耗用與組件產出同交易過帳、審批及三語頁 | P2 | DONE | 011 的後端契約 | 012d／a／b／c／e／f 完成；真實 SQL／HTTP 原子故障與成本競態通過，Release 後端 983/983、帶 DB DocGen 0 Error／64 Warning。011c 真實畫面同資料聯查仍獨立受 P0-015／016 阻擋 |
| WU-INV-012d | INV | 固定拆卸與組裝逆向成本、SYS 審批及批序號邊界；需求、資料／API／介面規格與完整情境 | P2 | DONE | 011b2 | ADR-069；REQ_F4115.5.1–6、預留 REQ_DS43.158–160、介面 5.12–14、BR-INV-058～062、INV-DIS-001～016；帶 DB DocGen 0 Error／70 Warning、測試 7/7；三表待 012a migration 後登記正式 schema |
| WU-INV-012a | INV | 先寫真實 SQL／HTTP 失敗測試，再建三表 migration、拆卸草稿 CRUD／列表／詳情／權限與批序號保存 | P2 | DONE | 012d | `Inventory_DisassemblyDraft` 已套用 myerp；真實 SQL／HTTP 草稿、公司、權限、版本、狀態、1000 行、批號及錯誤驗收；送審／成本／過帳保留 012b；Release 後端 970/970、帶 DB DocGen 0 Error／64 Warning |
| WU-INV-012b | INV | 測試先行實作拆卸送審、SYS 最終核准、鎖定成品成本、權重分攤及原子 Out／In 過帳 | P2 | DONE | 012a | 真實 SQL／HTTP 即時與 SYS 最終核准、四位守恆、六位單價、零／未知／精度／溢位、批序號、雙連線、重放及整單回滾；Release 後端 977/977，帶 DB DocGen 0 Error／64 Warning |
| WU-INV-012c | INV | 三語亮暗拆卸列表／建立／編輯、選擇框、審批歷史、錯誤、列印安全 CSV、E2E 及 QA 截圖 | P2 | DONE | 012b | 5.12–14 三語亮暗與錯誤／權限截圖已審；前端 672/672、模擬 E2E 全量 232 通過／5 略過、拆卸專項 18/18；真實瀏覽器同資料聯查仍依賴 P0-015／016 |
| WU-INV-012e | INV | 補拆卸資料邊界與引用狀態的真實 SQL／HTTP 測試：負值／跨年日期、MD 已刪或停用、錯倉儲位、已保存停用名稱、駁回歷史；發現產品缺陷時修正 | P2 | DONE | 012c | INV-DIS-002／003／008 新增真實 SQL／HTTP 測試首次即通過，現有守衛符合契約；拒絕及駁回均零異動。Release 後端 980/980、帶 DB DocGen 0 Error／64 Warning；父 012 暫不結案 |
| WU-INV-012f | INV | 補拆卸原子失敗與成本競態的真實 SQL／HTTP 測試：第二組件故障回滾、來源序號不存在、成本快照與並發入庫交錯；發現產品缺陷時修正 | P2 | DONE | 012e | 來源序號缺失與第二組件交易內 SQL 故障整單回滾、SYS 核准時停用倉庫後同版本重試、成本快照與外部入庫雙連線交錯均驗證；無產品缺陷；Release 後端 983/983，帶 DB DocGen 0 Error／64 Warning |
| WU-INV-013 | INV | 安全庫存：物料／倉庫門檻、低量查詢、補貨提示與測試 | P2 | DONE | 006 | 013d 設計、013a 真實 SQL／API、013b 三語亮暗頁與 E2E 均完成；只提示，不直接改異動帳。真實瀏覽器與 SQL 同資料聯查仍依賴 P0-015／016 及 011c。 |
| WU-INV-013d | INV | 固定安全庫存門檻、低量判定、權限與介面契約；建立完整驗收情境 | P2 | DONE | 006 | ADR-067；REQ_F4115.26.1–3、REQ_DS43.157、介面 5.26、INV-SAF-001～014；DocGen 契約測試、後端 939／939、前端 665／665、模擬 E2E 189 通過／5 略過；設計切片不改 DB 或產品程式 |
| WU-INV-013a | INV | 測試先行建立門檻 migration、受權 CRUD 與低量查詢；真實 SQL 驗證並發、公司隔離與結存 | P2 | DONE | 013d | 2026-10-16T20:43:13+08:00 MD 受權排序／搜尋契約、萬筆升降冪首末頁與 SQL IO、空門檻零 MD 掃描、400／409／422／500 traceId 後端驗收；安全庫存專項 15/15、Release 後端全量 954/954；三語頁及 E2E 留 013b |
| WU-INV-013b | INV | 三語亮暗門檻及低量頁、補貨提示、權限／錯誤／E2E 截圖 | P2 | DONE | 013a | 2026-10-18T05:03:47+08:00 三語頁、唯讀／編輯選項權限、數值與錯誤提示、5.26 亮暗 QA 截圖；後端 954/954、前端 667/667、模擬 E2E 全量 199 通過／5 略過，加新分頁 1/1、無權限 2/2 專項；帶 DB docgen 0 Error／64 Warning。 |
| WU-INV-014 | INV | 庫存估值 SP：依異動帳計算成本與期末值，邊界、精度及 SQL 效能測試 | P1 | DONE | 007, 009, 010 | ADR-063 的 014d／014a／014b 均完成；不改已過帳異動；FI 期間封存另列後續單元 |
| WU-INV-014d | INV | 確定移動加權平均、未定價、調撥及 FI 邊界；新增需求、資料／API／介面規格與驗收案例 | P1 | DONE | 007, 009, 010 | ADR-063；REQ_F4115.25.1–3、REQ_IO_A5.25.1–4、INV-VAL-001～006；設計單元不改程式或資料庫 |
| WU-INV-014a | INV | 真實 SQL 測試先行；唯讀 `usp_inv_value_item`、索引及 migration、內部估值契約；驗證精度、異常、公司隔離、100,000 筆執行計畫 | P1 | DONE | 014d | INV-VAL-001～005 通過；100,000 筆 Index Seek；API／前端留 014b |
| WU-INV-014b | INV | 受權估值 API、三語暗色查詢頁、未定價提示與模擬 E2E | P1 | DONE | 014a | INV-VAL-006：HTTP 權限／公司隔離／截止、估值專用物料選項、Vitest、模擬 E2E 通過；父 014 結案 |
| WU-INV-015 | INV | 提供 PUR 採購入庫與 SD 銷售出庫的過帳契約、重放防護與跨模組整合測試 | P1 | 已拆分 | 003, 008 | ADR-064：015d 設計、015a INV 契約／真實 SQL、015b 真實 PUR／SD 單據與 SYS／HTTP 整合；父項待兩來源都驗收才結案 |
| WU-INV-015d | INV | 固定 PUR／SD 與 INV 的過帳及審批交易邊界、需求與驗收案例 | P1 | DONE | 003, 008 | ADR-064；REQ_F4115.22.4–5、BR-INV-040～042、INV-XMOD-001～006；設計不改程式／資料庫 |
| WU-INV-015a | INV | INV Contracts 固定 PUR／SD 來源與方向，測試先行驗證真實 SQL 重放、公司隔離、交易回滾、批序號及並發 | P1 | DONE | 015d | `IExternalInventoryPostingService` 固定兩種來源、方向及 SD 空成本，要求呼叫端交易；INV-XMOD-001～004／006 的 INV 側 SQL 4 個專項通過；實際來源／SYS／HTTP 留 015b |
| WU-INV-015b | INV／PUR／SD | 實際 PUR／SD 已保存單據經 SYS 核准回呼，同交易呼叫 INV；真實 HTTP／SQL 跨模組驗收 | P1 | TODO | 015a，PUR／SD 來源單據 | PUR／SD 尚未開始；來源模組建立時補各自 backlog 子單元，兩者完成後才結案 015 |
| WU-INV-016 | INV | 共用物料／倉庫／儲位選擇介面、權限與三語 E2E；核對 INV 介面清單 | P2 | DONE | 006 | 016d、016a、016b 全部完成；INV-SEL-001～006 通過，介面 4.4.1.5.20；不複製 MD 主檔 |
| WU-INV-016d | INV | 固定 Select Data Interface 的需求、權限邊界、MD 唯讀契約、API／介面與驗收案例 | P2 | DONE | 006 | ADR-065；REQ_F4115.20.1–3、REQ_IO_A5.20.1–6、INV-SEL-001–006；本切片無程式或 DB 變更 |
| WU-INV-016a | INV | 測試先行實作 INV 物料／倉庫／儲位選項 API，經 MD Contracts 唯讀查詢；真實 SQL／HTTP 權限與公司隔離 | P2 | DONE | 016d | `IInventorySelectionOptions`、三個分頁 API；`RequireAnyPermission` 限現行 INV 單據 CREATE／EDIT；INV-SEL-001～003 真實 SQL／HTTP 3/3，後端全量 924/924；無新資料表 |
| WU-INV-016b | INV | 共用選擇對話框接入 INV 單據、三語與暗色、Vitest／Playwright E2E；更新 BlueWhale 介面對照 | P2 | DONE | 016a | 016b1 與 016b2 完成；5.20 Select Data Interface，三語暗色及表單 E2E 通過 |
| WU-INV-016b1 | INV | 三類 INV 選項前端服務、共用 erp-select-dialog 入口、三語與專項測試 | P2 | DONE | 016a | 只走 INV 受權選項 API；僅返回 id/code/name；無全表預載 |
| WU-INV-016b2 | INV | 將共用選擇框接入其他入庫／出庫、調撥、期初及盤點表單；追蹤模式、停用歷史顯示、保存 guard、Vitest／Playwright E2E 與 BlueWhale 對照 | P2 | DONE | 016b1 | 016b2a 與 016b2b 全部完成；全量前端 655/655、E2E 169 通過／5 略過 |
| WU-INV-016b2a | INV | 物料選項回傳追蹤模式，維持 INV 權限及公司隔離；真實 SQL／前端客戶端驗證 | P2 | DONE | 016b1 | SQL 測試先紅後綠；Release 後端 924/924、前端 637/637；未改表單、無 DB 變更 |
| WU-INV-016b2b | INV | 全部 INV 表單使用受權分頁選擇框，保留編輯中停用歷史引用；確認／取消、換倉、保存 guard、三語暗色 E2E | P2 | DONE | 016b2a | b1 已存引用、b2 其他入庫／出庫、b3 調撥／期初、b4 盤點均完成；INV-SEL-004～006 結案 |
| WU-INV-016b2b1 | INV | 已保存單據的停用物料／倉庫／儲位顯示契約與五種單據受權端點 | P2 | DONE | 016b2a | MD Contracts 唯讀批次查詢；單據 VIEW＋公司隔離後從已保存 ID 查；SQL／HTTP／前端服務測試，後端 925/925、前端 638/638；表單接入留 b2–b4 |
| WU-INV-016b2b2 | INV | 其他入庫／出庫表單接入受權分頁選擇框與已存引用顯示 | P2 | DONE | 016b2b1 | 兩張表單不再預載 MD 三類；已存停用引用、選擇競態、追蹤模式與 MD 保存 guard；Vitest 648/648、E2E 165 通過／5 略過，帶 DB docgen 0 Error／64 Warning |
| WU-INV-016b2b3 | INV | 調撥／期初表單接入受權分頁選擇框與已存引用顯示 | P2 | DONE | 016b2b2 | 調撥來源／目的倉儲獨立、期初預覽確認後失效；已存停用引用及 MD guard；前端 651/651、E2E 167 通過／5 略過、後端 925/925、帶 DB docgen 0 Error／64 Warning |
| WU-INV-016b2b4 | INV | 盤點範圍／實地新增行接入選擇框並完成三語暗色與權限 E2E | P2 | DONE | 016b2b3 | 範圍多選／實地單選、停用引用及 MD guard、分類權限提示；前端 655/655、後端 925/925、E2E 169 通過／5 略過、帶 DB docgen 0 Error／64 Warning；INV-SEL-004～006 結案 |
| WU-INV-017 | INV | 庫存報表與大量資料效能：進出存、批序號追溯、匯出；補索引與測試 | P2 | DONE | 008, 014 | ADR-068 的 017d／017a／017b／017c 全部完成；BI 跨模組儀表板留 EP-BI1 |
| WU-INV-017d | INV | 固定進出存日期／快照口徑、受權匯出格式及上限、追溯完整歷史範圍；需求、API、介面及完整測試情境 | P2 | DONE | 008, 014 | ADR-068；REQ_F4115.27.1–3、REQ_IO_A5.27.1–6／5.23.7、BR-INV-055～057、INV-RPT-001～014；設計不改產品程式或資料庫 |
| WU-INV-017a | INV | 真實 SQL／HTTP 測試先行：進出存唯讀報表、公司隔離、快照分頁、10 萬異動 SQL IO 與必要索引 | P2 | DONE | 017d | 真實 SQL／HTTP 4 個專項與全量後端 959/959；SNAPSHOT 交易、日期／公司隔離、負期末及六位精度；10 萬異動等價 SQL IO 2052→986，索引與啟用快照 migration 已套用；帶 DB docgen 0 Error／64 Warning，詳見 optimization-log |
| WU-INV-017b | INV | 三語亮暗進出存頁、受權伺服器 CSV 完整匯出、取消／上限／公式安全、Playwright | P2 | DONE | 017a | 真實 SQL／HTTP 匯出、權限、快照、上限與取消；三語亮暗頁及 E2E 13/13，完整回歸後端 963/963、前端 668/668、E2E 213 通過／5 略過；CSV 明示格式與實際列數 |
| WU-INV-017c | INV | 批序號完整歷史 CSV、超 1000 筆及大量追溯 SQL 效能、三語匯出動作與 E2E | P2 | DONE | 017b | 2026-10-18T15:53:32+08:00 詳情仍限 1000，CSV 1001 列完整匯出、上限 422 零位元組；10,000 列追蹤表 IO 20,059→71，覆蓋索引 migration 已套用；僅 TRACKING.VIEW 可讀 INV 選項；後端 967/967 加權限專項、前端 669/669、E2E 全量 216 通過／5 略過及追溯 8/8；帶 DB DocGen 0 Error／64 Warning，5.23 亮暗三語截圖已檢 |
| WU-INV-018 | INV | 模組收尾：代碼／架構審查、完整回歸、真實 API E2E、帶 DB docgen、BlueWhale 對照表與 SRS/SDS 草稿、tag | P1 | TODO | 004–017 | 發現缺口先補 backlog；按第 14 節模組完成規則驗收 |
