# 項目章程（Project Charter）

> 建立：WU-P0-003（2026-10-10）。來源：MASTER_PROMPT v1.1、`reference-notes.md`（BlueWhale 基線已核對）。
> 本章程是 SRS 1.2 Scope、2.2 Product Features、4.6 Project Planning 的敘述來源之一；變更須在本檔末「變更記錄」追加。

## 1. 項目背景與目標

MyERP 是一套由 AI 代理（Claude Code / Codex，經 Runner 無人值守輪替）自主開發的企業級 ERP，以 BlueWhale（藍微）ERP 的功能為基線，擴充至大型企業 ERP 的模組範圍。

| # | 目標 | 量度方式 |
|---|---|---|
| G1 | 交付**可運行**的前端 + 後端 + 資料庫，業務邏輯真實 | 本機一鍵啟動；Playwright E2E 覆蓋登入及主要單據建立 / 審批 / 查詢 |
| G2 | 100% 覆蓋 BlueWhale 功能基線，並在其上擴充 | `reference-notes.md` 對照表沒有「未開始」或「進行中」（只容許「完成」或有理據的 WONT_DO） |
| G3 | 完整、可追溯的文件，最終產出 SRS / SDS（Word）與需求總表（Excel） | docgen 一致性檢查 `consistency-report.md` 零錯誤 |
| G4 | 以專業項目公司標準管理 | 需求、設計、決策（ADR）、測試、變更、風險、進度分檔記錄且持續更新 |

## 2. 範圍

### 2.1 在範圍內 — 模組（MASTER_PROMPT 13 / 15.1）

| SRS 模組編號 | 模組 | MyERP 代碼 | 階段 | BlueWhale 基線 REQ_F | BlueWhale 基線介面 |
|---|---|---|---|---|---|
| 1 / 8（部分） | User、Settings（用戶權限、參數、列印、Logo、操作日誌） | SYS | P0 | 15 | 11 |
| 8 | Settings（主檔） | MD | P1 | 62（含資金帳戶 4） | 49（含資金帳戶 3） |
| 5 | Inventory | INV | P1 | 34 | 20 |
| 4 | Purchase | PUR | P1 | 12 | 7 |
| 2 | Sales | SD | P1 | 12 | 7 |
| 6 | Accounting | FI | P2 | 36 | 16 |
| 10 | Controlling | CO | P2 | 0（擴充） | 0 |
| 7 | Report | BI | P2 / P5 | 23 | 22 |
| 3 | Production | PP | P3 | 28 | 15 |
| 11 | Quality | QM | P3 | 0（擴充） | 0 |
| 12 | Maintenance | PM | P3 | 0（擴充） | 0 |
| 13 | CRM | CRM | P4 | 0（擴充） | 0 |
| 14 | Human Resources | HR | P4 | 0（擴充） | 0 |
| 15 | Project | PS | P4 | 0（擴充） | 0 |
| 16 | Supply Chain | SCM | P4 | 0（擴充） | 0 |
| 9 | General Interface（確認框、提示框、選擇視窗） | 前端共用 | P0 | — | 3 |
| **合計** | | | | **222** | **150** |

另：BlueWhale 資料表 88 張（REQ_DS43），其中 3 張 WONT_DO（CartList、DataBacklist、Months，理由見 ADR-002 / reference-notes 4A），其餘映射到 MyERP 表（可合併）。

### 2.2 在範圍內 — 橫切能力

- 多公司 / 多分店隔離、多幣別（本位幣 + 外幣）、多語言（zh-Hant / zh-Hans / en）、Dark mode。
- 安全：JWT（Access + Refresh）、Captcha、RBAC + 資料範圍、IP 白名單、Rate limiting、稽核日誌。
- 共用服務：單號規則、審批流程引擎、過帳引擎（`IGlPostingService`）、站內通知。
- 報表：儀表板、比較 / 鑽取 / 匯出 / 排程發送。
- 文件：SRS / SDS（英文、BlueWhale 格式）、Excel 需求總表、原型與實際系統截圖。
- Phase 5：創意擴充（MASTER_PROMPT 17）產生的新功能，SRS 模組編號 17 起追加。

### 2.3 不在範圍內

| 項目 | 理由 |
|---|---|
| Docker / Kubernetes / 雲端部署自動化 | MASTER_PROMPT 4：Windows VM，不使用 Docker |
| 更換技術棧（MASTER_PROMPT 4） | 固定；升級只可經 ADR |
| 微信商城購物車（BlueWhale CartList） | 屬電商前台，非 ERP 範圍（WONT_DO） |
| 資料庫備份記錄（BlueWhale DataBacklist） | 屬 DBA 維運，不在應用層（WONT_DO） |
| 真實客戶資料、真實合約金額、真實人名 | 公開 repo；SRS / SDS 用佔位符 |
| 生產環境上線、資料移轉自舊系統 | 本項目交付可運行系統與文件，不含上線服務 |
| 原生行動 App | 只做響應式 Web；行動版介面可經創意擴充評估 |

## 3. 成功標準（可量化，對應 MASTER_PROMPT 1「停止條件」）

| # | 標準 | 門檻 |
|---|---|---|
| S1 | Backlog 全部項目 | DONE 或有理據的 WONT_DO；`blocked.md` 無未處理項目 |
| S2 | BlueWhale 功能基線 | 222 REQ_F、150 介面全部「完成」（WONT_DO 須在 ADR 說明） |
| S3 | 測試 | 本機全部測試通過；Domain + Application 單元測試覆蓋率 ≥ 80%；三條跨模組整合流程（O2C、P2P、生產成本）通過 |
| S4 | 建置 / CI | `main` 永遠可建置；GitHub Actions 綠燈 |
| S5 | 非功能 | 符合 SRS 4.2 的 REQ_NF 指標（例：列表 API 10 萬筆資料 P95 < 500ms，詳見 WU-P0-005 NFR） |
| S6 | 文件 | SRS.docx、SDS.docx、Requirements.xlsx 由 docgen 產生，一致性檢查零錯誤 |
| S7 | 創意擴充 | 連續 3 輪沒有 ≥ 6 分的新想法 |
| S8 | 安全 | repo 內無機密；`docs/reference/`、`logs/` 從未提交 |

## 4. 里程碑

| 里程碑 | 內容 | 完成判定 | Tag |
|---|---|---|---|
| M0 規劃與骨架（P0 基礎） | WU-P0-001～013：章程、架構、conventions、NFR、後端 / 前端骨架、共用核心、DB 基線、日誌、CI、E2E、docgen 骨架 | 建置與測試可跑；CI 綠燈 | `v0.1.0-foundation` |
| M1 系統管理（P0 SYS） | WU-SYS-001～014 | 可登入、權限、稽核、單號、審批引擎可用 | `v0.2.0-sys` |
| M2 主檔與核心交易（P1） | MD → INV → PUR → SD | O2C 前段、P2P 前段整合測試通過 | 每模組一個 tag（例 `v0.3.0-md`） |
| M3 財務與分析（P2） | FI → CO → BI 第一期 | O2C、P2P 全流程含過帳通過；BlueWhale 22 張報表完成 | 每模組 tag |
| M4 製造與營運（P3） | PP → QM → PM | 生產成本整合流程通過 | 每模組 tag |
| M5 擴展業務（P4） | CRM、HR、PS、SCM | 各模組 DoD 完成 | 每模組 tag |
| M6 持續擴充與交付（P5） | BI 第二期、創意擴充、SRS / SDS 最終檢查、final-report | 第 3 節 S1～S8 全部達成 | `v1.0.0` |

> 本項目以工作單元（30–90 分鐘）計量，不設日曆日期；實際進度見 `progress.md` 與 `sprint-log.md`。

## 5. 角色與職責

AI 代理一人分飾全部角色（MASTER_PROMPT 0）；人手只負責 Runner、憑證、VM 與總指令。

| 角色 | 擔任 | 主要職責 | 主要產出 |
|---|---|---|---|
| 項目發起人 / 產品負責人 | 人手 | 維護 MASTER_PROMPT、審批 change request、處理 BLOCKED | MASTER_PROMPT、change-requests 回覆 |
| 基礎設施管理員 | 人手 | VM、SQL Server、Redis、Git 憑證、Runner 安裝 | 環境 |
| 項目經理（PM） | AI | Backlog、Sprint、風險、進度、狀態 | backlog、sprint-log、risk-register、progress、state.json |
| 業務分析師（BA） | AI | 需求、業務規則、BlueWhale 對照 | requirements.md、business-rules.md、reference-notes |
| 系統架構師 | AI | 架構、ADR、conventions、模組邊界 | 01-architecture/ |
| 後端 / 前端工程師 | AI | 實作、migration、SP、頁面 | src/ |
| DBA | AI | 資料表設計、索引、SP、效能 | data-structure.md、database/ |
| QA 測試工程師 | AI | 測試先行、整合 / E2E、test-plan | tests/、test-plan.md |
| 技術文件撰寫員 | AI | SRS / SDS 來源與產出 | 05-doc-sources/、99-output/ |

## 6. 假設與限制

**假設**
- A1：VM 已安裝 .NET 10 SDK、Node.js、SQL Server 2022（實例 `localhost\MSSQLSERVER2022`，Windows 驗證）、Redis 相容服務（Memurai）。
- A2：Git 憑證已由人手設定，可 push 到 `origin`。
- A3：BlueWhale 參考文件（`docs/reference/`）長期可讀，但不提交。
- A4：Runner 每輪呼叫一次 AI，單輪上限 120 分鐘（`runner.config.json`）。

**限制**
- C1：技術棧固定（MASTER_PROMPT 4）；不使用 Docker；不使用 `System.Data.SqlClient`、`DateTime.Now`、`float/double` 業務數值。
- C2：無人值守，不可提問；不確定處寫 ADR。
- C3：repo 公開：不得提交 NDA 內容、客戶資料、機密。
- C4：舊系統原始碼只可理解業務規則，不得複製代碼 / SQL / 表結構。
- C5：VM 沒有 Python（只有 node / dotnet），工具腳本以 PowerShell、Node.js 或 .NET 撰寫。

## 7. 變更記錄

| 日期 | 版本 | 變更 | 工作單元 |
|---|---|---|---|
| 2026-10-10 | 1.0 | 初版 | WU-P0-003 |
