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

## WU-MD-006 供應商分類

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-SCAT-001 | REQ_F4118.9.1, REQ_F4118.9.2, REQ_F4118.9.3, REQ_F4118.9.4 | SQL Server 整合：生命週期、跨公司 404、同代碼可跨公司、重複與軟刪除代碼保留、舊版本 409、錯誤碼前綴 | 通過 |
| MD-SCAT-002 | REQ_F4118.9.1, REQ_F4118.9.2, REQ_F4118.9.3 | 分頁、名稱降冪排序、排序白名單、代碼／名稱長度及空白驗證；雙 DbContext 並發；未選公司 403；`(company_id,id)` 唯一約束存在 | 通過（SQL Server 整合測試 3 個案例） |
| MD-SCAT-003 | REQ_F4118.9.1, REQ_F4118.9.2, REQ_F4118.9.3, REQ_F4118.9.4 | 前端 API 契約與錯誤碼只認本資源；共用分類頁以供應商設定渲染、表單驗證、重複送出、404 保留對話框、刪除帶版本與 IN_USE 顯示 | 通過（Vitest 4 個案例） |
| MD-SCAT-004 | REQ_F4118.9.1, REQ_F4118.9.2, REQ_F4118.9.3, REQ_F4118.9.4 | Playwright：由選單進入、新增／停用／刪除、IN_USE 錯誤三語訊息、切換公司；只讀權限隱藏寫入操作 | 通過（2 個 Playwright 案例） |

## WU-MD-007 供應商主檔

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-SUP-001 | REQ_F4118.2.1, REQ_F4118.2.2, REQ_F4118.2.3 | SQL Server 整合：本公司分類、跨公司分類拒絕（服務與資料庫複合外鍵）、跨公司修改 404、停用與搜尋、分類被引用時刪除回 `MD.SUPPLIER_CATEGORY.IN_USE`，解除引用後可刪 | 通過 |
| MD-SUP-002 | REQ_F4118.2.1, REQ_F4118.2.2, REQ_F4118.2.3 | 前端 API 契約（路由、空白關鍵字不送、版本修改、錯誤碼只認本資源）；共用 `PartnerList` 以供應商設定渲染 testid、載入供應商分類、409 重複代碼保留對話框 | 通過（Vitest 2 個案例；客戶頁 3 個案例改用共用元件後仍通過） |
| MD-SUP-003 | REQ_F4118.2.1, REQ_F4118.2.2, REQ_F4118.2.3 | Playwright：由選單進入、新增（含分類）、重複代碼三語錯誤、停用、搜尋、切換公司；只讀權限隱藏寫入操作且不載入分類 | 通過（2 個 Playwright 案例） |
| MD-SUP-004 | REQ_F4118.2.1, REQ_F4118.2.2, REQ_F4118.2.3 | 代碼永久唯一與格式／長度、名稱空白與長度、分類 Id 0、停用分類不可新選但原供應商可保留、舊版本 409、不存在 404、排序白名單、分頁、未選公司 403 | 通過（SQL Server 整合測試 2 個案例） |

## WU-MD-010 計量單位與換算

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-UOM-001 | REQ_F4118.13.1, REQ_F4118.13.2, REQ_F4118.13.3, REQ_F4118.13.4 | SQL Server 整合：單位公司隔離、代碼轉大寫及永久唯一、小數位數 0–6、舊版本 409、軟刪除後 404；換算拒絕自換、同向、反向、經中間單位的兩方向、係數 0／負數／7 位小數／超過 9 位整數、停用單位；被引用單位刪除回 `MD.UOM.IN_USE`；刪除換算後可反向重建 | 通過（2 個案例） |
| MD-UOM-002 | REQ_F4118.13.1, REQ_F4118.13.2 | SQL Server 整合：換算經兩段路徑正反方向、按目標單位小數位數遠離零捨入（含負數、同單位）、無路徑 422 `NO_PATH`、別公司單位 404；兩個 DbContext 並行新增 A→B 與 B→A 只有一個成功；`ak_md_uom_company_id_id`、兩條複合外鍵、`decimal(15,6)`；未選公司 403 | 通過（3 個案例） |
| MD-UOM-003 | REQ_F4118.13.1, REQ_F4118.13.2, REQ_F4118.13.3, REQ_F4118.13.4 | 前端 API 契約（路徑、正規化、15 位係數 JSON 無損、試算數量以原文字傳遞）、係數與數量格式驗證、只認 UoM 錯誤碼 | 通過（Vitest 2 個案例） |
| MD-UOM-004 | REQ_F4118.13.1, REQ_F4118.13.2, REQ_F4118.13.3, REQ_F4118.13.4 | 前端頁面：列表與換算規則文字、單位表單驗證與重複送出、編輯 404 保留對話框；換算不同單位驗證、PATH_EXISTS、編輯係數、刪除帶版本、IN_USE；試算驗證、結果與 NO_PATH | 通過（Vitest 3 個案例） |
| MD-UOM-005 | REQ_F4118.13.1, REQ_F4118.13.2, REQ_F4118.13.3, REQ_F4118.13.4 | Playwright：由選單進入、新增／編輯單位、新增換算（相同單位提示）、PATH_EXISTS 與 IN_USE 三語訊息、試算與 NO_PATH、切換公司；只讀權限隱藏寫入操作但可試算 | 通過（2 個 Playwright 案例） |

## WU-MD-011 物料品牌與分類

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-BRD-001 | REQ_F4118.11.1, REQ_F4118.11.2, REQ_F4118.11.3, REQ_F4118.11.4 | SQL Server 整合：品牌公司隔離（別公司修改／刪除 404、可用相同代碼）、代碼轉大寫及永久唯一、名稱去空白、舊版本 409、軟刪除後列表排除且代碼不可重用、刪除後 404；排序、分頁、代碼格式與名稱長度 400、未選公司 403；`ak_md_item_brand_company_id_id` | 通過（3 個案例） |
| MD-ICAT-001 | REQ_F4118.10.1, REQ_F4118.10.2, REQ_F4118.10.3 | SQL Server 整合：樹按代碼排序、公司隔離、別公司上層 `INVALID_PARENT`、別公司修改 404、重複代碼 409、未選公司 403；自己為上層與掛到子孫 `CYCLE`、不存在／停用上層 `INVALID_PARENT`、子樹搬移、上層停用後保留原上層、舊版本 409 | 通過（2 個案例） |
| MD-ICAT-002 | REQ_F4118.10.2, REQ_F4118.10.3 | SQL Server 整合：5 層上限（新增第 6 層與搬移兩層子樹到第 4 層皆 `TOO_DEEP`，到第 3 層可行且子節點跟隨）；兩個 DbContext 並行互掛 5 次，每次只有一個成功、不成環 | 通過（2 個案例） |
| MD-ICAT-003 | REQ_F4118.10.4 | SQL Server 整合：有子分類 `HAS_CHILDREN` 409、舊版本 409、先刪葉再刪根、已刪分類不可作上層、再刪 404；資料庫複合外鍵拒絕跨公司上層（547）與 `ak_md_item_category_company_id_id` | 通過（2 個案例） |
| MD-BRD-002 | REQ_F4118.11.1, REQ_F4118.11.2, REQ_F4118.11.3, REQ_F4118.11.4 | 前端 API 契約（路徑、關鍵字去空白、代碼轉大寫、版本）與只認品牌錯誤碼 | 通過（Vitest 2 個案例） |
| MD-BRD-003 | REQ_F4118.11.1, REQ_F4118.11.2, REQ_F4118.11.3, REQ_F4118.11.4 | Playwright：由選單進入品牌頁、重複代碼三語訊息、新增／停用／刪除、切換公司；只讀權限隱藏寫入操作並阻擋分類頁 | 通過（2 個 Playwright 案例） |
| MD-ICAT-004 | REQ_F4118.10.1, REQ_F4118.10.2, REQ_F4118.10.3, REQ_F4118.10.4 | 前端 API 契約與樹規則錯誤碼；組樹（上層在前、同層按代碼、孤兒當最上層、層級與子樹高度）、搜尋保留上層、上層選項（停用、自己與子孫、5 層上限、保留目前停用上層） | 通過（Vitest 5 個案例） |
| MD-ICAT-005 | REQ_F4118.10.1, REQ_F4118.10.2, REQ_F4118.10.3, REQ_F4118.10.4 | 前端頁面：樹狀順序與層級、停用分類無新增子分類、前端搜尋、新增子分類預選上層、表單驗證與重複送出、編輯排除子樹、INVALID_PARENT 保留對話框、刪除帶版本與 HAS_CHILDREN、載入錯誤 | 通過（Vitest 4 個案例） |
| MD-ICAT-006 | REQ_F4118.10.1, REQ_F4118.10.2, REQ_F4118.10.3, REQ_F4118.10.4 | Playwright：由選單進入、樹狀順序、新增子分類、搜尋保留上層、上層選項排除子孫、TOO_DEEP 與 HAS_CHILDREN 三語訊息、搬移、刪除、切換公司；只讀權限隱藏寫入操作並阻擋品牌頁 | 通過（2 個 Playwright 案例） |

## WU-MD-012a 物料主檔後端

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-ITEM-001 | REQ_F4118.3.1, REQ_F4118.3.2, REQ_F4118.3.3 | SQL Server 整合：公司隔離、代碼正規化及永久唯一、規格去空白、版本衝突、停用仍可搜尋、被引用單位／品牌／分類不可刪除 | 通過（ItemTests 1 個案例） |
| MD-ITEM-002 | REQ_F4118.3.2, REQ_F4118.3.3 | SQL Server 整合：外公司／無效參照拒絕、停用單位／品牌／分類不可新選、代碼／名稱／規格邊界、未選公司 403 | 通過（ItemTests 1 個案例） |
| MD-ITEM-UI-001 | REQ_F4118.3.1, REQ_F4118.3.2, REQ_F4118.3.3 | Vitest：API 查詢與寫入負載、列表與參照顯示、表單驗證、版本修改；Playwright：搜尋、新增、編輯、公司切換與只讀權限 | 通過（Vitest 3 個、Playwright 2 個案例；前端全量 466/466、E2E 83 通過 3 略過） |

## WU-MD-012c 物料專屬換算

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-ITEM-CONV-001 | REQ_F4118.3.5 | SQL Server 整合：專屬係數覆寫公司級（1 BOX = 12 PCS 而非公司級 10，公司級換算本身不變）、專屬單位互換、沒有專屬換算的單位以公司級路徑補到基本單位、兩端都換不到基本單位時用公司級直接路徑、按目標單位小數位數遠離零捨入（含負數、同單位）、NO_PATH 422、單位不存在 404、停用物料仍可換算、別公司物料 404 | 通過（ItemUomConversionTests 1 個案例） |
| MD-ITEM-CONV-002 | REQ_F4118.3.3, REQ_F4118.3.5 | SQL Server 整合：基本單位／停用／別公司／無效單位 400、重複單位 409、係數邊界 400、別公司物料 404、依單位代碼排序、被專屬換算引用的單位 IN_USE、帶版本修改與舊版本 409、換算不屬該物料 404；有專屬換算時改基本單位 409 `BASE_UOM_LOCKED` 而其他欄位可改，刪除後可改基本單位並重建 | 通過（ItemUomConversionTests 1 個案例） |
| MD-ITEM-CONV-003 | REQ_F4118.3.5 | SQL Server 整合：`ak_md_item_company_id_id`、兩條複合外鍵、`decimal(15,6)`；未選公司 403 | 通過（ItemUomConversionTests 1 個案例） |
| MD-ITEM-CONV-UI-001 | REQ_F4118.3.3, REQ_F4118.3.5 | Vitest：API 路徑與 15 位係數 JSON 無損、試算數量原文字、BASE_UOM_LOCKED 訊息；對話框規則文字、排除基本單位、係數驗證、DUPLICATE、帶版本修改與刪除、試算與 NO_PATH、只讀權限隱藏維護；物料列開啟對話框 | 通過（Vitest 4 個案例） |
| MD-ITEM-CONV-UI-002 | REQ_F4118.3.5 | Playwright：由物料列開啟換算、規則文字、基本單位不可選、重複單位三語訊息、新增、試算與 NO_PATH、確認後帶版本刪除 | 通過（1 個 Playwright 案例） |
