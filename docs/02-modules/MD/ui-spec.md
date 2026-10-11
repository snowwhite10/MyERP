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
