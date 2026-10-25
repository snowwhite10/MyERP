# MD 測試計畫

| Test Case ID | Related REQ_F | Case | Expected |
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

## WU-MD-023 客戶審批與刪除

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-CUS-REV-001 | REQ_F4118.1.4, REQ_F4118.1.6 | 新客戶 Pending 不可交易；審批中不可刪除（從未送審可刪，ADR-048）；無步驟即核准；版本衝突、軟刪除後代碼不可重用 | 通過（CustomerReviewTests；審批中刪除 422 見 SupplierReviewTests.CustomerInOpenApprovalCannotBeEditedButApprovedCustomerCan） |
| MD-CUS-REV-002 | REQ_F4118.1.4, REQ_F4118.1.5, REQ_F4118.1.6 | SYS 最終結果回呼更新客戶；被單據引用時不可刪除 | 通過（CustomerReviewTests，以 SYS 回呼介面驗證；多步驟 SYS 流程端到端留待 WU-MD-022 真實 API E2E） |
| MD-CUS-REV-003 | REQ_F4118.1.4, REQ_F4118.1.6 | 前端版本送審／刪除、錯誤顯示、防重複送出與三語狀態；Playwright 頁面操作 | 通過（partner-list.spec、business-partner.service.spec、customer.spec） |
| MD-CUS-REV-004 | REQ_F4118.1.4 | 審批中的客戶修改回 422 `MD.CUSTOMER.APPROVAL_PENDING`；核准後可修改且不查 SYS（ADR-048）；前端已辨識的客戶審批錯誤碼與 `SYS.APPROVAL.ALREADY_PENDING` 三語都有翻譯 | 通過（SupplierReviewTests.CustomerInOpenApprovalCannotBeEditedButApprovedCustomerCan、business-partner.service.spec MD-PARTNER-I18N） |

## WU-MD-005 客戶匯入

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-IMP-001 | REQ_F4118.1.7 | xlsx／UTF-8 CSV 範本往返，BOM、引號、換行、共用字串、稀疏儲存格及實際列號 | 通過（005a，單元測試） |
| MD-IMP-002 | REQ_F4118.1.7 | 空檔、舊式 xls、Big5、損壞 xlsx、重複或缺少表頭、超過檔案／列數上限、損壞 CSV 引號 | 通過（005a，單元測試） |
| MD-IMP-003 | REQ_F4118.1.7 | SQL Server 整合：公司隔離、預覽無寫入、分類與子資料驗證、整批原子性、冪等略過、併發唯一約束 | 通過（005b，`CustomerImportTests`） |
| MD-IMP-004 | REQ_F4118.1.7 | API 權限、422 錯誤碼及範本下載；客戶／供應商／物料六個上傳端點都有 `RequestSizeLimit` 與 2 MB + 64 KB 的 `RequestFormLimits`（MD-B09） | 通過（005b，端點／範本整合及服務例外測試） |
| MD-IMP-005 | REQ_F4118.1.7 | Vitest：同一 File 預覽／提交、錯誤列禁止提交、換檔清空、遲到回應忽略、409／422 後重新預覽；Playwright：下載 CSV 範本、實際列號與欄位錯誤、修正後提交與列表刷新、只讀權限隱藏入口 | 通過（005c；Vitest 4、Playwright 2） |

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

## WU-MD-024 供應商審批與刪除

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-SUP-REV-001 | REQ_F4118.2.4, REQ_F4118.2.6 | 新供應商 Pending 不可交易（`NOT_APPROVED`）；審批中不可刪（`APPROVAL_PENDING`），從未送審或已撤回可刪、送審遞增版本令並發刪除 409（PendingSupplierCanBeDeletedUnlessInOpenApproval）；無步驟即核准、單據類型 `MD.SUPPLIER`；已核准重送 `ALREADY_APPROVED`；停用後不可交易；舊版本 409；軟刪除後列表不顯示、查找 404、代碼不可重用 | 通過（SupplierReviewTests） |
| MD-SUP-REV-002 | REQ_F4118.2.4, REQ_F4118.2.5, REQ_F4118.2.6 | SYS 最終核准回呼更新供應商、非待審再收結果 `APPROVAL_STATE_CHANGED`；多步驟送審保持 Pending、駁回後轉 Rejected 不可交易且可刪除；`ISupplierUsage` 回報引用時 `IN_USE` | 通過（SupplierReviewTests 3 個案例；客戶 CustomerReviewTests 改用共用基底後仍通過） |
| MD-SUP-REV-003 | REQ_F4118.2.4, REQ_F4118.2.5, REQ_F4118.2.6 | 前端供應商路徑送審／刪除帶版本、供應商審批錯誤碼三語、審核狀態欄、防重複送出；Playwright：待審也有刪除鈕、送審後審批中刪除顯示 `APPROVAL_PENDING`（022c，MD-B02）、駁回者被引用顯示錯誤後可刪除 | 通過（business-partner.service.spec、partner-list.spec、supplier.spec） |
| MD-SUP-REV-004 | REQ_F4118.2.4 | 從未送審的供應商可修改；審批中修改回 422 `MD.SUPPLIER.APPROVAL_PENDING` 且主檔不變；撤回後可修改（ADR-048） | 通過（SupplierReviewTests.SupplierInOpenApprovalCannotBeEditedUntilDecided） |

## WU-MD-025 往來對象預設付款條件與結算方式

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-PAY-DEF-001 | REQ_F4118.1.8, REQ_F4118.2.8 | SQL Server 整合：建立／修改帶預設值、停用值可保留但不可新選、舊多載保留原值、明確傳入完整取代；跨公司或 id ≤ 0 回 400（欄位名）；資料庫複合外鍵拒絕跨公司；`RequireApprovedAsync` 帶出代碼、不在使用方 DbContext 留下追蹤實體、不存在 404（MD-B07）；被客戶或供應商設為預設的付款條件／結算方式刪除 `IN_USE`，清除或刪除往來對象後可刪；四個外鍵存在 | 通過（PartnerPaymentDefaultsTests 4 個案例；既有客戶／供應商／付款設定測試加入 `PaymentSetupDbModel` 後仍通過） |
| MD-PAY-DEF-002 | REQ_F4118.1.8, REQ_F4118.2.8 | 前端：依 VIEW 權限載入選項、列表顯示預設付款條件名稱、建立與修改送出兩個 id、編輯時現有停用值可選、選項載入失敗清空；Playwright 客戶頁選擇、清除並保留停用現值 | 通過（partner-list.spec 2 個案例；customer.spec 1 個案例） |

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
| MD-ITEM-003 | REQ_F4118.3.2, REQ_F4118.3.3 | SQL Server 整合（MD-B03）：交易內建立物料後，另一連線取不到單位／品牌排他鎖但可取共享鎖；品牌刪除持有排他鎖時其他連線取不到共享鎖，不影響單位鎖 | 通過（ItemTests.ReferenceWritesHoldSharedLocksThatExcludeDeletesOnly） |
| MD-ITEM-UI-001 | REQ_F4118.3.1, REQ_F4118.3.2, REQ_F4118.3.3 | Vitest：API 查詢與寫入負載、列表與參照顯示、表單驗證、版本修改；Playwright：搜尋、新增、編輯、公司切換與只讀權限 | 通過（Vitest 3 個、Playwright 2 個案例；前端全量 466/466、E2E 83 通過 3 略過） |

## WU-INV-008a 物料批號／序號追蹤方式

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-ITEM-TRK-001 | REQ_F4118.3.7 | SQL Server 整合：建立預設 None、可建 Lot／Serial，資料庫存字串；未定義列舉值 400 `trackingMode` 且不建立 | 通過（ItemTrackingModeTests） |
| MD-ITEM-TRK-002 | REQ_F4118.3.3, REQ_F4118.3.7 | SQL Server 整合：省略或相同追蹤方式不詢問使用方；未使用可改；`IItemUsage` 使用中改變回 409 `MD.ITEM.TRACKING_MODE_LOCKED` 且名稱、版本不變；使用中仍可改名稱與停用；未定義值 400 | 通過（ItemTrackingModeTests） |
| MD-ITEM-TRK-003 | REQ_F4118.3.7 | SQL Server 整合：交易內改追蹤方式時另一連線取不到 `md_item` 共享鎖，提交後可取得（與 INV 引用端互斥） | 通過（ItemTrackingModeTests） |
| MD-ITEM-TRK-004 | REQ_F4118.3.7 | SQL Server 整合：`IInventoryReferenceGuard.RunWithCodesAsync` 回傳物料追蹤方式 | 通過（ItemTrackingModeTests） |
| MD-ITEM-TRK-005 | REQ_F4118.3.7 | Vitest：列表顯示三語追蹤方式、下拉三個選項且預設 None、建立與修改負載帶 `trackingMode`、409 `TRACKING_MODE_LOCKED` 保留對話框並顯示三語錯誤；服務負載測試 | 通過（item-page.spec、item.service.spec） |
| MD-ITEM-TRK-006 | REQ_F4118.3.7 | Playwright（模擬 API）：列表顯示「批號」、新增預設只按數量並可選「序號」，送出 `trackingMode: Serial` | 通過（item.spec.ts） |

## WU-MD-028d 基準單位換算證據 reader（WU-MD-028a 待實作）

All cases trace to REQ_F4118.3.8 and BR-MD-034–040. `U` = unit, `I` = real SQL integration; `E` is not applicable because this Application-only reader has no UI or HTTP endpoint. WU-MD-028a writes failing tests before implementation; all below remain TODO in this design unit.

| Test Case ID | Category | Scenario and assertion | Level | Status |
|---|---|---|---|---|
| MD-CONV-EV-001 | Normal | Item-specific factor takes priority over company path; receipt names base UOM, item edge ID/version/factor and exact six-place result. | I | TODO |
| MD-CONV-EV-002 | Normal | Company path with forward and reverse edges saves their order, direction, versions and rational result; only final result is rounded. | I | TODO |
| MD-CONV-EV-003 | Normal | Source equals base signs a zero-edge receipt with item and base UOM versions; zero quantity also signs a receipt rather than reporting missing data. | I | TODO |
| MD-CONV-EV-004 | Boundary | Positive and negative half-unit cases round once away from zero at six places, independently of the UOM display precision; decimal inputs at six places survive exactly. | U,I | TODO |
| MD-CONV-EV-005 | Boundary | Empty/invalid operation key, zero or invalid IDs, unsupported policy and quantity with over six decimal places fail by named field/reason, without receipt. | U,I | TODO |
| MD-CONV-EV-006 | Boundary | Largest representable `decimal(19,6)` result succeeds; larger result, reciprocal overflow and intermediate product overflow return `Overflow` without partial evidence. | U,I | TODO |
| MD-CONV-EV-007 | Conflict | Identical operation key/payload replays byte-identical ID/digest; same key with changed item, UOM, quantity or policy conflicts. | I | TODO |
| MD-CONV-EV-008 | Conflict | Two SQL connections issue the same key simultaneously: one immutable receipt, identical replies; an edit racing issuance yields one wholly committed path version. | I | TODO |
| MD-CONV-EV-009 | Conflict | Existing soft-deleted conversion or unit code is never mistaken for a newer active path; multiple equal-priority paths return `AmbiguousPath`. | I | TODO |
| MD-CONV-EV-010 | Partial result | Batch issue is not in this reader: partial success, all skipped and all failed are not applicable. SCM aggregates per-row reasons; an unavailable row must never become quantity zero. | — | N/A: single receipt |
| MD-CONV-EV-011 | Permission | Anonymous/unauthorized caller, missing company, other-company item or evidence, and revoked scope expose no receipt existence, count or digest. | I | TODO |
| MD-CONV-EV-012 | Permission | Replay rechecks current authorization even for a valid evidence ID; a formerly allowed caller loses access immediately after revocation. | I | TODO |
| MD-CONV-EV-013 | State | New issue rejects inactive/soft-deleted item, UOM or path with a named reason; authorized historical replay stays identical after later deactivation or deletion. | I | TODO |
| MD-CONV-EV-014 | State | Factor correction and permitted base-UOM change issue new evidence for a new key; replay of old ID keeps old base/path/result. | I | TODO |
| MD-CONV-EV-015 | Relation | Existing `BASE_UOM_LOCKED` and `UOM.IN_USE` guards continue to block referenced changes; evidence history does not mutate transactional stock or documents. | I | TODO |
| MD-CONV-EV-016 | Replay | At the 90-day boundary evidence remains replayable; after expiry or missing immutable payload returns `EvidenceExpired`/`ReplayUnavailable`, never current recomputation. | I | TODO |
| MD-CONV-EV-017 | Error | No path, invalid path, precision loss and overflow return distinct reason codes; a failed issue leaves no evidence row/edge. | U,I | TODO |
| MD-CONV-EV-018 | Error | SQL failure or transaction rollback leaves no half-written receipt; retry by permanent key can succeed once. | I | TODO |
| MD-CONV-EV-019 | Error | Timeout after commit is an unknown outcome: query by operation key retrieves saved receipt before any new issue or success claim. | I | TODO |
| MD-CONV-EV-020 | Error | HTTP 400/409/422/500 and frontend messaging are not applicable to this internal reader; later public endpoint/SCM UI units must test their own boundaries. | — | N/A: no HTTP/UI |
| MD-CONV-EV-021 | File | No file input/output: empty, malformed, oversized and invalid encoding files are not applicable. | — | N/A: no file |
| MD-CONV-EV-022 | Compatibility/audit | Existing `IUomConverter` and HTTP calculator keep their target-UOM rounding; issue/replay audit captures actor, company, operation key, evidence ID and outcome without exposing hidden values. | I | TODO |

Trace: REQ_F4118.3.8 → BR-MD-034–040 → MD-CONV-EV-001–022 → WU-MD-028a. SCM's saved manifest tests remain SCM-OWN-006–007, 012, 014 and 016. No product test is marked passed here.

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
| MD-TAX-002 | REQ_F4118.38.5, REQ_F4118.38.6 | SQL Server 整合：按業務日與方向查詢、代碼不分大小寫、方向不符／缺稅率錯誤碼、TWD 金額精度下含稅試算；幣別不在主檔 404 `MD.CURRENCY.NOT_FOUND`，停用的 3 位小數幣別按主檔精度試算（MD-B04） | 通過（TaxRateTests 1 個案例） |
| MD-TAX-003 | REQ_F4118.37.3, REQ_F4118.38.2, REQ_F4118.38.3, REQ_F4118.38.5 | SQL Server 整合：修改時重疊與舊版本衝突；稅別停用後不可新增稅率；契約查詢與試算停用稅別 422 `MD.TAX_CODE.INACTIVE`，`includeInactive` 可查歷史稅率（MD-B04，ADR-049）；類型改成 Both 後可查兩個方向 | 通過（TaxRateTests 1 個案例） |
| MD-ITEM-DEL-001 | REQ_F4118.3.4 | SQL Server 整合（ADR-050）：別公司／不存在 404、舊版本與版本 0 回 409；刪除後列表不見、`is_deleted`／`is_active` 正確；專屬換算軟刪除、圖片與屬性值刪除；之後修改、再刪、子資料 API 均 404；同代碼建立 409 `DUPLICATE_CODE`；屬性定義可刪 | 通過（ItemDeleteTests） |
| MD-ITEM-DEL-002 | REQ_F4118.3.4 | SQL Server 整合（ADR-049／050）：`IItemUsage` 使用中 409 `MD.ITEM.IN_USE`，以代碼詢問，換算與物料不變；未使用時可刪 | 通過（ItemDeleteTests） |
| MD-ITEM-DEL-003 | REQ_F4118.3.4 | SQL Server 整合：停用且無子資料的物料可刪，刪除後再刪 404 | 通過（ItemDeleteTests） |
| MD-ITEM-DEL-004 | REQ_F4118.3.4 | SQL Server 整合：交易內刪除持有 `md_item`、屬性、圖片、換算排他鎖（另一連線取不到共享鎖）；回滾後物料仍在 | 通過（ItemDeleteTests） |
| MD-ITEM-DEL-005 | REQ_F4118.3.4 | 控制器：`DELETE` 需 `MD.ITEM.DELETE`（已宣告於權限目錄），回 204 | 通過（ItemDeleteTests） |
| MD-ITEM-DEL-UI | REQ_F4118.3.4 | Vitest：確認對話框帶代碼、帶版本刪除一次並重新查詢、取消不送出、IN_USE 不重查、404／版本衝突重查、無權限不顯示；`ItemService.remove`。Playwright：IN_USE 三語訊息、成功刪除後列表移除、只讀權限無刪除鈕 | 通過（item-page.spec、item.service.spec、e2e item.spec） |
| MD-USAGE-001 | REQ_F4118.3.3 | SQL Server 整合（ADR-049）：`IItemUsage` 回報使用中時改基本單位 409 `MD.ITEM.BASE_UOM_LOCKED`，以物料代碼詢問；基本單位不變不詢問；未使用時可改 | 通過（MasterDataUsageTests 1 個案例） |
| MD-USAGE-002 | REQ_F4118.4.4 | SQL Server 整合：`IWarehouseUsage` 回報使用中時刪除倉庫 409 `MD.WAREHOUSE.IN_USE`，以倉庫代碼詢問；未使用時可刪 | 通過（MasterDataUsageTests 1 個案例） |
| MD-USAGE-003 | REQ_F4118.4.5 | SQL Server 整合：`IStorageLocationUsage` 回報使用中時刪除儲位 409 `MD.STORAGE_LOCATION.IN_USE`，以倉庫代碼＋儲位代碼詢問；未使用時可刪 | 通過（MasterDataUsageTests 1 個案例） |
| MD-USAGE-004 | REQ_F4118.37.4, REQ_F4118.38.3, REQ_F4118.38.4 | SQL Server 整合：`ITaxCodeUsage` 以單據日 2026-03-15 模擬：改百分比檢查整個原期間 409 `MD.TAX_RATE.IN_USE`；只延長不詢問；縮短到單據日之後只詢問不再涵蓋的尾段且可改；起始日移過單據日詢問頭段 409；刪除涵蓋單據日的稅率 409；不涵蓋的稅率可改可刪；刪除稅別詢問全期間 409 `MD.TAX_CODE.IN_USE` | 通過（MasterDataUsageTests 1 個案例） |
| MD-TAX-004 | REQ_F4118.37.1, REQ_F4118.38.1 | SQL Server 整合：同公司複合外鍵、稅率生效日篩選唯一索引、類型／期間／百分比資料庫 CHECK | 通過（TaxRateTests 1 個案例） |
| MD-TAX-005 | REQ_F4118.38.5, REQ_F4118.38.6 | 單元：`TaxDirection` 在 JSON 以名稱 `Output`／`Input` 傳遞（前端試算送名稱） | 通過（TaxRateTests 1 個案例） |
| MD-TAX-UI-001 | REQ_F4118.37.1, REQ_F4118.37.2, REQ_F4118.37.3, REQ_F4118.37.4, REQ_F4118.38.1, REQ_F4118.38.2, REQ_F4118.38.3, REQ_F4118.38.4, REQ_F4118.38.6 | Vitest：API 路徑、代碼大寫與去空白、`on` 篩選、版本參數、試算請求正規化與錯誤碼；稅別頁類型與今日稅率（無稅率標示）、代碼格式驗證、DUPLICATE_CODE、帶版本修改類型與停用、刪除確認與 IN_USE、開啟稅率與試算、無權限隱藏維護但保留稅率與試算；稅率對話框 0–100 四位小數規則、按日篩選、期間驗證、OVERLAP、帶版本修改與刪除並通知列表重新載入、停用稅別隱藏新增、只讀隱藏維護；試算對話框金額規則、方向依類型限制、日期預設今日、驗證、結果顯示、輸入改變清除結果、NOT_FOUND | 通過（Vitest 13 個案例） |
| MD-TAX-UI-002 | REQ_F4118.37.1, REQ_F4118.37.2, REQ_F4118.37.3, REQ_F4118.37.4, REQ_F4118.38.1, REQ_F4118.38.2, REQ_F4118.38.3, REQ_F4118.38.4, REQ_F4118.38.6 | Playwright：類型與今日稅率、搜尋、代碼提示、新增銷項稅別、修改停用、IN_USE 三語訊息、切換公司；稅率重疊訊息、百分比驗證、新增持續有效並重新載入列表、按日篩選與顯示全部、修改、確認後帶版本刪除、停用稅別不可新增；含稅試算結果與方向無稅率訊息；只讀權限 | 通過（3 個 Playwright 案例） |

## WU-MD-020a 收支類別與資金帳戶後端

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-FUND-001 | REQ_F4118.12.1, REQ_F4118.12.2, REQ_F4118.12.3, REQ_F4118.12.4, REQ_F4118.12.5 | SQL Server：收入／支出方向、對應科目類別與可過帳驗證、公司隔離、停用與查詢契約（代碼 null 回 null／404，MD-B10）、版本和引用刪除保護 | 通過（FundSetupTests） |
| MD-FUND-002 | REQ_F4118.5.1, REQ_F4118.5.2, REQ_F4118.5.3, REQ_F4118.5.4, REQ_F4118.5.5 | SQL Server：資金帳戶幣別固定、Cash 銀行欄位清空、Bank 欄位驗證、科目限制、公司隔離、查詢契約（代碼 null 回 null／404，MD-B10）、引用刪除保護 | 通過（FundSetupTests） |
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
| MD-PROC-003 | REQ_F4118.7.5 | SQL Server：`IProcessLookup` 含停用查找、RequireActive 404／422、代碼 null 回 null／404（MD-B10）、啟用清單按類別與工序排序號、無類別排最後、類別篩選、公司隔離 | 通過（ProcessTests） |
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

## WU-MD-004a 客戶聯絡人與地址後端

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-CONT-001 | REQ_F4118.41.1, REQ_F4118.41.2, REQ_F4118.41.3, REQ_F4118.41.4 | SQL Server：聯絡人正規化、新預設取代原預設（別客戶不受影響）、列表預設排第一、被取消預設者版本遞增、停用不可為預設、刪除取消預設、子資料須屬路徑客戶、別公司 404、停用客戶仍可維護、未選公司 403 | 通過（CustomerContactTests） |
| MD-CONT-002 | REQ_F4118.41.2 | 聯絡人欄位邊界：姓名必填與 100 字、職稱／手機／電話／通訊帳號／備註上限、電郵格式（顯示名稱、多地址、超長）、上限內可保存、同名可保存 | 通過（CustomerContactTests） |
| MD-CONT-003 | REQ_F4118.42.1, REQ_F4118.42.2, REQ_F4118.42.3, REQ_F4118.42.4 | 地址：正規化、預設取代、已是預設時重存不影響他筆、舊版本 409、停用不可為預設、軟刪除、404、欄位邊界 | 通過（CustomerContactTests） |
| MD-CONT-004 | REQ_F4118.41.2, REQ_F4118.42.2 | SQL Server：複合外鍵名稱、篩選唯一索引定義、CHECK；直接寫入兩筆預設、停用預設、跨公司客戶均被資料庫拒絕 | 通過（CustomerContactTests） |

## WU-MD-004b 客戶聯絡人與地址頁面

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-CONT-UI-001 | REQ_F4118.41.1, REQ_F4118.41.2, REQ_F4118.41.3, REQ_F4118.41.4, REQ_F4118.42.1, REQ_F4118.42.2, REQ_F4118.42.3, REQ_F4118.42.4 | Vitest：API 路徑、去空白與空白轉 null、版本參數、子資料錯誤碼；對話框兩段列表、姓名／電郵／地址驗證、新增後重新讀取（預設排序以伺服器為準）、帶版本修改、停用不可預設、409 保留編輯、確認後刪除並重設編輯、取消確認不刪除、列表錯誤、只讀隱藏表單、關閉清空；客戶列「聯絡人」開啟對話框、供應商不顯示 | 通過（Vitest） |
| MD-CONT-UI-002 | REQ_F4118.41.1, REQ_F4118.41.2, REQ_F4118.41.3, REQ_F4118.41.4, REQ_F4118.42.1, REQ_F4118.42.2, REQ_F4118.42.4 | Playwright：開啟對話框、新增聯絡人並設預設（原預設取消）、電郵驗證、修改帶版本、NOT_FOUND 三語訊息、刪除、新增地址；只讀權限只可查看 | 通過（2 個 Playwright 案例） |

## WU-MD-008 供應商聯絡人

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-SUP-CONT-001 | REQ_F4118.43.1, REQ_F4118.43.2, REQ_F4118.43.3, REQ_F4118.43.4 | SQL Server：正規化、新預設取代原預設（別供應商不受影響）、列表預設排第一、被取消預設者舊版本 409、停用不可為預設、欄位驗證、刪除取消預設、子資料須屬路徑供應商、別公司 404 `MD.SUPPLIER.NOT_FOUND`、停用供應商仍可維護、未選公司 403 | 通過（SupplierContactTests） |
| MD-SUP-CONT-002 | REQ_F4118.43.2 | SQL Server：複合外鍵名稱、篩選唯一索引定義、CHECK；直接寫入兩筆預設、停用預設、跨公司供應商均被資料庫拒絕 | 通過（SupplierContactTests） |
| MD-SUP-CONT-UI-001 | REQ_F4118.43.1, REQ_F4118.43.2, REQ_F4118.43.3, REQ_F4118.43.4 | Vitest：供應商 API 路徑與錯誤碼；對話框只有聯絡人段、不讀地址、用 `MD.SUPPLIER.EDIT` 判斷維護（只有客戶 EDIT 時唯讀）、帶版本修改、404 取消編輯並重讀、確認後刪除；供應商列「聯絡人」開啟對話框 | 通過（Vitest） |
| MD-SUP-CONT-UI-002 | REQ_F4118.43.1, REQ_F4118.43.2, REQ_F4118.43.3, REQ_F4118.43.4 | Playwright：開啟對話框（無地址段、不請求地址）、新增預設聯絡人（原預設取消）、停用、NOT_FOUND 三語訊息、刪除帶版本；只讀權限只可查看 | 通過（2 個 Playwright 案例） |

## WU-MD-009 供應商匯入

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-SUP-IMP-001 | REQ_F4118.2.7 | SQL Server：預覽無寫入、小寫代碼／分類代碼對應、既有代碼略過不更新、建立預設聯絡人（無聯絡欄位則不建）、重複提交冪等、公司隔離；找不到或停用分類、檔內重複、只有手機沒有姓名、電郵格式錯誤均為欄位錯誤且整批 422；地址等未知表頭忽略；兩個連線併發只新增一筆 | 通過（SupplierImportTests 4 個案例；客戶 CustomerImportTests 4 個案例改用共用基底後仍通過） |
| MD-SUP-IMP-002 | REQ_F4118.2.7 | API：三個端點皆需 `MD.SUPPLIER.IMPORT`、權限目錄含此代碼、範本沒有地址欄且檔名 `supplier-import.*`、xls 格式 400、超過 2 MB 422；Vitest：供應商路徑、標題、data-testid、`MD.SUPPLIER_IMPORT.HAS_ERRORS` 錯誤後不可提交、範本下載 | 通過（整合 1 個、Vitest 2 個） |
| MD-SUP-IMP-003 | REQ_F4118.2.7 | Playwright：下載 CSV 範本、預覽實際列號與 `contactName` 錯誤、修正後提交與列表刷新；只有 VIEW 權限不顯示入口 | 通過（2 個 Playwright 案例） |

## WU-MD-013a 物料圖片後端

| Test Case ID | Related REQ_F | Case | Result |
|---|---|---|---|
| MD-IMG-001 | REQ_F4118.44.1, REQ_F4118.44.2, REQ_F4118.44.3, REQ_F4118.44.4 | SQL Server：第一張自動主圖、排序號預設最大 + 1、檔名去路徑或依類型命名；上傳時指定主圖取消原主圖並遞增版本；列表按主圖、排序號、id；內容與版本查詢；修改排序與主圖、不可直接取消主圖、舊版本 409；刪除主圖由排序號最小者接任、刪除後 404 | 通過（ItemImageTests） |
| MD-IMG-002 | REQ_F4118.44.1, REQ_F4118.44.2, REQ_F4118.44.3 | 空檔 400 `file`；GIF／SVG 422 `UNSUPPORTED_TYPE`；超過 2 MB 422 `TOO_LARGE`；15 與 4097 像素 422 `INVALID_DIMENSIONS`；排序號 -1／10000 400；剛好 2 MB、16／4096 像素可接受；第 11 張 422 `LIMIT_REACHED`；停用物料仍可上傳；圖片不屬路徑上的物料 404；別公司 404 `MD.ITEM.NOT_FOUND`；未選公司 403 | 通過（ItemImageTests） |
| MD-IMG-003 | REQ_F4118.44.1, REQ_F4118.44.2, REQ_F4118.44.3, REQ_F4118.44.4 | API：查看與內容需 `MD.ITEM.VIEW`，上傳／修改／刪除需 `MD.ITEM.EDIT`；沒有檔案 400、宣告大小超過 2 MB 先拒絕；上傳回 201；內容 inline 並帶版本 ETag，`If-None-Match` 相符回 304；修改與刪除後主圖接任 | 通過（ItemImageTests） |
| MD-IMG-004 | REQ_F4118.44.2 | 兩個連線同時上傳第一張圖片，只有一張主圖、排序號 1 與 2；篩選唯一索引、排序索引、複合外鍵與兩個 CHECK 存在 | 通過（ItemImageTests） |
| MD-IMG-005 | REQ_F4118.44.4 | 新的 DbContext 修改排序與主圖、刪除主圖：不載入圖片內容（追蹤實體內容為 null）、版本遞增、主圖接任、兩張圖片內容不變、過期版本 409（MD-B08） | 通過（ItemImageTests） |

SYS 公司圖片的內容檢查移到 `MyERP.Shared.Infrastructure.Imaging.ImageInspector` 後，`CompanyImageTests`（13 個案例）仍全部通過。

## WU-MD-013b 物料屬性後端

| Test Case ID | Related REQ_F | Case | Result |
|---|---|---|---|
| MD-ATTR-001 | REQ_F4118.45.1, REQ_F4118.45.2, REQ_F4118.45.3, REQ_F4118.45.4 | SQL Server：代碼轉大寫與重複 409（含已刪除）；排序號 -1／10000 400；預設按排序號再代碼、倒序排序號、按名稱、關鍵字；修改名稱／排序／停用、舊版本 409；使用物料數；被使用時刪除 409 `IN_USE`、停用可行；移除使用後可刪除；別公司看不到也改不到、未選公司 403 | 通過（ItemAttributeTests） |
| MD-ATTR-002 | REQ_F4118.45.5, REQ_F4118.45.6 | 值去空白、列表按屬性排序號與代碼；整批取代修改／刪除／新增，未變更的值版本不變；停用屬性原有值可保留與修改，但不可新設定；空集合清除全部；停用物料仍可維護 | 通過（ItemAttributeTests） |
| MD-ATTR-003 | REQ_F4118.45.6 | 缺少集合、重複屬性、屬性 id 0、不存在、已刪除、別公司屬性、空白或超過 200 字的值均 400 並帶索引鍵；驗證失敗不寫入；剛好 50 個與 200 字可接受、51 個 400；別公司 404 `MD.ITEM.NOT_FOUND`、未選公司 403 | 通過（ItemAttributeTests） |
| MD-ATTR-004 | REQ_F4118.45.1, REQ_F4118.45.2, REQ_F4118.45.3, REQ_F4118.45.4, REQ_F4118.45.5, REQ_F4118.45.6 | API：屬性定義四個端點各需 `MD.ITEM_ATTRIBUTE.*`、權限目錄含四個代碼，物料屬性查看需 `MD.ITEM.VIEW`、取代需 `MD.ITEM.EDIT`；建立 201 帶使用數 0，取代後列表與修改回傳使用數 1、刪除 409；兩個連線同時取代仍每屬性一個值；唯一索引、屬性索引、兩個複合外鍵與 CHECK 存在 | 通過（ItemAttributeTests） |

## WU-MD-013c 物料圖片與屬性頁面

| Test Case ID | Related REQ_F | Case | Result |
|---|---|---|---|
| MD-IMG-UI-001 | REQ_F4118.44.1 | Vitest + Playwright：圖片以 Blob 下載後轉 data: URL 預覽、主圖標籤、大小與寬高；預覽失敗顯示「無法預覽」；列表錯誤三語提示；只有 `MD.ITEM.VIEW` 時不顯示上傳、設主圖、刪除與排序輸入 | 通過 |
| MD-IMG-UI-002 | REQ_F4118.44.2 | Vitest + Playwright：multipart 送 `file` 與 `isPrimary`、不送排序號；GIF、空檔、超過 2 MB、已有 10 張時不送出；422 `INVALID_DIMENSIONS` 三語提示；上傳後只下載新圖片預覽；切換物料後忽略舊上傳回應 | 通過 |
| MD-IMG-UI-003 | REQ_F4118.44.3 | Vitest + Playwright：排序號 0–9999 整數驗證並還原輸入、未變更不送出；改排序與設主圖帶版本；409 衝突顯示錯誤並重新載入最新版本 | 通過 |
| MD-IMG-UI-004 | REQ_F4118.44.4 | Vitest + Playwright：確認後帶版本刪除、取消不送出、刪除後列表與預覽更新 | 通過 |
| MD-ATTR-UI-001 | REQ_F4118.45.1, REQ_F4118.45.2, REQ_F4118.45.3, REQ_F4118.45.4 | Vitest + Playwright：`/md/item-attributes`（選單）按排序號列出物料數，代碼轉大寫建立、DUPLICATE_CODE 保留對話框、帶版本停用，刪除帶版本且 IN_USE 三語提示；沒有 `MD.ITEM_ATTRIBUTE.VIEW` 時路由 403 | 通過 |
| MD-ATTR-UI-002 | REQ_F4118.45.5 | Vitest + Playwright：沒有 `MD.ITEM.EDIT` 時唯讀列出屬性與值（停用屬性加標籤），不讀屬性定義 | 通過 |
| MD-ATTR-UI-003 | REQ_F4118.45.6 | Vitest + Playwright：新增列只列啟用屬性加已有值的停用屬性，並排除其他列已選；必填與 200 字驗證；整批 PUT 去空白；後端 `attributes[i].attributeId`／`value` 錯誤標在對應列，修改後清除；最多 50 列；沒有屬性定義權限時只能改現有屬性；切換物料後忽略舊儲存回應 | 通過 |

## WU-MD-014a 物料匯入後端

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-ITEM-IMPORT-001 | REQ_F4118.3.6 | SQL Server 整合：預覽只讀、實際列號、主檔及參照建立、既有代碼略過、重複提交冪等、公司隔離 | 通過（ItemImportTests） |
| MD-ITEM-IMPORT-002 | REQ_F4118.3.6 | SQL Server 整合：缺失／停用參照、檔內重複及缺名回欄位錯誤；任一錯誤整批拒絕；預覽後參照停用時提交重驗證 | 通過（ItemImportTests） |
| MD-ITEM-IMPORT-003 | REQ_F4118.3.6 | API 範本 xlsx／CSV、三個端點權限、提交；並行提交同碼只建立一次；缺少必需表頭、規格過長 | 通過（ItemImportTests） |

## WU-MD-014b 物料匯入介面

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-ITEM-IMPORT-UI-001 | REQ_F4118.3.6 | Vitest：物料端點、實際列號與參照欄位錯誤；錯誤時停用提交；409 後必須重新預覽才能提交 | 通過（partner-import-dialog.spec.ts） |
| MD-ITEM-IMPORT-UI-002 | REQ_F4118.3.6 | Playwright：下載範本、預覽錯誤與略過列、提交後刷新清單；沒有 IMPORT 權限時隱藏入口 | 通過（item-import.spec.ts） |

## WU-MD-022c 前端審查修正

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-CUS-REV-005 | REQ_F4118.1.6, REQ_F4118.2.6 | Vitest：待審客戶也顯示刪除鈕，審批中刪除 422 `APPROVAL_PENDING` 顯示訊息；Playwright：供應商送審後刪除顯示「仍在審批中」且列保留（MD-B02） | 通過（partner-list.spec、supplier.spec） |
| MD-FE-REV-001 | REQ_F4118.1.1, REQ_F4118.10.1 | MD-F02／F03：`fetchAllPages` 依序載入至 totalCount、空頁停止、錯誤傳出；往來對象付款條件跨頁載入；單位頁選項跨頁載入且翻頁不重載 | 通過（fetch-all-pages.spec、partner-list.spec、uom-page.spec） |
| MD-FE-REV-002 | REQ_F4118.1.1, REQ_F4118.3.1, REQ_F4118.4.1 | MD-F05／F06：404 或 409 版本衝突時重新查詢（IN_USE、審批中不算）；儲存衝突保留對話框並刷新列表版本；刪除進行中停用刪除鈕、忽略再次確認 | 通過（stale-row.spec、partner-list.spec、category-list.spec、item-page.spec、uom-page.spec、warehouse-page.spec） |
| MD-FE-REV-003 | REQ_F4118.41.1 | MD-F07：聯絡人保存期間切換往來對象，舊回應（成功或 409）不重新查詢舊對象、不顯示錯誤 | 通過（partner-contact-dialog.spec） |
| MD-FE-REV-004 | REQ_F4118.1.7, REQ_F4118.3.6 | MD-F08／F09：匯入列錯誤欄位名三語、未知欄位原樣；副檔名不符或超過 2 MB 不上傳，剛好 2 MB 的大寫副檔名可上傳；Playwright 客戶／供應商／物料匯入顯示中文欄位名 | 通過（partner-import-dialog.spec、*-import.spec） |
| MD-FE-REV-005 | REQ_F4118.38.6, REQ_F4118.3.1 | MD-F12：試算回傳 `decimalPlaces`（TWD 0、USD 3），前端千分位並補零；MD-F13：停用的單位／品牌／分類只有物料目前值可選；MD-F10：到期日以三語範本顯示 | 通過（TaxRateTests、tax-calculator-dialog.spec、item-page.spec、i18n-keys.spec） |

## WU-MD-022d 真實 API E2E

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| MD-REAL-001 | REQ_F4118.1.4, REQ_F4118.1.6, REQ_F4118.1.8 | 客戶經 SYS 兩級審批；審批中修改／刪除回 422，資料不變；核准後可修改；預設付款條件與結算方式可保存，被引用時不可刪除 | 通過（real-api-partner-approval.spec.ts，真實 SQL Server／Redis／API） |
| MD-REAL-002 | REQ_F4118.2.4, REQ_F4118.2.6, REQ_F4118.2.8 | 供應商同上，主管與財務分別核准兩級；完成後保留核准狀態及付款預設值 | 通過（real-api-partner-approval.spec.ts，真實 SQL Server／Redis／API） |

## WU-MD-027a 分類列表共用重構

| Test Case ID | Related REQ_F | 情境與測試層級 | Status |
|---|---|---|---|
| MD-LIST-001 | REQ_F4118.8.1、REQ_F4118.9.1、REQ_F4118.11.1 | 正常／邊界（U、E）：搜尋有可讀標籤；空關鍵字保留全列表，已套用關鍵字而無結果用不同三語訊息；錯誤重試保留查詢。 | 通過（category-list.spec、md-category-qa.spec） |
| MD-LIST-002 | REQ_F4118.8.1、REQ_F4118.9.1、REQ_F4118.11.1 | 排序／分頁（U）：只接受 `code`、`name`，未變狀態不重查；既有 `table-query` 共用測試與三路由 E2E 確認。 | 通過（shared-ui.spec、customer-category／supplier-category／item-brand.spec） |
| MD-LIST-003 | REQ_F4118.8.1、REQ_F4118.9.1、REQ_F4118.11.1 | 權限／跨公司（E）：無 CREATE 不顯示新增，無 VIEW 路由拒絕；切公司只列該公司資料。 | 通過（既有三路由 E2E） |
| MD-LIST-004 | REQ_F4118.8.1、REQ_F4118.9.1、REQ_F4118.11.1 | 錯誤／狀態／關聯（U、E）：400 顯示錯誤及重試；列表與新增 500 顯示追蹤碼，新增失敗保留輸入且列表無假資料；寫入 409、422、使用中與刪除沿用既有分類測試。逾時共用網絡錯誤提示待 027b 擴至各頁驗證。 | 通過（category-list.spec、md-category-qa.spec、既有分類 E2E）；逾時待 027b |
| MD-LIST-005 | REQ_F4118.8.1、REQ_F4118.9.1、REQ_F4118.11.1 | 重複代碼／軟刪除／部分結果／檔案（I）：純列表重構沒有寫入或批次、檔案流程；既有分類 CRUD／匯入測試不受影響。 | 不適用於此重構 |

## WU-MD-027b1 客戶／供應商列表共用重構

| Test Case ID | Related REQ_F | 情境與測試層級 | Status |
|---|---|---|---|
| MD-LIST-027b1-01 | REQ_F4118.1.1、REQ_F4118.2.1 | 正常（U、E）：兩路由顯示正確標題、資料、權限按鈕與三語文字；`erp-list-page` 保留舊 testId，搜尋框有可讀標籤。 | 通過（partner-list.spec、customer.spec、supplier.spec、md-partner-list-qa.spec） |
| MD-LIST-027b1-02 | REQ_F4118.1.1、REQ_F4118.2.1 | 邊界（U、E）：空關鍵字為全列表；僅提交後套用 trim 過的關鍵字，重查／翻頁不使用正在輸入的文字；空列表與搜尋無結果訊息不同。最大長度／最大筆數沿用既有列表 API，日期及小數不適用。 | 通過（partner-list.spec、md-partner-list-qa.spec） |
| MD-LIST-027b1-03 | REQ_F4118.1.1、REQ_F4118.2.1 | 排序及分頁（U）：僅 `code`、`name` 進入查詢，未變狀態不重查；保留 API 分頁參數。 | 通過（table-query 既有測試、partner-list.spec） |
| MD-LIST-027b1-04 | REQ_F4118.1.1、REQ_F4118.2.1 | 權限／跨公司（E）：無 VIEW 路由拒絕、唯讀不顯示新增及編輯；切公司只見該公司資料。 | 通過（customer.spec、supplier.spec 既有案例） |
| MD-LIST-027b1-05 | REQ_F4118.1.1、REQ_F4118.2.1 | 錯誤（U、E）：列表 500 顯示追蹤碼與重試，失敗時不呈現「沒有資料」；重試沿用已提交搜尋條件；操作 409／422 保留個別錯誤，不誤顯列表重試。400、逾時與網絡錯誤由共用錯誤翻譯處理，後續頁面回歸仍需覆蓋。 | 通過（partner-list.spec、md-partner-list-qa.spec、supplier.spec） |
| MD-LIST-027b1-06 | REQ_F4118.1.1、REQ_F4118.2.1 | 狀態／關聯／重複及衝突（U、E、I）：Pending／Approved、引用中、軟刪除、409 版本衝突沿用既有 CRUD 及審批測試；本工作單元無寫入規則變更。 | 通過（partner-list.spec、supplier.spec、MD-REAL-001～002 既有 SQL 證據；本輪 SQL 停止，未重跑） |
| MD-LIST-027b1-07 | REQ_F4118.1.1、REQ_F4118.2.1 | 部分結果／檔案（不適用）：列表查詢不作批次或匯入；客戶／供應商匯入由各自測試計畫與 E2E 覆蓋。 | 不適用 |

## WU-MD-027b2 物料／計量單位列表共用重構

| Test Case ID | Related REQ_F | 情境與測試層級 | Status |
|---|---|---|---|
| MD-LIST-027b2-01 | REQ_F4118.3.1、REQ_F4118.13.1 | 正常（U、E）：兩路由保留標題、資料、原 testId、權限動作及三語搜尋標籤；物料子對話框和單位換算、試算仍有入口。 | 通過（item-page.spec、uom-page.spec、item.spec、uom.spec、md-item-uom-list-qa.spec） |
| MD-LIST-027b2-02 | REQ_F4118.3.1、REQ_F4118.13.1 | 邊界（U、E）：空關鍵字列出全部；只在提交時套用 trim 後關鍵字，重查與翻頁不使用未提交輸入；空列表與搜尋無結果訊息分開。最大長度、最大筆數由現有列表 API 約束，日期及小數位不適用此查詢。 | 通過（U、E） |
| MD-LIST-027b2-03 | REQ_F4118.3.1、REQ_F4118.13.1 | 排序與分頁（U）：只接受 code、name，未變狀態不重查，保留頁碼與每頁筆數；切換公司重新查詢並只顯示本公司資料。 | 通過（table-query.spec、item-page.spec、uom-page.spec、既有 E2E） |
| MD-LIST-027b2-04 | REQ_F4118.3.1、REQ_F4118.13.1 | 權限（E）：無 VIEW 路由拒絕、唯讀不顯示新增與編輯；物料匯入及單位換算依原權限；跨公司資料不出現。 | 通過（既有 item／uom E2E） |
| MD-LIST-027b2-05 | REQ_F4118.3.1、REQ_F4118.13.1 | 錯誤（U、E）：列表 500 顯示 traceId、隱藏空表並可重試；重試沿用已提交關鍵字；400、逾時與網絡錯誤使用共用翻譯；操作 409／422 不誤顯列表重試；單位列表失敗不遮蓋換算；刪除執行期間的停用鈕顯示「處理中」。 | 500、409、422、重試及停用說明通過（U、E）；400、逾時由共用錯誤翻譯覆蓋，本頁未逐項重演 |
| MD-LIST-027b2-06 | REQ_F4118.3.1、REQ_F4118.13.1 | 狀態／關聯／重複與衝突（U、I、E）：停用項仍列出；軟刪除、引用中、409 版本衝突和物料基本單位鎖沿用既有 CRUD／真實 SQL 測試；本切片無資料或 API 寫入規則改變。 | 既有測試；本輪 SQL 停止待重驗 |
| MD-LIST-027b2-07 | REQ_F4118.3.1、REQ_F4118.13.1 | 部分結果與檔案（不適用）：列表查詢無批次或檔案；物料匯入另有專項計畫，不由此重構改動。 | 不適用 |

## WU-MD-027b3 倉庫／幣別／稅別列表共用重構

| Test Case ID | Related REQ_F | 情境與測試層級 | Status |
|---|---|---|---|
| MD-LIST-027b3-01 | REQ_F4118.4.1、REQ_F4118.35.1、REQ_F4118.37.1 | 正常（U、E）：三路由保留資料、標題、testId、權限按鈕；儲位／匯率／稅率與試算入口可用，三語及亮暗檢查。 | 通過（三頁 Vitest、12 個專項 E2E、30 張截圖） |
| MD-LIST-027b3-02 | REQ_F4118.4.1、REQ_F4118.35.1、REQ_F4118.37.1 | 邊界（U、E）：空關鍵字查全部；提交時才套用 trim 關鍵字，未提交輸入不影響重查與重試；空列表與搜尋無結果訊息不同。日期、金額、小數位與最大筆數由原 CRUD／API 測試負責，本次列表重構不更動。 | 通過（三頁 Vitest、專項 E2E） |
| MD-LIST-027b3-03 | REQ_F4118.4.1、REQ_F4118.35.1、REQ_F4118.37.1 | 排序與分頁（U）：只接受 code、name，未變狀態不重查；公司切換後仍只載入目前公司。 | 通過（共用 table-query 測試；三頁採用 helper；公司切換由既有測試覆蓋） |
| MD-LIST-027b3-04 | REQ_F4118.4.1、REQ_F4118.35.1、REQ_F4118.37.1 | 權限／跨公司（U、E、I）：無 VIEW 禁止進入路由，唯讀角色無新增／編輯／刪除，子表保留原權限；跨公司資料不得出現。 | 唯讀及路由權限既有 U／E 回歸通過；真實 SQL 公司隔離本輪未重驗 |
| MD-LIST-027b3-05 | REQ_F4118.4.1、REQ_F4118.35.1、REQ_F4118.37.1 | 錯誤（U、E）：列表 500 顯示 traceId 及重試、隱藏空表；400、逾時與網絡失敗使用共用錯誤翻譯；操作 409／422 不誤顯列表重試，刪除進行中有處理中文字。 | 500、重試及既有 409／422 通過；400、逾時共用翻譯沿用既有測試，本輪未逐頁重演 |
| MD-LIST-027b3-06 | REQ_F4118.4.1、REQ_F4118.35.1、REQ_F4118.37.1 | 狀態／關聯／重複與衝突（U、I、E）：停用記錄仍列出；已刪除代碼、引用中、本位幣保護、稅率期間及 409 版本衝突沿用既有 CRUD／SQL 測試；本切片不改寫入規則。 | 既有 U／E 回歸通過；真實 SQL 因服務停止未重驗 |
| MD-LIST-027b3-07 | REQ_F4118.4.1、REQ_F4118.35.1、REQ_F4118.37.1 | 部分成功／全部略過／全部失敗／檔案（不適用）：列表查詢沒有批次或匯入，0 筆由空狀態斷言；三頁原有對話框無檔案流程。 | 不適用 |

## WU-MD-027b4 付款條件／收支設定／工序列表共用重構

涵蓋介面 8.89、8.20、8.42、8.51、8.27、8.16；共用三個頁面元件。需求依序為 REQ_F4118.40.1、REQ_F4118.5.1、REQ_F4118.12.1、REQ_F4118.15.1、REQ_F4118.7.1、REQ_F4118.45.1。原有寫入規則與資料結構沿用既有需求、業務規則及測試。

| Test Case ID | Related REQ_F | 情境與測試層級 | Status |
|---|---|---|---|
| MD-LIST-027b4-01 | REQ_F4118.40.1、REQ_F4118.5.1、REQ_F4118.12.1、REQ_F4118.15.1、REQ_F4118.7.1、REQ_F4118.45.1 | 正常（U、E）：六路由保留標題、原 testId、表格資料與權限操作；付款到期日試算、資金／收支參照科目與工序類別／單位維持可用。亮暗及三語顯示。 | 通過：專項 24/24、全量模擬 E2E 396 通過／5 略過；六路由 60 張截圖已檢視 |
| MD-LIST-027b4-02 | REQ_F4118.40.1、REQ_F4118.5.1、REQ_F4118.12.1、REQ_F4118.15.1、REQ_F4118.7.1、REQ_F4118.45.1 | 邊界（U、E）：空關鍵字查全部；只在提交時套用去空白關鍵字，翻頁及重試不讀未提交輸入；無資料與無搜尋結果有不同提示。每頁筆數沿用 API 10／20／50／100／200；日期及小數位由既有表單／試算測試。 | 工序 Vitest 先紅後綠；專項 E2E 24/24 |
| MD-LIST-027b4-03 | REQ_F4118.40.1、REQ_F4118.5.1、REQ_F4118.12.1、REQ_F4118.15.1、REQ_F4118.7.1、REQ_F4118.45.1 | 排序／分頁（U）：付款及收支僅 code／name，工序僅 sortOrder／code／name；未變狀態不重查，非法欄位退回預設。 | `table-query` 共用測試、工序 Vitest；其餘頁以 helper 限制 |
| MD-LIST-027b4-04 | REQ_F4118.40.1、REQ_F4118.5.1、REQ_F4118.12.1、REQ_F4118.15.1、REQ_F4118.7.1、REQ_F4118.45.1 | 權限／跨公司（E、I）：無 VIEW 禁止進入；唯讀不顯示寫入操作；切公司重查並清空不再適用的工序類別篩選，列表只顯示所選公司資料。 | 原頁 E2E 及後端整合測試沿用；本輪 SQL Stopped 未重驗 |
| MD-LIST-027b4-05 | REQ_F4118.40.1、REQ_F4118.5.1、REQ_F4118.12.1、REQ_F4118.15.1、REQ_F4118.7.1、REQ_F4118.45.1 | 錯誤（U、E）：列表 500 顯示 traceId 和重試，不以空表掩蓋錯誤；重試使用已提交篩選。400、409、422、逾時及網絡錯誤由原表單與共用錯誤翻譯測試覆蓋。 | 通過：專項 24/24、全量模擬 E2E 396 通過／5 略過；本輪未對每頁重演所有原表單錯誤 |
| MD-LIST-027b4-06 | REQ_F4118.40.1、REQ_F4118.5.1、REQ_F4118.12.1、REQ_F4118.15.1、REQ_F4118.7.1、REQ_F4118.45.1 | 重複、軟刪除、版本衝突、草稿／停用狀態及引用限制（U、I、E）：CRUD 契約不變；付款條件／工序已有 409、422 與引用中測試。 | 既有測試沿用；真實 SQL 因服務停止未重驗 |
| MD-LIST-027b4-07 | REQ_F4118.40.1、REQ_F4118.5.1、REQ_F4118.12.1、REQ_F4118.15.1、REQ_F4118.7.1、REQ_F4118.45.1 | 部分成功／全部略過／全部失敗／檔案（不適用）：六列表均為唯讀分頁查詢，沒有批次或上傳；0 筆由空狀態測試。 | 不適用 |
