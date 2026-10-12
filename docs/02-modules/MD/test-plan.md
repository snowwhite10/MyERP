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

## WU-MD-015 倉庫與儲位

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-WH-001 | REQ_F4118.4.1, REQ_F4118.4.2, REQ_F4118.4.3, REQ_F4118.4.4 | SQL Server 整合：公司隔離（別公司修改／刪除 404、可用相同代碼）、代碼轉大寫與永久唯一、名稱與聯絡資料去空白、空白轉 null、地址可搜尋、帶版本修改與舊版本 409、軟刪除後列表排除且代碼不可重用、刪除後 404 | 通過（WarehouseTests 1 個案例） |
| MD-WH-002 | REQ_F4118.4.1, REQ_F4118.4.2 | SQL Server 整合：排序與分頁、代碼／名稱／地址／聯絡人／電話欄位 400 與剛好上限可保存、未選公司 403（倉庫與儲位） | 通過（WarehouseTests 1 個案例） |
| MD-WH-003 | REQ_F4118.4.3, REQ_F4118.4.4, REQ_F4118.4.5 | SQL Server 整合：儲位代碼轉大寫、同倉庫重複 409、不同倉庫可重複、搜尋與排序、欄位 400、別公司倉庫 404、帶版本修改、儲位不屬該倉庫 404；停用倉庫新增 422 `WAREHOUSE_INACTIVE` 但可修改既有儲位；有儲位 `HAS_LOCATIONS` 409、刪光儲位後可刪倉庫、儲位代碼刪除後保留 | 通過（WarehouseTests 1 個案例） |
| MD-WH-004 | REQ_F4118.4.5 | SQL Server 整合：`ak_md_warehouse_company_id_id`、複合外鍵 `fk_md_storage_location_md_warehouse_company_id_warehouse_id`、唯一索引 `(company_id, warehouse_id, code)` | 通過（WarehouseTests 1 個案例） |
| MD-WH-UI-001 | REQ_F4118.4.1, REQ_F4118.4.2, REQ_F4118.4.3, REQ_F4118.4.4, REQ_F4118.4.5 | Vitest：API 路徑、正規化（代碼大寫、空白轉 null）、版本參數與錯誤碼；倉庫頁表單驗證、建立與版本修改、409 保留對話框、刪除確認與 HAS_LOCATIONS、開啟儲位、無權限隱藏按鈕；儲位對話框搜尋與分頁、建立 DUPLICATE、版本修改與刪除、停用倉庫隱藏新增、只讀隱藏維護 | 通過（Vitest 8 個案例） |
| MD-WH-UI-002 | REQ_F4118.4.1, REQ_F4118.4.2, REQ_F4118.4.3, REQ_F4118.4.4, REQ_F4118.4.5 | Playwright：倉庫搜尋、新增、修改停用、HAS_LOCATIONS 三語訊息、切換公司；儲位列出、重複代碼訊息、新增、修改、確認後帶版本刪除、停用倉庫不可新增；只讀權限 | 通過（3 個 Playwright 案例） |

## WU-MD-016 幣別與匯率

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-CUR-001 | REQ_F4118.35.1, REQ_F4118.35.2, REQ_F4118.35.3, REQ_F4118.35.4 | SQL Server 整合：ISO 代碼轉大寫、非三個英文字母 400 `code`、小數位 0–4、符號長度、名稱必填；重複代碼 409、別公司可同碼、別公司刪除 404；排序；帶版本修改與舊版本 409；軟刪除後代碼保留；未選公司 403（幣別與匯率） | 通過（CurrencyTests 1 個案例） |
| MD-CUR-002 | REQ_F4118.35.3, REQ_F4118.35.4, REQ_F4118.36.2 | SQL Server 整合：無本位幣不可新增匯率；新本位幣取代舊本位幣；不可取消／停用／刪除本位幣；停用幣別不可設為本位幣；本位幣不可設匯率；有匯率後更換本位幣（修改或新增）409 `BASE_LOCKED`；有匯率的幣別 `IN_USE`；停用外幣新增匯率 422 | 通過（CurrencyTests 1 個案例） |
| MD-CUR-003 | REQ_F4118.36.1, REQ_F4118.36.2, REQ_F4118.36.3, REQ_F4118.36.4, REQ_F4118.36.5 | SQL Server 整合：8 位小數匯率、各幣別期間獨立、四種重疊情況（同日相接、無到期日、包含、跨年）409、到期日早於生效日 400、匯率 0／負數／9 位小數／12 位整數 400、幣別不存在 404；列表預設新到舊、可改舊到新、按日篩選；查詢按業務日期、不分大小寫、本位幣回 1、無匯率或期間空檔 422、未知幣別 404、別公司 404；修改重疊 409、不屬該幣別 404；刪除後期間可重設；停用幣別仍可查匯率 | 通過（CurrencyTests 1 個案例） |
| MD-CUR-004 | REQ_F4118.36.5 | SQL Server 整合：`GetPrecisionAsync` 以主檔小數位覆寫預設（TWD 2、KWD 3），未在主檔的幣別用預設；`Money.Round` 按此捨入 | 通過（CurrencyTests 1 個案例） |
| MD-CUR-005 | REQ_F4118.35.1, REQ_F4118.36.1 | SQL Server 整合：複合外鍵 `fk_md_exchange_rate_md_currency_company_id_currency_id`、篩選唯一索引 `ux_md_currency_company_id_base`／`ux_md_exchange_rate_company_id_currency_id_effective_from`、三個 CHECK、`code char(3)` | 通過（CurrencyTests 1 個案例） |
| MD-CUR-UI-001 | REQ_F4118.35.1, REQ_F4118.35.2, REQ_F4118.35.3, REQ_F4118.35.4, REQ_F4118.36.1, REQ_F4118.36.2, REQ_F4118.36.3, REQ_F4118.36.4 | Vitest：API 路徑、正規化、`on` 篩選、版本參數與錯誤碼；幣別頁本位幣標記並隱藏匯率／刪除、ISO 代碼與小數位驗證、建立、本位幣勾選鎖定、改本位幣 BASE_LOCKED 保留對話框、刪除確認與 IN_USE、開啟匯率、無權限隱藏；匯率對話框 6 位整數 8 位小數規則、按日篩選、期間與匯率驗證、OVERLAP、帶版本修改與刪除、停用幣別隱藏新增、只讀隱藏維護 | 通過（Vitest 10 個案例） |
| MD-CUR-UI-002 | REQ_F4118.35.1, REQ_F4118.35.2, REQ_F4118.35.3, REQ_F4118.35.4, REQ_F4118.36.1, REQ_F4118.36.2, REQ_F4118.36.3, REQ_F4118.36.4 | Playwright：本位幣標示、搜尋、ISO 代碼提示、新增、本位幣不可取消、BASE_LOCKED 與 IN_USE 三語訊息、切換公司；匯率重疊訊息、小數位驗證、新增持續有效、按日篩選與顯示全部、修改、確認後帶版本刪除、停用幣別不可新增；只讀權限 | 通過（3 個 Playwright 案例） |

## WU-MD-018 結算方式與付款條件

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-PAY-001 | REQ_F4118.14.1, REQ_F4118.14.2, REQ_F4118.14.3, REQ_F4118.14.4, REQ_F4118.14.5 | SQL Server 整合：結算方式代碼轉大寫與去空白、審計、重複代碼、空白名稱、搜尋、公司隔離（列表、修改 404、查詢 null）、停用與舊版本衝突；`ISettlementMethodLookup` 的 Find／RequireActive（INACTIVE、NOT_FOUND）；`ISettlementMethodUsage` 回報已使用時不可刪除；刪除後代碼保留、重複刪除 404；未選公司 403 | 通過（PaymentSetupTests 1 個案例） |
| MD-PAY-002 | REQ_F4118.40.1, REQ_F4118.40.2, REQ_F4118.40.3, REQ_F4118.40.4 | SQL Server 整合：三種規則只保存自己的欄位（不適用欄位清空）、重複代碼、修改時換規則類型、舊版本衝突、按代碼倒序、公司隔離、已使用不可刪除但仍可修改規則、刪除後列表 | 通過（PaymentSetupTests 1 個案例） |
| MD-PAY-003 | REQ_F4118.40.2 | 單元（邊界）：缺少或超出範圍的 `days`／`closingDay`／`months`／`paymentDay`、同月付款日不晚於結帳日（含 31 日）、未知 `dueType` 皆 400 並指出欄位 | 通過（16 個 InlineData） |
| MD-PAY-004 | REQ_F4118.40.5 | 單元：到期日計算 — 單據日後天數跨月、月底結帳跨年與閏年、25 日結帳當日屬當期、次日屬下期、結帳日大於當月天數取月底、結帳後第 M 月 D 日（含 2 月取月底、跨年、同月月底） | 通過（14 個 InlineData） |
| MD-PAY-005 | REQ_F4118.40.5 | SQL Server 整合：`IPaymentTermLookup.FindAsync` 代碼正規化、含停用、不存在 null、公司隔離；`CalculateDueDateAsync` 結果、INACTIVE 422、NOT_FOUND 404、超出日期範圍 400 `documentDate` | 通過（PaymentSetupTests 1 個案例） |
| MD-PAY-006 | REQ_F4118.40.2 | SQL Server 整合：`ck_md_payment_term_rule` 拒絕不屬於規則的欄位（547）；CHECK 與兩張表的公司代碼唯一索引存在 | 通過（PaymentSetupTests 1 個案例） |
| MD-PAY-007 | REQ_F4118.40.1, REQ_F4118.40.5 | 單元：`PaymentTermDueType` 在 JSON 以名稱傳遞 | 通過（PaymentSetupTests 1 個案例） |
| MD-PAY-008 | REQ_F4118.40.2, REQ_F4118.40.3 | Vitest：依規則類型只保留適用欄位；限制天數、結帳日、月數、同月付款順序 | 通過（2 個案例） |
| MD-PAY-009 | REQ_F4118.14.1, REQ_F4118.40.1, REQ_F4118.40.2, REQ_F4118.40.5 | Playwright：結算方式列表、付款條件規則欄位切換與同月驗證、已存條件到期日試算；只讀權限與無權限阻擋 | 通過（payment-setup.spec.ts 2 個案例） |

## WU-MD-019 會計科目

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-ACC-001 | REQ_F4118.39.1, REQ_F4118.39.2, REQ_F4118.39.3 | SQL Server 整合：公司隔離、根科目必填類別、數字代碼、類別預設方向、子科目沿用類別、備抵科目指定貸方、前綴與類別不符、重複代碼、樹按代碼排序與可過帳標記、別公司上層 422 與修改 404、未選公司 403 | 通過（AccountTests 1 個案例） |
| MD-ACC-002 | REQ_F4118.39.2, REQ_F4118.39.3, REQ_F4118.39.4 | SQL Server 整合：有啟用子科目不可停用、停用子科目後上層可停用、上層停用時不可啟用子科目或新增子科目、停用科目可改名與方向、舊版本衝突、方向列舉驗證、有子科目不可刪除、刪除後代碼保留、重複刪除 404 | 通過（AccountTests 1 個案例） |
| MD-ACC-003 | REQ_F4118.39.2, REQ_F4118.39.3, REQ_F4118.39.4, REQ_F4118.39.5 | SQL Server 整合：`IAccountUsage` 回報已使用時不可改方向、不可刪除、不可新增子科目；名稱與停用仍可改；未使用的同層科目不受影響 | 通過（AccountTests 1 個案例） |
| MD-ACC-004 | REQ_F4118.39.5 | SQL Server 整合：`IChartOfAccounts.FindAsync` 不存在回 null、代碼去空白、上層代碼與可過帳；`RequirePostableAsync` 的 NOT_POSTABLE／INACTIVE／NOT_FOUND | 通過（AccountTests 1 個案例） |
| MD-ACC-005 | REQ_F4118.39.1, REQ_F4118.39.2 | SQL Server 整合：同公司上層複合外鍵（跨公司 547）、類別／方向／數字代碼／上層 CHECK、類別與方向以名稱字串保存 | 通過（AccountTests 1 個案例） |
| MD-ACC-UI-001 | REQ_F4118.39.1, REQ_F4118.39.2, REQ_F4118.39.3, REQ_F4118.39.4 | Vitest：API 路徑、代碼與名稱去空白、子科目不送類別、版本參數與 10 個錯誤碼；預設方向、按代碼組樹、數字代碼與上層前綴驗證；科目頁樹狀順序與層級、類別／方向／可過帳標示、停用科目不可新增子科目、前端搜尋保留上層；一級科目必選類別並帶出預設方向、重複送出只送一次；子科目預填代碼、類別唯讀、前綴錯誤不送出、可改貸方、PARENT_IN_USE 保留對話框；帶版本修改名稱／方向／停用與 IN_USE；刪除確認、HAS_CHILDREN；載入錯誤與無權限隱藏新增 | 通過（Vitest 11 個案例） |
| MD-ACC-UI-002 | REQ_F4118.39.1, REQ_F4118.39.2, REQ_F4118.39.3, REQ_F4118.39.4 | Playwright：選單進入、樹狀順序與層級、可過帳／彙總與方向三語文字；新增一級科目帶出貸方、子科目預填代碼與類別唯讀、前綴提示、新增後上層變彙總、PARENT_IN_USE 三語訊息；搜尋；修改名稱與方向帶版本；HAS_CHILDREN 訊息與刪除末級科目；切換公司；只讀權限隱藏寫入並阻擋稅別頁 | 通過（2 個 Playwright 案例） |
| MD-ACC-006 | REQ_F4118.39.2 | SQL Server 整合（邊界）：30 位代碼可用、31 位與空白代碼 400、空白名稱 400、方向列舉驗證 | 通過（AccountTests 1 個案例） |
| MD-ACC-007 | REQ_F4118.39.2, REQ_F4118.39.4 | SQL Server 整合（並行）：同時刪除上層與新增子科目，applock 序列化後恰一個成功，不留下掛在已刪除上層下的子科目（5 次） | 通過（AccountTests 1 個案例） |
| MD-ACC-008 | REQ_F4118.39.1, REQ_F4118.39.5 | 單元：`AccountType`、`BalanceDirection` 在 JSON 以名稱傳遞 | 通過（AccountTests 1 個案例） |

## WU-MD-017 稅別與稅率

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-TAX-CALC-001 | REQ_F4118.38.6 | Shared 單元：含稅／未稅、零稅率、負數、TWD／USD 捨入、公司幣別精度、淨額加稅額恆等於含稅金額、稅率上下界與四位小數、null 精度 | 通過（TaxTests 11 個案例） |
| MD-TAX-001 | REQ_F4118.37.1, REQ_F4118.37.2, REQ_F4118.37.4, REQ_F4118.38.1, REQ_F4118.38.2, REQ_F4118.38.4 | SQL Server 整合：稅別公司隔離、代碼大寫與永久唯一、類型驗證、期間首尾包含且不可重疊、稅率四位小數、刪除引用限制、軟刪除後期間可重用、別公司稅別 404 | 通過（TaxRateTests 1 個案例） |
| MD-TAX-002 | REQ_F4118.38.5, REQ_F4118.38.6 | SQL Server 整合：按業務日與方向查詢、代碼不分大小寫、方向不符／缺稅率錯誤碼、TWD 金額精度下含稅試算 | 通過（TaxRateTests 1 個案例） |
| MD-TAX-003 | REQ_F4118.37.3, REQ_F4118.38.2, REQ_F4118.38.3, REQ_F4118.38.5 | SQL Server 整合：修改時重疊與舊版本衝突；稅別停用後不可新增稅率但仍可查歷史稅率；類型改成 Both 後可查兩個方向 | 通過（TaxRateTests 1 個案例） |
| MD-TAX-004 | REQ_F4118.37.1, REQ_F4118.38.1 | SQL Server 整合：同公司複合外鍵、稅率生效日篩選唯一索引、類型／期間／百分比資料庫 CHECK | 通過（TaxRateTests 1 個案例） |
| MD-TAX-005 | REQ_F4118.38.5, REQ_F4118.38.6 | 單元：`TaxDirection` 在 JSON 以名稱 `Output`／`Input` 傳遞（前端試算送名稱） | 通過（TaxRateTests 1 個案例） |
| MD-TAX-UI-001 | REQ_F4118.37.1, REQ_F4118.37.2, REQ_F4118.37.3, REQ_F4118.37.4, REQ_F4118.38.1, REQ_F4118.38.2, REQ_F4118.38.3, REQ_F4118.38.4, REQ_F4118.38.6 | Vitest：API 路徑、代碼大寫與去空白、`on` 篩選、版本參數、試算請求正規化與錯誤碼；稅別頁類型與今日稅率（無稅率標示）、代碼格式驗證、DUPLICATE_CODE、帶版本修改類型與停用、刪除確認與 IN_USE、開啟稅率與試算、無權限隱藏維護但保留稅率與試算；稅率對話框 0–100 四位小數規則、按日篩選、期間驗證、OVERLAP、帶版本修改與刪除並通知列表重新載入、停用稅別隱藏新增、只讀隱藏維護；試算對話框金額規則、方向依類型限制、日期預設今日、驗證、結果顯示、輸入改變清除結果、NOT_FOUND | 通過（Vitest 13 個案例） |
| MD-TAX-UI-002 | REQ_F4118.37.1, REQ_F4118.37.2, REQ_F4118.37.3, REQ_F4118.37.4, REQ_F4118.38.1, REQ_F4118.38.2, REQ_F4118.38.3, REQ_F4118.38.4, REQ_F4118.38.6 | Playwright：類型與今日稅率、搜尋、代碼提示、新增銷項稅別、修改停用、IN_USE 三語訊息、切換公司；稅率重疊訊息、百分比驗證、新增持續有效並重新載入列表、按日篩選與顯示全部、修改、確認後帶版本刪除、停用稅別不可新增；含稅試算結果與方向無稅率訊息；只讀權限 | 通過（3 個 Playwright 案例） |

## WU-MD-020a 收支類別與資金帳戶後端

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-FUND-001 | REQ_F4118.12.1, REQ_F4118.12.2, REQ_F4118.12.3, REQ_F4118.12.4, REQ_F4118.12.5 | SQL Server：收入／支出方向、對應科目類別與可過帳驗證、公司隔離、停用與查詢契約、版本和引用刪除保護 | 通過（FundSetupTests） |
| MD-FUND-002 | REQ_F4118.5.1, REQ_F4118.5.2, REQ_F4118.5.3, REQ_F4118.5.4, REQ_F4118.5.5 | SQL Server：資金帳戶幣別固定、Cash 銀行欄位清空、Bank 欄位驗證、科目限制、公司隔離、查詢契約、引用刪除保護 | 通過（FundSetupTests） |
| MD-FUND-003 | REQ_F4118.5.2, REQ_F4118.5.3, REQ_F4118.5.4, REQ_F4118.12.2, REQ_F4118.12.3, REQ_F4118.12.4 | SQL Server：被帳戶／類別引用的總帳科目不可刪除、改方向或再分子科目；解除引用後可維護 | 通過（FundSetupTests） |
| MD-FUND-004 | REQ_F4118.5.2, REQ_F4118.12.2 | SQL Server：資料表 CHECK、唯一索引、外鍵索引 | 通過（FundSetupTests） |
| MD-FUND-005 | REQ_F4118.5.1, REQ_F4118.12.1 | JSON 列舉以名稱傳遞 | 通過（FundSetupTests） |

WU-MD-020b 補三語頁面、前端測試與 Playwright。

## WU-MD-020b 收支類別與資金帳戶頁面

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-FUND-UI-001 | REQ_F4118.5.2, REQ_F4118.5.3, REQ_F4118.12.2, REQ_F4118.12.3 | Vitest：科目依資產／收入／費用與成本篩選，停用或非末級排除；Cash 銀行欄位清空；更新請求不送幣別、方向與代碼 | 通過（3 個 Vitest 案例） |
| MD-FUND-UI-002 | REQ_F4118.5.1, REQ_F4118.5.2, REQ_F4118.12.1, REQ_F4118.12.2 | Playwright：兩頁列表、類型和方向選擇、銀行欄位切換、科目選項與建立請求 | 通過 |
| MD-FUND-UI-003 | REQ_F4118.5.1, REQ_F4118.12.1 | Playwright：只有資金帳戶查看權限時不查無權限的科目／幣別 API、不顯示維護按鈕，收支類別路由 403 | 通過 |
| MD-FUND-UI-004 | REQ_F4118.5.1, REQ_F4118.5.3, REQ_F4118.5.4, REQ_F4118.12.1, REQ_F4118.12.3 | Playwright：類型／方向篩選、編輯時幣別／方向唯讀、Cash 清除銀行資料、刪除確認及版本參數 | 通過 |
| MD-FUND-UI-005 | REQ_F4118.5.3, REQ_F4118.5.4 | Playwright：銀行帳號無效時不送出；刪除已引用帳戶回 IN_USE 三語提示 | 通過 |

## WU-MD-021a 工序分類與工序後端

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-PROC-001 | REQ_F4118.15.1, REQ_F4118.15.2, REQ_F4118.15.3, REQ_F4118.15.4 | SQL Server：類別代碼正規化與永久唯一、排序號範圍、預設排序號排序與其他排序、版本、工序數、有工序不可刪、公司隔離 | 通過（ProcessTests） |
| MD-PROC-002 | REQ_F4118.7.1, REQ_F4118.7.2, REQ_F4118.7.3, REQ_F4118.7.4 | SQL Server：工價與排序號驗證、類別／單位必須啟用且同公司、未變更不重查、類別篩選與搜尋、單位被引用不可刪、IN_USE、軟刪除後代碼保留 | 通過（ProcessTests） |
| MD-PROC-003 | REQ_F4118.7.5 | SQL Server：`IProcessLookup` 含停用查找、RequireActive 404／422、啟用清單按類別與工序排序號、無類別排最後、類別篩選、公司隔離 | 通過（ProcessTests） |
| MD-PROC-004 | REQ_F4118.7.2, REQ_F4118.15.2 | SQL Server：CHECK、唯一索引、外鍵索引、`standard_rate` decimal(19,6) | 通過（ProcessTests） |

WU-MD-021b 補三語頁面、前端測試與 Playwright。

## WU-MD-021b 工序類別與工序頁面

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-PROC-UI-001 | REQ_F4118.7.1, REQ_F4118.7.2, REQ_F4118.7.3, REQ_F4118.15.2, REQ_F4118.15.3 | Vitest：工價格式、草稿正規化、類別篩選參數、更新不送代碼、錯誤碼對應 | 通過（4 個 Vitest 案例） |
| MD-PROC-UI-002 | REQ_F4118.15.1, REQ_F4118.15.2, REQ_F4118.15.4 | Vitest + Playwright：類別按排序號列出工序數、排序號超出範圍不送出、建立請求、刪除帶版本且 HAS_PROCESSES 三語提示 | 通過 |
| MD-PROC-UI-003 | REQ_F4118.7.1, REQ_F4118.7.2, REQ_F4118.7.3 | Vitest + Playwright：類別篩選、新增預設帶篩選類別、選項只列啟用並於編輯保留原停用值、工價驗證、編輯請求帶版本、422 錯誤保留對話框 | 通過 |
| MD-PROC-UI-004 | REQ_F4118.7.4 | Playwright：刪除已引用工序顯示 IN_USE 三語提示 | 通過 |
| MD-PROC-UI-005 | REQ_F4118.7.1, REQ_F4118.15.1 | Vitest + Playwright：只有工序查看權限時不讀類別／單位 API、不顯示維護按鈕，類別路由 403；列表錯誤顯示 | 通過 |
