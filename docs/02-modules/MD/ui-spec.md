# MD 介面規格

WU-MD-012a 僅交付物料後端；`/md/items` 的列表、編輯表單與三語欄位規格在 WU-MD-012b 落地後補入本檔。此段 API 可供後續前端直接接入，尚未對用戶開放新頁面。

| Interface | Route | Related requirement | Access | Fields |
|---|---|---|---|---|
| Master Data Portal Interface | `/md` | REQ_F4118.34.1 | `MD.MASTER.VIEW` | Title (Label), Description (Label) |

此頁採 lazy route、三語 `md.json` 與現有主框架的亮／暗模式。主檔維護頁由各後續工作單元依 BlueWhale 介面編號追加。

## WU-MD-002 客戶分類

| Interface | Route | Related requirement | Access | Fields |
|---|---|---|---|---|
| Client Category View Interface (8.30) | `/md/customer-categories` | REQ_F4118.8.1, REQ_F4118.8.4 | `MD.CUSTOMER_CATEGORY.VIEW`；刪除另需 DELETE | 搜尋、代碼／名稱排序、分頁、狀態、編輯及刪除按鈕 |
| Client Category Create Interface (8.31) | 同頁對話框 | REQ_F4118.8.2 | `MD.CUSTOMER_CATEGORY.CREATE` | 代碼、名稱、儲存／取消 |
| Client Category Edit Interface (8.32) | 同頁對話框 | REQ_F4118.8.3 | `MD.CUSTOMER_CATEGORY.EDIT` | 唯讀代碼、名稱、啟用狀態、儲存／取消 |

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.30 | Client Category View Interface | REQ_F4118.8.1, REQ_F4118.8.4 |
| 8.31 | Client Category Create Interface | REQ_F4118.8.2 |
| 8.32 | Client Category Edit Interface | REQ_F4118.8.3 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.30.1 | Search (Input) | 依代碼或名稱搜尋目前公司分類。 | 縮小列表。 | Text | 任意文字 | REQ_IO_A8.30.2 | Developer | REQ_F4118.8.1 |
| REQ_IO_A8.30.2 | Category List (Table) | 代碼、名稱、啟用狀態，代碼及名稱可排序，伺服器分頁。 | 檢視分類。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.31.1, REQ_IO_A8.32.1 | Developer | REQ_F4118.8.1 |
| REQ_IO_A8.30.3 | Delete (Button) | 二次確認後按版本軟刪除。 | 移除未使用的分類。 | Button | DELETE 權限 | REQ_IO_A8.30.2 | Developer | REQ_F4118.8.4 |
| REQ_IO_A8.31.1 | Code (Input) | 建立時填寫並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.30.2 | Developer | REQ_F4118.8.2 |
| REQ_IO_A8.31.2 | Name (Input) | 分類名稱。 | 向用戶顯示。 | Text | 非空白，最多 200 字 | REQ_IO_A8.30.2 | Developer | REQ_F4118.8.2 |
| REQ_IO_A8.32.1 | Code (Label) | 編輯時只讀。 | 防止歷史引用變更識別。 | Text | 最多 30 字 | REQ_IO_A8.30.2 | Developer | REQ_F4118.8.3 |
| REQ_IO_A8.32.2 | Name (Input) | 編輯分類名稱。 | 更新顯示名稱。 | Text | 非空白，最多 200 字 | REQ_IO_A8.30.2 | Developer | REQ_F4118.8.3 |
| REQ_IO_A8.32.3 | Active (Checkbox) | 控制是否可用於新客戶。 | 保留歷史並停用。 | Boolean | true／false | REQ_IO_A8.30.2 | Developer | REQ_F4118.8.3 |

## WU-MD-003 客戶主檔

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.1 | Client View Interface | `/md/customers` | REQ_F4118.1.1 | `MD.CUSTOMER.VIEW` |
| 8.2 | Client Create Interface | 同頁對話框 | REQ_F4118.1.2 | `MD.CUSTOMER.CREATE` |
| 8.3 | Client Edit Interface | 同頁對話框 | REQ_F4118.1.3 | `MD.CUSTOMER.EDIT` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.1.1 | Search (Input) | 代碼或名稱關鍵字。 | 縮小目前公司列表。 | Text | 任意文字 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.1 |
| REQ_IO_A8.1.2 | Customer List (Table) | 代碼、名稱、分類、狀態及編輯操作；代碼與名稱可排序，伺服器分頁。 | 查看客戶。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.2.1, REQ_IO_A8.3.1 | Developer | REQ_F4118.1.1 |
| REQ_IO_A8.2.1 | Code (Input) | 建立時輸入並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.2 |
| REQ_IO_A8.2.2 | Name (Input) | 客戶名稱。 | 顯示客戶。 | Text | 非空白、最多 200 字 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.2 |
| REQ_IO_A8.2.3 | Category (Dropdown) | 可空；列出本公司分類，停用分類不可新選。 | 客戶分類。 | Category ID | 本公司啟用分類或空值 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.2 |
| REQ_IO_A8.3.1 | Code (Label) | 編輯時唯讀。 | 保持歷史識別。 | Text | 最多 30 字 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.3 |
| REQ_IO_A8.3.2 | Name (Input) | 修改名稱。 | 顯示客戶。 | Text | 非空白、最多 200 字 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.3 |
| REQ_IO_A8.3.3 | Active (Checkbox) | 啟用或停用客戶。 | 保留歷史並停用。 | Boolean | true／false | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.3 |

## WU-MD-006 供應商分類

| Interface | Route | Related requirement | Access | Fields |
|---|---|---|---|---|
| Supplier Category View Interface (8.33) | `/md/supplier-categories` | REQ_F4118.9.1, REQ_F4118.9.4 | `MD.SUPPLIER_CATEGORY.VIEW`；刪除另需 DELETE | 搜尋、代碼／名稱排序、分頁、狀態、編輯及刪除按鈕 |
| Supplier Category Create Interface (8.34) | 同頁對話框 | REQ_F4118.9.2 | `MD.SUPPLIER_CATEGORY.CREATE` | 代碼、名稱、儲存／取消 |
| Supplier Category Edit Interface (8.35) | 同頁對話框 | REQ_F4118.9.3 | `MD.SUPPLIER_CATEGORY.EDIT` | 唯讀代碼、名稱、啟用狀態、儲存／取消 |

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.33 | Supplier Category View Interface | REQ_F4118.9.1, REQ_F4118.9.4 |
| 8.34 | Supplier Category Create Interface | REQ_F4118.9.2 |
| 8.35 | Supplier Category Edit Interface | REQ_F4118.9.3 |

客戶分類與供應商分類共用 `CategoryList` 元件（`category-list.ts`），路由以 `CATEGORY_PAGE` 提供 API、權限前綴（`MD.SUPPLIER_CATEGORY`）與 i18n 前綴（`md.supplierCategory`）；選單「主檔 → 供應商分類」。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.33.1 | Search (Input) | 依代碼或名稱搜尋目前公司分類。 | 縮小列表。 | Text | 任意文字 | REQ_IO_A8.33.2 | Developer | REQ_F4118.9.1 |
| REQ_IO_A8.33.2 | Category List (Table) | 代碼、名稱、啟用狀態，代碼及名稱可排序，伺服器分頁。 | 檢視分類。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.34.1, REQ_IO_A8.35.1 | Developer | REQ_F4118.9.1 |
| REQ_IO_A8.33.3 | Delete (Button) | 二次確認後按版本軟刪除；被引用時顯示錯誤。 | 移除未使用的分類。 | Button | DELETE 權限 | REQ_IO_A8.33.2 | Developer | REQ_F4118.9.4 |
| REQ_IO_A8.34.1 | Code (Input) | 建立時填寫並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.33.2 | Developer | REQ_F4118.9.2 |
| REQ_IO_A8.34.2 | Name (Input) | 分類名稱。 | 向用戶顯示。 | Text | 非空白，最多 200 字 | REQ_IO_A8.33.2 | Developer | REQ_F4118.9.2 |
| REQ_IO_A8.35.1 | Code (Label) | 編輯時只讀。 | 防止歷史引用變更識別。 | Text | 最多 30 字 | REQ_IO_A8.33.2 | Developer | REQ_F4118.9.3 |
| REQ_IO_A8.35.2 | Name (Input) | 編輯分類名稱。 | 更新顯示名稱。 | Text | 非空白，最多 200 字 | REQ_IO_A8.33.2 | Developer | REQ_F4118.9.3 |
| REQ_IO_A8.35.3 | Active (Checkbox) | 控制是否可用於新供應商。 | 保留歷史並停用。 | Boolean | true／false | REQ_IO_A8.33.2 | Developer | REQ_F4118.9.3 |

## WU-MD-007 供應商主檔

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.6 | Supplier View Interface | `/md/suppliers` | REQ_F4118.2.1 | `MD.SUPPLIER.VIEW` |
| 8.7 | Supplier Create Interface | 同頁對話框 | REQ_F4118.2.2 | `MD.SUPPLIER.CREATE` |
| 8.8 | Supplier Edit Interface | 同頁對話框 | REQ_F4118.2.3 | `MD.SUPPLIER.EDIT` |

客戶與供應商共用 `PartnerList` 元件（`partner-list.ts`），路由以 `PARTNER_PAGE` 提供 API、分類 API、權限前綴（`MD.SUPPLIER`）、分類查看權限、i18n 前綴（`md.supplier`）與 data-testid 前綴（`md-supplier`）；選單「主檔 → 供應商」。沒有 `MD.SUPPLIER_CATEGORY.VIEW` 時不載入分類，分類欄顯示空白。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.6.1 | Search (Input) | 代碼或名稱關鍵字。 | 縮小目前公司列表。 | Text | 任意文字 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.1 |
| REQ_IO_A8.6.2 | Supplier List (Table) | 代碼、名稱、分類、狀態及編輯操作；代碼與名稱可排序，伺服器分頁。 | 查看供應商。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.7.1, REQ_IO_A8.8.1 | Developer | REQ_F4118.2.1 |
| REQ_IO_A8.7.1 | Code (Input) | 建立時輸入並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.2 |
| REQ_IO_A8.7.2 | Name (Input) | 供應商名稱。 | 顯示供應商。 | Text | 非空白、最多 200 字 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.2 |
| REQ_IO_A8.7.3 | Category (Dropdown) | 可空；列出本公司供應商分類，停用分類不可新選。 | 供應商分類。 | Category ID | 本公司啟用分類或空值 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.2 |
| REQ_IO_A8.8.1 | Code (Label) | 編輯時唯讀。 | 保持歷史識別。 | Text | 最多 30 字 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.3 |
| REQ_IO_A8.8.2 | Name (Input) | 修改名稱。 | 顯示供應商。 | Text | 非空白、最多 200 字 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.3 |
| REQ_IO_A8.8.3 | Active (Checkbox) | 啟用或停用供應商。 | 保留歷史並停用。 | Boolean | true／false | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.3 |

## WU-MD-010 計量單位與換算

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.45 | Unit of Measurement View Interface | `/md/uoms` | REQ_F4118.13.1 | `MD.UOM.VIEW` |
| 8.46 | Unit of Measurement Create Interface | 同頁對話框（單位、換算） | REQ_F4118.13.2 | `MD.UOM.CREATE` |
| 8.47 | Unit of Measurement Edit Interface | 同頁對話框（單位、換算係數） | REQ_F4118.13.3, REQ_F4118.13.4 | `MD.UOM.EDIT`／`MD.UOM.DELETE` |

頁面 `UomPage`（`uom-page.ts`）由上而下：單位列表、單位換算表（顯示為「1 KG = 1000 G」）、換算試算（數量、來源、目標，由後端換算）。選單「主檔 → 計量單位」。換算對話框的單位選項列出本公司全部單位，停用單位不可選；編輯時只可改係數。前端不做數量運算。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.45.1 | Search (Input) | 代碼或名稱關鍵字。 | 縮小目前公司單位列表。 | Text | 任意文字 | REQ_IO_A8.45.2 | Developer | REQ_F4118.13.1 |
| REQ_IO_A8.45.2 | Unit List (Table) | 代碼、名稱、小數位數、狀態及編輯／刪除操作；代碼與名稱可排序，伺服器分頁。 | 查看單位。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.46.1, REQ_IO_A8.47.1 | Developer | REQ_F4118.13.1 |
| REQ_IO_A8.45.3 | Conversion List (Table) | 來源、目標、換算規則及編輯／刪除操作。 | 查看換算。 | Table | 全部換算 | REQ_IO_A8.46.4 | Developer | REQ_F4118.13.1 |
| REQ_IO_A8.45.4 | Calculator (Form) | 數量、來源單位、目標單位、換算按鈕與結果。 | 試算換算結果。 | Decimal | 數量最多 12 位整數及 6 位小數 | REQ_IO_A8.45.3 | Developer | REQ_F4118.13.1 |
| REQ_IO_A8.46.1 | Code (Input) | 建立時輸入並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.45.2 | Developer | REQ_F4118.13.2 |
| REQ_IO_A8.46.2 | Name (Input) | 單位名稱。 | 顯示單位。 | Text | 非空白、最多 200 字 | REQ_IO_A8.45.2 | Developer | REQ_F4118.13.2 |
| REQ_IO_A8.46.3 | Decimal Places (Dropdown) | 數量小數位數。 | 換算結果捨入。 | Integer | 0–6 | REQ_IO_A8.45.2 | Developer | REQ_F4118.13.2 |
| REQ_IO_A8.46.4 | From / To Unit (Dropdown) | 換算兩端單位；停用單位不可選，兩端不可相同。 | 定義換算。 | Unit ID | 本公司啟用單位 | REQ_IO_A8.45.3 | Developer | REQ_F4118.13.2 |
| REQ_IO_A8.46.5 | Factor (Input) | 1 來源 = 係數 目標。 | 換算係數。 | Decimal | 大於 0，最多 9 位整數及 6 位小數 | REQ_IO_A8.45.3 | Developer | REQ_F4118.13.2 |
| REQ_IO_A8.47.1 | Code (Label) | 編輯時唯讀。 | 保持歷史識別。 | Text | 最多 30 字 | REQ_IO_A8.45.2 | Developer | REQ_F4118.13.3 |
| REQ_IO_A8.47.2 | Name / Decimal Places / Active | 修改名稱、小數位數、啟用狀態。 | 維護單位。 | Text／Integer／Boolean | 同建立 | REQ_IO_A8.45.2 | Developer | REQ_F4118.13.3 |
| REQ_IO_A8.47.3 | Factor (Input) | 編輯換算時只可改係數。 | 修正換算。 | Decimal | 同建立 | REQ_IO_A8.45.3 | Developer | REQ_F4118.13.3 |
| REQ_IO_A8.47.4 | Delete (Button) | 確認後帶版本刪除單位或換算；被引用單位顯示 IN_USE。 | 移除錯誤資料。 | Action | - | REQ_IO_A8.45.2, REQ_IO_A8.45.3 | Developer | REQ_F4118.13.4 |

## WU-MD-012b 物料主檔

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.11 | Item View Interface | `/md/items` | REQ_F4118.3.1 | `MD.ITEM.VIEW` |
| 8.12 | Item Create Interface | 同頁對話框 | REQ_F4118.3.2 | `MD.ITEM.CREATE` |
| 8.13 | Item Edit Interface | 同頁對話框 | REQ_F4118.3.3 | `MD.ITEM.EDIT` |

列表由伺服器按代碼／名稱／規格搜尋、分頁與代碼／名稱排序。單位、品牌、分類名稱只在擁有各自 VIEW 權限時載入。編輯時既有停用參照保留在下拉顯示，但不能重新選取；沒有參照權限時下拉無選項。頁面隨公司切換重新載入並關閉對話框。亮暗主題沿用共用色彩 token；三語欄位和錯誤碼分別由 `md.json`／`errors.json` 提供。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.11.1 | Search (Input) | 搜尋代碼、名稱或規格。 | 找到目前公司物料。 | Text | 任意文字 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.1 |
| REQ_IO_A8.11.2 | Item List (Table) | 代碼、名稱、規格、單位、品牌、分類、狀態；代碼與名稱可排序，伺服器分頁；每列有「換算」開啟介面 8.77。 | 查看物料。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.12.1, REQ_IO_A8.13.1, REQ_IO_A8.77.1 | Developer | REQ_F4118.3.1 |
| REQ_IO_A8.12.1 | Code (Input) | 建立時轉大寫，公司內永久唯一。 | 識別物料。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.2 | Name (Input) | 物料名稱。 | 顯示物料。 | Text | 非空白，最多 200 字 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.3 | Specification (Input) | 可空規格，去除前後空白。 | 區分型號。 | Text | 最多 500 字 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.4 | Base Unit (Dropdown) | 必選本公司啟用單位。 | 設定基本計量單位。 | Unit ID | `MD.UOM.VIEW` 可見的啟用單位 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.5 | Brand (Dropdown) | 可空品牌。 | 分類品牌。 | Brand ID | `MD.ITEM_BRAND.VIEW` 可見的啟用品牌 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.6 | Category (Dropdown) | 可空的階層分類，按樹縮排。 | 分類物料。 | Category ID | `MD.ITEM_CATEGORY.VIEW` 可見的啟用分類 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.13.1 | Code (Label) | 編輯時不可修改。 | 保留既有識別。 | Text | 最多 30 字 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.3 |
| REQ_IO_A8.13.2 | Name / Specification / References (Form) | 編輯名稱、規格、單位、品牌、分類；原參照已停用仍可保留；有專屬換算時改基本單位顯示 BASE_UOM_LOCKED。 | 維護物料。 | Text／IDs | 同建立；帶 version | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.3 |
| REQ_IO_A8.13.3 | Active (Checkbox) | 啟用或停用物料。 | 控制可用狀態。 | Boolean | true／false | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.3 |

## WU-MD-012c 物料專屬換算

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.77 | Item Unit Conversion Interface | `/md/items` 列「換算」對話框 | REQ_F4118.3.5 | `MD.ITEM.VIEW`；維護需 `MD.ITEM.EDIT` |

對話框標題含物料代碼，說明「同一單位以物料換算為準」。換算表以「1 單位 = 係數 基本單位」顯示；有 `MD.ITEM.EDIT` 才顯示新增表單與編輯／刪除按鈕。單位下拉排除基本單位，停用單位不可新選；編輯時單位唯讀。試算由後端以 decimal 計算，前端只驗證格式並顯示結果或錯誤。錯誤以三語 `errors.json` 顯示；切換公司時對話框關閉。亮暗主題沿用共用 token。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.77.1 | Conversion List (Table) | 單位、換算規則（1 單位 = 係數 基本單位）、編輯／刪除操作。 | 查看物料專屬換算。 | Table | 該物料全部換算 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.5 |
| REQ_IO_A8.77.2 | Unit (Dropdown) | 換算單位；排除基本單位，停用單位不可新選，編輯時唯讀。 | 選擇包裝等單位。 | Unit ID | 本公司啟用單位 | REQ_IO_A8.77.1 | Developer | REQ_F4118.3.5 |
| REQ_IO_A8.77.3 | Factor (Input) | 1 單位 = 係數 基本單位。 | 設定物料換算係數。 | Decimal | 大於 0，最多 9 位整數及 6 位小數 | REQ_IO_A8.77.1 | Developer | REQ_F4118.3.5 |
| REQ_IO_A8.77.4 | Delete (Button) | 確認後帶版本刪除換算。 | 移除換算，以便修改基本單位。 | Action | `MD.ITEM.EDIT` | REQ_IO_A8.77.1 | Developer | REQ_F4118.3.5 |
| REQ_IO_A8.77.5 | Calculator (Form) | 數量、來源與目標單位、換算按鈕及結果。 | 按物料試算數量。 | Decimal | 數量最多 12 位整數及 6 位小數 | REQ_IO_A8.77.1 | Developer | REQ_F4118.3.5 |

## WU-MD-011 物料分類與品牌

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.36 | Item Category View Interface | `/md/item-categories` | REQ_F4118.10.1, REQ_F4118.10.4 | `MD.ITEM_CATEGORY.VIEW`；刪除另需 DELETE |
| 8.37 | Item Category Create Interface | 同頁對話框（「新增」或列上的「新增子分類」） | REQ_F4118.10.2 | `MD.ITEM_CATEGORY.CREATE` |
| 8.38 | Item Category Edit Interface | 同頁對話框 | REQ_F4118.10.3 | `MD.ITEM_CATEGORY.EDIT` |
| 8.39 | Item Brand View Interface | `/md/item-brands` | REQ_F4118.11.1, REQ_F4118.11.4 | `MD.ITEM_BRAND.VIEW`；刪除另需 DELETE |
| 8.40 | Item Brand Create Interface | 同頁對話框 | REQ_F4118.11.2 | `MD.ITEM_BRAND.CREATE` |
| 8.41 | Item Brand Edit Interface | 同頁對話框 | REQ_F4118.11.3 | `MD.ITEM_BRAND.EDIT` |

品牌頁沿用共用 `CategoryList`（路由提供 `ITEM_BRAND_PAGE`），行為與供應商分類頁相同。分類頁 `ItemCategoryPage`（`item-category-page.ts`）一次載入公司全部分類，以 `parentId` 組樹：上層在前、同層按代碼排序，代碼欄按層級縮排，列帶 `aria-level`；上層不存在的節點當作最上層顯示。搜尋只在前端過濾，保留命中節點的所有上層。啟用且未到第 5 層的分類列有「新增子分類」，開啟時預選該上層。上層下拉只列啟用分類（編輯時保留目前的上層，即使已停用），排除自己與子孫，也排除掛上後整棵子樹會超過 5 層的節點；後端仍檢查並回 `INVALID_PARENT`／`CYCLE`／`TOO_DEEP`，錯誤留在對話框內顯示。有子分類時刪除顯示 `HAS_CHILDREN`。選單「主檔 → 物料分類／物料品牌」。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.36.1 | Search (Input) | 依代碼或名稱過濾分類樹，保留命中節點的上層。 | 找到分類。 | Text | 任意文字 | REQ_IO_A8.36.2 | Developer | REQ_F4118.10.1 |
| REQ_IO_A8.36.2 | Category Tree (Table) | 代碼（按層級縮排）、名稱、狀態及新增子分類／編輯／刪除操作。 | 檢視分類階層。 | Table | 全部未刪除分類，最多 5 層 | REQ_IO_A8.37.1, REQ_IO_A8.38.1 | Developer | REQ_F4118.10.1 |
| REQ_IO_A8.36.3 | Delete (Button) | 二次確認後按版本軟刪除；有子分類時顯示錯誤。 | 移除未使用的分類。 | Button | DELETE 權限 | REQ_IO_A8.36.2 | Developer | REQ_F4118.10.4 |
| REQ_IO_A8.37.1 | Code (Input) | 建立時填寫並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.36.2 | Developer | REQ_F4118.10.2 |
| REQ_IO_A8.37.2 | Name (Input) | 分類名稱。 | 向用戶顯示。 | Text | 非空白，最多 200 字 | REQ_IO_A8.36.2 | Developer | REQ_F4118.10.2 |
| REQ_IO_A8.37.3 | Parent (Dropdown) | 上層分類，可不選（最上層）；選項按層級縮排。 | 決定分類位置。 | Category ID | 本公司啟用分類，新節點不超過第 5 層 | REQ_IO_A8.36.2 | Developer | REQ_F4118.10.2 |
| REQ_IO_A8.38.1 | Code (Label) | 編輯時只讀。 | 防止歷史引用變更識別。 | Text | 最多 30 字 | REQ_IO_A8.36.2 | Developer | REQ_F4118.10.3 |
| REQ_IO_A8.38.2 | Name / Active | 修改名稱與啟用狀態。 | 維護分類。 | Text／Boolean | 同建立 | REQ_IO_A8.36.2 | Developer | REQ_F4118.10.3 |
| REQ_IO_A8.38.3 | Parent (Dropdown) | 搬移整棵子樹；排除自己與子孫。 | 調整階層。 | Category ID | 啟用分類或目前上層，搬移後不超過 5 層 | REQ_IO_A8.36.2 | Developer | REQ_F4118.10.3 |
| REQ_IO_A8.39.1 | Search (Input) | 依代碼或名稱搜尋目前公司品牌。 | 縮小列表。 | Text | 任意文字 | REQ_IO_A8.39.2 | Developer | REQ_F4118.11.1 |
| REQ_IO_A8.39.2 | Brand List (Table) | 代碼、名稱、啟用狀態，代碼及名稱可排序，伺服器分頁。 | 檢視品牌。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.40.1, REQ_IO_A8.41.1 | Developer | REQ_F4118.11.1 |
| REQ_IO_A8.39.3 | Delete (Button) | 二次確認後按版本軟刪除。 | 移除未使用的品牌。 | Button | DELETE 權限 | REQ_IO_A8.39.2 | Developer | REQ_F4118.11.4 |
| REQ_IO_A8.40.1 | Code (Input) | 建立時填寫並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.39.2 | Developer | REQ_F4118.11.2 |
| REQ_IO_A8.40.2 | Name (Input) | 品牌名稱。 | 向用戶顯示。 | Text | 非空白，最多 200 字 | REQ_IO_A8.39.2 | Developer | REQ_F4118.11.2 |
| REQ_IO_A8.41.1 | Code (Label) | 編輯時只讀。 | 防止歷史引用變更識別。 | Text | 最多 30 字 | REQ_IO_A8.39.2 | Developer | REQ_F4118.11.3 |
| REQ_IO_A8.41.2 | Name / Active | 修改名稱與啟用狀態。 | 維護品牌。 | Text／Boolean | 同建立 | REQ_IO_A8.39.2 | Developer | REQ_F4118.11.3 |

## WU-MD-015 倉庫與儲位

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.17 | Warehouse View Interface | `/md/warehouses` | REQ_F4118.4.1, REQ_F4118.4.4 | `MD.WAREHOUSE.VIEW`；刪除另需 DELETE |
| 8.18 | Warehouse Create Interface | 同頁對話框 | REQ_F4118.4.2 | `MD.WAREHOUSE.CREATE` |
| 8.19 | Warehouse Edit Interface | 同頁對話框 | REQ_F4118.4.3 | `MD.WAREHOUSE.EDIT` |
| 8.78 | Storage Location Interface | `/md/warehouses` 列「儲位」對話框 | REQ_F4118.4.5 | `MD.WAREHOUSE.VIEW`；維護需 `MD.WAREHOUSE.EDIT` |

倉庫頁 `WarehousePage`（`warehouse-page.ts`）由伺服器按代碼／名稱／地址搜尋、分頁與代碼／名稱排序；每列有「儲位」（只需 VIEW）、「編輯」（EDIT）、「刪除」（DELETE，二次確認後帶版本，有儲位時顯示 `HAS_LOCATIONS`）。儲位對話框 `StorageLocationDialog` 標題含倉庫代碼，提示「儲位只有一層，區／排／層寫在代碼中」；伺服器分頁（每頁 10）與搜尋；有 EDIT 才顯示新增表單與編輯／刪除按鈕。停用倉庫顯示警告並隱藏新增表單，既有儲位仍可編輯（編輯中表單保留）或刪除；後端仍回 `WAREHOUSE_INACTIVE`。頁面隨公司切換重新載入並關閉兩個對話框。選單「主檔 → 倉庫與儲位」。亮暗主題沿用共用 token，三語文字在 `md.json`（`warehouse`、`storageLocation`）與 `errors.json`。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.17.1 | Search (Input) | 搜尋代碼、名稱或地址。 | 找到目前公司倉庫。 | Text | 任意文字 | REQ_IO_A8.17.2 | Developer | REQ_F4118.4.1 |
| REQ_IO_A8.17.2 | Warehouse List (Table) | 代碼、名稱、地址、聯絡人、電話、狀態；代碼與名稱可排序，伺服器分頁；每列有儲位／編輯／刪除。 | 查看倉庫。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.18.1, REQ_IO_A8.19.1, REQ_IO_A8.78.1 | Developer | REQ_F4118.4.1 |
| REQ_IO_A8.17.3 | Delete (Button) | 二次確認後帶版本軟刪除；仍有儲位時顯示錯誤。 | 移除未使用的倉庫。 | Button | DELETE 權限 | REQ_IO_A8.17.2 | Developer | REQ_F4118.4.4 |
| REQ_IO_A8.18.1 | Code (Input) | 建立時轉大寫，公司內永久唯一。 | 識別倉庫。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.17.2 | Developer | REQ_F4118.4.2 |
| REQ_IO_A8.18.2 | Name (Input) | 倉庫名稱。 | 顯示倉庫。 | Text | 非空白，最多 200 字 | REQ_IO_A8.17.2 | Developer | REQ_F4118.4.2 |
| REQ_IO_A8.18.3 | Address / Contact / Phone (Input) | 可空，去除前後空白，空白送 null。 | 聯絡與收貨資訊。 | Text | 最多 300／100／50 字 | REQ_IO_A8.17.2 | Developer | REQ_F4118.4.2 |
| REQ_IO_A8.19.1 | Code (Label) | 編輯時不可修改。 | 保留既有識別。 | Text | 最多 30 字 | REQ_IO_A8.17.2 | Developer | REQ_F4118.4.3 |
| REQ_IO_A8.19.2 | Name / Details / Active (Form) | 修改名稱、地址、聯絡人、電話與啟用狀態；帶 version。 | 維護倉庫。 | Text／Boolean | 同建立 | REQ_IO_A8.17.2 | Developer | REQ_F4118.4.3 |
| REQ_IO_A8.78.1 | Location List (Table) | 代碼、名稱、狀態、編輯／刪除；搜尋代碼或名稱，伺服器分頁。 | 查看倉庫儲位。 | Table | 每頁 10 | REQ_IO_A8.17.2 | Developer | REQ_F4118.4.5 |
| REQ_IO_A8.78.2 | Code (Input) | 新增時轉大寫，同一倉庫內永久唯一；編輯時唯讀。 | 識別儲位（可含區／排／層）。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.78.1 | Developer | REQ_F4118.4.5 |
| REQ_IO_A8.78.3 | Name / Active (Input) | 儲位名稱；編輯時可改啟用狀態。 | 維護儲位。 | Text／Boolean | 非空白，最多 200 字 | REQ_IO_A8.78.1 | Developer | REQ_F4118.4.5 |
| REQ_IO_A8.78.4 | Delete (Button) | 確認後帶版本刪除儲位，代碼仍保留。 | 移除儲位，以便刪除倉庫。 | Action | `MD.WAREHOUSE.EDIT` | REQ_IO_A8.78.1 | Developer | REQ_F4118.4.5 |

## WU-MD-016 幣別與匯率

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.79 | Currency View Interface | `/md/currencies` | REQ_F4118.35.1, REQ_F4118.35.4 | `MD.CURRENCY.VIEW`；刪除另需 DELETE |
| 8.80 | Currency Create Interface | 同頁對話框 | REQ_F4118.35.2 | `MD.CURRENCY.CREATE` |
| 8.81 | Currency Edit Interface | 同頁對話框 | REQ_F4118.35.3 | `MD.CURRENCY.EDIT` |
| 8.82 | Exchange Rate Interface | `/md/currencies` 外幣列「匯率」對話框 | REQ_F4118.36.1, REQ_F4118.36.2, REQ_F4118.36.3, REQ_F4118.36.4 | `MD.CURRENCY.VIEW`；維護需 `MD.CURRENCY.EDIT` |

幣別頁 `CurrencyPage`（`currency-page.ts`）說明「每間公司一個本位幣、1 外幣 = 匯率 × 本位幣」；由伺服器按代碼／名稱搜尋、分頁與排序，本位幣代碼旁顯示「本位幣」標籤，本位幣列不顯示「匯率」與「刪除」。編輯本位幣時「設為本位幣」勾選鎖定並提示更換方式（ADR-037）。匯率對話框 `ExchangeRateDialog` 標題含幣別代碼，可按適用日期篩選或顯示全部，按生效日新到舊、每頁 10；到期日空白顯示「持續有效」。有 EDIT 才顯示新增表單與編輯／刪除；停用幣別顯示警告並隱藏新增表單，既有匯率仍可修改或刪除。匯率前端限 6 位整數及 8 位小數（14 位有效數字內 JSON 數值不失真；後端允許 11 位整數）。頁面隨公司切換重新載入並關閉兩個對話框。選單「主檔 → 幣別與匯率」。亮暗主題沿用共用 token，三語文字在 `md.json`（`currency`、`exchangeRate`）與 `errors.json`。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.79.1 | Search (Input) | 搜尋代碼或名稱。 | 找到目前公司幣別。 | Text | 任意文字 | REQ_IO_A8.79.2 | Developer | REQ_F4118.35.1 |
| REQ_IO_A8.79.2 | Currency List (Table) | 代碼（本位幣加標籤）、名稱、符號、金額小數位、狀態；代碼與名稱可排序，伺服器分頁；外幣列有匯率／編輯／刪除，本位幣列只有編輯。 | 查看幣別與本位幣。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.80.1, REQ_IO_A8.81.1, REQ_IO_A8.82.1 | Developer | REQ_F4118.35.1 |
| REQ_IO_A8.79.3 | Delete (Button) | 二次確認後帶版本軟刪除；有匯率時顯示錯誤。 | 移除未使用的外幣。 | Button | DELETE 權限，本位幣不顯示 | REQ_IO_A8.79.2 | Developer | REQ_F4118.35.4 |
| REQ_IO_A8.80.1 | Code (Input) | ISO 4217 代碼，送出時轉大寫，公司內永久唯一。 | 識別幣別。 | Text | 3 個英文字母 | REQ_IO_A8.79.2 | Developer | REQ_F4118.35.2 |
| REQ_IO_A8.80.2 | Name / Symbol (Input) | 名稱必填；符號可空，空白送 null。 | 顯示幣別。 | Text | 名稱非空白最多 200 字；符號最多 10 字 | REQ_IO_A8.79.2 | Developer | REQ_F4118.35.2 |
| REQ_IO_A8.80.3 | Amount Decimals (Input) | 金額小數位，供 `Money.Round` 按幣別捨入。 | 設定金額精度。 | Number | 0–4 的整數，預設 2 | REQ_IO_A8.79.2 | Developer | REQ_F4118.35.2 |
| REQ_IO_A8.80.4 | Base Currency (Checkbox) | 設為本位幣；會取代原本位幣。 | 設定公司本位幣。 | Boolean | 已有匯率時後端回 BASE_LOCKED | REQ_IO_A8.79.2 | Developer | REQ_F4118.35.2 |
| REQ_IO_A8.81.1 | Code (Label) | 編輯時不可修改。 | 保留既有識別。 | Text | 3 字 | REQ_IO_A8.79.2 | Developer | REQ_F4118.35.3 |
| REQ_IO_A8.81.2 | Name / Symbol / Decimals / Base / Active (Form) | 修改名稱、符號、小數位、本位幣與啟用狀態；帶 version。本位幣的勾選鎖定並提示「把另一個幣別設為本位幣」。 | 維護幣別與更換本位幣。 | Text／Number／Boolean | 同建立 | REQ_IO_A8.79.2 | Developer | REQ_F4118.35.3 |
| REQ_IO_A8.82.1 | Rate List (Table) | 生效日、到期日（空白顯示持續有效）、匯率、編輯／刪除；按生效日新到舊，伺服器分頁。 | 查看外幣匯率。 | Table | 每頁 10 | REQ_IO_A8.79.2 | Developer | REQ_F4118.36.1 |
| REQ_IO_A8.82.2 | Effective On (Date Picker) | 只列出該日適用的匯率；「顯示全部」清除。 | 查單據日的匯率。 | Date | yyyy-MM-dd | REQ_IO_A8.82.1 | Developer | REQ_F4118.36.1 |
| REQ_IO_A8.82.3 | From / To (Date Picker) | 生效日必填；到期日可空（持續有效），不可早於生效日。 | 設定匯率期間。 | Date | 期間含首尾，不可與同幣別重疊 | REQ_IO_A8.82.1 | Developer | REQ_F4118.36.2, REQ_F4118.36.3 |
| REQ_IO_A8.82.4 | Rate (Input) | 1 外幣 = 匯率 × 本位幣。 | 設定記帳匯率。 | Decimal | 大於 0，最多 6 位整數及 8 位小數 | REQ_IO_A8.82.1 | Developer | REQ_F4118.36.2, REQ_F4118.36.3 |
| REQ_IO_A8.82.5 | Delete (Button) | 確認後帶版本刪除匯率。 | 移除錯誤匯率，期間可重設。 | Action | `MD.CURRENCY.EDIT` | REQ_IO_A8.82.1 | Developer | REQ_F4118.36.4 |

## WU-MD-017 稅別與稅率

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.83 | Tax Code View Interface | `/md/tax-codes` | REQ_F4118.37.1, REQ_F4118.37.4 | `MD.TAX_CODE.VIEW`；刪除另需 DELETE |
| 8.84 | Tax Code Edit Interface | 同頁對話框 | REQ_F4118.37.2, REQ_F4118.37.3 | 新增需 `MD.TAX_CODE.CREATE`，修改需 EDIT |
| 8.85 | Tax Rate Interface | `/md/tax-codes` 稅別列「稅率」對話框 | REQ_F4118.38.1, REQ_F4118.38.2, REQ_F4118.38.3, REQ_F4118.38.4 | `MD.TAX_CODE.VIEW`；維護需 `MD.TAX_CODE.EDIT` |
| 8.86 | Tax Calculation Interface | `/md/tax-codes` 稅別列「試算」對話框 | REQ_F4118.38.5, REQ_F4118.38.6 | `MD.TAX_CODE.VIEW` |

稅別頁 `TaxCodePage`（`tax-code-page.ts`）說明「稅率以百分比按生效期間設定，單據按業務日期取稅率並保存」；由伺服器按代碼／名稱搜尋、分頁與排序，列出類型（銷項／進項／銷售與採購）與今日稅率（無適用期間顯示「今日無稅率」）。稅率對話框 `TaxRateDialog` 仿匯率對話框：可按適用日期篩選或顯示全部，按生效日新到舊、每頁 10，到期日空白顯示「持續有效」；有 EDIT 才顯示新增表單與編輯／刪除，停用稅別顯示警告並隱藏新增表單；稅率新增、修改、刪除成功後通知列表重新載入今日稅率。試算對話框 `TaxCalculatorDialog` 呼叫 `POST /api/v1/md/tax-rates/calculate`，方向選項依稅別類型限制，日期預設今日（瀏覽器本地日期），結果顯示稅率、未稅、稅額、含稅；任何輸入改變即清除舊結果。`TaxDirection` 在 API 以名稱 `Output`／`Input` 傳遞。頁面隨公司切換重新載入並關閉所有對話框。選單「主檔 → 稅別與稅率」。亮暗主題沿用共用 token，三語文字在 `md.json`（`taxCode`、`taxRate`、`taxCalc`）與 `errors.json`。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.83.1 | Search (Input) | 搜尋代碼或名稱。 | 找到目前公司稅別。 | Text | 任意文字 | REQ_IO_A8.83.2 | Developer | REQ_F4118.37.1 |
| REQ_IO_A8.83.2 | Tax Code List (Table) | 代碼、名稱、適用類型、今日稅率、狀態；代碼與名稱可排序，伺服器分頁；每列有稅率／試算／編輯／刪除。 | 查看稅別與目前稅率。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.84.1, REQ_IO_A8.85.1, REQ_IO_A8.86.1 | Developer | REQ_F4118.37.1 |
| REQ_IO_A8.83.3 | Delete (Button) | 二次確認後帶版本軟刪除；有稅率時顯示 IN_USE。 | 移除未使用的稅別。 | Button | DELETE 權限 | REQ_IO_A8.83.2 | Developer | REQ_F4118.37.4 |
| REQ_IO_A8.84.1 | Code (Input) | 建立時填寫並轉大寫，編輯時唯讀。 | 公司內永久唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.83.2 | Developer | REQ_F4118.37.2, REQ_F4118.37.3 |
| REQ_IO_A8.84.2 | Name (Input) | 名稱必填。 | 顯示稅別。 | Text | 非空白，最多 200 字 | REQ_IO_A8.83.2 | Developer | REQ_F4118.37.2, REQ_F4118.37.3 |
| REQ_IO_A8.84.3 | Applies To (Select) | 銷項（Output）、進項（Input）或兩者（Both），預設兩者。 | 限制可用的單據方向。 | Enum | Output／Input／Both | REQ_IO_A8.83.2 | Developer | REQ_F4118.37.2, REQ_F4118.37.3 |
| REQ_IO_A8.84.4 | Active (Checkbox) | 只在編輯時出現；帶 version 送出。 | 停用不再使用的稅別。 | Boolean | 啟用／停用 | REQ_IO_A8.83.2 | Developer | REQ_F4118.37.3 |
| REQ_IO_A8.85.1 | Rate List (Table) | 生效日、到期日（空白顯示持續有效）、稅率百分比、編輯／刪除；按生效日新到舊，伺服器分頁。 | 查看稅率期間。 | Table | 每頁 10 | REQ_IO_A8.83.2 | Developer | REQ_F4118.38.1 |
| REQ_IO_A8.85.2 | Effective On (Date Picker) | 只列出該日適用的稅率；「顯示全部」清除。 | 查單據日的稅率。 | Date | yyyy-MM-dd | REQ_IO_A8.85.1 | Developer | REQ_F4118.38.1 |
| REQ_IO_A8.85.3 | From / To (Date Picker) | 生效日必填；到期日可空（持續有效），不可早於生效日。 | 設定稅率期間。 | Date | 期間含首尾，不可與同稅別重疊 | REQ_IO_A8.85.1 | Developer | REQ_F4118.38.2, REQ_F4118.38.3 |
| REQ_IO_A8.85.4 | Rate % (Input) | 稅率百分比。 | 設定稅率。 | Decimal | 0–100，最多 4 位小數 | REQ_IO_A8.85.1 | Developer | REQ_F4118.38.2, REQ_F4118.38.3 |
| REQ_IO_A8.85.5 | Delete (Button) | 確認後帶版本刪除稅率。 | 移除錯誤稅率，期間可重設。 | Action | `MD.TAX_CODE.EDIT` | REQ_IO_A8.85.1 | Developer | REQ_F4118.38.4 |
| REQ_IO_A8.86.1 | Direction / Date (Select, Date Picker) | 方向依稅別類型限制；業務日期預設今日。 | 取得該日適用稅率。 | Enum／Date | Output／Input；yyyy-MM-dd | REQ_IO_A8.86.3 | Developer | REQ_F4118.38.5 |
| REQ_IO_A8.86.2 | Currency / Amount / Includes Tax (Input, Checkbox) | 幣別代碼轉大寫；金額可為負數；勾選表示金額已含稅。 | 依幣別精度試算。 | Text／Decimal／Boolean | 3 個英文字母；最多 11 位整數及 4 位小數 | REQ_IO_A8.86.3 | Developer | REQ_F4118.38.6 |
| REQ_IO_A8.86.3 | Result (Label) | 稅率、未稅、稅額、含稅；含稅 = 未稅 + 稅額；方向不符或無稅率顯示錯誤。 | 確認稅額計算。 | Decimal | 依幣別小數位捨入 | REQ_IO_A8.86.1, REQ_IO_A8.86.2 | Developer | REQ_F4118.38.5, REQ_F4118.38.6 |
