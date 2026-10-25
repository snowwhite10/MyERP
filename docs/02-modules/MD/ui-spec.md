# MD 介面規格

WU-MD-027a：介面 8.30 客戶分類、8.33 供應商分類及 8.39 物料品牌使用同一 `CategoryList`。標題、主要動作、搜尋與載入錯誤沿用 `erp-list-page`；搜尋輸入以目前語言的提示作 `aria-label`。按查詢後才套用關鍵字；空列表與有條件卻無結果顯示不同三語提示；載入失敗保留已套用條件並提供重試。500 載入及新增失敗顯示追蹤碼，新增失敗保留輸入；刪除失敗只顯示操作錯誤，避免誤導的列表重試。表格只接受 `code`／`name` 排序，分頁或排序未變時不重查。既有權限與 CRUD 行為不變（REQ_F4118.8.1、REQ_F4118.9.1、REQ_F4118.11.1）。

WU-MD-012a 僅交付物料後端；`/md/items` 的列表、編輯表單與三語欄位規格在 WU-MD-012b 落地後補入本檔。此段 API 可供後續前端直接接入，尚未對用戶開放新頁面。

| Interface | Route | Related requirement | Access | Fields |
|---|---|---|---|---|
| Master Data Portal Interface | `/md` | REQ_F4118.34.1 | `MD.MASTER.VIEW` | Title (Label), Description (Label) |

此頁採 lazy route、三語 `md.json` 與現有主框架的亮／暗模式。主檔維護頁由各後續工作單元依 BlueWhale 介面編號追加。

**MD 頁面共用行為（WU-MD-022c 審查修正）**
- 下拉選項（分類、品牌、單位、付款條件、結算方式、幣別、屬性定義等）以 `fetchAllPages` 每頁 200 筆載齊，不只取第一頁；計量單位頁的選項只在切換公司及單位異動後重載，翻頁不重載（MD-F02、F03）。
- 儲存或刪除回 404（資料已被刪除）或 409 `COMMON.CONCURRENCY.CONFLICT` 時，顯示錯誤並重新查詢列表，讓重試帶最新版本；子表對話框在 404 時放棄編輯中的資料。同為 409 的「使用中」「審批中」不重新查詢（MD-F06）。
- 刪除進行中停用所有刪除鈕，避免第二次確認帶舊版本（MD-F05）。
- 子資料對話框切換往來對象或物料後，舊對象的保存回應不再更新畫面（MD-F07）。
- 停用的參照選項不可選，但物料目前使用的單位、品牌、分類仍可選（MD-F13）。

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
| 8.1 | Client View Interface | `/md/customers` | REQ_F4118.1.1, REQ_F4118.1.4, REQ_F4118.1.5, REQ_F4118.1.6 | `MD.CUSTOMER.VIEW`；送審／刪除另需 APPROVE／DELETE |
| 8.2 | Client Create Interface | 同頁對話框 | REQ_F4118.1.2 | `MD.CUSTOMER.CREATE` |
| 8.3 | Client Edit Interface | 同頁對話框 | REQ_F4118.1.3 | `MD.CUSTOMER.EDIT` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.1.1 | Search (Input) | 代碼或名稱關鍵字。 | 縮小目前公司列表。 | Text | 任意文字 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.1 |
| REQ_IO_A8.1.2 | Customer List (Table) | 代碼、名稱、分類、啟用及審核狀態、編輯／送審／刪除操作；代碼與名稱可排序，伺服器分頁。 | 查看客戶。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.2.1, REQ_IO_A8.3.1, REQ_IO_A8.1.3, REQ_IO_A8.1.4 | Developer | REQ_F4118.1.1, REQ_F4118.1.4, REQ_F4118.1.5, REQ_F4118.1.6 |
| REQ_IO_A8.1.3 | Submit (Button) | Pending／Rejected 客戶按版本送 SYS 審批；核准或駁回由 SYS 待辦頁處理。 | 控制交易使用資格。 | Button | APPROVE 權限且未核准 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.4, REQ_F4118.1.5 |
| REQ_IO_A8.1.4 | Delete (Button) | 二次確認後按版本軟刪除。待審（Pending）也顯示，因為前端無法分辨「從未送審」與「審批中」：審批中由後端回 422 `MD.CUSTOMER.APPROVAL_PENDING` 並顯示訊息；已被單據引用顯示錯誤；資料已被刪除（404）或版本衝突時重新查詢列表（MD-B02、MD-F06）。 | 移除未使用客戶。 | Button | DELETE 權限 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.6 |
| REQ_IO_A8.2.1 | Code (Input) | 建立時輸入並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.2 |
| REQ_IO_A8.2.2 | Name (Input) | 客戶名稱。 | 顯示客戶。 | Text | 非空白、最多 200 字 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.2 |
| REQ_IO_A8.2.3 | Category (Dropdown) | 可空；列出本公司分類，停用分類不可新選。 | 客戶分類。 | Category ID | 本公司啟用分類或空值 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.2 |
| REQ_IO_A8.3.1 | Code (Label) | 編輯時唯讀。 | 保持歷史識別。 | Text | 最多 30 字 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.3 |
| REQ_IO_A8.3.2 | Name (Input) | 修改名稱。 | 顯示客戶。 | Text | 非空白、最多 200 字 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.3 |
| REQ_IO_A8.3.3 | Active (Checkbox) | 啟用或停用客戶。 | 保留歷史並停用。 | Boolean | true／false | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.3 |
| REQ_IO_A8.2.4 | Default Payment Term (Dropdown) | 可空（「不設定」）；列出本公司付款條件，停用者不可新選，編輯時目前已選的停用值仍可保留；無 `MD.PAYMENT_TERM.VIEW` 時無選項。列表另顯示預設付款條件名稱（WU-MD-025）。 | SD 單據帶出預設付款條件。 | Payment Term ID | 本公司啟用付款條件或空值 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.8 |
| REQ_IO_A8.2.5 | Default Settlement Method (Dropdown) | 可空；規則同上，選項需 `MD.SETTLEMENT_METHOD.VIEW`。 | SD／FI 單據帶出預設結算方式。 | Settlement Method ID | 本公司啟用結算方式或空值 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.8 |
| REQ_IO_A8.3.4 | Default Payment Term / Settlement Method (Dropdown) | 編輯時同 REQ_IO_A8.2.4–5；選「不設定」即清除。 | 維護預設值。 | ID | 同建立 | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.8 |

WU-MD-027b1：8.1 Client View 與 8.6 Supplier View 共用 `erp-list-page`；搜尋框有三語可讀標籤，輸入時不立即改查詢，按查詢或 Enter 才套用。空列表與搜尋無結果使用不同提示；列表載入失敗時隱藏表格並顯示錯誤、追蹤碼與重試，操作錯誤不顯示列表重試。排序只接受代碼及名稱，分頁與排序保持原 API 參數及 `data-testid`。
WU-MD-027b2：8.11 Item View 與 8.45 Unit of Measurement View 採相同共用列表契約。單位載入失敗時，8.45 換算列表和試算仍顯示；物料換算、圖片、屬性及匯入對話框保持既有入口。刪除執行中，停用鈕以三語顯示「處理中」。兩頁皆保留原 `data-testid`、三語、亮暗模式及權限動作。

## WU-MD-004b 客戶聯絡人與地址

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.4 | Client Contact Management Interface | `/md/customers` 列「聯絡人」對話框 | REQ_F4118.41.1, REQ_F4118.41.2, REQ_F4118.41.3, REQ_F4118.41.4, REQ_F4118.42.1, REQ_F4118.42.2, REQ_F4118.42.3, REQ_F4118.42.4 | `MD.CUSTOMER.VIEW`；維護需 `MD.CUSTOMER.EDIT` |

對話框由客戶列表每列的「聯絡人」按鈕開啟（只有查看權限也可開啟，唯讀），不放在客戶編輯對話框內：子資料逐筆保存（ADR-043 第 5 條），與客戶主檔的「保存／取消」分開，避免使用者以為取消客戶編輯會一併撤銷子資料。對話框上下兩段：聯絡人與送貨地址，各有列表與新增／編輯表單；保存或刪除後重新讀取該段列表，因為設為預設會自動取消他筆。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.4.1 | Contact List (Table) | 姓名（預設標籤、備註）、職稱、手機、電話、電郵、通訊軟體帳號、狀態、編輯／刪除；預設排第一，不分頁。 | 查看客戶聯絡人。 | Table | 全部未刪除聯絡人 | REQ_IO_A8.1.2 | Developer | REQ_F4118.41.1 |
| REQ_IO_A8.4.2 | Contact Form (Input) | 姓名必填；職稱、手機、電話、電郵、通訊軟體帳號、備註選填，去除前後空白，空白送 null；電郵須為單一合法地址。 | 新增或修改聯絡人。 | Text | 姓名／職稱 100、手機／電話 50、電郵 200、通訊帳號 100、備註 500 字 | REQ_IO_A8.4.1 | Developer | REQ_F4118.41.2, REQ_F4118.41.3 |
| REQ_IO_A8.4.3 | Default / Active (Checkbox) | 設為預設會取消原預設；編輯時可停用；停用且勾預設時在前端提示並不送出。 | 指定單據帶出的聯絡人。 | Boolean | 預設必為啟用 | REQ_IO_A8.4.1 | Developer | REQ_F4118.41.2, REQ_F4118.41.3 |
| REQ_IO_A8.4.4 | Delete Contact (Button) | 確認後帶版本刪除，刪除預設後客戶暫無預設。 | 移除聯絡人。 | Action | `MD.CUSTOMER.EDIT` | REQ_IO_A8.4.1 | Developer | REQ_F4118.41.4 |
| REQ_IO_A8.4.5 | Address List (Table) | 地址（預設標籤）、郵遞區號、收件人、電話、狀態、編輯／刪除；預設排第一，不分頁。 | 查看送貨地址。 | Table | 全部未刪除地址 | REQ_IO_A8.1.2 | Developer | REQ_F4118.42.1 |
| REQ_IO_A8.4.6 | Address Form (Input) | 地址必填；郵遞區號、收件人、電話選填；預設與啟用規則同聯絡人。 | 新增或修改地址。 | Text／Boolean | 地址 300、郵遞區號 20、收件人 100、電話 50 字 | REQ_IO_A8.4.5 | Developer | REQ_F4118.42.2, REQ_F4118.42.3 |
| REQ_IO_A8.4.7 | Delete Address (Button) | 確認後帶版本刪除地址。 | 移除地址。 | Action | `MD.CUSTOMER.EDIT` | REQ_IO_A8.4.5 | Developer | REQ_F4118.42.4 |

## WU-MD-005c 客戶匯入

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.5 | Client Import Interface | `/md/customers`「匯入」對話框 | REQ_F4118.1.7 | `MD.CUSTOMER.IMPORT` |

客戶列表頂端以獨立 IMPORT 權限顯示入口。對話框可下載 xlsx／UTF-8 CSV 範本，選檔後須先預覽；預覽列出檔案實際列號、代碼、名稱、Create／Skip／Error 狀態及每個欄位錯誤。只要有 Error 即不可提交；換檔、切公司或關閉均清空預覽。提交重新上傳預覽的同一個 `File`；422／409 錯誤以三語提示並要求重新預覽，成功後更新客戶列表。介面沿用主框架亮／暗模式。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.5.1 | Template (Button) | 下載 xlsx 或 CSV 表頭範本。 | 提供正確欄位。 | xlsx／UTF-8 CSV | 固定英文鍵表頭 | REQ_IO_A8.5.2 | Developer | REQ_F4118.1.7 |
| REQ_IO_A8.5.2 | Import File (Input) | 選擇本機檔案；換檔清除舊預覽。副檔名不是 xlsx／csv 或超過 2 MB 時在前端即拒絕，顯示 `COMMON.IMPORT.UNSUPPORTED_FORMAT`／`TOO_LARGE`，不上傳（MD-F09）。 | 選擇匯入資料。 | xlsx／CSV | 最多 2 MB、1,000 資料列 | REQ_IO_A8.5.3 | Developer | REQ_F4118.1.7 |
| REQ_IO_A8.5.3 | Preview (Button, Table) | 顯示實際列號、代碼、名稱、Create／Skip／Error 與欄位錯誤；欄位名以三語顯示（如「分類代碼: 找不到」），未知欄位原樣顯示（MD-F08）。 | 提交前驗證。 | Table | 每頁 20 列 | REQ_IO_A8.5.4 | Developer | REQ_F4118.1.7 |
| REQ_IO_A8.5.4 | Import (Button) | 無錯誤時重新上傳同一檔案，成功後更新列表；衝突須重新預覽。 | 整批寫入。 | Action | IMPORT 權限、預覽 0 Error | REQ_IO_A8.1.2 | Developer | REQ_F4118.1.7 |

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
| 8.6 | Supplier View Interface | `/md/suppliers` | REQ_F4118.2.1, REQ_F4118.2.4, REQ_F4118.2.5, REQ_F4118.2.6 | `MD.SUPPLIER.VIEW`；送審／刪除另需 APPROVE／DELETE（WU-MD-024） |
| 8.7 | Supplier Create Interface | 同頁對話框 | REQ_F4118.2.2 | `MD.SUPPLIER.CREATE` |
| 8.8 | Supplier Edit Interface | 同頁對話框 | REQ_F4118.2.3 | `MD.SUPPLIER.EDIT` |

客戶與供應商共用 `PartnerList` 元件（`partner-list.ts`），路由以 `PARTNER_PAGE` 提供 API、分類 API、權限前綴（`MD.SUPPLIER`）、分類查看權限、i18n 前綴（`md.supplier`）與 data-testid 前綴（`md-supplier`）；選單「主檔 → 供應商」。沒有 `MD.SUPPLIER_CATEGORY.VIEW` 時不載入分類，分類欄顯示空白。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.6.1 | Search (Input) | 代碼或名稱關鍵字。 | 縮小目前公司列表。 | Text | 任意文字 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.1 |
| REQ_IO_A8.6.2 | Supplier List (Table) | 代碼、名稱、分類、啟用及審核狀態、編輯／送審／刪除操作；代碼與名稱可排序，伺服器分頁。 | 查看供應商。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.7.1, REQ_IO_A8.8.1, REQ_IO_A8.6.3, REQ_IO_A8.6.4 | Developer | REQ_F4118.2.1, REQ_F4118.2.4, REQ_F4118.2.5, REQ_F4118.2.6 |
| REQ_IO_A8.6.3 | Submit (Button) | Pending／Rejected 供應商按版本送 SYS 審批；核准或駁回由 SYS 待辦頁處理。 | 控制交易使用資格。 | Button | APPROVE 權限且未核准 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.4, REQ_F4118.2.5 |
| REQ_IO_A8.6.4 | Delete (Button) | 二次確認後按版本軟刪除。待審（Pending）也顯示，審批中由後端回 422 `MD.SUPPLIER.APPROVAL_PENDING` 並顯示訊息；已被單據引用顯示錯誤；資料已被刪除（404）或版本衝突時重新查詢列表（MD-B02、MD-F06）。 | 移除未使用供應商。 | Button | DELETE 權限 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.6 |
| REQ_IO_A8.7.1 | Code (Input) | 建立時輸入並轉大寫。 | 公司內唯一識別。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.2 |
| REQ_IO_A8.7.2 | Name (Input) | 供應商名稱。 | 顯示供應商。 | Text | 非空白、最多 200 字 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.2 |
| REQ_IO_A8.7.3 | Category (Dropdown) | 可空；列出本公司供應商分類，停用分類不可新選。 | 供應商分類。 | Category ID | 本公司啟用分類或空值 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.2 |
| REQ_IO_A8.8.1 | Code (Label) | 編輯時唯讀。 | 保持歷史識別。 | Text | 最多 30 字 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.3 |
| REQ_IO_A8.8.2 | Name (Input) | 修改名稱。 | 顯示供應商。 | Text | 非空白、最多 200 字 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.3 |
| REQ_IO_A8.8.3 | Active (Checkbox) | 啟用或停用供應商。 | 保留歷史並停用。 | Boolean | true／false | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.3 |
| REQ_IO_A8.7.4 | Default Payment Term (Dropdown) | 同 REQ_IO_A8.2.4，適用供應商；列表顯示預設付款條件名稱（WU-MD-025）。 | PUR 單據帶出預設付款條件。 | Payment Term ID | 本公司啟用付款條件或空值 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.8 |
| REQ_IO_A8.7.5 | Default Settlement Method (Dropdown) | 同 REQ_IO_A8.2.5，適用供應商。 | PUR／FI 單據帶出預設結算方式。 | Settlement Method ID | 本公司啟用結算方式或空值 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.8 |
| REQ_IO_A8.8.4 | Default Payment Term / Settlement Method (Dropdown) | 編輯時同 REQ_IO_A8.7.4–5；選「不設定」即清除。 | 維護預設值。 | ID | 同建立 | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.8 |

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
| 8.11 | Item View Interface | `/md/items`（列操作含刪除，REQ_IO_A8.11.3） | REQ_F4118.3.1, REQ_F4118.3.4 | `MD.ITEM.VIEW`；刪除 `MD.ITEM.DELETE` |
| 8.12 | Item Create Interface | 同頁對話框 | REQ_F4118.3.2 | `MD.ITEM.CREATE` |
| 8.13 | Item Edit Interface | 同頁對話框 | REQ_F4118.3.3 | `MD.ITEM.EDIT` |
| 8.14 | Item Import Interface | `/md/items` 的匯入對話框 | REQ_F4118.3.6 | `MD.ITEM.IMPORT` |

列表由伺服器按代碼／名稱／規格搜尋、分頁與代碼／名稱排序。單位、品牌、分類名稱只在擁有各自 VIEW 權限時載入。編輯時既有停用參照保留在下拉顯示，但不能重新選取；沒有參照權限時下拉無選項。頁面隨公司切換重新載入並關閉對話框。亮暗主題沿用共用色彩 token；三語欄位和錯誤碼分別由 `md.json`／`errors.json` 提供。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.11.1 | Search (Input) | 搜尋代碼、名稱或規格。 | 找到目前公司物料。 | Text | 任意文字 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.1 |
| REQ_IO_A8.11.2 | Item List (Table) | 代碼、名稱、規格、單位、品牌、分類、追蹤方式、狀態；代碼與名稱可排序，伺服器分頁；每列有「換算」開啟介面 8.77。 | 查看物料。 | Table | 每頁 10／20／50／100／200 | REQ_IO_A8.12.1, REQ_IO_A8.13.1, REQ_IO_A8.77.1 | Developer | REQ_F4118.3.1 |
| REQ_IO_A8.11.3 | Delete (Button) | 列操作，`MD.ITEM.DELETE` 才顯示。確認對話框說明專屬換算、圖片、屬性一併刪除且代碼不可再使用；確認後按版本軟刪除，刪除中按鈕停用、不重複送出（MD-F05）。已有庫存、交易或單據時顯示 `MD.ITEM.IN_USE`，不重新查詢；資料已被刪除（404）或版本衝突時重新查詢列表（MD-F06）。 | 移除未使用物料。 | Button | DELETE 權限 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.4 |
| REQ_IO_A8.12.1 | Code (Input) | 建立時轉大寫，公司內永久唯一。 | 識別物料。 | Text | 1–30 位英數、`-`、`_` | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.2 | Name (Input) | 物料名稱。 | 顯示物料。 | Text | 非空白，最多 200 字 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.3 | Specification (Input) | 可空規格，去除前後空白。 | 區分型號。 | Text | 最多 500 字 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.4 | Base Unit (Dropdown) | 必選本公司啟用單位。 | 設定基本計量單位。 | Unit ID | `MD.UOM.VIEW` 可見的啟用單位 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.5 | Brand (Dropdown) | 可空品牌。 | 分類品牌。 | Brand ID | `MD.ITEM_BRAND.VIEW` 可見的啟用品牌 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.12.6 | Category (Dropdown) | 可空的階層分類，按樹縮排。 | 分類物料。 | Category ID | `MD.ITEM_CATEGORY.VIEW` 可見的啟用分類 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.2 |
| REQ_IO_A8.13.1 | Code (Label) | 編輯時不可修改。 | 保留既有識別。 | Text | 最多 30 字 | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.3 |
| REQ_IO_A8.13.2 | Name / Specification / References (Form) | 編輯名稱、規格、單位、品牌、分類；原參照已停用仍可保留；有專屬換算時改基本單位顯示 BASE_UOM_LOCKED。 | 維護物料。 | Text／IDs | 同建立；帶 version | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.3 |
| REQ_IO_A8.13.3 | Active (Checkbox) | 啟用或停用物料。 | 控制可用狀態。 | Boolean | true／false | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.3 |
| REQ_IO_A8.12.7 | Tracking Mode (Dropdown) | 建立與編輯共用：只按數量／批號／序號，建立預設只按數量；下方提示已有庫存或單據使用後不可修改，改變被拒時顯示 `MD.ITEM.TRACKING_MODE_LOCKED` 並保留對話框。列表新增「追蹤方式」欄（WU-INV-008a）。 | 決定 INV 入出庫是否須輸入批號或序號。 | Code | `None`／`Lot`／`Serial` | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.7 |
| REQ_IO_A8.14.1 | Template (Button) | 下載 xlsx／CSV 英文鍵範本。 | 提供正確欄位。 | File | xlsx／UTF-8 CSV | REQ_IO_A8.14.2 | Developer | REQ_F4118.3.6 |
| REQ_IO_A8.14.2 | Import File (Input) | 選擇檔案後預覽並顯示實際列號、建立／略過／錯誤及欄位錯誤。 | 提交前核對物料。 | File／Table | 2 MB、1,000 列 | REQ_IO_A8.14.3 | Developer | REQ_F4118.3.6 |
| REQ_IO_A8.14.3 | Import (Button) | 沒有錯誤列才能提交；若提交時狀態變化，提示重新預覽。 | 冪等建立物料主檔。 | Action | `MD.ITEM.IMPORT` | REQ_IO_A8.11.2 | Developer | REQ_F4118.3.6 |

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

稅別頁 `TaxCodePage`（`tax-code-page.ts`）說明「稅率以百分比按生效期間設定，單據按業務日期取稅率並保存」；由伺服器按代碼／名稱搜尋、分頁與排序，列出類型（銷項／進項／銷售與採購）與今日稅率（無適用期間顯示「今日無稅率」）。稅率對話框 `TaxRateDialog` 仿匯率對話框：可按適用日期篩選或顯示全部，按生效日新到舊、每頁 10，到期日空白顯示「持續有效」；有 EDIT 才顯示新增表單與編輯／刪除，停用稅別顯示警告並隱藏新增表單；稅率新增、修改、刪除成功後通知列表重新載入今日稅率。試算對話框 `TaxCalculatorDialog` 呼叫 `POST /api/v1/md/tax-rates/calculate`，方向選項依稅別類型限制，日期預設今日（瀏覽器本地日期），結果顯示稅率、未稅、稅額、含稅，金額依介面語言加千分位，並按回應的 `decimalPlaces`（幣別小數位）補零（MD-F12）；任何輸入改變即清除舊結果。`TaxDirection` 在 API 以名稱 `Output`／`Input` 傳遞。頁面隨公司切換重新載入並關閉所有對話框。選單「主檔 → 稅別與稅率」。亮暗主題沿用共用 token，三語文字在 `md.json`（`taxCode`、`taxRate`、`taxCalc`）與 `errors.json`。

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

## WU-MD-019 會計科目

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.87 | Account View Interface | `/md/accounts` | REQ_F4118.39.1, REQ_F4118.39.4 | `MD.ACCOUNT.VIEW`；刪除另需 DELETE |
| 8.88 | Account Edit Interface | 同頁對話框 | REQ_F4118.39.2, REQ_F4118.39.3 | 新增需 `MD.ACCOUNT.CREATE`，修改需 EDIT |

會計科目頁 `AccountPage`（`account-page.ts`）仿物料分類樹頁：`GET /api/v1/md/accounts` 一次載入目前公司全部科目，前端以 `parentId` 組樹（同層按代碼排序，`aria-level` 表示層級），搜尋只在前端過濾並保留命中節點的上層。頁首說明「子科目代碼以上層代碼開頭並沿用上層類別；只有啟用且沒有子科目的科目可以過帳；代碼、上層與類別建立後不可修改」（ADR-039）。「新增一級科目」須選類別，選擇後帶出預設餘額方向（資產、成本、費用為借方，其餘貸方），仍可改（例如備抵科目）；啟用科目列有「新增子科目」，對話框顯示上層、代碼預填上層代碼、類別沿用上層且唯讀，前端先檢查代碼前綴，送出時不帶類別由後端沿用。修改只可改名稱、方向與啟用狀態，帶 version 送出。樹的共用函式（`flattenCategoryTree`、`filterCategoryRows`）改為泛型，物料分類與會計科目共用。頁面隨公司切換重新載入並關閉對話框。選單「主檔 → 會計科目」。亮暗主題沿用共用 token，三語文字在 `md.json`（`account`）與 `errors.json`（`MD.ACCOUNT.*` 12 個，含 FI 過帳檢查用的 `INACTIVE`、`NOT_POSTABLE`）。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.87.1 | Search (Input) | 搜尋代碼或名稱（不分大小寫），保留命中科目的上層。 | 在科目樹中找到科目。 | Text | 任意文字 | REQ_IO_A8.87.2 | Developer | REQ_F4118.39.1 |
| REQ_IO_A8.87.2 | Account Tree (Table) | 代碼（按層縮排）、名稱、類別、餘額方向、過帳（可過帳／彙總）、狀態；每列有新增子科目（只限啟用科目）／編輯／刪除。 | 查看科目階層與可過帳科目。 | Tree Table | 不分頁，含停用 | REQ_IO_A8.88.1 | Developer | REQ_F4118.39.1 |
| REQ_IO_A8.87.3 | Delete (Button) | 二次確認後帶版本軟刪除；有子科目顯示 HAS_CHILDREN，已被使用顯示 IN_USE。 | 移除未使用的末級科目。 | Button | DELETE 權限 | REQ_IO_A8.87.2 | Developer | REQ_F4118.39.4 |
| REQ_IO_A8.88.1 | Code (Input) | 新增時填寫，子科目預填上層代碼；編輯時唯讀。 | 公司內永久唯一識別。 | Text | 1–30 位數字；子科目須以上層代碼開頭且較長 | REQ_IO_A8.87.2 | Developer | REQ_F4118.39.2 |
| REQ_IO_A8.88.2 | Name (Input) | 名稱必填。 | 顯示科目。 | Text | 非空白，最多 200 字 | REQ_IO_A8.87.2 | Developer | REQ_F4118.39.2, REQ_F4118.39.3 |
| REQ_IO_A8.88.3 | Type (Select) | 一級科目必選；子科目沿用上層並唯讀；編輯時唯讀。 | 決定科目在報表中的分類與預設方向。 | Enum | Asset／Liability／Equity／Cost／Revenue／Expense | REQ_IO_A8.88.4 | Developer | REQ_F4118.39.2 |
| REQ_IO_A8.88.4 | Normal Balance (Select) | 按類別預設，可改；已被使用時改方向顯示 IN_USE。 | 決定餘額正負方向。 | Enum | Debit／Credit | REQ_IO_A8.88.3 | Developer | REQ_F4118.39.2, REQ_F4118.39.3 |
| REQ_IO_A8.88.5 | Active (Checkbox) | 只在編輯時出現；有啟用子科目不可停用，上層停用時不可啟用。 | 停用不再使用的科目。 | Boolean | 啟用／停用 | REQ_IO_A8.87.2 | Developer | REQ_F4118.39.3 |

## WU-MD-018 結算方式與付款條件

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.48 | Settlement Method View Interface | `/md/settlement-methods` | REQ_F4118.14.1, REQ_F4118.14.4 | `MD.SETTLEMENT_METHOD.VIEW`；刪除另需 DELETE |
| 8.49 | Settlement Method Create Interface | 同頁對話框 | REQ_F4118.14.2 | `MD.SETTLEMENT_METHOD.CREATE` |
| 8.50 | Settlement Method Edit Interface | 同頁對話框 | REQ_F4118.14.3 | `MD.SETTLEMENT_METHOD.EDIT` |
| 8.89 | Payment Term View Interface | `/md/payment-terms` | REQ_F4118.40.1, REQ_F4118.40.4, REQ_F4118.40.5 | `MD.PAYMENT_TERM.VIEW`；刪除另需 DELETE |
| 8.90 | Payment Term Create Interface | 同頁對話框 | REQ_F4118.40.2 | `MD.PAYMENT_TERM.CREATE` |
| 8.91 | Payment Term Edit Interface | 同頁對話框 | REQ_F4118.40.3 | `MD.PAYMENT_TERM.EDIT` |

兩頁均依目前公司以代碼或名稱搜尋、伺服器分頁與代碼／名稱排序；新增代碼轉大寫，編輯時唯讀，刪除帶版本並需確認。切換公司時清除對話框並重新載入。結算方式沿用扁平分類頁 `CategoryList`，可停用。付款條件列表顯示三語規則說明；表單依 `dueType` 顯示 `days`（NetDays）、`days + closingDay`（ClosingDays）、`closingDay + months + paymentDay`（ClosingMonthDay），只送出適用欄位。`days` 0–365、`closingDay`／`paymentDay` 1–31（31 代表月底）、`months` 0–12；同月付款日須大於結帳日。已存條件可輸入單據日期呼叫到期日 API 試算，停用條件顯示 INACTIVE。三語文字在 `md.json`、`layout.json` 與 `errors.json`，亮暗主題沿用共用 token。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.48.1 | Search / List (Input, Table) | 搜尋並列出代碼、名稱、狀態與操作。 | 查看結算方式。 | Text／Table | 每頁 10／20／50／100／200 | REQ_IO_A8.49.1, REQ_IO_A8.50.1 | Developer | REQ_F4118.14.1 |
| REQ_IO_A8.48.2 | Delete (Button) | 確認後帶版本軟刪除；被引用時提示 IN_USE。 | 移除未使用項。 | Action | DELETE 權限 | REQ_IO_A8.48.1 | Developer | REQ_F4118.14.4 |
| REQ_IO_A8.49.1 | Code / Name (Input) | 公司內唯一代碼與名稱。 | 建立結算方式。 | Text | 代碼 1–30 位英數、`-`、`_`；名稱非空白最多 200 字 | REQ_IO_A8.48.1 | Developer | REQ_F4118.14.2 |
| REQ_IO_A8.50.1 | Name / Active (Input, Checkbox) | 代碼唯讀；名稱與啟用狀態可改，帶版本。 | 維護結算方式。 | Text／Boolean | 同建立 | REQ_IO_A8.48.1 | Developer | REQ_F4118.14.3 |
| REQ_IO_A8.89.1 | Search / List (Input, Table) | 搜尋並列出代碼、名稱、規則說明、狀態與操作。 | 查看付款條件。 | Text／Table | 每頁 10／20／50／100／200 | REQ_IO_A8.90.1, REQ_IO_A8.91.1 | Developer | REQ_F4118.40.1 |
| REQ_IO_A8.89.2 | Due Date (Date Picker, Label) | 已存條件輸入單據日後從 API 顯示到期日；停用時顯示錯誤。 | 驗證付款期限。 | Date | yyyy-MM-dd | REQ_IO_A8.89.1 | Developer | REQ_F4118.40.5 |
| REQ_IO_A8.89.3 | Delete (Button) | 確認後帶版本軟刪除；被引用時提示 IN_USE。 | 移除未使用條件。 | Action | DELETE 權限 | REQ_IO_A8.89.1 | Developer | REQ_F4118.40.4 |
| REQ_IO_A8.90.1 | Code / Name (Input) | 新增時代碼轉大寫；編輯時唯讀。 | 識別付款條件。 | Text | 代碼 1–30 位英數、`-`、`_`；名稱非空白最多 200 字 | REQ_IO_A8.89.1 | Developer | REQ_F4118.40.2 |
| REQ_IO_A8.90.2 | Due Type / Rule Fields (Select, Input) | 依類型切換欄位並清除不適用值。 | 設定到期規則。 | Enum／Integer | NetDays／ClosingDays／ClosingMonthDay；日數 0–365、日 1–31、月 0–12 | REQ_IO_A8.89.1 | Developer | REQ_F4118.40.2 |
| REQ_IO_A8.91.1 | Name / Rule / Active (Form) | 修改名稱、規則與啟用狀態，帶版本。 | 維護付款條件。 | Text／Enum／Integer／Boolean | 同建立；同月付款日須晚於結帳日 | REQ_IO_A8.89.1 | Developer | REQ_F4118.40.3 |

## WU-MD-020b 收支類別與資金帳戶

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.20 | Fund Account View Interface | `/md/fund-accounts` | REQ_F4118.5.1, REQ_F4118.5.4 | `MD.FUND_ACCOUNT.VIEW`；刪除另需 DELETE |
| 8.21 | Fund Account Create Interface | 同頁對話框 | REQ_F4118.5.2 | `MD.FUND_ACCOUNT.CREATE` |
| 8.22 | Fund Account Edit Interface | 同頁對話框 | REQ_F4118.5.3 | `MD.FUND_ACCOUNT.EDIT` |
| 8.42 | Income Expense Category View Interface | `/md/income-expense-categories` | REQ_F4118.12.1, REQ_F4118.12.4 | `MD.INCOME_EXPENSE_CATEGORY.VIEW`；刪除另需 DELETE |
| 8.43 | Income Expense Category Create Interface | 同頁對話框 | REQ_F4118.12.2 | `MD.INCOME_EXPENSE_CATEGORY.CREATE` |
| 8.44 | Income Expense Category Edit Interface | 同頁對話框 | REQ_F4118.12.3 | `MD.INCOME_EXPENSE_CATEGORY.EDIT` |

兩頁依公司搜尋、類型／方向篩選、伺服器分頁與排序。代碼建立時轉大寫、編輯時唯讀；刪除先確認並帶版本。資金帳戶的 Cash 不顯示銀行欄位且送出時清空，Bank 可填銀行名稱與帳號；幣別建立後唯讀。收支類別的方向建立後唯讀。參照科目只列啟用且可過帳的對應類別，原有停用科目在編輯時保留供查看。沒有 `MD.ACCOUNT.VIEW` 或 `MD.CURRENCY.VIEW` 時不讀對應 API。切換公司清除編輯狀態及參照清單，重新查詢；列表與錯誤訊息均為三語並沿用亮暗主題 token。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.20.1 | Search / Type / List (Input, Select, Table) | 搜尋、Cash／Bank 篩選，列出帳戶及狀態。 | 查看資金帳戶。 | Text／Enum／Table | 每頁 10／20／50／100／200 | REQ_IO_A8.21.1, REQ_IO_A8.22.1 | Developer | REQ_F4118.5.1 |
| REQ_IO_A8.20.2 | Delete (Button) | 確認後帶版本軟刪除；引用時顯示 IN_USE。 | 移除未使用帳戶。 | Action | DELETE 權限 | REQ_IO_A8.20.1 | Developer | REQ_F4118.5.4 |
| REQ_IO_A8.21.1 | Code / Name / Currency / Type (Input, Select) | 建立代碼、名稱、幣別和類型。 | 定義資金帳戶。 | Text／Enum | 代碼 1–30 位；幣別須啟用 | REQ_IO_A8.20.1 | Developer | REQ_F4118.5.2 |
| REQ_IO_A8.21.2 | Bank / Ledger Account (Input, Select) | Bank 時顯示銀行欄位；科目限資產類末級。 | 設定銀行資訊與對應科目。 | Text／Select | 銀行帳號 0–50 位英數、空格、連字號 | REQ_IO_A8.20.1 | Developer | REQ_F4118.5.2 |
| REQ_IO_A8.22.1 | Name / Type / Bank / Ledger / Active (Form) | 修改時幣別與代碼唯讀，類型可改，帶版本。 | 維護帳戶。 | Text／Enum／Boolean | 同建立 | REQ_IO_A8.20.1 | Developer | REQ_F4118.5.3 |
| REQ_IO_A8.42.1 | Search / Direction / List (Input, Select, Table) | 搜尋並依 Income／Expense 篩選。 | 查看收支類別。 | Text／Enum／Table | 每頁 10／20／50／100／200 | REQ_IO_A8.43.1, REQ_IO_A8.44.1 | Developer | REQ_F4118.12.1 |
| REQ_IO_A8.42.2 | Delete (Button) | 確認後帶版本軟刪除；引用時顯示 IN_USE。 | 移除未使用類別。 | Action | DELETE 權限 | REQ_IO_A8.42.1 | Developer | REQ_F4118.12.4 |
| REQ_IO_A8.43.1 | Code / Name / Direction / Ledger (Form) | 收入科目限 Revenue；支出限 Expense／Cost。 | 建立收支類別。 | Text／Enum／Select | 代碼 1–30 位；科目啟用且可過帳 | REQ_IO_A8.42.1 | Developer | REQ_F4118.12.2 |
| REQ_IO_A8.44.1 | Name / Ledger / Active (Form) | 方向與代碼唯讀，帶版本。 | 維護收支類別。 | Text／Select／Boolean | 同建立 | REQ_IO_A8.42.1 | Developer | REQ_F4118.12.3 |

## WU-MD-021b 工序類別與工序

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.51 | Process Category View Interface | `/md/process-categories` | REQ_F4118.15.1, REQ_F4118.15.4 | `MD.PROCESS_CATEGORY.VIEW`；刪除另需 DELETE |
| 8.52 | Process Category Create Interface | 同頁對話框 | REQ_F4118.15.2 | `MD.PROCESS_CATEGORY.CREATE` |
| 8.53 | Process Category Edit Interface | 同頁對話框 | REQ_F4118.15.3 | `MD.PROCESS_CATEGORY.EDIT` |
| 8.27 | Process View Interface | `/md/processes` | REQ_F4118.7.1, REQ_F4118.7.4 | `MD.PROCESS.VIEW`；刪除另需 DELETE |
| 8.28 | Process Create Interface | 同頁對話框 | REQ_F4118.7.2 | `MD.PROCESS.CREATE` |
| 8.29 | Process Edit Interface | 同頁對話框 | REQ_F4118.7.3 | `MD.PROCESS.EDIT` |

兩頁共用 `ProcessPage`（路由以 `PROCESS_PAGE_KIND` 區分），依公司搜尋、伺服器分頁，預設按排序號（`sort=sortOrder`）排列，亦可按代碼、名稱排序。代碼建立時轉大寫、編輯時唯讀；排序號為 0–9999 整數；刪除先確認並帶版本，類別仍有工序時顯示 `HAS_PROCESSES`，工序已被生產資料引用時顯示 `IN_USE`。工序頁可按類別篩選（選項含停用類別以便查看），新增時預設帶入目前篩選的啟用類別；表單的類別與計件單位只列啟用項，編輯時保留原有停用值。標準工價以文字輸入，須 ≥ 0、最多 9 位整數及 6 位小數（前端限制 15 位有效數字以免 JSON 數字失真，後端欄位為 decimal(19,6)）。沒有 `MD.PROCESS_CATEGORY.VIEW` 或 `MD.UOM.VIEW` 時不讀對應 API，列表以 ID 顯示。切換公司清除編輯狀態、篩選與參照清單並重新查詢；文字三語並沿用亮暗主題 token。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.51.1 | Search / List (Input, Table) | 搜尋代碼或名稱，列出代碼、名稱、排序號、工序數及狀態。 | 查看工序類別。 | Text／Table | 每頁 10／20／50／100／200 | REQ_IO_A8.52.1, REQ_IO_A8.53.1 | Developer | REQ_F4118.15.1 |
| REQ_IO_A8.51.2 | Delete (Button) | 確認後帶版本軟刪除；仍有工序時顯示 HAS_PROCESSES。 | 移除未使用類別。 | Action | DELETE 權限 | REQ_IO_A8.51.1 | Developer | REQ_F4118.15.4 |
| REQ_IO_A8.52.1 | Code / Name / Sort Order (Form) | 建立類別。 | 定義工序類別。 | Text／Integer | 代碼 1–30 位英數、`-`、`_`；名稱非空白最多 200 字；排序號 0–9999 | REQ_IO_A8.51.1 | Developer | REQ_F4118.15.2 |
| REQ_IO_A8.53.1 | Name / Sort Order / Active (Form) | 代碼唯讀，帶版本修改。 | 維護工序類別。 | Text／Integer／Boolean | 同建立 | REQ_IO_A8.51.1 | Developer | REQ_F4118.15.3 |
| REQ_IO_A8.27.1 | Search / Category / List (Input, Select, Table) | 搜尋並按類別篩選，列出類別、標準工價、計件單位、排序號及狀態。 | 查看工序。 | Text／Select／Table | 每頁 10／20／50／100／200 | REQ_IO_A8.28.1, REQ_IO_A8.29.1 | Developer | REQ_F4118.7.1 |
| REQ_IO_A8.27.2 | Delete (Button) | 確認後帶版本軟刪除；已引用時顯示 IN_USE。 | 移除未使用工序。 | Action | DELETE 權限 | REQ_IO_A8.27.1 | Developer | REQ_F4118.7.4 |
| REQ_IO_A8.28.1 | Code / Name / Category / Piece Unit (Form) | 類別與單位可不指定，只列啟用選項。 | 定義工序。 | Text／Select | 代碼 1–30 位；名稱最多 200 字 | REQ_IO_A8.27.1 | Developer | REQ_F4118.7.2 |
| REQ_IO_A8.28.2 | Standard Rate / Sort Order (Input) | 輸入標準工價與排序號。 | 設定計件工價及顯示順序。 | Decimal／Integer | 工價 0–999999999.999999；排序號 0–9999 | REQ_IO_A8.27.1 | Developer | REQ_F4118.7.2 |
| REQ_IO_A8.29.1 | Name / Category / Unit / Rate / Sort / Active (Form) | 代碼唯讀，保留原停用類別／單位，帶版本。 | 維護工序。 | Text／Select／Decimal／Boolean | 同建立 | REQ_IO_A8.27.1 | Developer | REQ_F4118.7.3 |

## WU-MD-008 供應商聯絡人

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.9 | Supplier Contact Management Interface | `/md/suppliers` 列「聯絡人」對話框 | REQ_F4118.43.1, REQ_F4118.43.2, REQ_F4118.43.3, REQ_F4118.43.4 | `MD.SUPPLIER.VIEW`；維護需 `MD.SUPPLIER.EDIT` |

與客戶共用 `PartnerContactDialog`（`kind = supplier`）：只有聯絡人段，沒有送貨地址；標題「{代碼} 的聯絡人」，提示採購單據會帶出預設聯絡人。開啟方式、唯讀行為、保存後重新讀取與 404／409 處理同介面 8.4。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.9.1 | Contact List (Table) | 姓名（預設標籤、備註）、職稱、手機、電話、電郵、通訊軟體帳號、狀態、編輯／刪除；預設排第一，不分頁。 | 查看供應商聯絡人。 | Table | 全部未刪除聯絡人 | REQ_IO_A8.6.2 | Developer | REQ_F4118.43.1 |
| REQ_IO_A8.9.2 | Contact Form (Input) | 姓名必填；其他欄位選填，去除前後空白，空白送 null；電郵須為單一合法地址。 | 新增或修改聯絡人。 | Text | 姓名／職稱 100、手機／電話 50、電郵 200、通訊帳號 100、備註 500 字 | REQ_IO_A8.9.1 | Developer | REQ_F4118.43.2, REQ_F4118.43.3 |
| REQ_IO_A8.9.3 | Default / Active (Checkbox) | 設為預設會取消原預設；編輯時可停用；停用且勾預設時在前端提示並不送出。 | 指定採購單據帶出的聯絡人。 | Boolean | 預設必為啟用 | REQ_IO_A8.9.1 | Developer | REQ_F4118.43.2, REQ_F4118.43.3 |
| REQ_IO_A8.9.4 | Delete Contact (Button) | 確認後帶版本刪除，刪除預設後供應商暫無預設。 | 移除聯絡人。 | Action | `MD.SUPPLIER.EDIT` | REQ_IO_A8.9.1 | Developer | REQ_F4118.43.4 |

## WU-MD-009 供應商匯入

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.10 | Supplier Import Interface | `/md/suppliers`「匯入」對話框 | REQ_F4118.2.7 | `MD.SUPPLIER.IMPORT` |

與客戶匯入共用 `PartnerImportDialog`（`kind = supplier`）：標題「匯入供應商」，提示每列可帶一位預設聯絡人；範本沒有地址欄。預覽、提交、換檔／切公司／關閉清空預覽、422／409 後重新預覽、亮／暗模式同介面 8.5。data-testid 前綴 `md-supplier-import-`。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.10.1 | Template (Button) | 下載 xlsx 或 CSV 表頭範本（代碼、名稱、分類代碼、聯絡人姓名／手機／電話／電郵）。 | 提供正確欄位。 | xlsx／UTF-8 CSV | 固定英文鍵表頭 | REQ_IO_A8.10.2 | Developer | REQ_F4118.2.7 |
| REQ_IO_A8.10.2 | Import File (Input) | 選擇本機檔案；換檔清除舊預覽。 | 選擇匯入資料。 | xlsx／CSV | 最多 2 MB、1,000 資料列 | REQ_IO_A8.10.3 | Developer | REQ_F4118.2.7 |
| REQ_IO_A8.10.3 | Preview (Button, Table) | 顯示實際列號、代碼、名稱、Create／Skip／Error 與欄位錯誤。 | 提交前驗證。 | Table | 每頁 20 列 | REQ_IO_A8.10.4 | Developer | REQ_F4118.2.7 |
| REQ_IO_A8.10.4 | Import (Button) | 無錯誤時重新上傳同一檔案，成功後更新供應商列表；衝突須重新預覽。 | 整批寫入。 | Action | IMPORT 權限、預覽 0 Error | REQ_IO_A8.6.2 | Developer | REQ_F4118.2.7 |

## WU-MD-013c 物料圖片與屬性

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 8.15 | Product Image Interface | `/md/items` 列「圖片」對話框 | REQ_F4118.44.1, REQ_F4118.44.2, REQ_F4118.44.3, REQ_F4118.44.4 | `MD.ITEM.VIEW`；上傳、排序、設主圖、刪除需 `MD.ITEM.EDIT` |
| 8.16 | Product Details Interface | `/md/items` 列「屬性」對話框；屬性定義頁 `/md/item-attributes` | REQ_F4118.45.1, REQ_F4118.45.2, REQ_F4118.45.3, REQ_F4118.45.4, REQ_F4118.45.5, REQ_F4118.45.6 | 物料屬性 `MD.ITEM.VIEW`／`MD.ITEM.EDIT`；定義頁 `MD.ITEM_ATTRIBUTE.VIEW/CREATE/EDIT/DELETE` |

圖片對話框（`ItemImageDialog`）：以 HttpClient 取 Blob 轉 data: URL 預覽（前端 CSP 的 img-src 只允許 `'self' data:`），主圖排第一；用戶端先檢查 PNG／JPEG、非空、2 MB 與 10 張上限，寬高由伺服器檢查。可勾「設為主圖」後上傳；排序號在列內輸入 0–9999 整數，改變時帶版本儲存；非主圖列可「設為主圖」，主圖只能由另一張取代。修改後重新讀取列表（主圖與排序可能連帶改變），已載入的預覽按 id 保留；404／409 時顯示錯誤並重新讀取。

屬性對話框（`ItemAttributeDialog`）：沒有 `MD.ITEM.EDIT` 時以定義清單唯讀顯示；可編輯時每列選屬性並輸入值，選項為啟用屬性加物料已有值的停用屬性（標示停用），並排除其他列已選；最多 50 列；儲存以完整集合 PUT 整批取代，後端 `attributes[i].attributeId`／`value` 錯誤標在對應列。沒有 `MD.ITEM_ATTRIBUTE.VIEW` 時不讀屬性定義，只能修改或移除現有屬性。屬性定義頁沿用 `ProcessPage`（`PROCESS_PAGE_KIND = attribute`）：預設按排序號，顯示物料數，被使用時刪除顯示 `IN_USE`。切換公司關閉兩個對話框；文字三語並沿用亮暗主題 token。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.15.1 | Image List (Table) | 預覽、檔名、大小與寬高、排序號、主圖、刪除；不分頁。 | 查看物料圖片。 | Image／Table | 每物料最多 10 張 | REQ_IO_A8.11.2 | Developer | REQ_F4118.44.1 |
| REQ_IO_A8.15.2 | Upload / Set as Primary (File, Checkbox) | 選擇檔案後立即上傳，可指定為主圖；同一檔案可再選。 | 新增物料圖片。 | PNG／JPEG | 1 B–2 MB、16–4096 像素 | REQ_IO_A8.15.1 | Developer | REQ_F4118.44.2 |
| REQ_IO_A8.15.3 | Sort Order / Set Primary (Input, Button) | 改排序號或設為主圖，帶版本。 | 決定顯示順序與主圖。 | Integer／Action | 0–9999 | REQ_IO_A8.15.1 | Developer | REQ_F4118.44.3 |
| REQ_IO_A8.15.4 | Delete (Button) | 確認後帶版本刪除；刪除主圖由排序最前者接任。 | 移除圖片。 | Action | `MD.ITEM.EDIT` | REQ_IO_A8.15.1 | Developer | REQ_F4118.44.4 |
| REQ_IO_A8.16.1 | Attribute Definition List (Table) | 搜尋代碼或名稱，列出代碼、名稱、排序號、物料數及狀態；刪除使用中屬性顯示 IN_USE。 | 查看與刪除屬性定義。 | Text／Table | 每頁 10／20／50／100／200 | REQ_IO_A8.16.2 | Developer | REQ_F4118.45.1, REQ_F4118.45.4 |
| REQ_IO_A8.16.2 | Code / Name / Sort Order / Active (Form) | 建立時代碼轉大寫，編輯時代碼唯讀並帶版本，可停用。 | 定義物料屬性。 | Text／Integer／Boolean | 代碼 1–30 位英數、`-`、`_`；名稱最多 200 字；排序號 0–9999 | REQ_IO_A8.16.1 | Developer | REQ_F4118.45.2, REQ_F4118.45.3 |
| REQ_IO_A8.16.3 | Item Attributes (Select, Input) | 每列一個屬性與值，可新增、移除列，整批儲存。 | 描述物料規格。 | Select／Text | 最多 50 個屬性；值 1–200 字 | REQ_IO_A8.11.2, REQ_IO_A8.16.1 | Developer | REQ_F4118.45.5, REQ_F4118.45.6 |
