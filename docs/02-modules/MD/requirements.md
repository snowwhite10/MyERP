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
