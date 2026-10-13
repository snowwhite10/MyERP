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
| WU-P0-014 | - | 整合測試偶發 SQL 逾時：逾時時自動傾印 `dm_exec_requests`／阻擋源與執行緒集區狀態；評估測試連線關閉 MARS 或限制整合測試並行數；找到原因後修正 | P2 | TODO | - | WU-SYS-002／008／016d 都出現過：跨不相關表（含 `sys.tables`）同時 35 秒逾時，伺服器端監看沒有鎖等待，重跑通過；分析見 log-analysis.md「SYS 完整回歸」。偶發失敗會削弱「全部測試通過」的提交條件 |

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
| EP-PUR | PUR | 請購、詢價比價、採購單、收貨（採購入庫）、退貨、三單比對 | P1 | 待拆分 | INV | |
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
| WU-INV-007b | INV | 期初庫存前端：三語 lazy 頁（列表、批次輸入、預覽逐行錯誤、提交／重送、詳情）、`INV.OPENING_STOCK.VIEW/CREATE` 權限、MD 名稱、列印／CSV、暗色、Vitest 與 Playwright | P1 | DONE | 007a | INV-OPN-007 通過；介面 5.8／5.9（對應 BlueWhale 4.4.1.8.14）；requestKey 由前端每批產生 GUID，重試沿用 |
| WU-INV-008 | INV | 批號與序號追蹤：物料追蹤設定、入庫採集／出庫核對、同公司唯一與追溯查詢 | P1 | 已拆分 | 004, 005 | 2026-10-13 拆為 008a／008b／008c（ADR-060）；先支援其他入出庫與期初，再供調撥／盤點等單據使用；序號不可重複出庫 |
| WU-INV-008a | INV | MD 物料追蹤方式 `tracking_mode`（None／Lot／Serial）：migration、API、使用後鎖定與 `md_item` 排他鎖、`InventoryReferenceCodes.TrackingMode` 契約、物料頁三語欄位、測試 | P1 | DONE | 004, 005 | REQ_F4118.3.7、REQ_IO_A8.12.7；MD-ITEM-TRK-001～006；`MasterData_ItemTrackingMode` 已套用 myerp |
| WU-INV-008b | INV | 批號／序號後端：`inv_lot`／`inv_serial`／`inv_stock_movement_tracking`（REQ_DS43.142–144）與單據 `*_line_tracking` 子表 migration；過帳契約 `Tracking`、其他入庫與期初採集、其他出庫核對、序號重複入庫／出庫防護與並發測試；追溯 API `INV.TRACKING.VIEW`；REQ_F4115.24.x | P1 | TODO | 008a | ADR-060 第 4–9 點；如超過單輪，先做資料表＋過帳契約＋其他入庫，出庫／期初／追溯另拆 |
| WU-INV-008c | INV | 批號／序號前端：其他入庫／出庫／期初頁的批序號輸入與檢查、追溯查詢三語頁、權限、Vitest 與 Playwright | P1 | TODO | 008b | 序號可貼上多行；列印／CSV 帶批序號 |
| WU-INV-009 | INV | 調撥：來源／目的倉庫儲位、草稿與審批、同交易出入雙向異動及三語頁 | P1 | TODO | 006, 008 | REQ_F4115.6.1–6；介面 4.4.1.5.15–17；總量守恆測試 |
| WU-INV-010 | INV | 盤點：盤點單快照、實盤輸入、差異報告、審批後盈虧異動及三語頁 | P1 | TODO | 006, 008 | REQ_F4115.7.1–2；介面 4.4.1.5.18；防止快照後異動造成誤差 |
| WU-INV-011 | INV | 組裝入庫：組件清單、耗料與成品同交易過帳、審批及三語頁 | P2 | TODO | 008 | REQ_F4115.4.1–6；介面 4.4.1.5.9–11；與 PP BOM 的邊界寫 ADR |
| WU-INV-012 | INV | 拆卸入庫：成品耗用與組件產出同交易過帳、審批及三語頁 | P2 | TODO | 011 | REQ_F4115.5.1–6；介面 4.4.1.5.12–14 |
| WU-INV-013 | INV | 安全庫存：物料／倉庫門檻、低量查詢、補貨提示與測試 | P2 | TODO | 006 | 只提示，不直接改異動帳；需求 ID 隨 001 基線分配 |
| WU-INV-014 | INV | 庫存估值 SP：依異動帳計算成本與期末值，邊界、精度及 SQL 效能測試 | P1 | TODO | 007, 009, 010 | 不直接覆寫已過帳成本；成本法與 FI 過帳介面由 ADR 決定 |
| WU-INV-015 | INV | 提供 PUR 採購入庫與 SD 銷售出庫的過帳契約、重放防護與跨模組整合測試 | P1 | TODO | 003, 008 | 介面 4.4.1.5.8、4.4.1.5.19；真正單據由 SD／PUR 建立，INV 只收經驗證的過帳指令 |
| WU-INV-016 | INV | 共用物料／倉庫／儲位選擇介面、權限與三語 E2E；核對 INV 介面清單 | P2 | TODO | 006 | 介面 4.4.1.5.20；不複製 MD 主檔 |
| WU-INV-017 | INV | 庫存報表與大量資料效能：進出存、批序號追溯、匯出；補索引與測試 | P2 | TODO | 008, 014 | BI 的跨模組儀表板留 EP-BI1 |
| WU-INV-018 | INV | 模組收尾：代碼／架構審查、完整回歸、真實 API E2E、帶 DB docgen、BlueWhale 對照表與 SRS/SDS 草稿、tag | P1 | TODO | 004–017 | 發現缺口先補 backlog；按第 14 節模組完成規則驗收 |
