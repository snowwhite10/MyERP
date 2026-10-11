# MD 介面規格

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
