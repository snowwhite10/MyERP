# MD 測試計畫

| Test ID | Requirement | Case | Expected |
|---|---|---|---|
| MD-BASE-001 | REQ_F4118.34.1 | 後端組件載入與權限宣告 | MD 與 Contracts 組件載入，`MD.MASTER.VIEW` 只屬 MD |
| MD-BASE-002 | REQ_F4118.34.1 | 前端 `/md` 路由 | 登入、公司授權與 `MD.MASTER.VIEW` 守衛生效，三語資源可載入 |
| MD-BASE-003 | REQ_F4118.34.1 | 完整回歸 | 後端、前端與既有 E2E 維持通過 |

MD 業務資料的公司隔離整合測試自第一個資料表 WU-MD-002 起建立。

## WU-MD-002 客戶分類

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-CAT-001 | REQ_F4118.8.1, REQ_F4118.8.2, REQ_F4118.8.3, REQ_F4118.8.4 | 後端分類生命週期與跨公司存取；代碼唯一、版本與軟刪除 | 通過（SQL Server 整合測試） |
| MD-CAT-002 | REQ_F4118.8.1, REQ_F4118.8.2 | 列表分頁、排序白名單及輸入驗證 | 通過（SQL Server 整合測試） |
| MD-CAT-003 | REQ_F4118.8.1, REQ_F4118.8.2, REQ_F4118.8.3, REQ_F4118.8.4 | 前端 API 契約：分頁、搜尋、正規化與版本 | 通過（Vitest） |
| MD-CAT-004 | REQ_F4118.8.1, REQ_F4118.8.2, REQ_F4118.8.3, REQ_F4118.8.4 | Playwright 介面生命週期、切換公司及只讀權限 | 通過（2 個 Playwright 案例） |
| MD-CAT-005 | REQ_F4118.8.1, REQ_F4118.8.3 | 雙 DbContext 並發編輯及未選公司 | 通過（舊版本衝突、無公司拒絕） |

## WU-MD-003 客戶主檔

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-CUS-001 | REQ_F4118.1.1, REQ_F4118.1.2, REQ_F4118.1.3 | SQL Server 整合：本公司分類、跨公司拒絕（含資料庫外鍵）、停用與搜尋，分類有引用不可刪除 | 通過 |
| MD-CUS-002 | REQ_F4118.1.1, REQ_F4118.1.2, REQ_F4118.1.3 | 前端 API 分頁、正規化建立與版本修改；頁面表單驗證、無分類權限不查分類、重複送出防護、409 保留對話框、搜尋回第 1 頁 | 通過（Vitest 4 個案例） |
| MD-CUS-003 | REQ_F4118.1.1, REQ_F4118.1.2, REQ_F4118.1.3 | Playwright 客戶生命週期及只讀權限 | 通過（2 個 Playwright 案例） |
| MD-CUS-004 | REQ_F4118.1.2, REQ_F4118.1.3 | 代碼永久唯一、輸入格式、版本衝突、停用分類不可新選但原客戶可保留 | 通過（SQL Server 整合測試） |

## WU-MD-001 結果（2026-10-12）

- MD-BASE-001：架構測試通過；MD 實作與 Contracts 已加入解決方案，權限前綴為 MD。
- MD-BASE-002：新增兩個 Playwright 案例，驗證三語與無權限 403，皆通過。
- MD-BASE-003：後端建置 0 警告，完整後端 600/600；前端 432/432，E2E 67 通過、3 依既有設定略過。完整後端首次與前端並行執行時出現 SQL 逾時，單獨重跑完整後端全過。
