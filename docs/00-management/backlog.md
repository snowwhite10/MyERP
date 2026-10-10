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

## Phase 0 — SYS 系統管理（SRS 模組 1 與 8 的一部分）

| ID | 模組 | 工作單元 | 優先級 | 狀態 | 依賴 | 備註 |
|---|---|---|---|---|---|---|
| WU-SYS-001 | SYS | 公司 / 分店 / 部門（多公司隔離） | P1 | IN_PROGRESS | P0-007 | 已完成三表模型／migration、分店與部門部分內部服務、跨公司複合 FK／循環／代碼／版本測試；待公司服務、授權後 API、前端與完整驗收；未完成不得標 DONE |
| WU-SYS-002 | SYS | 用戶、ASP.NET Core Identity PasswordHasher、密碼政策、帳號鎖定 | P1 | TODO | 001 | |
| WU-SYS-003 | SYS | 登入 / 登出、JWT Access + Refresh（Redis、輪替）、Captcha（含 Development 萬用碼與啟動檢查） | P1 | TODO | 002 | |
| WU-SYS-004 | SYS | 角色、權限代碼（模組.資源.動作）、資料範圍、前端權限指令 | P1 | TODO | 003 | |
| WU-SYS-005 | SYS | 安全中介層：HTTPS/HSTS、CORS、Rate limiting、安全標頭、ForwardedHeaders | P1 | TODO | 003 | |
| WU-SYS-006 | SYS | IP 白名單（全系統 / 角色 / 用戶、CIDR、有效期限、總開關） | P2 | TODO | 004 | |
| WU-SYS-007 | SYS | 稽核日誌（sys_audit_log）+ 稽核查詢頁 + 操作日誌（BlueWhale Operation Log） | P1 | TODO | 001 | |
| WU-SYS-008 | SYS | 單號規則服務（併發不重號） | P1 | TODO | 001 | |
| WU-SYS-009 | SYS | 審批流程引擎（多級、金額門檻、代理人） | P1 | TODO | 004 | |
| WU-SYS-010 | SYS | 系統參數、列印設定、Logo 與印章 | P2 | TODO | 001 | BlueWhale 4.1.1.8.17–19 |
| WU-SYS-011 | SYS | 首頁、修改密碼、個人設定（語言、主題） | P2 | TODO | 003 | BlueWhale 4.1.1.1 |
| WU-SYS-012 | SYS | 站內通知 | P3 | TODO | 001 | |
| WU-SYS-013 | SYS | 種子資料：測試帳號（MASTER_PROMPT 第 12 節）、2 間公司、分店 | P1 | TODO | 004 | |
| WU-SYS-014 | - | 共用介面：確認框、提示框、選擇視窗（BlueWhale 4.4.1.9）、標準列表頁 / 單據頁範本 | P1 | TODO | P0-010 | 之後所有模組重用 |

## Phase 1–4 — Epic（開始前先拆分）

| ID | 模組 | Epic | 優先級 | 狀態 | 依賴 | 備註 |
|---|---|---|---|---|---|---|
| EP-MD | MD | 主檔：客戶（含聯絡人、匯入、分類）、供應商（含聯絡人、匯入、分類）、物料（品牌、分類、圖片、期初庫存）、UoM 換算、倉庫儲位、幣別匯率、稅率、付款條件 / 結算方式、會計科目、收支類別、工序 / 工序分類 | P1 | 待拆分 | SYS | |
| EP-INV | INV | 入庫出庫（其他入庫 / 出庫）、調撥、盤點、組裝 / 拆卸、批號序號、安全庫存、庫存估值（SP） | P1 | 待拆分 | MD | |
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
