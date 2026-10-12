# MD 主檔需求

## WU-MD-001 共用入口

| Sub-function No. | Sub-function |
|---|---|
| 8.34 | Master Data Portal |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.34.1 | 1.0 | System should allow authorized users to view the master data portal. | Developer | `/md` 僅向已登入且在目前公司有 `MD.MASTER.VIEW` 的用戶開放；無權限導向 403；三語標題與說明一致。 |

後續客戶、供應商與物料等主檔沿用 BlueWhale 的功能編號，各工作單元再加入驗收條件。MD 主檔均以目前公司為隔離邊界，跨模組只透過 Contracts 或 Application 介面存取。

## WU-MD-002 客戶分類（Client Category）

| Sub-function No. | Sub-function |
|---|---|
| 8.8 | Client Category |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.8.1 | 1.0 | System should enable user to view records of client categories. | Developer | 具 `MD.CUSTOMER_CATEGORY.VIEW` 可依目前公司分頁、搜尋代碼／名稱、排序；已軟刪除及別公司資料不出現；可看停用分類。 |
| REQ_F4118.8.2 | 1.0 | System should enable user to create records of client categories. | Developer | 具 CREATE 權限可建立公司內唯一的代碼（1–30 英數、`-`、`_`，轉大寫）及非空白名稱（最多 200 字）；重複代碼回 409，軟刪除資料的代碼也不可重用。 |
| REQ_F4118.8.3 | 1.0 | System should enable user to edit records of client categories. | Developer | 具 EDIT 權限可以版本號修改名稱及啟用狀態；代碼不可修改，版本衝突回 409，別公司或已刪除資料回 404。 |
| REQ_F4118.8.4 | 1.0 | System should enable user to delete records of client categories. | Developer | 具 DELETE 權限可用版本號軟刪除未被引用的分類；再次刪除或別公司資料回 404；已有客戶引用後僅能停用。 |

## WU-MD-003 客戶主檔（Client Management）

| Sub-function No. | Sub-function |
|---|---|
| 8.1 | Client Management |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.1.1 | 1.0 | System should enable user to view records of clients. | Developer | 具 `MD.CUSTOMER.VIEW` 可按目前公司以代碼或名稱搜尋、分頁及排序；含停用客戶，排除軟刪除及別公司資料。 |
| REQ_F4118.1.2 | 1.0 | System should enable user to create records of clients. | Developer | 具 CREATE 權限可建立公司內唯一代碼（1–30 英數、`-`、`_`，轉大寫）及非空白名稱（最多 200 字）；可指定本公司啟用分類；重複代碼回 409。 |
| REQ_F4118.1.3 | 1.0 | System should enable user to edit records of clients. | Developer | 具 EDIT 權限可帶版本修改名稱、分類及啟用狀態；代碼不可變；版本衝突 409、別公司或不存在 404。停用後仍可查閱及重新啟用。 |

核准／駁回（REQ_F4118.1.4–5）、刪除（1.6）及匯入（1.7）由後續獨立工作單元處理；此單元的停用是新增的主檔基本操作，不等同於刪除。

## WU-MD-005 客戶匯入（Client Import）

沿用 8.1 Client Management 子功能編號。

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.1.7 | 1.0 | System should enable user to import records of clients from spreadsheets. | Developer | 具 `MD.CUSTOMER.IMPORT` 可下載固定英文鍵表頭的 xlsx／UTF-8 CSV 範本，預覽每列新增／略過／錯誤後提交；每列一位客戶，可帶預設聯絡人及地址；最多 2 MB、1,000 資料列，格式或表頭不符回 422。以本公司代碼判斷，已存在則略過、不更新；檔內重複或其他驗證錯誤使整批不寫入，併發由交易及唯一索引保護；錯誤有實際列號及欄位。詳見 ADR-044。 |

WU-MD-005a 完成共用檔案讀取與範本產生；005b 完成客戶服務、API、獨立權限與整合測試；005c 完成三語匯入對話框（介面 8.5）、逐列預覽與提交、Vitest、Playwright。換檔或切換公司會清空預覽；422／409 後須重新預覽。

## WU-MD-006 供應商分類（Supplier Category）

| Sub-function No. | Sub-function |
|---|---|
| 8.9 | Supplier Category |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.9.1 | 1.0 | System should enable user to view records of supplier categories. | Developer | 具 `MD.SUPPLIER_CATEGORY.VIEW` 可依目前公司分頁、搜尋代碼／名稱、以代碼或名稱排序；已軟刪除及別公司資料不出現；可看停用分類。未選公司回 403。 |
| REQ_F4118.9.2 | 1.0 | System should enable user to create records of supplier categories. | Developer | 具 CREATE 權限可建立公司內唯一的代碼（1–30 英數、`-`、`_`，轉大寫）及非空白名稱（最多 200 字）；重複代碼回 409 `MD.SUPPLIER_CATEGORY.DUPLICATE_CODE`，軟刪除資料的代碼也不可重用；不同公司可用相同代碼。 |
| REQ_F4118.9.3 | 1.0 | System should enable user to edit records of supplier categories. | Developer | 具 EDIT 權限可以版本號修改名稱及啟用狀態；代碼不可修改；版本衝突回 409，別公司或已刪除資料回 404 `MD.SUPPLIER_CATEGORY.NOT_FOUND`。 |
| REQ_F4118.9.4 | 1.0 | System should enable user to delete records of supplier categories. | Developer | 具 DELETE 權限可用版本號軟刪除；再次刪除或別公司資料回 404。WU-MD-007 建立供應商後，已被供應商引用的分類回 409 `MD.SUPPLIER_CATEGORY.IN_USE`，只能停用。 |

## WU-MD-007 供應商主檔（Supplier Management）

| Sub-function No. | Sub-function |
|---|---|
| 8.2 | Supplier Management |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.2.1 | 1.0 | System should enable user to view records of suppliers. | Developer | 具 `MD.SUPPLIER.VIEW` 可按目前公司以代碼或名稱搜尋、分頁及排序（代碼／名稱）；含停用供應商，排除軟刪除及別公司資料；未選公司回 403。 |
| REQ_F4118.2.2 | 1.0 | System should enable user to create records of suppliers. | Developer | 具 CREATE 權限可建立公司內唯一代碼（1–30 英數、`-`、`_`，轉大寫）及非空白名稱（最多 200 字）；可指定本公司啟用的供應商分類；重複代碼回 409 `MD.SUPPLIER.DUPLICATE_CODE`。 |
| REQ_F4118.2.3 | 1.0 | System should enable user to edit records of suppliers. | Developer | 具 EDIT 權限可帶版本修改名稱、分類及啟用狀態；代碼不可變；版本衝突 409、別公司或不存在 404 `MD.SUPPLIER.NOT_FOUND`。停用後仍可查閱及重新啟用。 |

核准／駁回（REQ_F4118.2.4–5）及刪除（2.6）列入 WU-MD-024，匯入（2.7）屬 WU-MD-009。

## WU-MD-010 計量單位與換算（Unit of Measurement）

| Sub-function No. | Sub-function |
|---|---|
| 8.13 | Unit of Measurement |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.13.1 | 1.0 | System should enable user to view unit of measurement. | Developer | 具 `MD.UOM.VIEW` 可按目前公司以代碼或名稱搜尋、分頁及排序（代碼／名稱）單位，含停用單位；可查看全部換算及以換算試算數量（結果按目標單位小數位數四捨五入）。排除軟刪除及別公司資料；未選公司回 403。 |
| REQ_F4118.13.2 | 1.0 | System should enable user to create unit of measurement. | Developer | 具 CREATE 權限可建立公司內唯一代碼（1–30 英數、`-`、`_`，轉大寫）、非空白名稱（最多 200 字）及小數位數（0–6）的單位；重複代碼回 409 `MD.UOM.DUPLICATE_CODE`。可新增換算（1 來源 = 係數 目標；係數 > 0、最多 9 位整數及 6 位小數）：兩端須為本公司啟用單位且不同；兩單位已可直接、反向或經其他單位換算時回 409 `MD.UOM_CONVERSION.PATH_EXISTS`。 |
| REQ_F4118.13.3 | 1.0 | System should enable user to edit unit of measurement. | Developer | 具 EDIT 權限可帶版本修改單位名稱、小數位數及啟用狀態（代碼不可變），以及換算係數；版本衝突 409，別公司或不存在 404（`MD.UOM.NOT_FOUND`／`MD.UOM_CONVERSION.NOT_FOUND`）。 |
| REQ_F4118.13.4 | 1.0 | System should enable user to delete unit of measurement. | Developer | 具 DELETE 權限可帶版本軟刪除單位或換算；單位代碼刪除後仍保留；被換算引用的單位回 409 `MD.UOM.IN_USE`，只可停用。 |

換算模型與小數傳遞決策見 ADR-034；其他模組以 `IUomConverter` 契約換算，不得讀 MD 資料表。

## WU-MD-011 物料分類與品牌（Item Category / Item Brand）

| Sub-function No. | Sub-function |
|---|---|
| 8.10 | Item Category |
| 8.11 | Item Brand |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.10.1 | 1.0 | System should enable user to view item category. | Developer | 具 `MD.ITEM_CATEGORY.VIEW` 可取得目前公司全部未刪除分類（含停用，按代碼排序），以 `parentId` 組成最多 5 層的樹；排除別公司資料；未選公司回 403。 |
| REQ_F4118.10.2 | 1.0 | System should enable user to create item category. | Developer | 具 CREATE 權限可建立公司內唯一代碼（1–30 英數、`-`、`_`，轉大寫，軟刪除後仍保留）、非空白名稱（最多 200 字）的分類，可選上層；上層必須是本公司未刪除且啟用的分類（否則 422 `MD.ITEM_CATEGORY.INVALID_PARENT`），超過 5 層回 422 `MD.ITEM_CATEGORY.TOO_DEEP`；重複代碼 409 `MD.ITEM_CATEGORY.DUPLICATE_CODE`。 |
| REQ_F4118.10.3 | 1.0 | System should enable user to edit item category. | Developer | 具 EDIT 權限可帶版本修改名稱、啟用狀態與上層（整棵子樹一起移動）；代碼不可變。不可掛到自己或子孫下（422 `MD.ITEM_CATEGORY.CYCLE`），移動後子樹不可超過 5 層；上層不變時即使已停用亦可保留。版本衝突 409，別公司或不存在 404 `MD.ITEM_CATEGORY.NOT_FOUND`。 |
| REQ_F4118.10.4 | 1.0 | System should enable user to delete item category. | Developer | 具 DELETE 權限可帶版本軟刪除分類；有未刪除子分類時回 409 `MD.ITEM_CATEGORY.HAS_CHILDREN`。物料主檔（WU-MD-012）加入後，被物料引用的分類亦不可刪除。 |
| REQ_F4118.11.1 | 1.0 | System should enable user to view item brand. | Developer | 具 `MD.ITEM_BRAND.VIEW` 可按目前公司以代碼或名稱搜尋、分頁及排序（代碼／名稱）品牌，含停用品牌；排除軟刪除及別公司資料；未選公司回 403。 |
| REQ_F4118.11.2 | 1.0 | System should enable user to create item brand. | Developer | 具 CREATE 權限可建立公司內唯一代碼（規則同分類）、非空白名稱的品牌；重複代碼 409 `MD.ITEM_BRAND.DUPLICATE_CODE`。 |
| REQ_F4118.11.3 | 1.0 | System should enable user to edit item brand. | Developer | 具 EDIT 權限可帶版本修改名稱及啟用狀態（代碼不可變）；版本衝突 409，別公司或不存在 404 `MD.ITEM_BRAND.NOT_FOUND`。 |
| REQ_F4118.11.4 | 1.0 | System should enable user to delete item brand. | Developer | 具 DELETE 權限可帶版本軟刪除品牌，代碼仍保留。物料主檔（WU-MD-012）加入後，被物料引用的品牌回 409 `MD.ITEM_BRAND.IN_USE`，只可停用。 |

## WU-MD-012a 物料主檔後端

| Sub-function No. | Sub-function |
|---|---|
| 8.3 | Product Management |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.3.1 | 1.0 | System should enable user to view product. | Developer | `MD.ITEM.VIEW` 可在目前公司按代碼、名稱或規格搜尋，按代碼／名稱排序及分頁；含停用物料，不含其他公司。未選公司回 403。 |
| REQ_F4118.3.2 | 1.0 | System should enable user to create product. | Developer | `MD.ITEM.CREATE` 可建立公司內永久唯一、轉大寫的 1–30 位代碼、非空白名稱（最多 200 字）、可空規格（最多 500 字）、本公司啟用的基本單位、可空的啟用品牌與分類。重複代碼 409，別公司／停用參照 400。 |
| REQ_F4118.3.3 | 1.0 | System should enable user to edit product. | Developer | `MD.ITEM.EDIT` 可帶版本修改名稱、規格、基本單位、品牌、分類、啟用狀態，代碼不可改；原參照停用後可保留，重新選擇須啟用。舊版本 409、別公司物料 404。已被物料引用的單位、品牌、分類不可刪除，只可停用。物料有專屬換算時修改基本單位回 409 `MD.ITEM.BASE_UOM_LOCKED`（WU-MD-012c）。 |

本段提供後端與 API；三語頁、E2E 及物料專屬換算由後續 WU-MD-012b／012c 補足。期初庫存歸 INV，不在物料主檔維護。物料原誤用 `REQ_F4118.12.x`（BlueWhale 4.1.1.8.12 為 Income & Expense Category），WU-MD-012c 改為 BlueWhale Product Management 的 `REQ_F4118.3.x`（ADR-035）；`REQ_F4118.3.4`（delete product）保留給日後的物料刪除。

## WU-MD-012c 物料專屬換算

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.3.5 | 1.0 | System should enable user to maintain item-specific unit conversions. | Developer | `MD.ITEM.VIEW` 可查看物料專屬換算（1 換算單位 = 係數 基本單位，依單位代碼排序）並按物料試算；`MD.ITEM.EDIT` 可新增、帶版本修改係數及軟刪除。單位須為本公司啟用單位且不是基本單位（400 `uomId`）；同一物料同一單位只能一條（409 `MD.ITEM_UOM_CONVERSION.DUPLICATE`）；係數 > 0、最多 9 位整數及 6 位小數（400 `factor`）。按物料換算時，專屬換算覆寫該單位的公司級換算，其他單位以公司級路徑換到基本單位；任一端換不到基本單位時用公司級直接路徑；都沒有回 422 `MD.UOM_CONVERSION.NO_PATH`。結果按目標單位小數位數遠離零捨入。別公司物料 404 `MD.ITEM.NOT_FOUND`；換算不屬該物料 404 `MD.ITEM_UOM_CONVERSION.NOT_FOUND`；舊版本 409。被專屬換算引用的單位不可刪除（409 `MD.UOM.IN_USE`）。其他模組經 `IUomConverter.ConvertForItemAsync` 使用。 |

## WU-MD-015 倉庫與儲位（Warehouse Management）

| Sub-function No. | Sub-function |
|---|---|
| 8.4 | Warehouse Management |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.4.1 | 1.0 | System should enable user to view records of warehouse. | Developer | 具 `MD.WAREHOUSE.VIEW` 可按目前公司以代碼、名稱或地址搜尋，按代碼／名稱排序及分頁；含停用倉庫，排除軟刪除及別公司資料；未選公司回 403。 |
| REQ_F4118.4.2 | 1.0 | System should enable user to create records of warehouse. | Developer | 具 `MD.WAREHOUSE.CREATE` 可建立公司內永久唯一、轉大寫的 1–30 位代碼（英數、`-`、`_`）、非空白名稱（最多 200 字），以及可空的地址（300）、聯絡人（100）、電話（50）；空白轉為空值，超長回 400（欄位 `address`／`contactName`／`phone`）。重複代碼（含已刪除）409 `MD.WAREHOUSE.DUPLICATE_CODE`；不同公司可用相同代碼。 |
| REQ_F4118.4.3 | 1.0 | System should enable user to edit records of warehouse. | Developer | 具 `MD.WAREHOUSE.EDIT` 可帶版本修改名稱、地址、聯絡人、電話及啟用狀態；代碼不可變；舊版本 409，別公司或已刪除 404 `MD.WAREHOUSE.NOT_FOUND`。停用倉庫不可新增儲位。 |
| REQ_F4118.4.4 | 1.0 | System should enable user to delete records of warehouse. | Developer | 具 `MD.WAREHOUSE.DELETE` 可帶版本軟刪除倉庫，代碼仍保留；仍有未刪除儲位時回 409 `MD.WAREHOUSE.HAS_LOCATIONS`。INV 加入後，被庫存或單據引用的倉庫亦不可刪除（EP-INV 決定契約）。 |
| REQ_F4118.4.5 | 1.0 | System should enable user to maintain storage locations of a warehouse. | Developer | `MD.WAREHOUSE.VIEW` 可按倉庫以代碼或名稱搜尋、排序及分頁儲位（含停用）；`MD.WAREHOUSE.EDIT` 可新增（代碼規則同倉庫，在同一倉庫內永久唯一，409 `MD.STORAGE_LOCATION.DUPLICATE_CODE`；不同倉庫可重複）、帶版本修改名稱及啟用狀態、軟刪除。只能在啟用倉庫新增儲位（422 `MD.STORAGE_LOCATION.WAREHOUSE_INACTIVE`）；別公司或已刪除倉庫 404 `MD.WAREHOUSE.NOT_FOUND`；儲位不屬該倉庫 404 `MD.STORAGE_LOCATION.NOT_FOUND`；舊版本 409。 |

模型決策見 ADR-036：儲位只有一層，區／排／層以代碼表達；MD 不保存任何庫存數量。儲位是 BlueWhale 沒有的追加需求（ADR-002）。

## WU-MD-016 幣別與匯率（Currency、Exchange Rate）

| Sub-function No. | Sub-function |
|---|---|
| 8.35 | Currency |
| 8.36 | Exchange Rate |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.35.1 | 1.0 | System should enable user to view currencies. | Developer | 具 `MD.CURRENCY.VIEW` 可按目前公司以代碼或名稱搜尋、按代碼／名稱排序及分頁幣別（含停用），顯示符號、金額小數位與本位幣標記；排除軟刪除及別公司資料；未選公司回 403。 |
| REQ_F4118.35.2 | 1.0 | System should enable user to create currencies. | Developer | 具 `MD.CURRENCY.CREATE` 可建立 ISO 4217 三個英文字母代碼（轉大寫，公司內永久唯一含已刪除，409 `MD.CURRENCY.DUPLICATE_CODE`；格式不符 400 `code`）、非空白名稱（200）、可空符號（10，400 `symbol`）、金額小數位 0–4（400 `decimalPlaces`），可同時設為本位幣。不同公司可用相同代碼。 |
| REQ_F4118.35.3 | 1.0 | System should enable user to edit currencies and set the base currency. | Developer | 具 `MD.CURRENCY.EDIT` 可帶版本修改名稱、符號、小數位、啟用狀態與本位幣；代碼不可變。每間公司最多一個本位幣，設定新本位幣時自動取消舊的；公司已有匯率後不可更換本位幣（409 `MD.CURRENCY.BASE_LOCKED`）；不可直接取消本位幣（422 `MD.CURRENCY.BASE_REQUIRED`），本位幣必須啟用（422 `MD.CURRENCY.BASE_INACTIVE`）。舊版本 409，別公司或已刪除 404 `MD.CURRENCY.NOT_FOUND`。 |
| REQ_F4118.35.4 | 1.0 | System should enable user to delete currencies. | Developer | 具 `MD.CURRENCY.DELETE` 可帶版本軟刪除幣別，代碼仍保留；本位幣 409 `MD.CURRENCY.IS_BASE`；有未刪除匯率 409 `MD.CURRENCY.IN_USE`。 |
| REQ_F4118.36.1 | 1.0 | System should enable user to view exchange rates of a currency. | Developer | `MD.CURRENCY.VIEW` 可查看某幣別的匯率（1 外幣 = 匯率 本位幣），預設按生效日新到舊、可改舊到新，可篩選某日適用的匯率，分頁；幣別不屬目前公司 404 `MD.CURRENCY.NOT_FOUND`。 |
| REQ_F4118.36.2 | 1.0 | System should enable user to create exchange rates. | Developer | `MD.CURRENCY.EDIT` 可為啟用的外幣新增匯率：生效日、可空到期日（含當日；早於生效日 400 `effectiveTo`）、匯率大於 0、最多 11 位整數及 8 位小數（400 `rate`）。同幣別未刪除的期間不可重疊（409 `MD.EXCHANGE_RATE.OVERLAP`）；本位幣 422 `MD.EXCHANGE_RATE.BASE_CURRENCY`；公司未設本位幣 422 `MD.EXCHANGE_RATE.NO_BASE_CURRENCY`；停用幣別 422 `MD.EXCHANGE_RATE.CURRENCY_INACTIVE`。 |
| REQ_F4118.36.3 | 1.0 | System should enable user to edit exchange rates. | Developer | `MD.CURRENCY.EDIT` 可帶版本修改生效日、到期日與匯率，驗證與重疊規則同新增（排除自己）；幣別不可修改；匯率不屬該幣別 404 `MD.EXCHANGE_RATE.NOT_FOUND`；舊版本 409。已存單據保存自己的成交匯率，不受影響。 |
| REQ_F4118.36.4 | 1.0 | System should enable user to delete exchange rates. | Developer | `MD.CURRENCY.EDIT` 可帶版本軟刪除匯率，刪除後該期間可重新設定；不屬該幣別 404。 |
| REQ_F4118.36.5 | 1.0 | System should enable other modules and users to look up the exchange rate of a business date. | Developer | `ICurrencyLookup.GetRateAsync` 與 `GET /api/v1/md/exchange-rates/lookup?currencyCode=&date=`（`MD.CURRENCY.VIEW`）回傳涵蓋該業務日期的匯率，代碼不分大小寫，本位幣回 1；停用幣別仍可查詢；幣別不存在 404 `MD.CURRENCY.NOT_FOUND`；沒有本位幣或沒有匯率 422 `MD.EXCHANGE_RATE.NO_BASE_CURRENCY`／`MD.EXCHANGE_RATE.NOT_FOUND`。`GetPrecisionAsync` 以主檔小數位提供 `Money.Round` 的捨入設定。 |

模型決策見 ADR-037：本位幣是 MD 幣別旗標（不在 `sys_company`），匯率方向為 1 外幣 = rate 本位幣、`DECIMAL(19,8)`、人手維護。幣別與匯率是 BlueWhale 沒有的追加需求（ADR-002）。

## WU-MD-017 稅別與稅率（Tax Code、Tax Rate）

| Sub-function No. | Sub-function |
|---|---|
| 8.37 | Tax Code |
| 8.38 | Tax Rate |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.37.1 | 1.0 | System should enable user to view tax codes. | Developer | 具 `MD.TAX_CODE.VIEW` 可按目前公司以代碼或名稱搜尋、按代碼／名稱排序及分頁稅別（含停用），顯示稅別類型（銷項／進項／兩者）與今日適用稅率；排除軟刪除及別公司資料；未選公司回 403。 |
| REQ_F4118.37.2 | 1.0 | System should enable user to create tax codes. | Developer | 具 `MD.TAX_CODE.CREATE` 可建立代碼（1–30 位英數、`-`、`_`，轉大寫，公司內永久唯一含已刪除，409 `MD.TAX_CODE.DUPLICATE_CODE`；格式不符 400 `code`）、非空白名稱（200，400 `name`）及類型 `Output`／`Input`／`Both`（400 `taxType`）。不同公司可用相同代碼。 |
| REQ_F4118.37.3 | 1.0 | System should enable user to edit tax codes. | Developer | 具 `MD.TAX_CODE.EDIT` 可帶版本修改名稱、類型與啟用狀態；代碼不可變。舊版本 409 `COMMON.CONCURRENCY.CONFLICT`，別公司或已刪除 404 `MD.TAX_CODE.NOT_FOUND`。 |
| REQ_F4118.37.4 | 1.0 | System should enable user to delete tax codes. | Developer | 具 `MD.TAX_CODE.DELETE` 可帶版本軟刪除稅別，代碼仍保留；有未刪除稅率 409 `MD.TAX_CODE.IN_USE`。 |
| REQ_F4118.38.1 | 1.0 | System should enable user to view tax rates of a tax code. | Developer | `MD.TAX_CODE.VIEW` 可查看某稅別的稅率期間（百分比），預設按生效日新到舊、可改舊到新，可篩選某日適用的稅率，分頁；稅別不屬目前公司 404 `MD.TAX_CODE.NOT_FOUND`。 |
| REQ_F4118.38.2 | 1.0 | System should enable user to create tax rates. | Developer | `MD.TAX_CODE.EDIT` 可為啟用的稅別新增稅率：生效日、可空到期日（含當日；早於生效日 400 `effectiveTo`）、稅率 0–100 且最多 4 位小數（400 `ratePercent`）。同稅別未刪除的期間不可重疊（409 `MD.TAX_RATE.OVERLAP`）；停用稅別 422 `MD.TAX_RATE.TAX_CODE_INACTIVE`。 |
| REQ_F4118.38.3 | 1.0 | System should enable user to edit tax rates. | Developer | `MD.TAX_CODE.EDIT` 可帶版本修改生效日、到期日與稅率，驗證與重疊規則同新增（排除自己）；稅別不可修改；稅率不屬該稅別 404 `MD.TAX_RATE.NOT_FOUND`；舊版本 409。已存單據保存自己的稅率與稅額，不受影響。 |
| REQ_F4118.38.4 | 1.0 | System should enable user to delete tax rates. | Developer | `MD.TAX_CODE.EDIT` 可帶版本軟刪除稅率，刪除後該期間可重新設定；不屬該稅別 404。 |
| REQ_F4118.38.5 | 1.0 | System should enable other modules and users to look up the tax rate of a business date and direction. | Developer | `ITaxRateLookup.GetRatePercentAsync` 與 `GET /api/v1/md/tax-rates/lookup?taxCode=&direction=&date=`（`MD.TAX_CODE.VIEW`）回傳涵蓋該業務日期的稅率百分比，代碼不分大小寫；停用稅別仍可查詢；稅別不存在 404 `MD.TAX_CODE.NOT_FOUND`；銷項查進項專用稅別（或相反）422 `MD.TAX_RATE.DIRECTION_MISMATCH`；沒有稅率 422 `MD.TAX_RATE.NOT_FOUND`。 |
| REQ_F4118.38.6 | 1.0 | System should calculate line tax amounts with decimal and currency rounding. | Developer | `Tax.Calculate` 與 `ITaxRateLookup.CalculateAsync`、`POST /api/v1/md/tax-rates/calculate`（`MD.TAX_CODE.VIEW`）：未稅金額 → 未稅 = 取整（金額）、稅額 = 取整（未稅 × 稅率 ÷ 100）、含稅 = 未稅 + 稅額；含稅金額 → 含稅 = 取整（金額）、未稅 = 取整（含稅 × 100 ÷（100 + 稅率））、稅額 = 含稅 − 未稅。取整用 `Money.Round` 與 `ICurrencyLookup.GetPrecisionAsync` 的幣別小數位（AwayFromZero），負數對稱；幣別代碼格式錯誤 400 `currencyCode`。 |

模型決策見 ADR-038：稅別 + 稅率期間兩張表、百分比 `DECIMAL(7,4)`、含稅與否由單據決定、計算在明細行。稅別與稅率是 BlueWhale 沒有的追加需求（ADR-002）。

## WU-MD-019 會計科目（Chart of Accounts）

| Sub-function No. | Sub-function |
|---|---|
| 8.39 | Chart of Accounts |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.39.1 | 1.0 | System should enable user to view the chart of accounts. | Developer | 具 `MD.ACCOUNT.VIEW` 可一次取得目前公司全部未刪除科目（含停用，按代碼排序），每筆有上層、類別、借貸方向、啟用狀態與是否可過帳（啟用且沒有子科目）；排除別公司資料；未選公司回 403。 |
| REQ_F4118.39.2 | 1.0 | System should enable user to create accounts. | Developer | 具 `MD.ACCOUNT.CREATE` 可建立科目：代碼 1–30 位數字（400 `code`），公司內永久唯一含已刪除（409 `MD.ACCOUNT.DUPLICATE_CODE`）；名稱必填（400 `name`）；上層可空，若有須為本公司啟用科目（422 `MD.ACCOUNT.INVALID_PARENT`），代碼須以上層代碼開頭且較長（422 `MD.ACCOUNT.CODE_PREFIX`），上層已被使用不可再分子科目（422 `MD.ACCOUNT.PARENT_IN_USE`）。根科目必填類別 `Asset`／`Liability`／`Equity`／`Cost`／`Revenue`／`Expense`（400 `accountType`）；子科目可省略類別而沿用上層，不同則 422 `MD.ACCOUNT.TYPE_MISMATCH`。借貸方向 `Debit`／`Credit` 可省略，按類別預設（資產、成本、費用借方；其餘貸方）。 |
| REQ_F4118.39.3 | 1.0 | System should enable user to edit accounts. | Developer | 具 `MD.ACCOUNT.EDIT` 可帶版本修改名稱、借貸方向與啟用狀態；代碼、上層與類別不可修改。已被使用的科目不可改方向（409 `MD.ACCOUNT.IN_USE`）；有啟用子科目不可停用（422 `MD.ACCOUNT.HAS_ACTIVE_CHILDREN`）；上層停用時不可啟用（422 `MD.ACCOUNT.PARENT_INACTIVE`）。舊版本 409 `COMMON.CONCURRENCY.CONFLICT`，別公司或已刪除 404 `MD.ACCOUNT.NOT_FOUND`。 |
| REQ_F4118.39.4 | 1.0 | System should enable user to delete accounts. | Developer | 具 `MD.ACCOUNT.DELETE` 可帶版本軟刪除末級科目，代碼仍保留；有子科目 409 `MD.ACCOUNT.HAS_CHILDREN`；已被使用 409 `MD.ACCOUNT.IN_USE`。 |
| REQ_F4118.39.5 | 1.0 | System should enable other modules to look up accounts and validate postable accounts. | Developer | `IChartOfAccounts.FindAsync(code)` 回傳科目資料（含停用，不存在回 null）；`RequirePostableAsync(code)` 不存在 404 `MD.ACCOUNT.NOT_FOUND`、停用 422 `MD.ACCOUNT.INACTIVE`、有子科目 422 `MD.ACCOUNT.NOT_POSTABLE`。使用檢查經使用方實作的 `IAccountUsage`，MD 不讀 FI 表。 |

模型決策見 ADR-039：單表 `md_account`、數字代碼加前綴規則、上層與類別建立後固定、末級才可過帳、使用檢查由 FI 等模組實作 `IAccountUsage`。會計科目是 BlueWhale 沒有的追加需求（ADR-002；BlueWhale 的 Account 指資金帳戶）。

## WU-MD-018 結算方式與付款條件（Settlement Method、Payment Term）

| Sub-function No. | Sub-function |
|---|---|
| 8.14 | Settlement Method |
| 8.40 | Payment Term |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.14.1 | 1.0 | System should enable the user to view settlement methods. | Developer | 具 `MD.SETTLEMENT_METHOD.VIEW` 可按目前公司以代碼或名稱搜尋、分頁及排序（代碼／名稱）結算方式，含停用；排除軟刪除及別公司資料；未選公司回 403。 |
| REQ_F4118.14.2 | 1.0 | System should enable users to create new settlement methods. | Developer | 具 CREATE 權限可建立公司內唯一代碼（1–30 英數、`-`、`_`，轉大寫，軟刪除後仍保留）、非空白名稱（最多 200 字）的結算方式；重複代碼 409 `MD.SETTLEMENT_METHOD.DUPLICATE_CODE`。不綁定資金帳戶（ADR-040）。 |
| REQ_F4118.14.3 | 1.0 | System should enable the user to edit existing settlement methods. | Developer | 具 EDIT 權限可帶版本修改名稱及啟用狀態（代碼不可變）；版本衝突 409，別公司或不存在 404 `MD.SETTLEMENT_METHOD.NOT_FOUND`。 |
| REQ_F4118.14.4 | 1.0 | System should enable the user to delete existing settlement methods. | Developer | 具 DELETE 權限可帶版本軟刪除，代碼仍保留；使用方模組（`ISettlementMethodUsage`）回報已被單據引用時 409 `MD.SETTLEMENT_METHOD.IN_USE`，只可停用。 |
| REQ_F4118.14.5 | 1.0 | System should enable other modules to look up settlement methods. | Developer | `ISettlementMethodLookup.FindAsync(code)` 回傳代碼、名稱、啟用狀態（含停用，代碼去空白並轉大寫，不存在回 null）；`RequireActiveAsync(code)` 不存在 404 `MD.SETTLEMENT_METHOD.NOT_FOUND`、停用 422 `MD.SETTLEMENT_METHOD.INACTIVE`。 |
| REQ_F4118.40.1 | 1.0 | System should enable the user to view payment terms. | Developer | 具 `MD.PAYMENT_TERM.VIEW` 可按目前公司以代碼或名稱搜尋、分頁及排序付款條件，含停用；每筆有到期規則與其欄位；排除軟刪除及別公司資料；未選公司回 403。 |
| REQ_F4118.40.2 | 1.0 | System should enable the user to create payment terms. | Developer | 具 CREATE 權限可建立代碼（規則同結算方式）、名稱與到期規則：`NetDays` 需 `days` 0–365；`ClosingDays` 需 `closingDay` 1–31（31 = 月底）與 `days`；`ClosingMonthDay` 需 `closingDay`、`months` 0–12、`paymentDay` 1–31，`months = 0` 時 `paymentDay` 須大於 `closingDay`。不符回 400（欄位 `dueType`／`days`／`closingDay`／`months`／`paymentDay`）；不適用的欄位忽略並存 null；重複代碼 409 `MD.PAYMENT_TERM.DUPLICATE_CODE`。 |
| REQ_F4118.40.3 | 1.0 | System should enable the user to edit payment terms. | Developer | 具 EDIT 權限可帶版本修改名稱、到期規則（可換類型）與啟用狀態；代碼不可變。已被單據引用仍可修改（單據保存自己的到期日，不回溯）。版本衝突 409，別公司或不存在 404 `MD.PAYMENT_TERM.NOT_FOUND`。 |
| REQ_F4118.40.4 | 1.0 | System should enable the user to delete payment terms. | Developer | 具 DELETE 權限可帶版本軟刪除，代碼仍保留；使用方模組（`IPaymentTermUsage`）回報已被引用時 409 `MD.PAYMENT_TERM.IN_USE`。 |
| REQ_F4118.40.5 | 1.0 | System should calculate due dates from payment terms. | Developer | `IPaymentTermLookup.CalculateDueDateAsync(code, documentDate)` 與 `GET /api/v1/md/payment-terms/due-date`（`MD.PAYMENT_TERM.VIEW`）：`NetDays` = 單據日 + 天數；`ClosingDays` = 結帳日 + 天數；`ClosingMonthDay` = 結帳日後第 M 個月的 D 日。單據日不晚於當月結帳日屬當期結帳日，否則屬下月結帳日；結帳日、付款日大於當月天數時取月底。不存在 404 `MD.PAYMENT_TERM.NOT_FOUND`，停用 422 `MD.PAYMENT_TERM.INACTIVE`，結果超出日期範圍 400 `documentDate`。`FindAsync(code)` 含停用，不存在回 null。 |

模型決策見 ADR-040：結算方式為公司內代碼＋名稱清單（BlueWhale 4.1.1.8.14，不綁定資金帳戶）；付款條件是 BlueWhale 沒有的追加需求（ADR-002），三種到期規則、單據保存算出的到期日、使用檢查由引用方模組實作。

## WU-MD-020a 收支類別與資金帳戶後端

| Sub-function No. | Sub-function |
|---|---|
| 8.5 | Account Management |
| 8.12 | Income & Expense Category |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.5.1 | 1.0 | System should enable the user to view fund accounts. | Developer | 具 `MD.FUND_ACCOUNT.VIEW` 可按公司查未刪除帳戶，依代碼／名稱搜尋排序、分頁與 `Cash`／`Bank` 篩選；含停用，未選公司 403。 |
| REQ_F4118.5.2 | 1.0 | System should enable users to create fund accounts. | Developer | 具 CREATE 權限；代碼公司內永久唯一、名稱必填；幣別必須同公司且啟用，建立後固定；銀行欄位僅 Bank 保存；可選同公司啟用、可過帳的資產科目；無效參照 422。 |
| REQ_F4118.5.3 | 1.0 | System should enable the user to edit fund accounts. | Developer | 具 EDIT 權限，可帶版本修改名稱、類型、銀行資料、科目及啟用狀態；幣別不接受修改；舊版本 409、別公司或已刪除 404。 |
| REQ_F4118.5.4 | 1.0 | System should enable the user to delete fund accounts. | Developer | 具 DELETE 權限可帶版本軟刪除；使用方回報已引用時 409 `MD.FUND_ACCOUNT.IN_USE`。 |
| REQ_F4118.5.5 | 1.0 | System should enable other modules to look up active fund accounts. | Developer | `IFundAccountLookup.FindAsync` 含停用、不存在回 null；`RequireActiveAsync` 不存在 404、停用 422；FI 經契約讀取，不直接讀 MD 表。 |
| REQ_F4118.12.1 | 1.0 | System should enable the user to view income and expense categories. | Developer | 具 `MD.INCOME_EXPENSE_CATEGORY.VIEW` 可按公司查未刪除類別，搜尋、排序、分頁與方向篩選，含停用。 |
| REQ_F4118.12.2 | 1.0 | System should enable users to create income and expense categories. | Developer | 具 CREATE 權限；代碼永久唯一、名稱必填；方向 Income／Expense 建立後固定；收入只可選可過帳收入科目，支出只可選可過帳費用或成本科目。 |
| REQ_F4118.12.3 | 1.0 | System should enable the user to edit income and expense categories. | Developer | 具 EDIT 權限可帶版本修改名稱、科目與啟用狀態；方向不接受修改；舊版本 409。 |
| REQ_F4118.12.4 | 1.0 | System should enable the user to delete income and expense categories. | Developer | 具 DELETE 權限可帶版本軟刪除；使用方回報已引用時 409 `MD.INCOME_EXPENSE_CATEGORY.IN_USE`。 |
| REQ_F4118.12.5 | 1.0 | System should enable other modules to look up categories by direction. | Developer | `IIncomeExpenseCategoryLookup.FindAsync` 含停用；`RequireActiveAsync` 不存在 404、停用或方向不符 422。 |

模型與模組邊界見 ADR-041；三語頁面與介面欄位由 WU-MD-020b 驗收。

WU-MD-020b 前端驗收：兩頁由 VIEW 權限保護，新增／編輯／刪除按各自權限顯示；Cash 隱藏並清除銀行欄位、幣別與方向建立後唯讀；科目選項按類型且僅限啟用可過帳科目；沒有參照 VIEW 權限時不請求對應 API。介面欄位見 `ui-spec.md` 8.20–8.22、8.42–8.44，測試見 MD-FUND-UI-001～003。

## WU-MD-021a 工序分類與工序後端

| Sub-function No. | Sub-function |
|---|---|
| 8.7 | Process Management |
| 8.15 | Process Category |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.15.1 | 1.0 | System should enable user to view process categories. | Developer | 具 `MD.PROCESS_CATEGORY.VIEW` 可按公司查未刪除類別（含停用），預設按排序號再按代碼，可按代碼／名稱排序、搜尋與分頁；每筆帶未刪除工序數；未選公司 403。 |
| REQ_F4118.15.2 | 1.0 | System should enable user to create process categories. | Developer | 具 CREATE 權限；代碼公司內永久唯一（含已刪除）、名稱必填、排序號 0–9999；重複代碼 409 `MD.PROCESS_CATEGORY.DUPLICATE_CODE`，欄位錯誤 400。 |
| REQ_F4118.15.3 | 1.0 | System should enable user to edit process categories. | Developer | 具 EDIT 權限，可帶版本修改名稱、排序號與啟用狀態；代碼不可修改；舊版本 409、別公司或已刪除 404。 |
| REQ_F4118.15.4 | 1.0 | System should enable user to delete process categories. | Developer | 具 DELETE 權限可帶版本軟刪除；類別下仍有未刪除工序時 409 `MD.PROCESS_CATEGORY.HAS_PROCESSES`。 |
| REQ_F4118.7.1 | 1.0 | System should enable user to view processes. | Developer | 具 `MD.PROCESS.VIEW` 可按公司查未刪除工序（含停用），預設按排序號再按代碼，可按 `categoryId` 篩選、搜尋、排序與分頁。 |
| REQ_F4118.7.2 | 1.0 | System should enable user to create processes. | Developer | 具 CREATE 權限；代碼永久唯一、名稱必填；類別與計件單位可空，若設定必須是同公司啟用資料（422 `MD.PROCESS.INVALID_CATEGORY`／`INVALID_UOM`）；標準工價 ≥ 0、最多 6 位小數（400 `standardRate`）；排序號 0–9999。 |
| REQ_F4118.7.3 | 1.0 | System should enable user to edit processes. | Developer | 具 EDIT 權限可帶版本修改名稱、類別、單位、工價、排序號與啟用狀態；類別或單位未變更時不重新檢查是否啟用；舊版本 409。 |
| REQ_F4118.7.4 | 1.0 | System should enable user to delete processes. | Developer | 具 DELETE 權限可帶版本軟刪除；PP 經 `IProcessUsage` 回報已引用時 409 `MD.PROCESS.IN_USE`，只可停用。被工序使用的計量單位不可刪除（`MD.UOM.IN_USE`）。 |
| REQ_F4118.7.5 | 1.0 | System should enable production planning to look up active processes. | Developer | `IProcessLookup.FindAsync` 含停用、不存在回 null；`RequireActiveAsync` 不存在 404 `MD.PROCESS.NOT_FOUND`、停用 422 `MD.PROCESS.INACTIVE`；`ListActiveAsync(categoryCode?)` 只回啟用工序，按類別排序號、類別代碼、工序排序號、工序代碼排序，無類別排最後；回傳類別代碼／名稱、單位代碼、標準工價。 |

模型與模組邊界見 ADR-042；三語頁面與介面欄位由 WU-MD-021b 驗收。

WU-MD-021b 前端驗收：兩頁由 VIEW 權限保護，新增／編輯／刪除按各自權限顯示；預設按排序號排列，類別列表顯示工序數；工序可按類別篩選，類別與計件單位選項只列啟用項（編輯保留原值）；標準工價 ≥ 0、最多 6 位小數；沒有類別或單位 VIEW 權限時不請求對應 API；HAS_PROCESSES、IN_USE 等錯誤三語顯示。介面欄位見 `ui-spec.md` 8.27–8.29、8.51–8.53，測試見 MD-PROC-UI-001～005。

## WU-MD-004a 客戶聯絡人與地址後端

| Sub-function No. | Sub-function |
|---|---|
| 8.41 | Client Contact（Client Contact Management Interface 8.4） |
| 8.42 | Client Address（Client Contact Management Interface 8.4） |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.41.1 | 1.0 | System should enable user to view the contacts of a client. | Developer | 具 `MD.CUSTOMER.VIEW` 可列出目前公司某客戶的未刪除聯絡人（含停用），預設聯絡人排第一，其餘按建立順序；客戶不存在、已刪除或屬別公司 404 `MD.CUSTOMER.NOT_FOUND`；未選公司 403。 |
| REQ_F4118.41.2 | 1.0 | System should enable user to add contacts to a client. | Developer | 具 `MD.CUSTOMER.EDIT` 可新增聯絡人：姓名必填（≤ 100）、職稱 ≤ 100、手機／電話 ≤ 50、電郵 ≤ 200 且為單一合法地址、通訊軟體帳號 ≤ 100、備註 ≤ 500，空白轉 null，欄位錯誤 400；姓名可重複。設為預設時取消同客戶原預設，每位客戶最多一位預設。停用的客戶仍可維護。 |
| REQ_F4118.41.3 | 1.0 | System should enable user to edit the contacts of a client. | Developer | 具 EDIT 權限可帶版本修改全部欄位、預設與啟用；停用且要求預設 400 `isDefault`；聯絡人不屬路徑上的客戶 404 `MD.CUSTOMER_CONTACT.NOT_FOUND`；舊版本 409。被取消預設的聯絡人版本遞增。 |
| REQ_F4118.41.4 | 1.0 | System should enable user to delete the contacts of a client. | Developer | 具 EDIT 權限可帶版本軟刪除，同時取消預設；刪除後不再列出、不可再修改。單據保存聯絡人快照，所以刪除不檢查引用。 |
| REQ_F4118.42.1 | 1.0 | System should enable user to view the delivery addresses of a client. | Developer | 具 `MD.CUSTOMER.VIEW` 可列出客戶的未刪除地址（含停用），預設地址排第一；公司隔離同 41.1。 |
| REQ_F4118.42.2 | 1.0 | System should enable user to add delivery addresses to a client. | Developer | 具 EDIT 權限可新增地址：地址必填（≤ 300）、郵遞區號 ≤ 20、收件人 ≤ 100、電話 ≤ 50；設為預設時取消原預設，每位客戶最多一筆預設。 |
| REQ_F4118.42.3 | 1.0 | System should enable user to edit the delivery addresses of a client. | Developer | 帶版本修改；停用且要求預設 400 `isDefault`；不屬路徑上的客戶 404 `MD.CUSTOMER_ADDRESS.NOT_FOUND`；舊版本 409。 |
| REQ_F4118.42.4 | 1.0 | System should enable user to delete the delivery addresses of a client. | Developer | 帶版本軟刪除並取消預設。 |

設計見 ADR-043；資料庫以篩選唯一索引與 CHECK 保證「最多一筆預設、預設必為啟用且未刪除」。三語子表、介面欄位與 E2E 由 WU-MD-004b 驗收。

WU-MD-004b 前端驗收：客戶列表每列有「聯絡人」按鈕（VIEW 即可開啟），對話框分聯絡人、送貨地址兩段，預設排第一並以標籤標示；只有 VIEW 時不顯示表單與編輯／刪除；姓名、電郵、地址在前端先驗證，停用時不可勾預設；保存或刪除後重新讀取該段列表，遇 404／409 也重新讀取；`MD.CUSTOMER_CONTACT.NOT_FOUND`／`MD.CUSTOMER_ADDRESS.NOT_FOUND` 三語顯示。介面欄位見 `ui-spec.md` 8.4，測試見 MD-CONT-UI-001～002。
